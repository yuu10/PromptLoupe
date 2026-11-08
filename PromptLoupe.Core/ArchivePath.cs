using System;
using System.IO;

namespace PromptLoupe;

/// <summary>
/// 書庫(zip・rar・7z)の中の画像を指すパス。「書庫のパス|書庫の中の名前」の形にする(例: C:\a\b.zip|人物/001.png)。
/// 「|」は Windows のファイル名に使えない文字なので、ふつうのパスと区別できる(Mac・Linux では使えるが、
/// 「|」の前が書庫のファイルとして実在するときだけ書庫の中とみなす)。書庫の中の名前の区切りは「/」にそろえる。
/// </summary>
public static class ArchivePath
{
    public const char Separator = '|';

    /// <summary>対応する書庫の拡張子(コミック用の cbz・cbr・cb7 も、中身は zip・rar・7z)。</summary>
    static readonly string[] Extensions = { ".zip", ".cbz", ".rar", ".cbr", ".7z", ".cb7" };

    /// <summary>ファイル名の拡張子が、対応する書庫か(ファイルがあるかは見ない)。</summary>
    public static bool HasArchiveExtension(string path)
    {
        string ext = Path.GetExtension(path);
        foreach (var e in Extensions)
            if (string.Equals(ext, e, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>実在する書庫のファイルか。</summary>
    public static bool IsArchiveFile(string path) => HasArchiveExtension(path) && File.Exists(path);

    /// <summary>書庫の中の画像のパスを作る。</summary>
    public static string Combine(string archive, string entry) => archive + Separator + NormalizeEntry(entry);

    /// <summary>書庫の中の名前の区切りを「/」にそろえ、先頭の「/」を外す。</summary>
    public static string NormalizeEntry(string entry) => entry.Replace('\\', '/').TrimStart('/');

    /// <summary>
    /// 書庫の中の画像のパスなら、書庫のパスと中の名前に分ける。
    /// mustExist なら、書庫のファイルが実在するときだけ true(Mac・Linux で「|」を含むふつうのファイル名と区別するため)。
    /// </summary>
    public static bool TrySplit(string path, out string archive, out string entry, bool mustExist = true)
    {
        archive = entry = "";
        int i = path.IndexOf(Separator);
        while (i > 0)
        {
            string a = path[..i];
            if (HasArchiveExtension(a) && (!mustExist || File.Exists(a)))
            {
                archive = a;
                entry = path[(i + 1)..];
                return entry.Length > 0;
            }
            i = path.IndexOf(Separator, i + 1);
        }
        return false;
    }

    /// <summary>書庫の中の画像のパスか(書庫が実在するときだけ)。</summary>
    public static bool IsEntry(string path) => TrySplit(path, out _, out _);
}
