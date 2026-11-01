using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace PromptLoupe;

/// <summary>チップの色(文字・背景・マウスを乗せたときの背景)と、分類の名前。</summary>
readonly record struct ChipStyle(Color Fg, Color Bg, Color BgHover, string Label);

// タグの色分け。Danbooru のタグ辞書(danbooru.csv)で、一般 / アーティスト / 作品名 / キャラクター名 / メタ に分け、
// 辞書に関係なく <lora:…> などは別の色にする。辞書は利用者が用意して読み込む(アプリには同梱しない)。
public partial class MainWindow
{
    TagDictionary? tagDict;

    // 分類(Danbooru の番号) → (ダークテーマの文字色, ライトテーマの文字色, 名前)
    static readonly Dictionary<int, (string Dark, string Light, string Label)> TagPalette = new()
    {
        [0] = ("#6CB4FF", "#0A64C8", "一般"),
        [1] = ("#FF8A8B", "#B3262A", "アーティスト"),
        [3] = ("#C797FF", "#8A2FC9", "作品名"),
        [4] = ("#4FD36B", "#0E8A2E", "キャラクター名"),
        [5] = ("#EAB35A", "#A65F00", "メタ"),
        [10] = ("#4FD1C5", "#0B7A75", "LoRA など"),
    };

    /// <summary>分類の並び順(一般 → アーティスト → … → LoRA など)。分類のないタグは最後。</summary>
    internal static int KindOrder(string label)
    {
        int i = 0;
        foreach (var pal in TagPalette.Values)
        {
            if (pal.Label == label) return i;
            i++;
        }
        return int.MaxValue;
    }

    static bool IsDarkTheme() =>
        ModernWpf.ThemeManager.Current.ActualApplicationTheme == ModernWpf.ApplicationTheme.Dark;

    /// <summary>タグの見た目を決める。色分けしないタグ(または色分けがオフ)のときは null。</summary>
    internal ChipStyle? ClassifyTag(string tag)
    {
        if (!settings.ColorTags) return null;

        int kind = -1;
        string t = tag.Trim();

        if (t.StartsWith('<'))
        {
            kind = 10; // <lora:…> <lyco:…> <hypernet:…> など
        }
        else
        {
            string clean = CleanTag(t);
            bool artistPrefix = false;
            if (clean.StartsWith("artist:", StringComparison.OrdinalIgnoreCase))
            {
                clean = clean[7..].Trim();
                artistPrefix = true;
            }

            if (tagDict != null) kind = tagDict.Lookup(FileItem.Normalize(clean).Trim());
            if (kind < 0 && artistPrefix) kind = 1;
        }

        if (!TagPalette.TryGetValue(kind, out var pal)) return null;

        bool dark = IsDarkTheme();
        var fg = (Color)ColorConverter.ConvertFromString(dark ? pal.Dark : pal.Light);
        return new ChipStyle(
            fg,
            Color.FromArgb(dark ? (byte)0x33 : (byte)0x22, fg.R, fg.G, fg.B),
            Color.FromArgb(dark ? (byte)0x5C : (byte)0x40, fg.R, fg.G, fg.B),
            pal.Label);
    }

    /// <summary>表示中の画像のメタデータを、いまの設定(色分け・テーマ・辞書)で描き直す。</summary>
    void RefreshMetaView()
    {
        if (currentItem != null) ShowMeta(currentItem, currentItem.Meta);
        NotifyTagStats(); // 集計ウィンドウのタグの色も描き直す
    }

    // ───────────────────── 辞書の読み込み ─────────────────────

    void UpdateTagDictStatus() =>
        TagDictStatus.Header = tagDict == null ? "辞書: 未読み込み" : $"辞書: {tagDict.Count:N0} 語を読み込み済み";

    /// <summary>起動時に、保存済みの辞書があれば読み込む(バックグラウンド)。</summary>
    void LoadSavedTagDictionary()
    {
        string path = TagDictionary.FilePath;
        if (!File.Exists(path)) return;

        _ = Task.Run(() => TagDictionary.Load(path)).ContinueWith(t =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (t.IsCompletedSuccessfully && t.Result.Dictionary != null)
                {
                    tagDict = t.Result.Dictionary;
                    UpdateTagDictStatus();
                    RefreshMetaView();
                }
            });
        });
    }

    void TagColor_Click(object sender, RoutedEventArgs e)
    {
        settings.ColorTags = TagColorMenu.IsChecked;
        settings.Save();
        RefreshMetaView();
    }

    async void LoadTagDict_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "タグ辞書(danbooru.csv)を選択",
            Filter = "CSV ファイル|*.csv|すべてのファイル|*.*"
        };
        if (dlg.ShowDialog(this) != true) return;

        string src = dlg.FileName;
        Status.Text = "タグ辞書を読み込み中…";
        var (dict, error) = await Task.Run(() => TagDictionary.Load(src));

        if (dict == null)
        {
            Status.Text = "タグ辞書を読み込めませんでした";
            MessageBox.Show(this, "タグ辞書を読み込めませんでした。\n\n" + error, "タグ辞書",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // 次回からも使えるよう、アプリの保存先にコピーしておく
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(TagDictionary.FilePath)!);
            if (!string.Equals(Path.GetFullPath(src), Path.GetFullPath(TagDictionary.FilePath), StringComparison.OrdinalIgnoreCase))
                File.Copy(src, TagDictionary.FilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "辞書は読み込みましたが、次回のための保存に失敗しました(今回だけ有効です)。\n\n" + ex.Message,
                "タグ辞書", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        tagDict = dict;
        UpdateTagDictStatus();
        if (!settings.ColorTags)
        {
            settings.ColorTags = true;
            TagColorMenu.IsChecked = true;
            settings.Save();
        }
        RefreshMetaView();
        Status.Text = $"タグ辞書を読み込みました({dict.Count:N0} 語)";
    }

    void RemoveTagDict_Click(object sender, RoutedEventArgs e)
    {
        tagDict = null;
        try { File.Delete(TagDictionary.FilePath); } catch { /* 無視 */ }
        UpdateTagDictStatus();
        RefreshMetaView();
        Status.Text = "タグ辞書を外しました";
    }

    // ───────────────────── ファイル操作の設定 ─────────────────────

    void AllowDelete_Click(object sender, RoutedEventArgs e)
    {
        settings.AllowDelete = AllowDeleteMenu.IsChecked;
        settings.Save();
        Status.Text = settings.AllowDelete ? "ファイルの削除を許可しました" : "ファイルの削除を禁止しました";
    }

    void ConfirmDelete_Click(object sender, RoutedEventArgs e)
    {
        settings.ConfirmDelete = ConfirmDeleteMenu.IsChecked;
        settings.Save();
    }
}
