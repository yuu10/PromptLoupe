using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using BitMiracle.LibTiff.Classic;
using PromptLoupe.Metadata;
using SkiaSharp;

namespace PromptLoupe;

/// <summary>
/// 画像の読み込み。WPF 版の WIC の代わり。
/// - PNG・JPEG・WebP・BMP・GIF(先頭フレーム)は SkiaSharp で読む。表示用で回転が要らないものは、Avalonia の Bitmap(Stream) に直接読ませる
/// - TIFF(先頭ページ)は LibTiff.NET で読む(どの OS でも。exe に入る)
/// - HEIC・HEIF は OS の機能で読む(<see cref="HeifLoader"/>)。その機能がない環境では <see cref="UnsupportedHint"/> で入れ方を案内する
/// - JPEG・WebP・TIFF は回転情報(Orientation)に従って向きを直す。JPEG・WebP の値は WPF 版と同じく MetadataReader.ReadExif で読む。
///   HEIC は OS の機能が irot・imir を当てて返すので、こちらでは回さない(二重に回る)
/// </summary>
public static class ImageLoader
{
    /// <summary>画像を読み込む。maxSide を指定すると、長辺がその大きさになるように縮小する(サムネイル用)。</summary>
    public static Bitmap Load(string path, int? maxSide = null)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".tif" or ".tiff" or ".heic" or ".heif")
        {
            using var decoded = Shrink(ext is ".tif" or ".tiff" ? DecodeTiff(path) : HeifLoader.Decode(path, maxSide), maxSide);
            return ToAvalonia(decoded);
        }

        // 表示用(縮小しない・回転しない)は、Avalonia に直接読ませる。
        // 自分で展開してから Avalonia の画像へ写すより、写す 1 回分だけ速く、読み込み中のメモリも半分で済む
        if (maxSide == null && ReadOrientation(path) == 1)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                return new Bitmap(fs);
            }
            catch (Exception ex) when (ex is not IOException and not UnauthorizedAccessException)
            {
                // Avalonia が読めない画像は、下の SkiaSharp の読み方で試す(読めなければ、そちらで例外になる)
            }
        }

        using var bmp = Decode(path, maxSide);
        return ToAvalonia(bmp);
    }

    /// <summary>表示できなかったときに添える案内(HEIC を読む OS の機能がないときの入れ方。それ以外は空)。</summary>
    public static string UnsupportedHint(Exception error) => error is HeifCodecMissingException ? HeifLoader.Hint : "";

    // ───────── TIFF(LibTiff.NET) ─────────

    /// <summary>LibTiff.NET の警告・エラーを、コンソールに出さない(読めないときは例外にする)。</summary>
    sealed class QuietTiffErrors : TiffErrorHandler
    {
        public override void WarningHandler(Tiff tif, string method, string format, params object[] args) { }
        public override void WarningHandlerExt(Tiff tif, object clientData, string method, string format, params object[] args) { }
        public override void ErrorHandler(Tiff tif, string method, string format, params object[] args) { }
        public override void ErrorHandlerExt(Tiff tif, object clientData, string method, string format, params object[] args) { }
    }

    static readonly Lazy<bool> TiffQuiet = new(() =>
    {
        Tiff.SetErrorHandler(new QuietTiffErrors());
        return true;
    });

    /// <summary>
    /// TIFF の先頭ページを読む。LibTiff の RGBA 読み出しは、CMYK・16 ビット・パレット・JPEG 圧縮などを 8 ビットの RGBA にそろえて返す
    /// (アルファは乗算済み)。回転は自分で直す: LibTiff の RGBA 読み出しは Orientation 5〜8 の回転を扱わない(左右・上下の反転だけ)ので、
    /// 保存されたままの並びで読み出し(要求する向きをファイルと同じ系統にすると反転しない)、ApplyOrientation で 1〜8 をまとめて直す
    /// </summary>
    static SKBitmap DecodeTiff(string path)
    {
        _ = TiffQuiet.Value;
        using var tif = Tiff.Open(path, "r") ?? throw new InvalidDataException("TIFF を開けませんでした");

        int w = tif.GetField(TiffTag.IMAGEWIDTH)?[0].ToInt() ?? 0;
        int h = tif.GetField(TiffTag.IMAGELENGTH)?[0].ToInt() ?? 0;
        if (w <= 0 || h <= 0) throw new InvalidDataException("TIFF の大きさを読めませんでした");
        int orientation = tif.GetField(TiffTag.ORIENTATION)?[0].ToInt() ?? 1;
        if (orientation is < 1 or > 8) orientation = 1;

        // ファイルの向き(5〜8 は LibTiff では 1〜4 と同じ扱い)をそのまま要求すると、並べ替えずに読み出す
        var asStored = (Orientation)(orientation > 4 ? orientation - 4 : orientation);
        var raster = new int[checked(w * h)];
        if (!tif.ReadRGBAImageOriented(w, h, raster, asStored))
            throw new InvalidDataException("TIFF を読めませんでした(対応していない形式です)");

        // raster の 1 画素は、メモリ上で R・G・B・A の順(アルファは乗算済み)
        var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
        Marshal.Copy(raster, 0, bmp.GetPixels(), raster.Length);
        return ApplyOrientation(bmp, orientation);
    }

    /// <summary>長辺が maxSide を超えていたら縮小した画像を返し、元の画像は捨てる(超えていなければ、元の画像をそのまま返す)。</summary>
    static SKBitmap Shrink(SKBitmap src, int? maxSide)
    {
        if (maxSide is not int m || Math.Max(src.Width, src.Height) <= m) return src;
        double s = (double)m / Math.Max(src.Width, src.Height);
        var small = new SKImageInfo(Math.Max(1, (int)Math.Round(src.Width * s)), Math.Max(1, (int)Math.Round(src.Height * s)),
            SKColorType.Bgra8888, SKAlphaType.Premul);
        var resized = src.Resize(small, SKFilterQuality.Medium);
        src.Dispose();
        return resized ?? throw new InvalidDataException("縮小できませんでした");
    }

    static SKBitmap Decode(string path, int? maxSide)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var codec = SKCodec.Create(fs) ?? throw new NotSupportedException("この形式の画像は読めません");

        var full = codec.Info;
        var size = new SKSizeI(full.Width, full.Height);
        if (maxSide is int max && Math.Max(full.Width, full.Height) > max)
        {
            // JPEG などは、縮小しながら読める(時間とメモリの節約)。読める大きさは形式ごとに決まっている
            float scale = (float)max / Math.Max(full.Width, full.Height);
            size = codec.GetScaledDimensions(scale);
        }

        var info = new SKImageInfo(size.Width, size.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var bmp = new SKBitmap(info);
        var result = codec.GetPixels(info, bmp.GetPixels());
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
        {
            bmp.Dispose();
            throw new InvalidDataException($"画像を読めませんでした({result})");
        }

        if (maxSide is int m && Math.Max(bmp.Width, bmp.Height) > m)
        {
            double s = (double)m / Math.Max(bmp.Width, bmp.Height);
            var small = new SKImageInfo(Math.Max(1, (int)Math.Round(bmp.Width * s)), Math.Max(1, (int)Math.Round(bmp.Height * s)),
                SKColorType.Bgra8888, SKAlphaType.Premul);
            var resized = bmp.Resize(small, SKFilterQuality.Medium);
            bmp.Dispose();
            bmp = resized ?? throw new InvalidDataException("縮小できませんでした");
        }

        return ApplyOrientation(bmp, ReadOrientation(path));
    }

    /// <summary>Orientation(1〜8)。JPEG・WebP 以外や、読めないときは 1。</summary>
    static int ReadOrientation(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is not (".jpg" or ".jpeg" or ".webp")) return 1;
        return MetadataReader.ReadExif(path)?.Orientation ?? 1;
    }

    /// <summary>Orientation に従って回転・反転した画像を返す(1 ならそのまま。元の画像は捨てる)。</summary>
    internal static SKBitmap ApplyOrientation(SKBitmap src, int orientation)
    {
        if (orientation is < 2 or > 8) return src;

        bool swap = orientation >= 5; // 90° か 270° 回すと、縦と横が入れ替わる
        int w = swap ? src.Height : src.Width;
        int h = swap ? src.Width : src.Height;
        var dst = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));

        using (var canvas = new SKCanvas(dst))
        {
            // 元の画像の点 (x, y) を、正しい向きの位置へ移す変換
            canvas.SetMatrix(orientation switch
            {
                2 => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),   // 左右反転
                3 => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),  // 180°
                4 => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),   // 上下反転
                5 => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),    // 左右反転 + 270°(転置)
                6 => new SKMatrix(0, -1, w, 1, 0, 0, 0, 0, 1),   // 時計回りに 90°
                7 => new SKMatrix(0, -1, w, -1, 0, h, 0, 0, 1),  // 左右反転 + 90°
                _ => new SKMatrix(0, 1, 0, -1, 0, h, 0, 0, 1),   // 8: 時計回りに 270°
            });
            canvas.DrawBitmap(src, 0, 0);
        }
        src.Dispose();
        return dst;
    }

    static Bitmap ToAvalonia(SKBitmap bmp)
    {
        var format = bmp.ColorType switch
        {
            SKColorType.Bgra8888 => PixelFormat.Bgra8888,
            SKColorType.Rgba8888 => PixelFormat.Rgba8888,
            _ => throw new InvalidDataException($"画素の形式に対応していません({bmp.ColorType})"),
        };
        var alpha = bmp.AlphaType == SKAlphaType.Unpremul ? AlphaFormat.Unpremul : AlphaFormat.Premul;
        return new Bitmap(format, alpha, bmp.GetPixels(), new PixelSize(bmp.Width, bmp.Height), new Vector(96, 96), bmp.RowBytes);
    }
}
