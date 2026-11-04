using PromptLoupe.Metadata;

namespace PromptLoupe.Tests;

public class ExifInfoTests
{
    static string? Row(ExifInfo x, string key, bool showGps = false) =>
        x.Rows(showGps).FirstOrDefault(r => r.Key == key).Value;

    [Fact]
    public void Rows_Formatting()
    {
        var x = new ExifInfo
        {
            Make = "Canon", Model = "Canon EOS R5",
            LensMake = "Canon", LensModel = "RF50mm",
            FNumber = 1.8, ExposureTime = 1.0 / 250, Iso = 100,
            FocalLength = 50, FocalLength35 = 50, ExposureBias = 0.7,
            ExposureProgram = 3, Flash = 16, WhiteBalance = 0,
            DateTaken = new DateTime(2026, 1, 2, 3, 4, 5),
        };
        Assert.Equal("Canon EOS R5", Row(x, "カメラ"));      // 機種名がメーカー名で始まるときは重ねない
        Assert.Equal("Canon RF50mm", Row(x, "レンズ"));
        Assert.Equal("F1.8", Row(x, "絞り"));
        Assert.Equal("1/250 秒", Row(x, "シャッター速度"));
        Assert.Equal("50 mm", Row(x, "焦点距離"));           // 換算値が同じなら書かない
        Assert.Equal("+0.7 EV", Row(x, "露出補正"));
        Assert.Equal("絞り優先", Row(x, "撮影モード"));
        Assert.Equal("発光なし", Row(x, "フラッシュ"));
        Assert.Equal("オート", Row(x, "ホワイトバランス"));
        Assert.Equal("2026/01/02 03:04:05", Row(x, "撮影日時"));
        Assert.Null(Row(x, "位置情報"));
    }

    [Fact]
    public void Rows_LongExposureAndCrop()
    {
        var x = new ExifInfo { ExposureTime = 2.5, FocalLength = 4.3, FocalLength35 = 26 };
        Assert.Equal("2.5 秒", Row(x, "シャッター速度"));
        Assert.Equal("4.3 mm(35mm 換算 26 mm)", Row(x, "焦点距離"));
    }

    [Fact]
    public void Rows_GpsHiddenByDefault()
    {
        var x = new ExifInfo { Latitude = 35.681236, Longitude = 139.767125, Altitude = 40 };
        Assert.Equal("記録あり(「設定」で表示できます)", Row(x, "位置情報", showGps: false));
        Assert.Equal("35.681236, 139.767125(標高 40 m)", Row(x, "位置情報", showGps: true));
    }

    [Fact]
    public void Rows_Empty() => Assert.Empty(new ExifInfo().Rows(true));
}
