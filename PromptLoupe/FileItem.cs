using System.ComponentModel;
using Avalonia.Media.Imaging;
using PromptLoupe.Metadata;

namespace PromptLoupe;

/// <summary>一覧の項目。WPF 版の FileItem と同じ内容で、サムネイルの型だけが違う。</summary>
public sealed class FileItem : INotifyPropertyChanged
{
    ImageMetadata? meta;

    public FileItem(string path, string rootFolder)
    {
        Path = path;
        Name = System.IO.Path.GetFileName(path);
        Display = System.IO.Path.GetRelativePath(rootFolder, path);
        string? dir = System.IO.Path.GetDirectoryName(Display);
        FolderPart = string.IsNullOrEmpty(dir) ? "" : dir + System.IO.Path.DirectorySeparatorChar;
        var fi = new FileInfo(path);
        Modified = fi.LastWriteTime;
        Size = fi.Length;
    }

    /// <summary>書庫の中の画像(Path は「書庫のパス|中の名前」。<see cref="ArchivePath"/>)。</summary>
    public FileItem(string archive, ArchiveEntryInfo entry)
    {
        Path = ArchivePath.Combine(archive, entry.Key);
        Name = System.IO.Path.GetFileName(entry.Key);
        Display = entry.Key.Replace('/', System.IO.Path.DirectorySeparatorChar);
        string? dir = System.IO.Path.GetDirectoryName(Display);
        FolderPart = string.IsNullOrEmpty(dir) ? "" : dir + System.IO.Path.DirectorySeparatorChar;
        Modified = entry.Modified;
        Size = entry.Size;
        InArchive = true;
    }

    public string Path { get; }

    /// <summary>書庫の中の画像か(名前の変更・削除・ファイルのコピーなどはできない)。</summary>
    public bool InArchive { get; }
    public string Name { get; } = "";

    /// <summary>開いたフォルダからの相対パス(サブフォルダ読み込み時にフォルダ構成が分かるように)。</summary>
    public string Display { get; }

    /// <summary>Display のうちフォルダの部分(「人物/」など。開いたフォルダの直下の画像なら空)。一覧で色を変えて出す。</summary>
    public string FolderPart { get; }
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
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Summary)));
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

    /// <summary>サムネイル。画面に表示されて初めてバインドされたときに、バックグラウンドで生成を依頼する。</summary>
    public Bitmap? Thumbnail
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

/// <summary>並び替え用。自然順(NaturalComparer)。メタデータが無い項目は常に末尾。</summary>
public sealed class FileItemComparer : IComparer<FileItem>
{
    readonly string key;
    readonly bool desc;

    public FileItemComparer(string key, bool desc)
    {
        this.key = key;
        this.desc = desc;
    }

    static int Natural(FileItem a, FileItem b) => NaturalComparer.Instance.Compare(a.Display, b.Display);

    static IComparable? KeyOf(FileItem f, string key) => key switch
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

    public int Compare(FileItem? a, FileItem? b)
    {
        if (a == null || b == null) return 0;

        if (key == "Name")
        {
            int n = Natural(a, b);
            return desc ? -n : n;
        }

        var ka = KeyOf(a, key);
        var kb = KeyOf(b, key);
        if (ka == null && kb == null) return Natural(a, b);
        if (ka == null) return 1;
        if (kb == null) return -1;

        int r = ka is string sa
            ? string.Compare(sa, (string)kb, StringComparison.OrdinalIgnoreCase)
            : ka.CompareTo(kb);
        if (desc) r = -r;
        return r != 0 ? r : Natural(a, b);
    }
}
