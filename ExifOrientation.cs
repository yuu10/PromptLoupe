using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MViewer;

/// <summary>
/// JPEG の EXIF 回転情報(Orientation、タグ 274)を読んで、画像を正しい向きに直す。
/// </summary>
public static class ExifOrientation
{
    /// <summary>
    /// Orientation の値(1〜8)を返す。読めないときや JPEG 以外は 1(そのまま)。
    /// frame は、元のストリームが開いているうちに呼ぶこと。
    /// </summary>
    public static int Read(BitmapFrame frame)
    {
        try
        {
            if (frame.Metadata is BitmapMetadata md &&
                md.GetQuery("/app1/ifd/{ushort=274}") is ushort v &&
                v >= 1 && v <= 8)
                return v;
        }
        catch { /* メタデータの形式が違う・壊れている */ }
        return 1;
    }

    /// <summary>
    /// Orientation に従って回転・反転した画像を返す(1 ならそのまま)。
    /// </summary>
    public static BitmapSource Apply(BitmapSource src, int orientation)
    {
        // 正しい向きに戻すための、回転(時計回り)と左右反転
        (double angle, bool flip) = orientation switch
        {
            2 => (0, true),
            3 => (180, false),
            4 => (180, true),
            5 => (90, true),
            6 => (90, false),
            7 => (270, true),
            8 => (270, false),
            _ => (0, false),
        };
        if (angle == 0 && !flip) return src;

        var t = new TransformGroup();
        if (angle != 0) t.Children.Add(new RotateTransform(angle));
        if (flip) t.Children.Add(new ScaleTransform(-1, 1));

        var bmp = new TransformedBitmap(src, t);
        bmp.Freeze();
        return bmp;
    }
}
