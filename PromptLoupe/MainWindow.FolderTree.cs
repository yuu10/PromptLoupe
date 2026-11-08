using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ShapePath = Avalonia.Controls.Shapes.Path;

namespace PromptLoupe;

// 左パネルのフォルダツリー。よく使う場所(ピクチャ・デスクトップなど)とドライブを根にして、開いたときに中のフォルダを読む。
// フォルダを開くと、ツリーもそのフォルダまで開いて選んだ状態にする(SyncFolderTree)。ツリーで選ぶと、そのフォルダを開く。
// 隠しフォルダ・システムフォルダ(Mac・Linux は . で始まる名前)は出さない。画像の枚数は数えない(遅くなるため)。
public partial class MainWindow
{
    /// <summary>ツリーの項目に付ける情報。</summary>
    sealed class FolderNode
    {
        public required string Path { get; init; }
        public Task? Loading; // 中のフォルダの読み込み(済んだか、読み込み中なら null 以外)
    }

    static readonly Geometry FolderIcon =
        Geometry.Parse("M1,3 Q1,2 2,2 L6,2 L7.5,3.5 L14,3.5 Q15,3.5 15,4.5 L15,12 Q15,13 14,13 L2,13 Q1,13 1,12 Z");
    static readonly Geometry DriveIcon =
        Geometry.Parse("M1,6 L15,6 L15,12 Q15,13 14,13 L2,13 Q1,13 1,12 Z M2,4 L14,4 L15,6 L1,6 Z");
    static readonly IBrush FolderBrush = new SolidColorBrush(Color.Parse("#E7B54A"));
    static readonly IBrush DriveBrush = new SolidColorBrush(Color.Parse("#9AA4B0"));

    Task? treeRootsTask;
    bool syncingTree;  // コードでツリーの選択を変えているあいだは、フォルダを開かない
    int treeSyncSeq;   // 新しいフォルダを開いたら、前の SyncFolderTree をやめる
    bool suppressDepthChange = true;

    void SetupFolderTree()
    {
        FtReveal.Header = $"{PlatformShell.FileManagerName}で表示(_E)";
        FolderTree.AddHandler(PointerPressedEvent, FolderTree_PointerPressed, RoutingStrategies.Tunnel);
        ApplyFolderTreeLayout();
        SelectDepthBox();
        suppressDepthChange = false;
        treeRootsTask = BuildTreeRootsAsync();
    }

    // ───── 根(よく使う場所とドライブ) ─────

    async Task BuildTreeRootsAsync()
    {
        var roots = await Task.Run(ListRoots);
        foreach (var (name, path, drive) in roots)
            FolderTree.Items.Add(CreateNode(name, path, drive, hasChildren: true));

        // Windows のドライブ名(ボリュームラベル)は、ネットワークドライブなどで時間がかかることがあるので、あとから付ける
        if (OperatingSystem.IsWindows())
        {
            foreach (var item in FolderTree.Items.OfType<TreeViewItem>().Where(t => t.Classes.Contains("drive")).ToList())
            {
                var node = (FolderNode)item.Tag!;
                string? label = await Task.Run(() => DriveLabel(node.Path));
                if (label != null) SetNodeName(item, label);
            }
        }
    }

    /// <summary>ツリーの根にする場所(表示名, パス, ドライブか)。あるものだけ。</summary>
    static List<(string Name, string Path, bool Drive)> ListRoots()
    {
        var list = new List<(string, string, bool)>();
        void AddPlace(string name, string? path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                if (!Directory.Exists(path)) return;
                path = FolderPath.Normalize(path);
            }
            catch { return; }
            if (list.Any(r => string.Equals(r.Item2, path, FolderPath.Comparison))) return;
            list.Add((name, path, false));
        }

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!OperatingSystem.IsWindows()) AddPlace("ホーム", home);
        AddPlace("ピクチャ", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
        AddPlace("デスクトップ", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        AddPlace("ドキュメント", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        AddPlace("ダウンロード", System.IO.Path.Combine(home, "Downloads"));

        if (OperatingSystem.IsWindows())
        {
            try
            {
                foreach (var d in DriveInfo.GetDrives())
                    list.Add((FolderPath.RootName(d.Name), d.Name, true));
            }
            catch { }
        }
        else
        {
            list.Add(("ルート (/)", "/", true));
            if (OperatingSystem.IsMacOS())
            {
                // 外付けのディスクなど(起動ディスクは / へのリンクなので外す)
                try
                {
                    foreach (var v in Directory.GetDirectories("/Volumes"))
                    {
                        if (new DirectoryInfo(v).LinkTarget != null) continue;
                        list.Add((System.IO.Path.GetFileName(v), v, true));
                    }
                }
                catch { }
            }
        }
        return list;
    }

    /// <summary>Windows のドライブの表示名(「ローカル ディスク (C:)」など)。分からなければ null。</summary>
    static string? DriveLabel(string root)
    {
        try
        {
            var d = new DriveInfo(root);
            string letter = FolderPath.RootName(root);
            string kind = d.DriveType switch
            {
                DriveType.Fixed => "ローカル ディスク",
                DriveType.Removable => "リムーバブル ディスク",
                DriveType.Network => "ネットワーク ドライブ",
                DriveType.CDRom => "DVD ドライブ",
                _ => "ドライブ",
            };
            string label = d.IsReady && !string.IsNullOrWhiteSpace(d.VolumeLabel) ? d.VolumeLabel : kind;
            return $"{label} ({letter})";
        }
        catch { return null; }
    }

    // ───── 項目 ─────

    TreeViewItem CreateNode(string name, string path, bool drive, bool hasChildren)
    {
        var icon = new ShapePath
        {
            Data = drive ? DriveIcon : FolderIcon,
            Fill = drive ? DriveBrush : FolderBrush,
            Width = 16, Height = 14, Stretch = Stretch.Uniform,
            Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center,
        };
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(icon);
        header.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });

        var item = new TreeViewItem { Header = header, Tag = new FolderNode { Path = path } };
        ToolTip.SetTip(item, path);
        if (drive) item.Classes.Add("drive");
        // 中にフォルダがあるときは、開く印(▸)を出すために仮の項目を入れておく。開いたときに読み込む
        if (hasChildren) item.Items.Add(new TreeViewItem { Header = "読み込み中…", IsEnabled = false });
        item.PropertyChanged += (_, e) =>
        {
            if (e.Property == TreeViewItem.IsExpandedProperty && item.IsExpanded) _ = LoadChildrenAsync(item);
        };
        return item;
    }

    static void SetNodeName(TreeViewItem item, string name)
    {
        if (item.Header is StackPanel { Children: [_, TextBlock text] }) text.Text = name;
    }

    /// <summary>項目の中のフォルダを読み込む(一度だけ。読み込み中なら、その終わりを待つ)。</summary>
    Task LoadChildrenAsync(TreeViewItem item)
    {
        if (item.Tag is not FolderNode node) return Task.CompletedTask;
        return node.Loading ??= LoadChildrenCoreAsync(item, node);
    }

    async Task LoadChildrenCoreAsync(TreeViewItem item, FolderNode node)
    {
        var children = await Task.Run(() => ListSubfolders(node.Path, probe: true));
        item.Items.Clear();
        foreach (var (name, path, hasChildren) in children)
            item.Items.Add(CreateNode(name, path, drive: false, hasChildren));
    }

    /// <summary>フォルダの中の(隠しでない)フォルダを、自然順で返す。probe なら、それぞれの中にフォルダがあるかも調べる。</summary>
    static List<(string Name, string Path, bool HasChildren)> ListSubfolders(string dir, bool probe)
    {
        var opt = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
        };
        var list = new List<(string, string, bool)>();
        try
        {
            foreach (var d in Directory.EnumerateDirectories(dir, "*", opt))
            {
                string name = System.IO.Path.GetFileName(d);
                if (!OperatingSystem.IsWindows() && name.StartsWith('.')) continue;
                bool has = !probe || HasSubfolder(d, opt);
                list.Add((name, d, has));
            }
        }
        catch { }
        list.Sort((a, b) => NaturalComparer.Instance.Compare(a.Item1, b.Item1));
        return list;
    }

    static bool HasSubfolder(string dir, EnumerationOptions opt)
    {
        try
        {
            return Directory.EnumerateDirectories(dir, "*", opt)
                .Any(d => OperatingSystem.IsWindows() || !System.IO.Path.GetFileName(d).StartsWith('.'));
        }
        catch { return false; }
    }

    // ───── 開いているフォルダに合わせる ─────

    /// <summary>ツリーを、開いたフォルダまで開いて、そのフォルダを選んだ状態にする。</summary>
    async void SyncFolderTree(string folder)
    {
        int seq = ++treeSyncSeq;
        if (treeRootsTask != null) await treeRootsTask;
        if (seq != treeSyncSeq) return;

        // そのフォルダを含む根のうち、いちばん近いもの(C: よりピクチャ)
        TreeViewItem? root = null;
        string[]? parts = null;
        foreach (var r in FolderTree.Items.OfType<TreeViewItem>())
        {
            if (r.Tag is not FolderNode n) continue;
            var p = FolderPath.RelativeParts(n.Path, folder, FolderPath.Comparison);
            if (p != null && (parts == null || p.Length < parts.Length))
            {
                root = r;
                parts = p;
            }
        }
        if (root == null || parts == null)
        {
            // どの根にも入らない(Windows のネットワークの共有フォルダなど)。そのルートを根に足す
            string? rootPath = System.IO.Path.GetPathRoot(folder);
            if (string.IsNullOrEmpty(rootPath)) return;
            root = CreateNode(FolderPath.RootName(rootPath), rootPath, drive: true, hasChildren: true);
            FolderTree.Items.Add(root);
            parts = FolderPath.RelativeParts(rootPath, folder, FolderPath.Comparison) ?? Array.Empty<string>();
        }

        var node = root;
        foreach (var part in parts)
        {
            await LoadChildrenAsync(node);
            if (seq != treeSyncSeq) return;
            var child = FindChild(node, part);
            if (child == null)
            {
                // あとから作られたフォルダや、隠しフォルダ。読み直して、それでもなければ足す
                await ReloadChildrenAsync(node);
                if (seq != treeSyncSeq) return;
                child = FindChild(node, part);
                if (child == null)
                {
                    string path = System.IO.Path.Combine(((FolderNode)node.Tag!).Path, part);
                    child = CreateNode(part, path, drive: false, hasChildren: true);
                    node.Items.Add(child);
                }
            }
            node.IsExpanded = true;
            node = child;
        }
        node.IsExpanded = true; // 開いたフォルダの中のフォルダも見えるようにする

        syncingTree = true;
        try { FolderTree.SelectedItem = node; }
        finally { syncingTree = false; }
        Dispatcher.UIThread.Post(() => node.BringIntoView(), DispatcherPriority.Loaded);
    }

    static TreeViewItem? FindChild(TreeViewItem parent, string name) =>
        parent.Items.OfType<TreeViewItem>().FirstOrDefault(c =>
            c.Tag is FolderNode n && string.Equals(System.IO.Path.GetFileName(n.Path), name, FolderPath.Comparison));

    /// <summary>中のフォルダを読み直す。開いていた項目は、読み直したあとも開いておく。</summary>
    async Task ReloadChildrenAsync(TreeViewItem item)
    {
        if (item.Tag is not FolderNode node) return;
        var expanded = item.Items.OfType<TreeViewItem>()
            .Where(c => c.IsExpanded && c.Tag is FolderNode)
            .Select(c => ((FolderNode)c.Tag!).Path).ToList();
        node.Loading = null;
        await LoadChildrenAsync(item);
        foreach (var c in item.Items.OfType<TreeViewItem>())
            if (c.Tag is FolderNode n && expanded.Any(p => string.Equals(p, n.Path, FolderPath.Comparison)))
                c.IsExpanded = true;
    }

    void FolderTree_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (syncingTree) return;
        if (FolderTree.SelectedItem is TreeViewItem { Tag: FolderNode node }) NavigateTo(node.Path);
    }

    // ───── 右クリックメニュー ─────

    TreeViewItem? menuTarget; // 右クリックした項目(右クリックでは選択を変えない = そのフォルダを開かない)
    FolderNode? MenuNode => menuTarget?.Tag as FolderNode;

    void FolderTree_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(FolderTree).Properties.IsRightButtonPressed)
        {
            menuTarget = null;
            return;
        }
        menuTarget = (e.Source as Visual)?.FindAncestorOfType<TreeViewItem>(includeSelf: true);
        e.Handled = true;
    }

    void FolderTreeMenu_Opening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        menuTarget ??= FolderTree.SelectedItem as TreeViewItem; // キーボード(メニューキー)で開いたとき
        FtReveal.IsEnabled = FtCopyPath.IsEnabled = MenuNode != null;
    }

    void FolderTree_Reveal(object? sender, RoutedEventArgs e)
    {
        if (MenuNode is not { } node) return;
        try { PlatformShell.Reveal(null, node.Path); }
        catch (Exception ex) { Status.Text = "開けませんでした: " + ex.Message; }
    }

    void FolderTree_CopyPath(object? sender, RoutedEventArgs e)
    {
        if (MenuNode is { } node) CopyText(node.Path);
    }

    async void FolderTree_Refresh(object? sender, RoutedEventArgs e)
    {
        if (menuTarget is { Tag: FolderNode } item) await ReloadChildrenAsync(item);
        else
        {
            FolderTree.Items.Clear();
            treeRootsTask = BuildTreeRootsAsync();
            await treeRootsTask;
        }
        if (currentFolder != null) SyncFolderTree(currentFolder);
    }

    // ───── 最小化・高さ ─────

    RowDefinition FolderTreeRow => LeftPanel.RowDefinitions[1];

    void FolderTree_Toggle(object? sender, RoutedEventArgs e)
    {
        settings.FolderTreeCollapsed = !settings.FolderTreeCollapsed;
        settings.Save();
        ApplyFolderTreeLayout();
    }

    void ApplyFolderTreeLayout()
    {
        bool collapsed = settings.FolderTreeCollapsed;
        FolderTreeArrow.Text = collapsed ? "▸" : "▾";
        FolderTreeBox.IsVisible = FolderTreeSplitter.IsVisible = !collapsed;
        FolderTreeRow.Height = collapsed ? new GridLength(0) : new GridLength(Math.Max(80, settings.FolderTreeHeight));
        LeftPanel.RowDefinitions[2].Height = new GridLength(collapsed ? 0 : 8);
    }

    void FolderTreeSplitter_DragCompleted(object? sender, Avalonia.Input.VectorEventArgs e)
    {
        settings.FolderTreeHeight = Math.Round(FolderTreeRow.ActualHeight);
        settings.Save();
    }

    // ───── サブフォルダの読み込みの深さ(メニューと同じ設定) ─────

    void SelectDepthBox()
    {
        bool prev = suppressDepthChange;
        suppressDepthChange = true;
        TreeDepthBox.SelectedItem = TreeDepthBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(i => i.Tag?.ToString() == settings.SubfolderDepth.ToString());
        suppressDepthChange = prev;
    }

    void TreeDepth_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (suppressDepthChange) return;
        if (TreeDepthBox.SelectedItem is ComboBoxItem { Tag: string tag } && int.TryParse(tag, out int depth))
            SetSubfolderDepth(depth);
    }
}
