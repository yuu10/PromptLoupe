using System.Runtime.InteropServices;
using SkiaSharp;

namespace PromptLoupe;

/// <summary>HEIC を読む OS の機能(拡張機能・プラグイン・ライブラリ)がないとき。このときだけ、入れ方の案内を出す。</summary>
public sealed class HeifCodecMissingException(string message) : NotSupportedException(message);

/// <summary>
/// HEIC・HEIF の読み込み。HEVC のデコーダーは LGPL などで exe に入れられないので、同梱せずに OS の機能を使う。
/// - Windows: WIC(WPF 版と同じ。Microsoft Store の「HEIF 画像拡張機能」と「HEVC ビデオ拡張機能」が必要)
/// - Mac: ImageIO(OS に標準で入っている)
/// - Linux: その PC に入っている libheif(libheif.so.1)と、HEVC のプラグイン(Ubuntu なら libheif-plugin-libde265)
/// どれも HEIF の回転(irot・imir)を当てて返すので、こちらでは回さない。
/// COM・Core Foundation・libheif の関数は、パッケージを足さずに関数ポインターで直接呼ぶ。
/// </summary>
public static class HeifLoader
{
    /// <summary>HEIC を読む OS の機能がないときの案内(OS ごと)。</summary>
    public static string Hint =>
        OperatingSystem.IsWindows() ? "(HEIC は Windows の「HEIF 画像拡張機能」と「HEVC ビデオ拡張機能」が必要です)"
        : OperatingSystem.IsMacOS() ? ""
        : "(HEIC を表示するには、libheif と HEVC のプラグイン(Ubuntu なら libheif-plugin-libde265)を入れてください)";

    /// <summary>
    /// 読んだ画像を返す。maxSide を指定すると、OS の機能で縮小しながら読む(Windows・Mac。Linux は呼び出し側で縮小する)。
    /// 返す画像は、指定より大きいことがある(呼び出し側で改めて縮小する)。
    /// </summary>
    public static SKBitmap Decode(string path, int? maxSide)
    {
        if (OperatingSystem.IsWindows()) return Wic.Decode(path, maxSide);
        if (OperatingSystem.IsMacOS()) return ImageIO.Decode(path, maxSide);
        return LibHeif.Decode(path);
    }

    // ───────────────────── Windows: WIC ─────────────────────

    static unsafe class Wic
    {
        [DllImport("ole32.dll")]
        static extern int CoInitializeEx(IntPtr reserved, uint coInit);

        [DllImport("ole32.dll")]
        static extern int CoCreateInstance(in Guid clsid, IntPtr outer, uint context, in Guid iid, out IntPtr obj);

        static readonly Guid ClsidImagingFactory = new("cacaf262-9370-4615-a13b-9f5539da4c0a");
        static readonly Guid IidImagingFactory = new("ec5ec8a9-c395-4314-9c77-54d7a935ff70");
        static readonly Guid PixelFormat32bppPBGRA = new("6fddc324-4e03-4bfe-b185-3d77768dc910");

        const uint CoInitMultithreaded = 0x0;
        const uint ClsCtxInprocServer = 0x1;
        const uint GenericRead = 0x80000000;
        const int DecodeMetadataCacheOnDemand = 0;
        const int InterpolationFant = 3;
        const int ComponentNotFound = unchecked((int)0x88982F50); // WINCODEC_ERR_COMPONENTNOTFOUND(HEIF の拡張機能がない)

        // 仮想関数表の位置(IUnknown の 3 つ: QueryInterface・AddRef・Release のあと)
        // IWICImagingFactory: 3 CreateDecoderFromFilename, 10 CreateFormatConverter, 11 CreateBitmapScaler
        // IWICBitmapDecoder: 13 GetFrame
        // IWICBitmapSource(フレーム・変換・縮小に共通): 3 GetSize, 7 CopyPixels
        // IWICFormatConverter・IWICBitmapScaler: 8 Initialize

        static void* Method(IntPtr obj, int index) => (*(void***)obj)[index];

        static void Release(IntPtr obj)
        {
            if (obj != IntPtr.Zero) ((delegate* unmanaged[Stdcall]<IntPtr, uint>)Method(obj, 2))(obj);
        }

        static void Check(int hr, string what)
        {
            if (hr == ComponentNotFound) throw new HeifCodecMissingException("HEIC を読む Windows の拡張機能がありません");
            if (hr < 0) throw new InvalidDataException($"HEIC を読めませんでした({what}: 0x{hr:X8})");
        }

        public static SKBitmap Decode(string path, int? maxSide)
        {
            // スレッドプールのスレッドは COM の準備がされていないことがある(済んでいれば S_FALSE などが返るだけ)
            CoInitializeEx(IntPtr.Zero, CoInitMultithreaded);

            IntPtr factory = IntPtr.Zero, decoder = IntPtr.Zero, frame = IntPtr.Zero, scaler = IntPtr.Zero, converter = IntPtr.Zero;
            try
            {
                Check(CoCreateInstance(ClsidImagingFactory, IntPtr.Zero, ClsCtxInprocServer, IidImagingFactory, out factory), "WIC");

                fixed (char* p = path)
                {
                    IntPtr d;
                    Check(((delegate* unmanaged[Stdcall]<IntPtr, char*, Guid*, uint, int, IntPtr*, int>)Method(factory, 3))
                        (factory, p, null, GenericRead, DecodeMetadataCacheOnDemand, &d), "デコーダー");
                    decoder = d;
                }

                IntPtr f;
                Check(((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)Method(decoder, 13))(decoder, 0, &f), "フレーム");
                frame = f;

                uint w, h;
                Check(((delegate* unmanaged[Stdcall]<IntPtr, uint*, uint*, int>)Method(frame, 3))(frame, &w, &h), "大きさ");
                IntPtr source = frame;

                // サムネイルは、WIC に縮小させながら読む(全体を展開してから縮めるより速い)
                if (maxSide is int max && Math.Max(w, h) > max)
                {
                    double s = (double)max / Math.Max(w, h);
                    uint sw = Math.Max(1, (uint)Math.Round(w * s)), sh = Math.Max(1, (uint)Math.Round(h * s));
                    IntPtr sc;
                    Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Method(factory, 11))(factory, &sc), "縮小");
                    scaler = sc;
                    Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, uint, int, int>)Method(scaler, 8))
                        (scaler, source, sw, sh, InterpolationFant), "縮小");
                    source = scaler;
                    (w, h) = (sw, sh);
                }

                IntPtr cv;
                Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Method(factory, 10))(factory, &cv), "変換");
                converter = cv;
                Guid format = PixelFormat32bppPBGRA;
                Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, Guid*, int, IntPtr, double, int, int>)Method(converter, 8))
                    (converter, source, &format, 0, IntPtr.Zero, 0.0, 0), "変換");

                var bmp = new SKBitmap(new SKImageInfo((int)w, (int)h, SKColorType.Bgra8888, SKAlphaType.Premul));
                uint stride = (uint)bmp.RowBytes;
                int hr = ((delegate* unmanaged[Stdcall]<IntPtr, void*, uint, uint, byte*, int>)Method(converter, 7))
                    (converter, null, stride, checked(stride * h), (byte*)bmp.GetPixels());
                if (hr < 0)
                {
                    bmp.Dispose();
                    Check(hr, "画素");
                }
                return bmp;
            }
            finally
            {
                Release(converter);
                Release(scaler);
                Release(frame);
                Release(decoder);
                Release(factory);
            }
        }
    }

    // ───────────────────── Mac: ImageIO ─────────────────────

    static unsafe class ImageIO
    {
        const string CoreFoundationLib = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        const string ImageIOLib = "/System/Library/Frameworks/ImageIO.framework/ImageIO";
        const string CoreGraphicsLib = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

        [StructLayout(LayoutKind.Sequential)]
        struct CGRect
        {
            public double X, Y, Width, Height;
        }

        [DllImport(CoreFoundationLib)]
        static extern IntPtr CFURLCreateFromFileSystemRepresentation(IntPtr allocator, byte* buffer, nint length,
            [MarshalAs(UnmanagedType.U1)] bool isDirectory);

        [DllImport(CoreFoundationLib)]
        static extern IntPtr CFDictionaryCreate(IntPtr allocator, IntPtr* keys, IntPtr* values, nint count,
            IntPtr keyCallBacks, IntPtr valueCallBacks);

        [DllImport(CoreFoundationLib)]
        static extern IntPtr CFNumberCreate(IntPtr allocator, nint type, void* value);

        [DllImport(CoreFoundationLib)]
        static extern void CFRelease(IntPtr obj);

        [DllImport(ImageIOLib)]
        static extern IntPtr CGImageSourceCreateWithURL(IntPtr url, IntPtr options);

        [DllImport(ImageIOLib)]
        static extern IntPtr CGImageSourceCreateThumbnailAtIndex(IntPtr source, nint index, IntPtr options);

        [DllImport(CoreGraphicsLib)]
        static extern nuint CGImageGetWidth(IntPtr image);

        [DllImport(CoreGraphicsLib)]
        static extern nuint CGImageGetHeight(IntPtr image);

        [DllImport(CoreGraphicsLib)]
        static extern IntPtr CGColorSpaceCreateWithName(IntPtr name);

        [DllImport(CoreGraphicsLib)]
        static extern void CGColorSpaceRelease(IntPtr space);

        [DllImport(CoreGraphicsLib)]
        static extern IntPtr CGBitmapContextCreate(void* data, nuint width, nuint height, nuint bitsPerComponent,
            nuint bytesPerRow, IntPtr space, uint bitmapInfo);

        [DllImport(CoreGraphicsLib)]
        static extern void CGContextDrawImage(IntPtr context, CGRect rect, IntPtr image);

        [DllImport(CoreGraphicsLib)]
        static extern void CGContextRelease(IntPtr context);

        const nint NumberSInt32Type = 3;                    // kCFNumberSInt32Type
        const uint PremultipliedFirstLittle = 2 | (2 << 12); // kCGImageAlphaPremultipliedFirst | kCGBitmapByteOrder32Little(メモリ上で B・G・R・A)

        /// <summary>フレームワークが公開している定数(CFStringRef など)の値を読む。</summary>
        static IntPtr Constant(string library, string name) =>
            *(IntPtr*)NativeLibrary.GetExport(NativeLibrary.Load(library), name);

        /// <summary>フレームワークが公開している構造体の場所(辞書のコールバックなど)。</summary>
        static IntPtr Address(string library, string name) =>
            NativeLibrary.GetExport(NativeLibrary.Load(library), name);

        public static SKBitmap Decode(string path, int? maxSide)
        {
            IntPtr url = IntPtr.Zero, src = IntPtr.Zero, options = IntPtr.Zero, number = IntPtr.Zero, image = IntPtr.Zero;
            IntPtr space = IntPtr.Zero, context = IntPtr.Zero;
            try
            {
                byte[] utf8 = System.Text.Encoding.UTF8.GetBytes(path);
                fixed (byte* p = utf8) url = CFURLCreateFromFileSystemRepresentation(IntPtr.Zero, p, utf8.Length, false);
                if (url == IntPtr.Zero) throw new InvalidDataException("HEIC を開けませんでした");
                src = CGImageSourceCreateWithURL(url, IntPtr.Zero);
                if (src == IntPtr.Zero) throw new InvalidDataException("HEIC を開けませんでした");

                // 「サムネイル」として作らせると、回転(irot など)を当てた画像が得られる。大きさを指定しなければ元の大きさ
                IntPtr yes = Constant(CoreFoundationLib, "kCFBooleanTrue");
                var keys = new List<IntPtr>
                {
                    Constant(ImageIOLib, "kCGImageSourceCreateThumbnailFromImageAlways"),
                    Constant(ImageIOLib, "kCGImageSourceCreateThumbnailWithTransform"),
                };
                var values = new List<IntPtr> { yes, yes };
                if (maxSide is int max)
                {
                    int m = max;
                    number = CFNumberCreate(IntPtr.Zero, NumberSInt32Type, &m);
                    keys.Add(Constant(ImageIOLib, "kCGImageSourceThumbnailMaxPixelSize"));
                    values.Add(number);
                }
                var k = keys.ToArray();
                var v = values.ToArray();
                fixed (IntPtr* kp = k)
                fixed (IntPtr* vp = v)
                    options = CFDictionaryCreate(IntPtr.Zero, kp, vp, k.Length,
                        Address(CoreFoundationLib, "kCFTypeDictionaryKeyCallBacks"),
                        Address(CoreFoundationLib, "kCFTypeDictionaryValueCallBacks"));

                image = CGImageSourceCreateThumbnailAtIndex(src, 0, options);
                if (image == IntPtr.Zero) throw new InvalidDataException("HEIC を読めませんでした");

                int w = (int)CGImageGetWidth(image), h = (int)CGImageGetHeight(image);
                var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
                space = CGColorSpaceCreateWithName(Constant(CoreGraphicsLib, "kCGColorSpaceSRGB"));
                context = CGBitmapContextCreate((void*)bmp.GetPixels(), (nuint)w, (nuint)h, 8, (nuint)bmp.RowBytes, space,
                    PremultipliedFirstLittle);
                if (context == IntPtr.Zero)
                {
                    bmp.Dispose();
                    throw new InvalidDataException("HEIC を読めませんでした");
                }
                CGContextDrawImage(context, new CGRect { Width = w, Height = h }, image);
                return bmp;
            }
            finally
            {
                if (context != IntPtr.Zero) CGContextRelease(context);
                if (space != IntPtr.Zero) CGColorSpaceRelease(space);
                foreach (var obj in new[] { image, options, number, src, url })
                    if (obj != IntPtr.Zero) CFRelease(obj);
            }
        }
    }

    // ───────────────────── Linux: libheif ─────────────────────

    static unsafe class LibHeif
    {
        [StructLayout(LayoutKind.Sequential)]
        struct HeifError
        {
            public int Code;    // 0 なら成功
            public int Subcode;
            public IntPtr Message;
        }

        const int ColorspaceRgb = 1;          // heif_colorspace_RGB
        const int ChromaInterleavedRgba = 11; // heif_chroma_interleaved_RGBA(メモリ上で R・G・B・A、アルファは乗算していない)
        const int ChannelInterleaved = 10;    // heif_channel_interleaved

        static readonly Lazy<IntPtr> Library = new(() =>
        {
            foreach (var name in new[] { "libheif.so.1", "libheif.so" })
                if (NativeLibrary.TryLoad(name, out var handle))
                {
                    // libheif 1.13 以降は、プラグイン(HEVC のデコーダーなど)を heif_init で読み込む
                    if (NativeLibrary.TryGetExport(handle, "heif_init", out var init))
                        ((delegate* unmanaged[Cdecl]<IntPtr, HeifError>)init)(IntPtr.Zero);
                    return handle;
                }
            return IntPtr.Zero;
        });

        static void* Fn(string name) => (void*)NativeLibrary.GetExport(Library.Value, name);

        const int ErrorUnsupportedFeature = 4;  // heif_error_Unsupported_feature
        const int SuberrorUnsupportedCodec = 3000; // heif_suberror_Unsupported_codec(HEVC のプラグインがない)

        static void Check(HeifError e)
        {
            if (e.Code == 0) return;
            string message = Marshal.PtrToStringUTF8(e.Message) ?? "";
            if (e.Code == ErrorUnsupportedFeature && e.Subcode == SuberrorUnsupportedCodec)
                throw new HeifCodecMissingException($"HEVC のデコーダーがありません({message})");
            throw new InvalidDataException($"HEIC を読めませんでした({message})");
        }

        public static SKBitmap Decode(string path)
        {
            if (Library.Value == IntPtr.Zero) throw new HeifCodecMissingException("libheif が入っていません");

            IntPtr ctx = ((delegate* unmanaged[Cdecl]<IntPtr>)Fn("heif_context_alloc"))();
            IntPtr handle = IntPtr.Zero, image = IntPtr.Zero;
            try
            {
                byte[] utf8 = System.Text.Encoding.UTF8.GetBytes(path + "\0");
                fixed (byte* p = utf8)
                    Check(((delegate* unmanaged[Cdecl]<IntPtr, byte*, IntPtr, HeifError>)Fn("heif_context_read_from_file"))
                        (ctx, p, IntPtr.Zero));

                IntPtr hd;
                Check(((delegate* unmanaged[Cdecl]<IntPtr, IntPtr*, HeifError>)Fn("heif_context_get_primary_image_handle"))(ctx, &hd));
                handle = hd;

                // 展開の設定を省くと、回転(irot・imir)を当てた画像が返る
                IntPtr img;
                Check(((delegate* unmanaged[Cdecl]<IntPtr, IntPtr*, int, int, IntPtr, HeifError>)Fn("heif_decode_image"))
                    (handle, &img, ColorspaceRgb, ChromaInterleavedRgba, IntPtr.Zero));
                image = img;

                int w = ((delegate* unmanaged[Cdecl]<IntPtr, int, int>)Fn("heif_image_get_width"))(image, ChannelInterleaved);
                int h = ((delegate* unmanaged[Cdecl]<IntPtr, int, int>)Fn("heif_image_get_height"))(image, ChannelInterleaved);
                int stride;
                byte* plane = ((delegate* unmanaged[Cdecl]<IntPtr, int, int*, byte*>)Fn("heif_image_get_plane_readonly"))
                    (image, ChannelInterleaved, &stride);
                if (plane == null || w <= 0 || h <= 0) throw new InvalidDataException("HEIC を読めませんでした");

                var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul));
                byte* dst = (byte*)bmp.GetPixels();
                for (int y = 0; y < h; y++)
                    Buffer.MemoryCopy(plane + (long)y * stride, dst + (long)y * bmp.RowBytes, bmp.RowBytes, w * 4);
                return bmp;
            }
            finally
            {
                if (image != IntPtr.Zero) ((delegate* unmanaged[Cdecl]<IntPtr, void>)Fn("heif_image_release"))(image);
                if (handle != IntPtr.Zero) ((delegate* unmanaged[Cdecl]<IntPtr, void>)Fn("heif_image_handle_release"))(handle);
                ((delegate* unmanaged[Cdecl]<IntPtr, void>)Fn("heif_context_free"))(ctx);
            }
        }
    }
}
