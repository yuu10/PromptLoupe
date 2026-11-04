using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using PromptLoupe.Metadata;

namespace PromptLoupe;

public sealed class FileItem : INotifyPropertyChanged
{
    ImageMetadata? meta;

    public FileItem(string path, string rootFolder)
    {
        Path = path;
        Display = System.IO.Path.GetRelativePath(rootFolder, path);
        var fi = new FileInfo(path);
        Modified = fi.LastWriteTime;
        Size = fi.Length;
    }

    public string Path { get; }
    public string Name => System.IO.Path.GetFileName(Path);

    /// <summary>開いたフォルダからの相対パス(サブフォルダ読み込み時にフォルダ構成が分かるように)。</summary>
    public string Display { get; }
    public DateTime Modified { get; }
    public long Size { get; }

    public ImageMetadata? Meta
    {
        get => meta;
        set
        {
            meta = value;
            normPrompt = normNegative = normModel = normCharPrompts = normCharNegatives = null;
            promptTags = negativeTags = charTags = null;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        }
    }

    // ───────── 絞り込み用の正規化済み文字列(初回の検索時に1度だけ作って保持する) ─────────

    string? normName, normPrompt, normNegative, normModel, normCharPrompts, normCharNegatives;

    public string NormName => normName ??= PromptText.Normalize(Display);
    public string NormPrompt => normPrompt ??= PromptText.Normalize(Meta?.Prompt);
    public string NormNegative => normNegative ??= PromptText.Normalize(Meta?.NegativePrompt);
    public string NormModel => normModel ??= PromptText.Normalize(Meta?.Model);

    /// <summary>全キャラクターのプロンプトを改行でつないだもの(語がキャラクターをまたいで一致しないように)。</summary>
    public string NormCharPrompts => normCharPrompts ??=
        PromptText.Normalize(Meta == null ? null : string.Join("\n", Meta.Characters.Select(c => c.Prompt)));

    public string NormCharNegatives => normCharNegatives ??=
        PromptText.Normalize(Meta == null ? null : string.Join("\n", Meta.Characters.Select(c => c.Negative)));

    // ───────── タグの集計用(初回の集計時に1度だけ作って保持する) ─────────

    string[]? promptTags, negativeTags, charTags;

    public string[] PromptTags => promptTags ??= PromptText.TagKeys(Meta?.Prompt);
    public string[] NegativeTags => negativeTags ??= PromptText.TagKeys(Meta?.NegativePrompt);

    /// <summary>全キャラクターのプロンプトのタグ(キャラクターをまたいで重複を除く)。</summary>
    public string[] CharacterTags => charTags ??=
        PromptText.TagKeys(Meta == null ? null : string.Join("\n", Meta.Characters.Select(c => c.Prompt)));

    bool thumbPending;

    /// <summary>
    /// サムネイル。画面に表示されて初めてバインドされたときに、バックグラウンドで生成を依頼する。
    /// (ImageSource は ImageMetadata と名前が衝突しないよう完全修飾で書いている)
    /// </summary>
    public System.Windows.Media.ImageSource? Thumbnail
    {
        get
        {
            if (ThumbnailService.TryGet(Path, out var img)) return img;
            if (!thumbPending)
            {
                thumbPending = true;
                ThumbnailService.Request(Path,
                    done: () =>
                    {
                        thumbPending = false;
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Thumbnail)));
                    },
                    cancelled: () => thumbPending = false);
            }
            return null;
        }
    }

    /// <summary>撮影日時(EXIF)。無ければ更新日時で代用する。</summary>
    public DateTime DateTaken => Meta?.Exif?.DateTaken ?? Modified;

    public string? Model => Meta?.Model;
    public string? Sampler => Meta?.Sampler;
    public int? Steps => Meta?.Steps;
    public double? Cfg => Meta?.Cfg;

    public string Summary
    {
        get
        {
            if (Meta is not { HasData: true } m) return "";
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(m.Model)) parts.Add(m.Model!);
            if (m.Steps != null) parts.Add($"{m.Steps} steps");
            if (!string.IsNullOrEmpty(m.Sampler)) parts.Add(m.Sampler!);
            return string.Join(" ・ ", parts);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>並び替え用。エクスプローラー風の自然順ソートに対応。メタデータが無い項目は常に末尾。</summary>
public sealed class FileItemComparer : IComparer
{
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    static extern int StrCmpLogicalW(string a, string b);

    readonly string key;
    readonly bool desc;

    public FileItemComparer(string key, bool desc)
    {
        this.key = key;
        this.desc = desc;
    }

    static IComparable? KeyOf(FileItem f, string key)
    {
        return key switch
        {
            "Modified" => f.Modified,
            "DateTaken" => f.DateTaken,
            "Size" => f.Size,
            "Model" => string.IsNullOrEmpty(f.Model) ? null : f.Model,
            "Sampler" => string.IsNullOrEmpty(f.Sampler) ? null : f.Sampler,
            "Steps" => f.Steps,
            "Cfg" => f.Cfg,
            _ => null
        };
    }

    public int Compare(object? x, object? y)
    {
        var a = (FileItem)x!;
        var b = (FileItem)y!;

        if (key == "Name")
        {
            int n = StrCmpLogicalW(a.Display, b.Display);
            return desc ? -n : n;
        }

        var ka = KeyOf(a, key);
        var kb = KeyOf(b, key);
        if (ka == null && kb == null) return StrCmpLogicalW(a.Display, b.Display);
        if (ka == null) return 1;
        if (kb == null) return -1;

        int r = ka is string sa
            ? string.Compare(sa, (string)kb, StringComparison.OrdinalIgnoreCase)
            : ka.CompareTo(kb);
        if (desc) r = -r;
        return r != 0 ? r : StrCmpLogicalW(a.Display, b.Display);
    }
}
