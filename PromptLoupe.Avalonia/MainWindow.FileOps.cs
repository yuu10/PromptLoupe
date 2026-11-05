using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace PromptLoupe;

// 左パネル(ファイル一覧)のファイル操作: 既定のアプリで開く / ファイルマネージャーで表示 / コピー / 切り取り / 貼り付け /
// 名前の変更 / 削除 / パス・画像・プロンプトのコピー / プロパティ(Windows のみ)。
// 一覧の右クリックメニューとキー操作から呼ばれる。ドラッグ&ドロップで他のアプリへ持ち出すこともできる(Linux を除く)。
public partial class MainWindow
{
    List<FileItem> SelectedFiles() => FileList.SelectedItems?.OfType<FileItem>().ToList() ?? new List<FileItem>();

    void FileMenu_Opening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        var sel = SelectedFiles();
        int n = sel.Count;

        FmOpen.IsEnabled = n == 1;
        FmReveal.IsEnabled = n > 0 || currentFolder != null;
        FmCut.IsEnabled = FmCopy.IsEnabled = FmCopyPath.IsEnabled = n > 0;
        FmRename.IsEnabled = n == 1;
        FmCopyImage.IsEnabled = FmProps.IsEnabled = n == 1;
        FmCopyPrompt.IsEnabled = n == 1 && !string.IsNullOrWhiteSpace(sel[0].Meta?.Prompt);
        UpdatePasteEnabled();

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

    /// <summary>クリップボードを読むのは非同期なので、メニューを開いてから「貼り付け」を使えるようにする。</summary>
    async void UpdatePasteEnabled()
    {
        FmPaste.IsEnabled = false;
        if (currentFolder == null || Clipboard == null) return;
        FmPaste.IsEnabled = await FileClipboard.HasFilesAsync(Clipboard);
    }

    void FileOp_Cut(object? sender, RoutedEventArgs e) => CopyFiles(cut: true);
    void FileOp_Copy(object? sender, RoutedEventArgs e) => CopyFiles(cut: false);
    void FileOp_Paste(object? sender, RoutedEventArgs e) => PasteFiles();
    void FileOp_Rename(object? sender, RoutedEventArgs e) => RenameSelected();
    void FileOp_Delete(object? sender, RoutedEventArgs e) => DeleteSelected();
    void FileOp_CopyPath(object? sender, RoutedEventArgs e) => CopyPaths();
    void FileOp_CopyImage(object? sender, RoutedEventArgs e) => CopyImage();
    void FileOp_CopyPrompt(object? sender, RoutedEventArgs e) => CopyText(SelectedFiles().FirstOrDefault()?.Meta?.Prompt ?? "");
    void FileOp_Properties(object? sender, RoutedEventArgs e) => ShowProperties();

    // ───────────────────── コピー / 切り取り / 貼り付け ─────────────────────

    /// <summary>選択したファイルをクリップボードに入れる。ファイルマネージャーでそのまま貼り付けられる。</summary>
    async void CopyFiles(bool cut)
    {
        var paths = SelectedFiles().Select(f => f.Path).Where(File.Exists).ToArray();
        if (paths.Length == 0 || Clipboard == null) return;

        try
        {
            await FileClipboard.SetFilesAsync(Clipboard, StorageProvider, paths, cut);
            Status.Text = cut
                ? $"{paths.Length} 個のファイルを切り取りました(貼り付けると移動します)"
                : $"{paths.Length} 個のファイルをコピーしました";
        }
        catch (Exception ex)
        {
            Status.Text = "クリップボードに入れられませんでした: " + ex.Message;
        }
    }

    /// <summary>クリップボードのファイルを、いま開いているフォルダに貼り付ける(画像のみ)。</summary>
    async void PasteFiles()
    {
        if (currentFolder == null || Clipboard == null) return;

        List<string> list;
        bool move;
        try
        {
            if (await FileClipboard.GetFilesAsync(Clipboard) is not { } got) return;
            (list, move) = got;
        }
        catch (Exception ex)
        {
            Status.Text = "クリップボードを読めませんでした: " + ex.Message;
            return;
        }

        string folder = currentFolder;
        string? last = null;
        int ok = 0, skipped = 0;
        var errors = new List<string>();

        await Task.Run(() =>
        {
            foreach (string src in list)
            {
                if (!File.Exists(src) || !IsImage(src)) { skipped++; continue; }
                try
                {
                    string srcDir = Path.GetFullPath(Path.GetDirectoryName(src)!).TrimEnd('\\', '/');
                    bool sameFolder = PathEquals(srcDir, Path.GetFullPath(folder).TrimEnd('\\', '/'));
                    if (move && sameFolder) { last = src; continue; } // 同じフォルダへの移動は、何もしない

                    string dest = UniquePath(Path.Combine(folder, Path.GetFileName(src)));
                    if (move) File.Move(src, dest);
                    else File.Copy(src, dest);
                    last = dest;
                    ok++;
                }
                catch (Exception ex)
                {
                    errors.Add($"{Path.GetFileName(src)}: {ex.Message}");
                }
            }
        });

        if (move && ok > 0)
        {
            try { await Clipboard.ClearAsync(); } catch { /* 無視 */ }
        }

        if (errors.Count > 0)
            await MessageDialog.Show(this, "貼り付けられなかったファイルがあります。\n\n" + string.Join("\n", errors.Take(8)), "貼り付け");

        if (ok > 0) OpenFolder(folder, last); // 読み込み直す(貼り付けたファイルを選択)
        else if (skipped > 0 && errors.Count == 0) Status.Text = "貼り付けられるのは画像ファイルだけです";
    }

    /// <summary>同じ名前のファイルがあるときは「名前 (2).拡張子」のようにして、重ならない名前にする。</summary>
    static string UniquePath(string path)
    {
        if (!File.Exists(path)) return path;
        string dir = Path.GetDirectoryName(path)!;
        string name = Path.GetFileNameWithoutExtension(path);
        string ext = Path.GetExtension(path);
        for (int i = 2; ; i++)
        {
            string p = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(p)) return p;
        }
    }

    // ───────────────────── パス / 画像 / プロパティ ─────────────────────

    void CopyPaths()
    {
        var paths = SelectedFiles().Select(f => f.Path).ToList();
        if (paths.Count > 0) CopyText(string.Join(Environment.NewLine, paths));
    }

    Bitmap? clipboardImage; // クリップボードに入れた画像(Linux では、貼り付けられるときに読まれるので、手放さずに持っておく)

    /// <summary>画像をクリップボードに入れる(他のアプリに貼り付ける用)。EXIF の回転は直した向きで入れる。</summary>
    async void CopyImage()
    {
        var item = SelectedFiles().FirstOrDefault();
        if (item == null || Clipboard == null) return;
        try
        {
            string path = item.Path;
            var bmp = await Task.Run(() => ImageLoader.Load(path));
            await FileClipboard.SetBitmapAsync(Clipboard, bmp);
            var old = clipboardImage;
            clipboardImage = bmp;
            old?.Dispose();
            Status.Text = "画像をクリップボードにコピーしました";
        }
        catch (Exception ex)
        {
            Status.Text = "画像をコピーできませんでした: " + ex.Message + ImageLoader.UnsupportedHint(item.Path);
        }
    }

    /// <summary>Windows 標準の「プロパティ」画面を開く(ほかの OS では、メニューに出さない)。</summary>
    void ShowProperties()
    {
        if (!OperatingSystem.IsWindows()) return;
        var item = SelectedFiles().FirstOrDefault();
        if (item == null) return;
        if (!PlatformShell.ShowProperties(item.Path)) Status.Text = "プロパティを開けませんでした";
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

    // ───────────────────── ドラッグ&ドロップで持ち出す ─────────────────────
    //
    // Windows と Mac だけ。Linux(X11)の Avalonia 11.3 には、ほかのアプリへドラッグする仕組みがない
    // (アプリの中だけのドラッグになる)ので、Linux では持ち出しを始めない。

    static bool CanDragOut => !OperatingSystem.IsLinux();

    bool draggingOut;           // 持ち出し中(自分のウィンドウへのドロップは無視する)
    bool dragArmed;             // 一覧の項目の上で左ボタンを押した(動かせばドラッグを始める)
    Point dragOrigin;
    ListBoxItem? pendingSingle; // 複数選択のうちの1枚を押した。ドラッグしないで離したら、その1枚だけを選ぶ

    void SetupFileListDrag()
    {
        if (!CanDragOut) return;
        // 一覧が選択を変える前に見たいので、トンネル(外側から先に呼ばれる)で受ける
        FileList.AddHandler(PointerPressedEvent, FileList_PointerPressed, RoutingStrategies.Tunnel);
        FileList.AddHandler(PointerMovedEvent, FileList_PointerMoved, RoutingStrategies.Tunnel);
        FileList.AddHandler(PointerReleasedEvent, FileList_PointerReleased, RoutingStrategies.Tunnel);
    }

    void FileList_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(FileList);
        dragArmed = false;
        pendingSingle = null;
        if (point.Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed || e.Pointer.Type != PointerType.Mouse) return;

        var lbi = (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true);
        if (lbi == null) return;
        dragArmed = true;
        dragOrigin = point.Position;

        // 複数選択したまま、そのうちの1枚をつかんでドラッグできるように、押した時点では選択を変えない
        // (ドラッグしないで離したときに、その1枚だけの選択にする)
        if (lbi.IsSelected && (FileList.SelectedItems?.Count ?? 0) > 1 && e.KeyModifiers == KeyModifiers.None)
        {
            pendingSingle = lbi;
            lbi.Focus();
            e.Handled = true;
        }
    }

    void FileList_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        dragArmed = false;
        if (pendingSingle?.DataContext is FileItem item)
        {
            FileList.SelectedItems?.Clear();
            FileList.SelectedItem = item;
        }
        pendingSingle = null;
    }

    async void FileList_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!dragArmed || draggingOut) return;
        var point = e.GetCurrentPoint(FileList);
        if (!point.Properties.IsLeftButtonPressed)
        {
            dragArmed = false;
            return;
        }

        var size = PlatformSettings?.GetTapSize(PointerType.Mouse) ?? new Size(4, 4);
        if (Math.Abs(point.Position.X - dragOrigin.X) < size.Width &&
            Math.Abs(point.Position.Y - dragOrigin.Y) < size.Height)
            return;

        dragArmed = false;
        pendingSingle = null;

        var files = SelectedFiles().Where(f => File.Exists(f.Path)).ToList();
        if (files.Count == 0) return;

        // 既定はコピー。Shift を押しながらドラッグを始めたときだけ、移動もできる
        bool shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
        var allowed = shift ? DragDropEffects.Copy | DragDropEffects.Move : DragDropEffects.Copy;

        draggingOut = true;
        try
        {
            var data = new DataTransfer();
            foreach (var f in files)
            {
                // デスクトップでは、すぐに終わる(待たない)
                var file = await StorageProvider.TryGetFileFromPathAsync(f.Path);
                if (file != null) data.Add(DataTransferItem.CreateFile(file));
            }
            if (data.Items.Count == 0) return;
            await DragDrop.DoDragDropAsync(e, data, allowed);
        }
        catch (Exception ex)
        {
            Status.Text = "ドラッグできませんでした: " + ex.Message;
        }
        finally
        {
            draggingOut = false;
        }

        // 移動された(元の場所から無くなった)ファイルは、一覧から取り除く
        var gone = files.Where(f => !File.Exists(f.Path)).ToList();
        if (gone.Count > 0) RemoveItems(gone);
    }
}
