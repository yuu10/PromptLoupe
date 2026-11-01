using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MViewer;

// 左パネル(ファイル一覧)のファイル操作: コピー / 切り取り / 貼り付け / 名前の変更 / 削除 / エクスプローラーで開く など。
// 一覧の右クリックメニューとキー操作から呼ばれる。ドラッグ&ドロップで他のアプリへ持ち出すこともできる。
public partial class MainWindow
{
    // ───────────────────── 選択 ─────────────────────

    List<FileItem> SelectedFiles() => FileList.SelectedItems.OfType<FileItem>().ToList();

    // ───────────────────── 右クリックメニュー ─────────────────────

    void FileList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var sel = SelectedFiles();
        int n = sel.Count;
        bool any = n > 0;

        FmOpen.IsEnabled = n == 1;
        FmExplorer.IsEnabled = any;
        FmCut.IsEnabled = FmCopy.IsEnabled = FmCopyPath.IsEnabled = any;
        FmPaste.IsEnabled = currentFolder != null && ClipboardHasFiles();
        FmRename.IsEnabled = n == 1;
        FmCopyImage.IsEnabled = FmProps.IsEnabled = n == 1;
        FmCopyPrompt.IsEnabled = n == 1 && !string.IsNullOrWhiteSpace(sel[0].Meta?.Prompt);

        FmDelete.IsEnabled = any && settings.AllowDelete;
        FmDelete.Header = settings.AllowDelete ? "削除(ごみ箱へ)(_D)" : "削除(設定で許可すると使えます)";
    }

    static bool ClipboardHasFiles()
    {
        try { return Clipboard.ContainsFileDropList(); }
        catch { return false; }
    }

    void FileOp_OpenDefault(object sender, RoutedEventArgs e) => OpenWithDefaultApp();
    void FileOp_Explorer(object sender, RoutedEventArgs e) => ShowInExplorer();
    void FileOp_Cut(object sender, RoutedEventArgs e) => CopyFiles(cut: true);
    void FileOp_Copy(object sender, RoutedEventArgs e) => CopyFiles(cut: false);
    void FileOp_Paste(object sender, RoutedEventArgs e) => PasteFiles();
    void FileOp_Rename(object sender, RoutedEventArgs e) => RenameSelected();
    void FileOp_Delete(object sender, RoutedEventArgs e) => DeleteSelected();
    void FileOp_CopyPath(object sender, RoutedEventArgs e) => CopyPaths();
    void FileOp_CopyImage(object sender, RoutedEventArgs e) => CopyImage();
    void FileOp_CopyPrompt(object sender, RoutedEventArgs e) => CopyText(SelectedFiles().FirstOrDefault()?.Meta?.Prompt ?? "");
    void FileOp_Properties(object sender, RoutedEventArgs e) => ShowProperties();

    // ───────────────────── 開く ─────────────────────

    void OpenWithDefaultApp()
    {
        var item = SelectedFiles().FirstOrDefault();
        if (item == null) return;
        try { Process.Start(new ProcessStartInfo(item.Path) { UseShellExecute = true }); }
        catch (Exception ex) { Status.Text = "開けません: " + ex.Message; }
    }

    /// <summary>選択中の画像を選んだ状態で、エクスプローラーを開く(選択がなければ、開いているフォルダ)。</summary>
    void ShowInExplorer()
    {
        try
        {
            var item = SelectedFiles().FirstOrDefault() ?? currentItem;
            if (item != null && File.Exists(item.Path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe") { Arguments = $"/select,\"{item.Path}\"", UseShellExecute = true });
            }
            else if (currentFolder != null)
            {
                Process.Start(new ProcessStartInfo("explorer.exe") { Arguments = $"\"{currentFolder}\"", UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            Status.Text = "エクスプローラーを開けません: " + ex.Message;
        }
    }

    // ───────────────────── コピー / 切り取り / 貼り付け ─────────────────────

    /// <summary>選択したファイルをクリップボードに入れる。エクスプローラーでそのまま貼り付けられる。</summary>
    void CopyFiles(bool cut)
    {
        var paths = SelectedFiles().Select(f => f.Path).Where(File.Exists).ToArray();
        if (paths.Length == 0) return;

        try
        {
            var list = new StringCollection();
            list.AddRange(paths);
            var data = new DataObject();
            data.SetFileDropList(list);
            // 切り取りか、コピーかを、エクスプローラーと同じ形式で伝える(1 = コピー、2 = 移動)
            data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(cut ? 2 : 1)));
            Clipboard.SetDataObject(data, true);
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
    void PasteFiles()
    {
        if (currentFolder == null) return;

        StringCollection? list;
        bool move = false;
        try
        {
            if (!Clipboard.ContainsFileDropList()) return;
            list = Clipboard.GetFileDropList();
            if (Clipboard.GetData("Preferred DropEffect") is MemoryStream ms)
            {
                var buf = new byte[4];
                ms.Position = 0;
                if (ms.Read(buf, 0, 4) == 4)
                {
                    int effect = BitConverter.ToInt32(buf, 0);
                    move = (effect & 2) != 0 && (effect & 1) == 0;
                }
            }
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

        foreach (string? src in list)
        {
            if (src == null || !File.Exists(src) || !IsImage(src)) { skipped++; continue; }
            try
            {
                string srcDir = Path.GetFullPath(Path.GetDirectoryName(src)!).TrimEnd('\\', '/');
                bool sameFolder = string.Equals(srcDir, Path.GetFullPath(folder).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
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

        if (move && ok > 0)
        {
            try { Clipboard.Clear(); } catch { /* 無視 */ }
        }

        if (errors.Count > 0)
            MessageBox.Show(this, "貼り付けられなかったファイルがあります。\n\n" + string.Join("\n", errors.Take(8)),
                "貼り付け", MessageBoxButton.OK, MessageBoxImage.Warning);

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

    // ───────────────────── 名前の変更 ─────────────────────

    void RenameSelected()
    {
        var sel = SelectedFiles();
        if (sel.Count != 1 || currentFolder == null) return;
        var item = sel[0];
        if (!File.Exists(item.Path)) { Status.Text = "ファイルが見つかりません"; return; }

        string dir = Path.GetDirectoryName(item.Path)!;
        var dlg = new RenameWindow(dir, item.Name) { Owner = this };
        if (dlg.ShowDialog() != true) return;

        string newName = dlg.NewFileName;
        if (string.Equals(newName, item.Name, StringComparison.Ordinal)) return;

        string newPath = Path.Combine(dir, newName);
        try
        {
            File.Move(item.Path, newPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "名前を変更できませんでした。\n\n" + ex.Message, "名前の変更",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // 一覧の項目を差し替える(メタデータは読み直さずに引き継ぐ)
        int idx = items.IndexOf(item);
        if (idx < 0) return;
        var renamed = new FileItem(newPath, currentFolder) { Meta = item.Meta };
        currentItem = null; // 選択し直したときに、表示を更新させる
        items[idx] = renamed;
        FileList.SelectedItem = renamed;
        FocusSelectedListItem();
        Status.Text = $"名前を変更しました: {newName}";
    }

    // ───────────────────── 削除 ─────────────────────

    /// <summary>選択したファイルをごみ箱に移す。設定で許可されていないときは何もしない。</summary>
    void DeleteSelected()
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
            var answer = MessageBox.Show(this, msg, "削除の確認", MessageBoxButton.YesNo,
                MessageBoxImage.Warning, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes) return;
        }

        var deleted = new List<FileItem>();
        var errors = new List<string>();
        foreach (var f in sel)
        {
            try
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(f.Path,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                deleted.Add(f);
            }
            catch (Exception ex)
            {
                errors.Add($"{f.Name}: {ex.Message}");
            }
        }

        RemoveItems(deleted);

        if (errors.Count > 0)
            MessageBox.Show(this, "削除できなかったファイルがあります。\n\n" + string.Join("\n", errors.Take(8)),
                "削除", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    /// <summary>一覧から項目を取り除き、近くの画像を選び直す(ファイルそのものは触らない)。</summary>
    void RemoveItems(List<FileItem> toRemove)
    {
        if (toRemove.Count == 0) return;

        int idx = toRemove.Select(f => FileList.Items.IndexOf(f)).Where(i => i >= 0).DefaultIfEmpty(0).Min();

        suppressSelection = true; // 1枚ずつ取り除く間は、画像の読み込みをさせない
        try
        {
            foreach (var f in toRemove) items.Remove(f);
        }
        finally
        {
            suppressSelection = false;
        }

        currentItem = null;
        UpdateCount();
        NotifyTagStats();
        int count = FileList.Items.Count;
        if (count == 0)
        {
            ClearImage();
            Status.Text = "画像がありません";
        }
        else
        {
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
        Dispatcher.InvokeAsync(() =>
        {
            FileList.UpdateLayout();
            if (FileList.ItemContainerGenerator.ContainerFromItem(target) is ListBoxItem container)
                container.Focus();
            else
                FileList.Focus();
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    // ───────────────────── パス / 画像 / プロパティ ─────────────────────

    void CopyPaths()
    {
        var paths = SelectedFiles().Select(f => f.Path).ToList();
        if (paths.Count > 0) CopyText(string.Join(Environment.NewLine, paths));
    }

    void CopyImage()
    {
        var item = SelectedFiles().FirstOrDefault();
        if (item == null) return;
        try
        {
            Clipboard.SetImage(Decode(item.Path));
            Status.Text = "画像をクリップボードにコピーしました";
        }
        catch (Exception ex)
        {
            Status.Text = "画像をコピーできませんでした: " + ex.Message;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct ShellExecuteInfo
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public string lpVerb;
        public string lpFile;
        public string? lpParameters;
        public string? lpDirectory;
        public int nShow;
        public IntPtr hInstApp;
        public IntPtr lpIDList;
        public string? lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIcon;
        public IntPtr hProcess;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool ShellExecuteEx(ref ShellExecuteInfo info);

    /// <summary>Windows 標準の「プロパティ」画面を開く。</summary>
    void ShowProperties()
    {
        var item = SelectedFiles().FirstOrDefault();
        if (item == null) return;

        var info = new ShellExecuteInfo
        {
            cbSize = Marshal.SizeOf<ShellExecuteInfo>(),
            fMask = 0x0000000C, // SEE_MASK_INVOKEIDLIST
            lpVerb = "properties",
            lpFile = item.Path,
            nShow = 1
        };
        if (!ShellExecuteEx(ref info)) Status.Text = "プロパティを開けませんでした";
    }

    // ───────────────────── ドラッグ&ドロップで持ち出す ─────────────────────

    bool draggingOut;
    bool dragArmed;
    Point dragOrigin;
    ListBoxItem? pendingSingle;

    static ListBoxItem? FindListBoxItem(object? source)
    {
        var d = source as DependencyObject;
        while (d != null && d is not ListBoxItem)
            d = (d is Visual || d is System.Windows.Media.Media3D.Visual3D) ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        return d as ListBoxItem;
    }

    void FileList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var lbi = FindListBoxItem(e.OriginalSource);
        dragArmed = lbi != null;
        dragOrigin = e.GetPosition(FileList);
        pendingSingle = null;

        // 複数選択したまま、そのうちの1枚をつかんでドラッグできるように、押した時点では選択を変えない
        // (ドラッグしないで離したときに、その1枚だけの選択にする)
        if (lbi is { IsSelected: true } && FileList.SelectedItems.Count > 1 && Keyboard.Modifiers == ModifierKeys.None)
        {
            pendingSingle = lbi;
            FileList.Focus();
            e.Handled = true;
        }
    }

    void FileList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        dragArmed = false;
        if (pendingSingle?.DataContext is FileItem item)
        {
            FileList.SelectedItem = item;
        }
        pendingSingle = null;
    }

    void FileList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        // 右クリックした項目が選択に入っていなければ、その1枚を選ぶ(入っていれば、選択はそのまま)
        var lbi = FindListBoxItem(e.OriginalSource);
        if (lbi is { IsSelected: false, DataContext: FileItem item })
            FileList.SelectedItem = item;
        FileList.Focus();
    }

    void FileList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!dragArmed || e.LeftButton != MouseButtonState.Pressed) return;

        var p = e.GetPosition(FileList);
        if (Math.Abs(p.X - dragOrigin.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(p.Y - dragOrigin.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        dragArmed = false;
        pendingSingle = null;

        var files = SelectedFiles().Where(f => File.Exists(f.Path)).ToList();
        if (files.Count == 0) return;

        var data = new DataObject(DataFormats.FileDrop, files.Select(f => f.Path).ToArray());
        // 既定はコピー。Shift を押しながらドラッグを始めたときだけ、移動もできる
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        var allowed = shift ? DragDropEffects.Copy | DragDropEffects.Move : DragDropEffects.Copy;

        draggingOut = true;
        try
        {
            DragDrop.DoDragDrop(FileList, data, allowed);
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
