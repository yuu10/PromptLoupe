using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using PromptLoupe.Metadata;
using SkiaSharp;

namespace PromptLoupe;

/// <summary>
/// 画像の読み込み(SkiaSharp)。WPF 版の WIC の代わり。
/// - 対応: PNG・JPEG・WebP・BMP・GIF(先頭フレーム)。TIFF・HEIC は読めない(<see cref="UnsupportedHint"/>)
/// - JPEG・WebP は EXIF の回転情報(Orientation)に従って向きを直す。値は WPF 版と同じく MetadataReader.ReadExif で読む
/// </summary>
public static class ImageLoader
{
    /// <summary>画像を読み込む。maxSide を指定すると、長辺がその大きさになるように縮小する(サムネイル用)。</summary>
    public static Bitmap Load(string path, int? maxSide = null)
    {
        using var bmp = Decode(path, maxSide);
        return ToAvalonia(bmp);
    }

    /// <summary>表示できない形式なら、その案内を返す(表示できる形式なら空)。</summary>
    public static string UnsupportedHint(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".heic" or ".heif" => "(HEIC はこの版ではまだ表示できません。メタデータは読めます)",
        ".tif" or ".tiff" => "(TIFF はこの版ではまだ表示できません)",
        _ => "",
    };

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

    static Bitmap ToAvalonia(SKBitmap bmp) =>
        new(PixelFormat.Bgra8888, AlphaFormat.Premul, bmp.GetPixels(),
            new PixelSize(bmp.Width, bmp.Height), new Vector(96, 96), bmp.RowBytes);
}
