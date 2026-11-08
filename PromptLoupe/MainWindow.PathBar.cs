using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace PromptLoupe;

// フォルダのパスの欄(絞り込み欄の上)と、移動の履歴(戻る・進む・上へ)。
// パスはフォルダ名のボタンを並べたパンくずで表示し、クリックでそのフォルダへ移動する。
// 空いたところをクリックすると入力欄に切り替わり、パスを手で入力できる(Enter で移動、Esc で取り消し)。
// 設定(ShowPathBar)で欄ごと隠せる。隠していても、戻る・進む・上へのキーとマウスのボタンは効く。
public partial class MainWindow
{
    readonly List<string> backStack = new();    // 戻る先(最後が直前のフォルダ)
    readonly List<string> forwardStack = new(); // 進む先(最後が次のフォルダ)
    const int MaxHistory = 50;
    bool navigatingHistory; // 戻る・進むで開いているあいだは、履歴に積まない

    void SetupPathBar()
    {
        PathBarMenu.IsChecked = settings.ShowPathBar;
        PathRow.IsVisible = settings.ShowPathBar;
        PathEdit.TextChanged += (_, _) => SetPathError(false);
        // パスが長くて入りきらないときは、いまのフォルダ(右端)が見えるようにする
        CrumbPanel.SizeChanged += (_, _) => ScrollCrumbsToEnd();
        CrumbScroll.SizeChanged += (_, _) => ScrollCrumbsToEnd();
        // マウスの「戻る」「進む」ボタン
        AddHandler(PointerPressedEvent, Window_PointerPressed, RoutingStrategies.Tunnel);
        UpdatePathBar();
    }

    /// <summary>フォルダを開くときに呼ぶ。前のフォルダを履歴に積み、パスの欄とフォルダツリーを新しいフォルダに合わせる。</summary>
    void OnFolderChanging(string? previous, string folder)
    {
        if (!navigatingHistory && previous != null && !FolderPath.Equal(previous, folder, FolderPath.Comparison))
        {
            backStack.Add(previous);
            if (backStack.Count > MaxHistory) backStack.RemoveAt(0);
            forwardStack.Clear();
        }
        UpdatePathBar();
        SyncFolderTree(folder);
    }

    /// <summary>パンくずと、戻る・進む・上へのボタンを、いまのフォルダに合わせる。</summary>
    void UpdatePathBar()
    {
        BackButton.IsEnabled = backStack.Count > 0;
        ForwardButton.IsEnabled = forwardStack.Count > 0;
        UpButton.IsEnabled = currentFolder != null && FolderPath.Parent(currentFolder) != null;
        string tip = OperatingSystem.IsMacOS() ? "⌘" : "Alt+";
        ToolTip.SetTip(BackButton, OperatingSystem.IsMacOS() ? "戻る(⌘[)" : "戻る(Alt+←)");
        ToolTip.SetTip(ForwardButton, OperatingSystem.IsMacOS() ? "進む(⌘])" : "進む(Alt+→)");
        ToolTip.SetTip(UpButton, $"親フォルダへ({tip}↑)");

        CrumbPanel.Children.Clear();
        if (currentFolder == null)
        {
            CrumbPanel.Children.Add(new TextBlock
            {
                Text = "フォルダを開いていません", Classes = { "muted" },
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Margin = new Thickness(6, 0),
            });
            return;
        }

        var segs = FolderPath.Segments(currentFolder);
        for (int i = 0; i < segs.Count; i++)
        {
            var (name, path) = segs[i];
            bool last = i == segs.Count - 1;
            var b = new Button { Content = name, Tag = path, Classes = { "crumb" } };
            if (last) b.Classes.Add("bold");
            ToolTip.SetTip(b, path);
            b.Click += Crumb_Click;
            CrumbPanel.Children.Add(b);
            if (!last) CrumbPanel.Children.Add(new TextBlock { Text = "›", Classes = { "sep" } });
        }
    }

    void ScrollCrumbsToEnd() =>
        CrumbScroll.Offset = new Vector(Math.Max(0, CrumbScroll.Extent.Width - CrumbScroll.Viewport.Width), 0);

    void Crumb_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string path }) NavigateTo(path);
    }

    /// <summary>フォルダへ移動する(いまのフォルダなら何もしない)。</summary>
    void NavigateTo(string folder)
    {
        if (currentFolder != null && FolderPath.Equal(currentFolder, folder, FolderPath.Comparison)) return;
        if (!Directory.Exists(folder))
        {
            Status.Text = "フォルダが見つかりません: " + folder;
            return;
        }
        OpenFolder(FolderPath.Normalize(folder), null);
    }

    // ───── 戻る・進む・上へ ─────

    void Back_Click(object? sender, RoutedEventArgs e) => GoBack();
    void Forward_Click(object? sender, RoutedEventArgs e) => GoForward();
    void Up_Click(object? sender, RoutedEventArgs e) => GoUp();

    void GoBack() => GoHistory(backStack, forwardStack);
    void GoForward() => GoHistory(forwardStack, backStack);

    /// <summary>履歴の from の最後のフォルダへ移動し、いまのフォルダを to に積む。消えたフォルダは飛ばす。</summary>
    void GoHistory(List<string> from, List<string> to)
    {
        while (from.Count > 0)
        {
            string target = from[^1];
            from.RemoveAt(from.Count - 1);
            if (!Directory.Exists(target)) continue;

            if (currentFolder != null) to.Add(currentFolder);
            navigatingHistory = true;
            try { OpenFolder(target, null); }
            finally { navigatingHistory = false; }
            return;
        }
        UpdatePathBar();
    }

    void GoUp()
    {
        if (currentFolder == null) return;
        string from = currentFolder;
        // 消えたフォルダにいるときも、あるところまで上がる
        string? parent = FolderPath.Parent(from);
        while (parent != null && !Directory.Exists(parent)) parent = FolderPath.Parent(parent);
        if (parent == null) return;
        // 上がったあと、いままでいたフォルダの画像は一覧にないので、選択は先頭のまま
        NavigateTo(parent);
    }

    void Window_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var props = e.GetCurrentPoint(this).Properties;
        if (props.IsXButton1Pressed) { GoBack(); e.Handled = true; }
        else if (props.IsXButton2Pressed) { GoForward(); e.Handled = true; }
    }

    /// <summary>戻る・進む・上へ・パスの入力のキー。処理したら true。</summary>
    bool HandleNavigationKey(KeyEventArgs e)
    {
        var mod = e.KeyModifiers;
        if (OperatingSystem.IsMacOS())
        {
            // Finder と同じ: ⌘[ 戻る、⌘] 進む、⌘↑ 上へ
            if (mod == KeyModifiers.Meta && e.Key == Key.OemOpenBrackets) GoBack();
            else if (mod == KeyModifiers.Meta && e.Key == Key.OemCloseBrackets) GoForward();
            else if (mod == KeyModifiers.Meta && e.Key == Key.Up) GoUp();
            else if (mod == KeyModifiers.Meta && e.Key == Key.L) BeginPathEdit();
            else return false;
            return true;
        }
        // エクスプローラーと同じ: Alt+← 戻る、Alt+→ 進む、Alt+↑ 上へ、Ctrl+L / Alt+D パスの入力
        if (mod == KeyModifiers.Alt && e.Key == Key.Left) GoBack();
        else if (mod == KeyModifiers.Alt && e.Key == Key.Right) GoForward();
        else if (mod == KeyModifiers.Alt && e.Key == Key.Up) GoUp();
        else if ((mod == KeyModifiers.Control && e.Key == Key.L) || (mod == KeyModifiers.Alt && e.Key == Key.D)) BeginPathEdit();
        else return false;
        return true;
    }

    // ───── パスの入力 ─────

    void CrumbBox_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // フォルダ名のボタンの上は、ボタンが処理するのでここには来ない
        if (e.GetCurrentPoint(CrumbBox).Properties.IsLeftButtonPressed) BeginPathEdit();
    }

    void BeginPathEdit()
    {
        if (!PathRow.IsVisible) return;
        PathEdit.Text = currentFolder ?? "";
        SetPathError(false);
        CrumbBox.IsVisible = false;
        PathEdit.IsVisible = true;
        Dispatcher.UIThread.Post(() =>
        {
            PathEdit.Focus();
            PathEdit.SelectAll();
        }, DispatcherPriority.Loaded);
    }

    void EndPathEdit()
    {
        if (!PathEdit.IsVisible) return;
        PathEdit.IsVisible = false;
        CrumbBox.IsVisible = true;
        SetPathError(false);
    }

    /// <summary>入力したパスが見つからないときの印(枠を赤くし、欄の右端に理由を出す。下の行をずらさない)。</summary>
    void SetPathError(bool error)
    {
        PathEdit.Classes.Set("invalid", error);
        PathError.IsVisible = error;
    }

    void PathEdit_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            EndPathEdit();
            FileList.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            CommitPathEdit();
            e.Handled = true;
        }
    }

    void PathEdit_LostFocus(object? sender, RoutedEventArgs e) => EndPathEdit();

    void CommitPathEdit()
    {
        string? path = FolderPath.FromInput(PathEdit.Text,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        if (path == null)
        {
            EndPathEdit();
            return;
        }

        try
        {
            if (Directory.Exists(path))
            {
                EndPathEdit();
                FileList.Focus();
                if (currentFolder == null || !FolderPath.Equal(currentFolder, path, FolderPath.Comparison))
                    OpenFolder(FolderPath.Normalize(path), null);
                return;
            }
            if (File.Exists(path))
            {
                // 画像のパスなら、そのフォルダを開いて、その画像を選ぶ
                EndPathEdit();
                FileList.Focus();
                OpenPath(path);
                return;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            // パスとして読めない文字が入っているときなど。下で「見つかりません」を出す
        }

        // 入力欄はそのままにして、直せるようにする
        SetPathError(true);
        PathEdit.Focus();
    }

    // ───── 設定 ─────

    void PathBar_Click(object? sender, RoutedEventArgs e)
    {
        settings.ShowPathBar = !settings.ShowPathBar;
        PathBarMenu.IsChecked = settings.ShowPathBar;
        settings.Save();
        EndPathEdit();
        if (!isFullscreen) PathRow.IsVisible = settings.ShowPathBar;
    }
}
