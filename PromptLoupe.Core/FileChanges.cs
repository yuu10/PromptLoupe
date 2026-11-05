using System;
using System.Collections.Generic;
using System.IO;

namespace PromptLoupe;

/// <summary>
/// ほかのアプリによるファイルの移動・削除・名前変更(FileSystemWatcher の通知)を、一覧の項目の変更に読み替える。
/// 画面(WPF・Avalonia)はフォルダを見張って通知をためておき、少し待ってからここで読み替えて、一覧に反映する。
/// </summary>
public static class FileChanges
{
    /// <summary>通知の1件。NewPath が null なら削除(またはフォルダの外への移動)、そうでなければ名前の変更。</summary>
    public readonly record struct Change(string OldPath, string? NewPath);

    /// <summary>一覧の項目への変更。NewPath が null なら一覧から外す、そうでなければその名前に差し替える。</summary>
    public readonly record struct Result(string Path, string? NewPath);

    /// <summary>
    /// 通知を、起きた順に当てはめて、変わった項目だけを返す。
    /// フォルダの削除・名前変更は、その中の項目すべてに当てはめる。
    /// 最後に、実際のファイルを確かめる(消えたはずのファイルが残っていれば、そのまま。
    /// 保存のときに「元のファイルを消して、一時ファイルの名前を元に戻す」アプリがあるため)。
    /// </summary>
    /// <param name="exists">ファイルがあるか(テストでは差し替える)</param>
    /// <param name="isImage">一覧に載せる画像か(名前の変更で拡張子が変わったときに使う)</param>
    public static List<Result> Map(IEnumerable<string> itemPaths, IReadOnlyList<Change> changes,
        Func<string, bool> exists, Func<string, bool> isImage, StringComparison comparison)
    {
        var results = new List<Result>();
        if (changes.Count == 0) return results;

        foreach (var original in itemPaths)
        {
            string? p = original;
            bool touched = false;
            foreach (var c in changes)
            {
                if (p == null) break;
                if (!Under(p, c.OldPath, comparison, out string rest)) continue;
                touched = true;
                p = c.NewPath == null ? null : rest.Length == 0 ? c.NewPath : Path.Combine(c.NewPath, rest);
            }
            if (!touched) continue;

            if (p != null && !string.Equals(p, original, comparison) && isImage(p) && exists(p))
                results.Add(new Result(original, p));
            else if (!exists(original))
                results.Add(new Result(original, null));
            // それ以外(元のファイルが残っている)は、何もしない
        }
        return results;
    }

    /// <summary>path が dir そのものか、その中にあるか。中にあるときは、dir からの相対パスを rest に入れる。</summary>
    static bool Under(string path, string dir, StringComparison comparison, out string rest)
    {
        rest = "";
        if (string.Equals(path, dir, comparison)) return true;
        string d = dir.TrimEnd('\\', '/');
        if (path.Length > d.Length + 1 && path.StartsWith(d, comparison) && path[d.Length] is '\\' or '/')
        {
            rest = path[(d.Length + 1)..];
            return true;
        }
        return false;
    }
}
