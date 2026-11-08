using System;
using System.Collections.Generic;
using System.IO;

namespace PromptLoupe;

/// <summary>フォルダのパスの扱い(パスの欄のパンくずと、フォルダツリーで使う)。</summary>
public static class FolderPath
{
    /// <summary>パスの大文字・小文字の扱い(Windows・Mac は区別しない、Linux は区別する)。</summary>
    public static StringComparison Comparison =>
        OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    /// <summary>絶対パスにして、末尾の区切り文字を取る(ルートの「C:\」「/」はそのまま)。</summary>
    public static string Normalize(string path)
    {
        string full = Path.GetFullPath(path);
        string root = Path.GetPathRoot(full) ?? "";
        if (full.Length > root.Length)
            full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return full;
    }

    /// <summary>ルートの表示名(「C:\」→「C:」、「/」→「/」、「\\server\share\」→「\\server\share」)。</summary>
    public static string RootName(string root)
    {
        if (root.Length <= 1) return root;
        return root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    /// <summary>
    /// パンくず用に、ルートから順に (表示名, そのフォルダのパス) を返す。
    /// 例: /home/yuto/Pictures → [("/", "/"), ("home", "/home"), ("yuto", "/home/yuto"), ("Pictures", "/home/yuto/Pictures")]
    /// </summary>
    public static List<(string Name, string Path)> Segments(string path)
    {
        var list = new List<(string, string)>();
        string? dir = Normalize(path);
        while (dir != null)
        {
            string? parent = Path.GetDirectoryName(dir);
            list.Add((parent == null ? RootName(dir) : Path.GetFileName(dir), dir));
            dir = parent;
        }
        list.Reverse();
        return list;
    }

    /// <summary>親フォルダ(ルートなら null)。</summary>
    public static string? Parent(string path) => Path.GetDirectoryName(Normalize(path));

    /// <summary>同じフォルダか。</summary>
    public static bool Equal(string a, string b, StringComparison comparison) =>
        string.Equals(Normalize(a), Normalize(b), comparison);

    /// <summary>
    /// path が baseDir と同じか、その中にあれば、baseDir から見たフォルダ名の並びを返す(同じなら空)。外なら null。
    /// 例: (/home/yuto, /home/yuto/Pictures/AI) → ["Pictures", "AI"]
    /// </summary>
    public static string[]? RelativeParts(string baseDir, string path, StringComparison comparison)
    {
        string b = Normalize(baseDir), p = Normalize(path);
        if (string.Equals(b, p, comparison)) return Array.Empty<string>();

        string prefix = b.EndsWith(Path.DirectorySeparatorChar) || b.EndsWith(Path.AltDirectorySeparatorChar)
            ? b : b + Path.DirectorySeparatorChar;
        if (!p.StartsWith(prefix, comparison)) return null;
        return p[prefix.Length..].Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// パスの欄に手で入力した文字を、パスにする。前後の空白と引用符(Windows の「パスのコピー」で付く)を外し、
    /// 環境変数(%USERPROFILE% など)と、先頭の ~(ホーム)を展開する。空なら null。
    /// </summary>
    public static string? FromInput(string? text, string home)
    {
        if (text == null) return null;
        string s = text.Trim();
        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"') s = s[1..^1].Trim();
        if (s.Length == 0) return null;

        s = Environment.ExpandEnvironmentVariables(s);
        if (s == "~") s = home;
        else if (s.StartsWith("~/") || s.StartsWith("~\\")) s = Path.Combine(home, s[2..]);
        return s;
    }
}
