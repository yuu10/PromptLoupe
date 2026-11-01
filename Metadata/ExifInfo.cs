using System;
using System.Collections.Generic;
using System.Globalization;

namespace PromptLoupe.Metadata;

/// <summary>EXIF の撮影情報(カメラ・露出・撮影日時・GPS など)。</summary>
public sealed class ExifInfo
{
    public string? Make { get; set; }
    public string? Model { get; set; }
    public string? LensMake { get; set; }
    public string? LensModel { get; set; }

    /// <summary>回転情報(1〜8)。無ければ null。</summary>
    public int? Orientation { get; set; }

    /// <summary>撮影日時(DateTimeOriginal。無ければ DateTime)。</summary>
    public DateTime? DateTaken { get; set; }

    public double? ExposureTime { get; set; }
    public double? FNumber { get; set; }
    public int? Iso { get; set; }
    public double? FocalLength { get; set; }
    public int? FocalLength35 { get; set; }
    public double? ExposureBias { get; set; }
    public int? ExposureProgram { get; set; }
    public int? Flash { get; set; }
    public int? WhiteBalance { get; set; }

    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? Altitude { get; set; }

    public bool HasGps => Latitude != null && Longitude != null;

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>表示用の行。showGps が false のときは、位置情報の有無だけを出す。</summary>
    public List<KeyValuePair<string, string>> Rows(bool showGps)
    {
        var rows = new List<KeyValuePair<string, string>>();
        void Add(string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) rows.Add(new KeyValuePair<string, string>(key, value));
        }

        Add("カメラ", JoinMaker(Make, Model));
        Add("レンズ", JoinMaker(LensMake, LensModel));
        Add("撮影日時", DateTaken?.ToString("yyyy/MM/dd HH:mm:ss", Inv));
        Add("絞り", FNumber is > 0 ? "F" + FNumber.Value.ToString("0.#", Inv) : null);
        Add("シャッター速度", FormatExposure(ExposureTime));
        Add("ISO 感度", Iso?.ToString(Inv));
        Add("焦点距離", FormatFocal());
        Add("露出補正", ExposureBias == null ? null
            : (ExposureBias.Value > 0 ? "+" : "") + ExposureBias.Value.ToString("0.#", Inv) + " EV");
        Add("撮影モード", ExposureProgram switch
        {
            1 => "マニュアル",
            2 => "プログラム",
            3 => "絞り優先",
            4 => "シャッター優先",
            5 => "クリエイティブ",
            6 => "アクション",
            7 => "ポートレート",
            8 => "風景",
            _ => null,
        });
        Add("フラッシュ", Flash == null ? null : (Flash.Value & 1) == 1 ? "発光" : "発光なし");
        Add("ホワイトバランス", WhiteBalance switch { 0 => "オート", 1 => "マニュアル", _ => null });

        if (HasGps)
        {
            if (showGps)
            {
                var pos = Latitude!.Value.ToString("0.000000", Inv) + ", " + Longitude!.Value.ToString("0.000000", Inv);
                if (Altitude != null) pos += $"(標高 {Altitude.Value.ToString("0", Inv)} m)";
                Add("位置情報", pos);
            }
            else
            {
                Add("位置情報", "記録あり(「設定」で表示できます)");
            }
        }
        return rows;
    }

    /// <summary>メーカー名と機種名をつなぐ(機種名がメーカー名で始まるときは重ねない)。</summary>
    static string? JoinMaker(string? make, string? model)
    {
        make = make?.Trim();
        model = model?.Trim();
        if (string.IsNullOrEmpty(model)) return string.IsNullOrEmpty(make) ? null : make;
        if (string.IsNullOrEmpty(make) || model.StartsWith(make, StringComparison.OrdinalIgnoreCase)) return model;
        return make + " " + model;
    }

    static string? FormatExposure(double? t)
    {
        if (t is not > 0) return null;
        if (t.Value >= 0.3) return t.Value.ToString("0.#", Inv) + " 秒";
        return "1/" + Math.Round(1 / t.Value).ToString(Inv) + " 秒";
    }

    string? FormatFocal()
    {
        if (FocalLength is not > 0) return null;
        var s = FocalLength.Value.ToString("0.#", Inv) + " mm";
        if (FocalLength35 is > 0 && Math.Abs(FocalLength35.Value - FocalLength.Value) >= 1)
            s += $"(35mm 換算 {FocalLength35.Value} mm)";
        return s;
    }
}
