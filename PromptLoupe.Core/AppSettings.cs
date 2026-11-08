using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace PromptLoupe;

/// <summary>ユーザー設定。%AppData%\PromptLoupe\settings.json に保存される。</summary>
public sealed class AppSettings
{
    /// <summary>サブフォルダを読み込む深さ。0 = 含めない、1〜3 = その階層まで、-1 = 無制限。</summary>
    public int SubfolderDepth { get; set; } = 0;

    /// <summary>画像を切り替えたときの表示方式。false = フィット、true = 等倍(100%)。</summary>
    public bool ActualSizeMode { get; set; } = false;

    /// <summary>テーマ。"System" = Windows の設定に合わせる、"Dark"、"Light"。</summary>
    public string Theme { get; set; } = "System";

    /// <summary>一覧のサムネイルの大きさ(ピクセル)。48 / 64 / 96。</summary>
    public int ThumbnailSize { get; set; } = 64;

    /// <summary>最小化している右パネルのカード("Prompt" / "Negative" / "Characters" / "Params" / "Exif")。</summary>
    public List<string> CollapsedSections { get; set; } = new();

    /// <summary>ファイルの削除を許可する(既定はOFF。OFFのあいだは削除できない)。</summary>
    public bool AllowDelete { get; set; } = false;

    /// <summary>削除の前に確認のメッセージを出す(既定はON)。</summary>
    public bool ConfirmDelete { get; set; } = true;

    /// <summary>プロンプトのタグを、タグ辞書(Danbooru)と形で色分けする。</summary>
    public bool ColorTags { get; set; } = true;

    /// <summary>撮影情報(Exif)に GPS の位置情報を表示する(既定はOFF。個人情報なので、オフのときは「記録あり」とだけ出す)。</summary>
    public bool ShowGps { get; set; } = false;

    /// <summary>ファイル一覧にサムネイルを表示する。</summary>
    public bool ShowThumbnails { get; set; } = true;

    /// <summary>右のパネル(プロンプト・パラメーターなどのカード)を表示する。</summary>
    public bool ShowInfoPanel { get; set; } = true;

    /// <summary>絞り込み欄の上に、開いているフォルダのパス(パンくず)を表示する。</summary>
    public bool ShowPathBar { get; set; } = true;

    /// <summary>左パネルのフォルダツリーを最小化している。</summary>
    public bool FolderTreeCollapsed { get; set; } = false;

    /// <summary>左パネルのフォルダツリーの高さ(ピクセル。一覧との境目をドラッグで変えられる)。</summary>
    public double FolderTreeHeight { get; set; } = 250;

    // 絞り込みの検索対象(既定はすべてON)
    public bool FilterFileName { get; set; } = true;
    public bool FilterPrompt { get; set; } = true;
    public bool FilterNegative { get; set; } = true;
    public bool FilterCharacters { get; set; } = true;
    public bool FilterCharacterNegative { get; set; } = true;
    public bool FilterModel { get; set; } = true;

    // タグの集計
    /// <summary>集計するプロンプト。"Prompt" / "Negative" / "Characters"。</summary>
    public string TagStatsSource { get; set; } = "Prompt";

    /// <summary>true = 絞り込み後に一覧に出ている画像だけを集計、false = フォルダのすべての画像。</summary>
    public bool TagStatsFilteredOnly { get; set; } = true;

    /// <summary>並び替えの列。"Mark" / "Tag" / "Kind" / "Count" / "Percent"。</summary>
    public string TagStatsSortKey { get; set; } = "Count";

    /// <summary>true = 降順、false = 昇順。</summary>
    public bool TagStatsSortDescending { get; set; } = true;

    /// <summary>設定や辞書の保存先(%AppData%\PromptLoupe)。</summary>
    public static string DataDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PromptLoupe");

    static string FilePath => Path.Combine(DataDir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch
        {
            // 壊れていたら既定値で起動
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // 保存失敗は無視(次回は既定値)
        }
    }
}
