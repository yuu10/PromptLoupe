using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PromptLoupe;

// 開いているフォルダの見張り。ほかのアプリで画像が移動・削除・名前変更されたら、一覧に反映する
// (切り取ってエクスプローラーに貼り付けたとき、エクスプローラーで消したり名前を変えたりしたとき)。
// 新しく増えた画像は一覧に加えない(フォルダを開き直すと出る)。読み替えは FileChanges(Core)が行う。
public partial class MainWindow
{
    FileSystemWatcher? watcher;
    readonly List<FileChanges.Change> pendingChanges = new();
    int changeSeq; // 通知のたびに増やす。待っている間に次の通知が来たら、前の待ちは何もしない

    /// <summary>フォルダの見張りを始める(前の見張りは止める)。サブフォルダを読み込む設定なら、その中も見張る。</summary>
    void StartWatcher(string folder, int depth)
    {
        StopWatcher();
        try
        {
            var w = new FileSystemWatcher(folder)
            {
                IncludeSubdirectories = depth != 0,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                InternalBufferSize = 64 * 1024, // まとめて動かしたときに、通知があふれにくくする
            };
            w.Deleted += (_, e) => Enqueue(new FileChanges.Change(e.FullPath, null));
            w.Renamed += (_, e) => Enqueue(new FileChanges.Change(e.OldFullPath, e.FullPath));
            w.EnableRaisingEvents = true;
            watcher = w;
        }
        catch
        {
            // 見張れないフォルダ(ネットワークの一部など)では、今までどおり見張らない
        }
    }

    void StopWatcher()
    {
        watcher?.Dispose();
        watcher = null;
        lock (pendingChanges) pendingChanges.Clear();
    }

    /// <summary>通知をためて、少し待ってからまとめて反映する(まとめて動かしたときに、1件ずつ反映しない)。</summary>
    void Enqueue(FileChanges.Change change)
    {
        lock (pendingChanges) pendingChanges.Add(change);
        int seq = Interlocked.Increment(ref changeSeq);
        _ = Task.Delay(300).ContinueWith(_ =>
        {
            if (seq == Volatile.Read(ref changeSeq)) Dispatcher.BeginInvoke(ApplyFileChanges);
        });
    }

    void ApplyFileChanges()
    {
        List<FileChanges.Change> changes;
        lock (pendingChanges)
        {
            changes = pendingChanges.ToList();
            pendingChanges.Clear();
        }
        if (currentFolder == null || changes.Count == 0) return;

        var results = FileChanges.Map(items.Select(f => f.Path).ToList(), changes,
            File.Exists, IsImage, StringComparison.OrdinalIgnoreCase);
        if (results.Count == 0) return;

        var byPath = items.ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase);
        var removed = new List<FileItem>();
        int renamed = 0;
        foreach (var r in results)
        {
            if (!byPath.TryGetValue(r.Path, out var item)) continue;
            if (r.NewPath == null)
            {
                removed.Add(item);
                continue;
            }

            // 名前が変わった項目を差し替える(メタデータは読み直さずに引き継ぐ)
            int idx = items.IndexOf(item);
            if (idx < 0) continue;
            var replacement = new FileItem(r.NewPath, currentFolder) { Meta = item.Meta };
            bool wasCurrent = currentItem == item;
            items[idx] = replacement;
            if (wasCurrent)
            {
                currentItem = null; // 選択し直したときに、表示を更新させる
                FileList.SelectedItem = replacement;
            }
            renamed++;
        }

        if (removed.Count > 0)
        {
            if (currentItem == null || removed.Contains(currentItem))
            {
                RemoveItems(removed); // 表示中の画像が消えたときは、近くの画像を選び直す
            }
            else
            {
                // 見ている画像はそのまま。消えた項目だけを、一覧から外す
                suppressSelection = true;
                try
                {
                    foreach (var f in removed) items.Remove(f);
                }
                finally
                {
                    suppressSelection = false;
                }
                UpdateCount();
                NotifyTagStats();
            }
        }

        var parts = new List<string>();
        if (removed.Count > 0) parts.Add($"{removed.Count} 枚が移動・削除されたので、一覧から外しました");
        if (renamed > 0) parts.Add($"{renamed} 枚の名前の変更を反映しました");
        if (parts.Count > 0) Status.Text = "ほかのアプリで、" + string.Join("。", parts);
    }
}
