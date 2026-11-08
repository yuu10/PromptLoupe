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

    /// <summary>
    /// 実在する書庫のファイルか。拡張子が書庫のものなら書庫とみなす。
    /// 拡張子がないか、拡張子に英数字以外が入っているとき(文字化けで「.zip」の「.」が壊れたファイルなど)は、
    /// ファイルの先頭の数バイト(zip・rar・7z の印)を見て判断する。
    /// </summary>
    public static bool IsArchiveFile(string path)
    {
        if (HasArchiveExtension(path)) return File.Exists(path);
        return MaySniff(path) && HasArchiveSignature(path);
    }

    /// <summary>中身を見て判断する対象か(拡張子がない、または拡張子が英数字だけでない)。</summary>
    static bool MaySniff(string path)
    {
        string ext = Path.GetExtension(path);
        if (ext.Length <= 1) return true;
        for (int i = 1; i < ext.Length; i++)
            if (!char.IsAsciiLetterOrDigit(ext[i])) return true;
        return false;
    }

    /// <summary>ファイルの先頭が zip・rar・7z の印か。読めなければ false。</summary>
    public static bool HasArchiveSignature(string path)
    {
        try
        {
            Span<byte> b = stackalloc byte[8];
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1);
            int n = fs.ReadAtLeast(b, b.Length, throwOnEndOfStream: false);
            return IsArchiveSignature(b[..n]);
        }
        catch { return false; }
    }

    /// <summary>先頭のバイト列が zip(PK\x03\x04・空の zip は PK\x05\x06)・rar(Rar!\x1A\x07)・7z(7z\xBC\xAF\x27\x1C)の印か。</summary>
    public static bool IsArchiveSignature(ReadOnlySpan<byte> b) =>
        (b.Length >= 4 && b[0] == 'P' && b[1] == 'K' && ((b[2] == 3 && b[3] == 4) || (b[2] == 5 && b[3] == 6)))
        || (b.Length >= 6 && b[..6].SequenceEqual("Rar!\x1A\x07"u8))
        || (b.Length >= 6 && b[..6].SequenceEqual(new byte[] { (byte)'7', (byte)'z', 0xBC, 0xAF, 0x27, 0x1C }));

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
            if (mustExist ? IsArchiveFile(a) : HasArchiveExtension(a))
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
