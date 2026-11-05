using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace PromptLoupe;

// 左パネル(ファイル一覧)のファイル操作: 既定のアプリで開く / ファイルマネージャーで表示 / 名前の変更 / 削除 / コピー。
// 一覧の右クリックメニューとキー操作から呼ばれる。
// WPF 版にある、ファイルのコピー・切り取り・貼り付け、画像のコピー、プロパティ、一覧からのドラッグ持ち出しは、まだない。
public partial class MainWindow
{
    List<FileItem> SelectedFiles() => FileList.SelectedItems?.OfType<FileItem>().ToList() ?? new List<FileItem>();

    void FileMenu_Opening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        var sel = SelectedFiles();
        int n = sel.Count;

        FmOpen.IsEnabled = n == 1;
        FmReveal.IsEnabled = n > 0 || currentFolder != null;
        FmCopyPath.IsEnabled = n > 0;
        FmRename.IsEnabled = n == 1;
        FmCopyPrompt.IsEnabled = n == 1 && !string.IsNullOrWhiteSpace(sel[0].Meta?.Prompt);

        FmDelete.IsEnabled = n > 0 && settings.AllowDelete;
        FmDelete.Header = settings.AllowDelete ? "削除(ごみ箱へ)(_D)" : "削除(設定で許可すると使えます)";
    }

    void FileOp_OpenDefault(object? sender, RoutedEventArgs e)
    {
        var item = SelectedFiles().FirstOrDefault();
        if (item == null) return;
        try { PlatformShell.OpenWithDefaultApp(item.Path); }
        catch (Exception ex) { Status.Text = "開けません: " + ex.Message; }
    }

    void FileOp_Reveal(object? sender, RoutedEventArgs e)
    {
        try { PlatformShell.Reveal((SelectedFiles().FirstOrDefault() ?? currentItem)?.Path, currentFolder); }
        catch (Exception ex) { Status.Text = $"{PlatformShell.FileManagerName}を開けません: " + ex.Message; }
    }

    void FileOp_Rename(object? sender, RoutedEventArgs e) => RenameSelected();
    void FileOp_Delete(object? sender, RoutedEventArgs e) => DeleteSelected();
    void FileOp_CopyPath(object? sender, RoutedEventArgs e) => CopyPaths();
    void FileOp_CopyPrompt(object? sender, RoutedEventArgs e) => CopyText(SelectedFiles().FirstOrDefault()?.Meta?.Prompt ?? "");

    void CopyPaths()
    {
        var paths = SelectedFiles().Select(f => f.Path).ToList();
        if (paths.Count > 0) CopyText(string.Join(Environment.NewLine, paths));
    }

    // ───────────────────── 名前の変更 ─────────────────────

    async void RenameSelected()
    {
        var sel = SelectedFiles();
        if (sel.Count != 1 || currentFolder == null) return;
        var item = sel[0];
        if (!File.Exists(item.Path)) { Status.Text = "ファイルが見つかりません"; return; }

        string dir = Path.GetDirectoryName(item.Path)!;
        var dlg = new RenameWindow(dir, item.Name);
        string? newName = await dlg.ShowDialog<string?>(this);
        if (newName == null || string.Equals(newName, item.Name, StringComparison.Ordinal)) return;

        string newPath = Path.Combine(dir, newName);
        try
        {
            File.Move(item.Path, newPath);
        }
        catch (Exception ex)
        {
            await MessageDialog.Show(this, "名前を変更できませんでした。\n\n" + ex.Message, "名前の変更");
            return;
        }

        // 一覧の項目を差し替える(メタデータは読み直さずに引き継ぐ)
        int idx = items.IndexOf(item);
        int shownIdx = shown.IndexOf(item);
        if (idx < 0) return;
        var renamed = new FileItem(newPath, currentFolder) { Meta = item.Meta };
        items[idx] = renamed;
        currentItem = null; // 選択し直したときに、表示を更新させる
        if (shownIdx >= 0) shown[shownIdx] = renamed;
        FileList.SelectedItem = renamed;
        FocusSelectedListItem();
        Status.Text = $"名前を変更しました: {newName}";
    }

    // ───────────────────── 削除 ─────────────────────

    /// <summary>選択したファイルをごみ箱に移す。設定で許可されていないときは何もしない。</summary>
    async void DeleteSelected()
    {
        if (!settings.AllowDelete)
        {
            Status.Text = "削除は許可されていません(設定 → ファイル操作 → 「削除を許可する」をオンにしてください)";
            return;
        }

        var sel = SelectedFiles();
        if (sel.Count == 0) return;

        if (settings.ConfirmDelete)
        {
            string msg = sel.Count == 1
                ? $"「{sel[0].Name}」をごみ箱に移動します。\n\nよろしいですか?"
                : $"選択した {sel.Count} 個のファイルをごみ箱に移動します。\n\nよろしいですか?";
            if (!await MessageDialog.Confirm(this, msg, "削除の確認")) return;
        }

        var deleted = new List<FileItem>();
        var errors = new List<string>();
        await Task.Run(() =>
        {
            foreach (var f in sel)
            {
                try
                {
                    PlatformShell.MoveToTrash(f.Path);
                    lock (deleted) deleted.Add(f);
                }
                catch (Exception ex)
                {
                    lock (errors) errors.Add($"{f.Name}: {ex.Message}");
                }
            }
        });

        RemoveItems(deleted);

        if (errors.Count > 0)
            await MessageDialog.Show(this, "削除できなかったファイルがあります。\n\n" + string.Join("\n", errors.Take(8)), "削除");
    }

    /// <summary>一覧から項目を取り除き、近くの画像を選び直す(ファイルそのものは触らない)。</summary>
    void RemoveItems(List<FileItem> toRemove)
    {
        if (toRemove.Count == 0) return;

        int idx = toRemove.Select(f => shown.IndexOf(f)).Where(i => i >= 0).DefaultIfEmpty(0).Min();

        suppressSelection = true; // 1枚ずつ取り除く間は、画像の読み込みをさせない
        try
        {
            foreach (var f in toRemove)
            {
                items.Remove(f);
                shown.Remove(f);
            }
        }
        finally
        {
            suppressSelection = false;
        }

        currentItem = null;
        UpdateCount();
        NotifyTagStats();
        int count = shown.Count;
        if (count == 0)
        {
            FileList.SelectedItems?.Clear();
            ClearImage();
            Status.Text = "画像がありません";
        }
        else
        {
            FileList.SelectedItems?.Clear();
            FileList.SelectedIndex = Math.Min(idx, count - 1);
            FocusSelectedListItem();
        }
    }

    /// <summary>
    /// 選択中の項目にキーボードフォーカスを移す。削除などで、フォーカスのあった項目が消えると、
    /// ↑↓キーが先頭から始まってしまうため、新しく選んだ項目にフォーカスを渡しておく。
    /// </summary>
    void FocusSelectedListItem()
    {
        var target = FileList.SelectedItem;
        if (target == null) return;

        FileList.ScrollIntoView(target);
        Dispatcher.UIThread.Post(() =>
        {
            if (FileList.ContainerFromItem(target) is ListBoxItem container) container.Focus();
            else FileList.Focus();
        }, DispatcherPriority.Loaded);
    }
}
