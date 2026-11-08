using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PromptLoupe.Metadata;

namespace PromptLoupe;

/// <summary>パラメーターの1行(名前と値)。XAML のバインディング用。</summary>
public sealed record KeyValue(string Key, string Value);

public partial class MainWindow : Window
{
    static readonly string[] ImageExts = { ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif", ".tif", ".tiff", ".heic", ".heif" };

    readonly AppSettings settings = AppSettings.Load();
    readonly string[] startupArgs;

    List<FileItem> items = new();                 // フォルダの画像すべて
    ObservableCollection<FileItem> shown = new(); // 絞り込み・並び替えのあと、一覧に出しているもの
    CancellationTokenSource? scanCts;
    string? currentFolder;
    int loadSeq;
    readonly ImagePreloader preloader = new(); // 表示用の画像と、その前後の先読み
    int lastShownIndex = -1; // 前に表示した画像の、一覧での位置(送る向きを知るため)
    int direction = 1;       // 送っている向き(↓ なら 1、↑ なら -1)。1 つずつ動いたときだけ更新する
    int folderSeq;
    bool initializing = true;

    FileItem? currentItem;       // 右パネルと中央に表示している項目(複数選択のときも、表示は1枚)
    bool suppressSelection;      // 一覧を作り直す間・項目をまとめて取り除く間、画像の読み込みを止める

    // 画像の拡大と移動(RenderTransform)
    readonly ScaleTransform Scale = new();
    readonly TranslateTransform Trans = new();

    bool dragging;
    Point dragStart;
    Point panOrigin;

    public MainWindow() : this(Array.Empty<string>()) { }

    public MainWindow(string[] args)
    {
        startupArgs = args;
        ApplyTheme(); // 画面を作る前に適用して、起動時のちらつきを防ぐ
        InitializeComponent();

        Img.RenderTransform = new TransformGroup { Children = { Scale, Trans } };
        Resources["ThumbSize"] = (double)settings.ThumbnailSize;
        CheckOnly(ThemeMenu, settings.Theme);
        CheckOnly(ThumbSizeMenu, settings.ThumbnailSize.ToString());
        CheckOnly(SubfolderMenu, settings.SubfolderDepth.ToString());
        ThumbMenu.IsChecked = settings.ShowThumbnails;
        AllowDeleteMenu.IsChecked = settings.AllowDelete;
        ConfirmDeleteMenu.IsChecked = settings.ConfirmDelete;
        TagColorMenu.IsChecked = settings.ColorTags;
        ShowGpsMenu.IsChecked = settings.ShowGps;
        TgtFileName.IsChecked = settings.FilterFileName;
        TgtPrompt.IsChecked = settings.FilterPrompt;
        TgtNegative.IsChecked = settings.FilterNegative;
        TgtCharacters.IsChecked = settings.FilterCharacters;
        TgtCharacterNegative.IsChecked = settings.FilterCharacterNegative;
        TgtModel.IsChecked = settings.FilterModel;
        FmReveal.Header = $"{PlatformShell.FileManagerName}で表示(_E)";
        TagStatsMenu.InputGesture = new KeyGesture(Key.T, CommandKey);
        FmDelete.InputGesture = OperatingSystem.IsMacOS() ? new KeyGesture(Key.Back, KeyModifiers.Meta) : new KeyGesture(Key.Delete);
        FmCut.InputGesture = new KeyGesture(Key.X, CommandKey);
        FmCopy.InputGesture = new KeyGesture(Key.C, CommandKey);
        FmPaste.InputGesture = new KeyGesture(Key.V, CommandKey);
        FmCopyPath.InputGesture = new KeyGesture(Key.C, CommandKey | KeyModifiers.Shift);
        FmProps.IsVisible = FmPropsSeparator.IsVisible = PlatformShell.CanShowProperties;

        UpdateTagDictStatus();
        UpdateViewModeButtons();
        ApplyListTemplate();
        ApplySections();
        FileList.ItemsSource = shown;
        ShowMeta(null, null);
        LoadSavedTagDictionary();

        AddHandler(KeyDownEvent, Window_KeyDown, RoutingStrategies.Tunnel);
        AddHandler(DragDrop.DragOverEvent, Window_DragOver);
        AddHandler(DragDrop.DropEvent, Window_Drop);
        SetupFileListDrag();
        Viewport.PointerWheelChanged += Viewport_PointerWheelChanged;
        Viewport.PointerPressed += Viewport_PointerPressed;
        Viewport.PointerMoved += Viewport_PointerMoved;
        Viewport.PointerReleased += Viewport_PointerReleased;
        Viewport.SizeChanged += (_, _) => UpdateZoomText();

        initializing = false;
        Opened += (_, _) =>
        {
            if (startupArgs.Length > 0) OpenPath(startupArgs[0]);
        };
    }

    /// <summary>ショートカットの修飾キー(Windows・Linux は Ctrl、Mac は ⌘)。</summary>
    KeyModifiers CommandKey => PlatformSettings?.HotkeyConfiguration.CommandModifiers ?? KeyModifiers.Control;

    // ───────────────────── フォルダ / ファイルを開く ─────────────────────

    /// <summary>一覧を丸ごと差し替える。</summary>
    void SetItems(List<FileItem> files)
    {
        currentItem = null;
        items = files;
        RefreshView();
        NotifyTagStats();
    }

    /// <summary>
    /// 絞り込みと並び替えをやり直して、一覧を作り直す。選んでいた項目と、表示中の1枚はそのまま。
    /// (WPF 版の ListCollectionView.Refresh の代わり)
    /// </summary>
    void RefreshView()
    {
        var keep = FileList.SelectedItems?.OfType<FileItem>().ToList() ?? new List<FileItem>();
        var list = items.Where(FilterItem).ToList();
        list.Sort(new FileItemComparer(SortKey, DescBox.IsChecked == true));

        suppressSelection = true;
        try
        {
            shown = new ObservableCollection<FileItem>(list);
            FileList.ItemsSource = shown;
            // Avalonia は一覧を差し替えても前の選択を残すことがあるので、いったん消してから選び直す
            FileList.Selection.Clear();
            var index = new Dictionary<FileItem, int>(list.Count);
            for (int i = 0; i < list.Count; i++) index[list[i]] = i;
            foreach (var f in keep)
                if (index.TryGetValue(f, out int i)) FileList.Selection.Select(i);
        }
        finally
        {
            suppressSelection = false;
        }

        var focus = currentItem != null && shown.Contains(currentItem) ? currentItem : FileList.SelectedItem;
        if (focus != null) FileList.ScrollIntoView(focus);
        UpdateCount();
    }

    /// <summary>
    /// フォルダ内の画像を列挙する。maxDepth: 0 = 直下のみ、1〜 = その階層のサブフォルダまで、負数 = 無制限。
    /// アクセスできないフォルダとシンボリックリンク/ジャンクション(ループ防止)は飛ばす。
    /// </summary>
    static IEnumerable<string> EnumerateImages(string root, int maxDepth)
    {
        var opt = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0 };
        var stack = new Stack<(string Dir, int Depth)>();
        stack.Push((root, 0));

        while (stack.Count > 0)
        {
            var (dir, depth) = stack.Pop();

            List<string> files;
            try { files = Directory.EnumerateFiles(dir, "*", opt).Where(IsImage).ToList(); }
            catch { files = new List<string>(); }
            foreach (var f in files) yield return f;

            if (maxDepth >= 0 && depth >= maxDepth) continue;

            List<string> subDirs;
            try { subDirs = Directory.EnumerateDirectories(dir, "*", opt).ToList(); }
            catch { continue; }

            foreach (var d in subDirs)
            {
                try
                {
                    if (new DirectoryInfo(d).Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                }
                catch { continue; }
                stack.Push((d, depth + 1));
            }
        }
    }

    static bool IsImage(string path) =>
        ImageExts.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    void OpenPath(string path)
    {
        if (Directory.Exists(path)) OpenFolder(path, null);
        else if (File.Exists(path)) OpenFolder(Path.GetDirectoryName(Path.GetFullPath(path))!, Path.GetFullPath(path));
    }

    async void OpenFolder(string folder, string? select)
    {
        scanCts?.Cancel();
        int seq = ++folderSeq;
        currentFolder = folder;
        int depth = settings.SubfolderDepth;
        Status.Text = "フォルダを読み込み中…";

        List<FileItem> files;
        try
        {
            files = await Task.Run(() =>
                EnumerateImages(folder, depth)
                    .Select(p => new FileItem(p, folder))
                    .ToList());
        }
        catch (Exception ex)
        {
            if (seq == folderSeq) Status.Text = "フォルダを開けません: " + ex.Message;
            return;
        }
        if (seq != folderSeq) return; // 別のフォルダが開かれた

        SetItems(files);
        StartWatcher(folder, depth); // ほかのアプリでの移動・削除・名前変更を、一覧に反映する

        Title = $"PromptLoupe - {folder}" + depth switch
        {
            0 => "",
            < 0 => "(サブフォルダ: 無制限)",
            _ => $"(サブフォルダ: {depth}階層まで)"
        };
        Status.Text = $"{files.Count} 枚の画像";

        if (files.Count == 0)
        {
            ClearImage();
            return;
        }

        var target = select == null ? null : shown.FirstOrDefault(f => PathEquals(f.Path, select));
        if (target != null) FileList.SelectedItem = target;
        else FileList.SelectedIndex = 0;

        StartScan(files);
    }

    /// <summary>パスが同じか(Windows・Mac は大文字・小文字を区別しない)。</summary>
    static bool PathEquals(string a, string b) =>
        string.Equals(a, b, OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

    /// <summary>バックグラウンドで全画像のメタデータを読み、ソート・絞り込みに使えるようにする。</summary>
    void StartScan(List<FileItem> files)
    {
        scanCts?.Cancel();
        var cts = scanCts = new CancellationTokenSource();
        var token = cts.Token;
        int total = files.Count;
        int done = 0;

        _ = Task.Run(() =>
        {
            try
            {
                Parallel.ForEach(files,
                    new ParallelOptions
                    {
                        CancellationToken = token,
                        MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2)
                    },
                    f =>
                    {
                        var meta = MetadataReader.Read(f.Path);
                        int n = Interlocked.Increment(ref done);
                        Dispatcher.UIThread.Post(() =>
                        {
                            if (token.IsCancellationRequested) return;
                            f.Meta = meta;
                            if (n % 25 == 0 || n == total)
                            {
                                Status.Text = $"メタデータ読み込み中… {n}/{total}";
                                NotifyTagStats();
                            }
                        });
                    });
            }
            catch (OperationCanceledException)
            {
                return;
            }

            Dispatcher.UIThread.Post(() =>
            {
                if (token.IsCancellationRequested) return;
                RefreshView();
                NotifyTagStats();
                int withMeta = files.Count(f => f.Meta?.HasData == true);
                Status.Text = $"{total} 枚(うちAI生成メタデータあり: {withMeta})";
            });
        });
    }

    // ───────────────────── 並び替え / 絞り込み ─────────────────────

    string SortKey => (SortBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "Name";

    void Sort_Changed(object? sender, SelectionChangedEventArgs e) => ApplySort();
    void Sort_Changed(object? sender, RoutedEventArgs e) => ApplySort();

    void ApplySort()
    {
        if (DescBox == null || FileList == null) return; // 画面を作っている途中
        DescBox.Content = DescBox.IsChecked == true ? "▼ 降順" : "▲ 昇順";
        if (!initializing) RefreshView();
    }

    // カンマ(, と 、)区切りの検索語。各語の前後の空白は除き、語の中のスペースはそのまま使う。
    string[] filterTerms = Array.Empty<string>();

    void Filter_Changed(object? sender, TextChangedEventArgs e)
    {
        filterTerms = (FilterBox.Text ?? "")
            .Split(new[] { ',', '、', '，' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => PromptText.Normalize(t).Trim())
            .Where(t => t.Length > 0)
            .ToArray();
        RefreshView();
        NotifyTagStats();
    }

    // 検索対象の文字列も語も PromptText.Normalize 済み(小文字・_ はスペース・\ なし)なので、そのまま比べる
    static bool Has(string s, string term) => s.Contains(term, StringComparison.Ordinal);

    bool FilterItem(FileItem f)
    {
        if (filterTerms.Length == 0) return true;

        foreach (var term in filterTerms)
        {
            bool hit =
                (settings.FilterFileName && Has(f.NormName, term)) ||
                (settings.FilterPrompt && Has(f.NormPrompt, term)) ||
                (settings.FilterNegative && Has(f.NormNegative, term)) ||
                (settings.FilterModel && Has(f.NormModel, term)) ||
                (settings.FilterCharacters && Has(f.NormCharPrompts, term)) ||
                (settings.FilterCharacterNegative && Has(f.NormCharNegatives, term));
            if (!hit) return false;
        }
        return true;
    }

    /// <summary>ステータスバー右側に、絞り込み結果の件数を表示する。</summary>
    void UpdateCount()
    {
        int total = items.Count;
        int count = shown.Count;
        int selected = FileList.SelectedItems?.Count ?? 0;
        string text = total == 0 ? "" : count == total ? $"{total} 件" : $"{total} 件中 {count} 件を表示";
        if (total > 0 && selected > 1) text += $"  ・  {selected} 件を選択中";
        CountText.Text = text;
    }

    void FilterTarget_Changed(object? sender, RoutedEventArgs e)
    {
        if (initializing) return;

        // 検索対象が1つも無いと何もヒットしなくなるので、最後の1つは外せないようにする
        bool any = TgtFileName.IsChecked == true || TgtPrompt.IsChecked == true ||
                   TgtNegative.IsChecked == true || TgtCharacters.IsChecked == true ||
                   TgtCharacterNegative.IsChecked == true || TgtModel.IsChecked == true;
        if (!any && sender is CheckBox cb)
        {
            cb.IsChecked = true;
            return; // 付け直したことで、もう一度呼ばれる
        }

        settings.FilterFileName = TgtFileName.IsChecked == true;
        settings.FilterPrompt = TgtPrompt.IsChecked == true;
        settings.FilterNegative = TgtNegative.IsChecked == true;
        settings.FilterCharacters = TgtCharacters.IsChecked == true;
        settings.FilterCharacterNegative = TgtCharacterNegative.IsChecked == true;
        settings.FilterModel = TgtModel.IsChecked == true;
        settings.Save();

        RefreshView();
        NotifyTagStats();
    }

    // ───────────────────── 画像表示 ─────────────────────

    async void FileList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (suppressSelection) return;
        UpdateCount();

        // 複数選択のときも、表示するのは1枚。新しく選んだ1枚を表示し、範囲選択や全選択では表示を変えない
        var selected = FileList.SelectedItems?.OfType<FileItem>().ToList() ?? new List<FileItem>();
        FileItem? item;
        bool currentStillSelected = currentItem != null && selected.Contains(currentItem);
        if (e.AddedItems.Count == 1) item = e.AddedItems[0] as FileItem;
        else if (e.AddedItems.Count > 1) item = currentStillSelected ? null : e.AddedItems[0] as FileItem;
        else item = selected.Count == 0 || currentStillSelected ? null : selected[0];
        if (item == null) return;

        currentItem = item;
        FileList.ScrollIntoView(item);

        int seq = ++loadSeq;
        string path = item.Path;

        // メタデータ(スキャン済みならキャッシュを使う)
        var meta = item.Meta;
        if (meta == null)
        {
            meta = await Task.Run(() => MetadataReader.Read(path));
            if (seq != loadSeq) return;
            item.Meta = meta;
        }
        ShowMeta(item, meta);

        // 画像(前後の画像は先読みしておき、送ったときに待たずに出す)。
        // 送っている向きには 2 枚、逆向きには 1 枚(↓ で送っていれば 次・次の次・前)。近い順に先読みする
        int index = shown.IndexOf(item);
        int step = index - lastShownIndex;
        if (step is 1 or -1) direction = step;
        lastShownIndex = index;
        var keep = new List<string> { path };
        if (index >= 0)
        {
            foreach (int offset in new[] { direction, -direction, 2 * direction })
            {
                int i = index + offset;
                if (i >= 0 && i < shown.Count) keep.Add(shown[i].Path);
            }
        }
        try
        {
            var bmp = await preloader.Get(path);
            if (seq != loadSeq) return; // 読んだ画像は先読みとして残る(要らなければ、次の Keep で捨てる)
            SetImage(bmp);
            EmptyPanel.IsVisible = false;
            ApplyViewMode();
            Status.Text = $"{index + 1}/{shown.Count}   {bmp.PixelSize.Width}×{bmp.PixelSize.Height}   {item.Size / 1024.0:N0} KB";
        }
        catch (Exception ex)
        {
            if (seq != loadSeq) return;
            SetImage(null);
            EmptyPanel.IsVisible = true;
            // 表示できない理由(HEIC を読む OS の機能がない環境など)は、ステータスバーだとフォルダの読み込みの表示に上書きされるので、ここにも出す
            string hint = ImageLoader.UnsupportedHint(ex);
            Placeholder.Text = hint.Length > 0 ? "画像を表示できません\n" + hint.Trim('(', ')') : "画像を表示できません";
            Status.Text = $"読み込み失敗: {ex.Message}{ImageLoader.UnsupportedHint(ex)}";
        }

        // 表示を切り替えてから、前後以外の画像を手放し、前後を先読みする
        preloader.Keep(keep);
        foreach (var p in keep.Skip(1)) preloader.Prefetch(p);
    }

    /// <summary>表示する画像を替える。画像の持ち主は preloader なので、ここでは手放さない。</summary>
    void SetImage(Bitmap? bmp)
    {
        Img.Source = bmp;
        UpdateZoomText();
    }

    void ClearImage()
    {
        currentItem = null;
        SetImage(null);
        preloader.Clear();
        Placeholder.Text = "画像が見つかりません";
        EmptyPanel.IsVisible = true;
        ShowMeta(null, null);
    }

    void ShowMeta(FileItem? item, ImageMetadata? m)
    {
        FileInfoText.Text = item == null
            ? ""
            : $"{item.Name}\n{item.Modified:yyyy/MM/dd HH:mm:ss}  ・  {item.Size / 1024.0:N0} KB";

        if (m == null)
        {
            SourceText.Text = PromptBox.Text = NegativeBox.Text = RawBox.Text = "";
            BuildChips(PromptChips, null);
            BuildChips(NegativeChips, null);
            PromptCard.IsVisible = NegativeCard.IsVisible = ParamsCard.IsVisible = false;
            ParamsList.ItemsSource = OtherParamsList.ItemsSource = null;
            OtherExpander.IsVisible = false;
            ExifList.ItemsSource = null;
            ExifCard.IsVisible = false;
            BuildCharacters(null);
            return;
        }

        SourceText.Text = m.Error != null ? $"読み取りエラー: {m.Error}" : m.Source ?? "AI生成メタデータは見つかりません";

        PromptBox.Text = m.Prompt ?? "";
        NegativeBox.Text = m.NegativePrompt ?? "";
        BuildChips(PromptChips, m.Prompt);
        BuildChips(NegativeChips, m.NegativePrompt);
        PromptCard.IsVisible = !string.IsNullOrWhiteSpace(m.Prompt);
        NegativeCard.IsVisible = !string.IsNullOrWhiteSpace(m.NegativePrompt);

        // 主要なパラメーターと、その他に分ける
        var main = m.Params.Where(kv => MainParamKeys.Contains(kv.Key)).Select(kv => new KeyValue(kv.Key, kv.Value)).ToList();
        var other = m.Params.Where(kv => !MainParamKeys.Contains(kv.Key)).Select(kv => new KeyValue(kv.Key, kv.Value)).ToList();
        ParamsList.ItemsSource = main;
        OtherParamsList.ItemsSource = other;
        ParamsCard.IsVisible = main.Count > 0 || other.Count > 0;
        OtherExpander.IsVisible = other.Count > 0;

        BuildCharacters(m);

        var exif = (m.Exif?.Rows(settings.ShowGps) ?? new List<KeyValuePair<string, string>>())
            .Select(kv => new KeyValue(kv.Key, kv.Value)).ToList();
        ExifList.ItemsSource = exif;
        ExifCard.IsVisible = exif.Count > 0;

        RawBox.Text = string.Join("\n\n", m.Raw.Select(kv => $"[{kv.Key}]\n{kv.Value}"));
    }

    static readonly HashSet<string> MainParamKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "Model", "Sampler", "Scheduler", "Schedule type", "Steps", "CFG scale", "Seed", "Size",
        "Software", "Title", "Generation time", "LoRA"
    };

    // ───────────────────── カードの最小化 / 展開 ─────────────────────

    /// <summary>最小化できるカード: 設定に保存するキー、本体、見出しの矢印。</summary>
    (string Key, Control Body, TextBlock Arrow)[] Sections => new[]
    {
        ("Prompt", (Control)PromptBody, PromptArrow),
        ("Negative", NegativeBody, NegativeArrow),
        ("Characters", CharactersPanel, CharactersArrow),
        ("Params", ParamsBody, ParamsArrow),
        ("Exif", ExifList, ExifArrow),
    };

    /// <summary>設定に合わせて、各カードの本体の表示/非表示と、矢印(▾ 展開 / ▸ 最小化)を反映する。</summary>
    void ApplySections()
    {
        foreach (var (key, body, arrow) in Sections)
        {
            bool collapsed = settings.CollapsedSections.Contains(key);
            body.IsVisible = !collapsed;
            arrow.Text = collapsed ? "▸" : "▾";
        }
    }

    void Section_Toggle(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: string key }) return;
        if (!settings.CollapsedSections.Remove(key)) settings.CollapsedSections.Add(key);
        settings.Save();
        ApplySections();
    }

    // ───────────────────── タグ(チップ)表示 ─────────────────────

    /// <summary>プロンプトを、タグごとの小さなチップにして並べる。</summary>
    void BuildChips(Panel panel, string? text)
    {
        panel.Children.Clear();
        if (string.IsNullOrWhiteSpace(text)) return;
        foreach (var tag in PromptText.SplitTags(text))
            panel.Children.Add(MakeChip(tag));
    }

    Border MakeChip(string tag)
    {
        var style = ClassifyTag(tag);
        const string hint = "クリックで絞り込みに追加(もう一度クリックで外す)";

        var text = new TextBlock { Text = tag, TextWrapping = TextWrapping.Wrap, MaxWidth = 320, FontSize = 12 };
        var chip = new Border { Child = text };
        chip.Classes.Add("chip");
        ToolTip.SetTip(chip, style is { } s ? $"{s.Label}  ・  {hint}" : hint);

        if (style is { } st)
        {
            // 色分けしたタグ: 分類の色の文字と、同じ色の薄い背景
            var normal = new SolidColorBrush(st.Bg);
            var hover = new SolidColorBrush(st.BgHover);
            text.Foreground = new SolidColorBrush(st.Fg);
            chip.Background = normal;
            chip.PointerEntered += (_, _) => chip.Background = hover;
            chip.PointerExited += (_, _) => chip.Background = normal;
        }
        else
        {
            chip.Classes.Add("plain"); // 背景はテーマの色(マウスを乗せると変わる)
        }

        chip.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton != MouseButton.Left) return;
            ToggleFilterTag(tag);
            e.Handled = true;
        };
        return chip;
    }

    /// <summary>タグを絞り込み欄に追加する。すでに入っているタグなら、外す。</summary>
    internal void ToggleFilterTag(string tag)
    {
        var clean = PromptText.CleanTag(tag);
        if (clean.Length == 0) return;
        var norm = PromptText.Normalize(clean).Trim();

        var parts = (FilterBox.Text ?? "")
            .Split(new[] { ',', '、', '，' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToList();

        int removed = parts.RemoveAll(p => PromptText.Normalize(p).Trim() == norm);
        if (removed == 0) parts.Add(clean);

        FilterBox.Text = string.Join(", ", parts);
        FilterBox.CaretIndex = FilterBox.Text.Length;
        Status.Text = removed == 0 ? $"絞り込みに追加: {clean}" : $"絞り込みから外しました: {clean}";
    }

    /// <summary>キャラクター別プロンプト欄を作る(該当しない画像では非表示)。</summary>
    void BuildCharacters(ImageMetadata? m)
    {
        CharactersPanel.Children.Clear();
        if (m == null || m.Characters.Count == 0)
        {
            CharactersCard.IsVisible = false;
            return;
        }

        CharactersCard.IsVisible = true; // 見出しは XAML 側(最小化ボタンを兼ねる)にある

        foreach (var c in m.Characters)
        {
            string title = $"キャラクター {c.Index}" + (c.Position != null ? $"  [{c.Position}]" : "");
            CharactersPanel.Children.Add(MakeHeader(title, c.Prompt, true));
            var chips = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            BuildChips(chips, c.Prompt);
            CharactersPanel.Children.Add(chips);

            if (!string.IsNullOrWhiteSpace(c.Negative))
            {
                CharactersPanel.Children.Add(MakeHeader("ネガティブ", c.Negative, false));
                var negChips = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
                BuildChips(negChips, c.Negative);
                CharactersPanel.Children.Add(negChips);
            }
        }
    }

    DockPanel MakeHeader(string title, string? copyText, bool bold)
    {
        var header = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var copy = new Button { Content = "コピー" };
        copy.Classes.Add("small");
        copy.Click += (_, _) => CopyText(copyText ?? "");
        DockPanel.SetDock(copy, Dock.Right);
        header.Children.Add(copy);
        header.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
            VerticalAlignment = VerticalAlignment.Center
        });
        return header;
    }

    // ───────────────────── ズーム / パン ─────────────────────
    // 画像は Viewport の中にフィットして置かれ、その上に拡大(Scale)と移動(Trans)をかける。
    // 変換の原点は、フィットした画像の左上(Viewport の中で中央寄せされた位置)。

    /// <summary>フィットしたときの倍率と、画像の左上の位置(Viewport の座標)。画像がなければ null。</summary>
    (double Fit, Point Origin)? FitInfo()
    {
        if (Img.Source is not Bitmap bmp) return null;
        double vw = Viewport.Bounds.Width, vh = Viewport.Bounds.Height;
        double sw = bmp.Size.Width, sh = bmp.Size.Height;
        if (vw <= 0 || vh <= 0 || sw <= 0 || sh <= 0) return null;
        double fit = Math.Min(vw / sw, vh / sh);
        return (fit, new Point((vw - sw * fit) / 2, (vh - sh * fit) / 2));
    }

    /// <summary>現在選んでいる表示方式(フィット / 等倍)を適用する。画像を切り替えたときもこれを使う。</summary>
    void ApplyViewMode()
    {
        if (settings.ActualSizeMode) ActualSize();
        else ResetView();
        UpdateViewModeButtons();
    }

    /// <summary>フィット表示にして、その方式を以後の画像にも引き継ぐ。</summary>
    void SetFitMode()
    {
        settings.ActualSizeMode = false;
        settings.Save();
        ApplyViewMode();
    }

    /// <summary>等倍(100%)表示にして、その方式を以後の画像にも引き継ぐ。</summary>
    void SetActualMode()
    {
        settings.ActualSizeMode = true;
        settings.Save();
        ApplyViewMode();
    }

    void UpdateViewModeButtons()
    {
        FitButton.Classes.Set("bold", !settings.ActualSizeMode);
        ActualButton.Classes.Set("bold", settings.ActualSizeMode);
    }

    void ResetView()
    {
        Scale.ScaleX = Scale.ScaleY = 1;
        Trans.X = Trans.Y = 0;
        UpdateScalingMode();
        UpdateZoomText();
    }

    double RenderScale => RenderScaling > 0 ? RenderScaling : 1;

    /// <summary>画像の右下に、現在の表示倍率(実際のピクセルに対する割合)を出す。</summary>
    void UpdateZoomText()
    {
        if (FitInfo() is not { } fi || Img.Source is not Bitmap bmp)
        {
            ZoomText.Text = "";
            return;
        }
        double percent = fi.Fit * Scale.ScaleX * bmp.Size.Width * RenderScale / bmp.PixelSize.Width * 100;
        ZoomText.Text = $"{percent:0}%";
    }

    void ActualSize()
    {
        if (FitInfo() is not { } fi || Img.Source is not Bitmap bmp) return;

        double target = (bmp.PixelSize.Width / RenderScale) / (fi.Fit * bmp.Size.Width);

        // Viewport の中心を軸にして拡大する
        double cx = Viewport.Bounds.Width / 2 - fi.Origin.X, cy = Viewport.Bounds.Height / 2 - fi.Origin.Y;
        Scale.ScaleX = Scale.ScaleY = target;
        Trans.X = cx - cx * target;
        Trans.Y = cy - cy * target;
        UpdateScalingMode();
        UpdateZoomText();
    }

    /// <summary>大きく拡大したときは、ぼかさずに画素をそのまま見せる。</summary>
    void UpdateScalingMode() =>
        RenderOptions.SetBitmapInterpolationMode(Img,
            Scale.ScaleX >= 3 ? BitmapInterpolationMode.None : BitmapInterpolationMode.HighQuality);

    void Viewport_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (FitInfo() is not { } fi) return;
        var p = e.GetPosition(Viewport) - fi.Origin;
        double old = Scale.ScaleX;
        double ns = Math.Clamp(old * (e.Delta.Y > 0 ? 1.15 : 1 / 1.15), 0.05, 64);
        double f = ns / old;

        Trans.X = p.X - (p.X - Trans.X) * f;
        Trans.Y = p.Y - (p.Y - Trans.Y) * f;
        Scale.ScaleX = Scale.ScaleY = ns;
        UpdateScalingMode();
        UpdateZoomText();
        e.Handled = true;
    }

    void Viewport_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Img.Source == null || !e.GetCurrentPoint(Viewport).Properties.IsLeftButtonPressed) return;
        if (e.Source is Visual v && v.FindAncestorOfType<Button>(includeSelf: true) != null) return; // 右下のボタン
        if (e.ClickCount == 2) { ApplyViewMode(); return; } // 手動のズーム・移動を、選択中の表示方式に戻す

        dragging = true;
        dragStart = e.GetPosition(Viewport);
        panOrigin = new Point(Trans.X, Trans.Y);
        e.Pointer.Capture(Viewport);
        Viewport.Cursor = new Cursor(StandardCursorType.SizeAll);
    }

    void Viewport_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!dragging) return;
        var p = e.GetPosition(Viewport);
        Trans.X = panOrigin.X + (p.X - dragStart.X);
        Trans.Y = panOrigin.Y + (p.Y - dragStart.Y);
    }

    void Viewport_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!dragging) return;
        dragging = false;
        e.Pointer.Capture(null);
        Viewport.Cursor = Cursor.Default;
    }

    void Fit_Click(object? sender, RoutedEventArgs e) => SetFitMode();
    void Actual_Click(object? sender, RoutedEventArgs e) => SetActualMode();

    // ───────────────────── 操作 ─────────────────────

    async void OpenFolder_Click(object? sender, RoutedEventArgs e)
    {
        var result = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "画像フォルダを選択",
            AllowMultiple = false,
        });
        if (result.Count > 0 && result[0].TryGetLocalPath() is { } path) OpenFolder(path, null);
    }

    async void OpenFile_Click(object? sender, RoutedEventArgs e)
    {
        var result = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "画像を選択",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("画像ファイル")
                {
                    Patterns = ImageExts.Select(x => "*" + x).ToArray(),
                    AppleUniformTypeIdentifiers = new[] { "public.image" },
                    MimeTypes = new[] { "image/*" },
                },
                FilePickerFileTypes.All,
            },
        });
        if (result.Count > 0 && result[0].TryGetLocalPath() is { } path) OpenPath(path);
    }

    // ───────────────────── テーマ / サムネイル / サブフォルダ ─────────────────────

    /// <summary>設定のテーマを適用する。"System" は OS の設定に合わせる。</summary>
    void ApplyTheme()
    {
        if (Application.Current == null) return;
        Application.Current.RequestedThemeVariant = settings.Theme switch
        {
            "Dark" => ThemeVariant.Dark,
            "Light" => ThemeVariant.Light,
            _ => ThemeVariant.Default
        };
    }

    /// <summary>ラジオボタンのように、現在の値に当たる項目だけにチェックを付ける。</summary>
    static void CheckOnly(MenuItem parent, string current)
    {
        foreach (var o in parent.Items)
            if (o is MenuItem mi) mi.IsChecked = mi.Tag?.ToString() == current;
    }

    void Theme_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string theme }) return;
        settings.Theme = theme;
        settings.Save();
        ApplyTheme();
        CheckOnly(ThemeMenu, settings.Theme);
        // テーマの切り替えが画面に反映されてから、タグの色をテーマ(ダーク/ライト)に合わせて作り直す
        Dispatcher.UIThread.Post(RefreshMetaView);
    }

    void ThumbSize_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi || !int.TryParse(mi.Tag?.ToString(), out int size)) return;
        settings.ThumbnailSize = size;
        settings.Save();
        Resources["ThumbSize"] = (double)size;
        CheckOnly(ThumbSizeMenu, size.ToString());
    }

    void Thumb_Click(object? sender, RoutedEventArgs e)
    {
        settings.ShowThumbnails = !settings.ShowThumbnails;
        ThumbMenu.IsChecked = settings.ShowThumbnails;
        settings.Save();
        ApplyListTemplate();
    }

    void ShowGps_Click(object? sender, RoutedEventArgs e)
    {
        settings.ShowGps = !settings.ShowGps;
        ShowGpsMenu.IsChecked = settings.ShowGps;
        settings.Save();
        if (currentItem != null) ShowMeta(currentItem, currentItem.Meta);
    }

    /// <summary>サムネイル設定に合わせて、一覧の表示テンプレートを切り替える。</summary>
    void ApplyListTemplate() =>
        FileList.ItemTemplate = (Avalonia.Controls.Templates.IDataTemplate)Resources[
            settings.ShowThumbnails ? "ThumbItemTemplate" : "TextItemTemplate"]!;

    void SubfolderDepth_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi || !int.TryParse(mi.Tag?.ToString(), out int depth)) return;

        settings.SubfolderDepth = depth;
        settings.Save();
        CheckOnly(SubfolderMenu, depth.ToString());

        // 開いているフォルダを新しい設定で読み込み直す(表示中の画像は維持)
        if (currentFolder != null) OpenFolder(currentFolder, currentItem?.Path);
    }

    void Exit_Click(object? sender, RoutedEventArgs e) => Close();

    HelpWindow? helpWindow;

    void Help_Click(object? sender, RoutedEventArgs e) => ShowHelp();

    /// <summary>使い方を開く。すでに開いていれば前面に出す(ウィンドウを増やさない)。</summary>
    void ShowHelp()
    {
        if (helpWindow != null)
        {
            helpWindow.Activate();
            return;
        }
        helpWindow = new HelpWindow();
        helpWindow.Closed += (_, _) => helpWindow = null;
        helpWindow.Show(this);
    }

    LicenseWindow? licenseWindow;

    /// <summary>ライセンス文を開く。すでに開いていれば前面に出す。</summary>
    void License_Click(object? sender, RoutedEventArgs e)
    {
        if (licenseWindow != null)
        {
            licenseWindow.Activate();
            return;
        }
        licenseWindow = new LicenseWindow();
        licenseWindow.Closed += (_, _) => licenseWindow = null;
        licenseWindow.Show(this);
    }

    async void About_Click(object? sender, RoutedEventArgs e)
    {
        var asm = typeof(App).Assembly;
        string version = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                         ?? asm.GetName().Version?.ToString() ?? "不明";
        int plus = version.IndexOf('+');
        if (plus >= 0) version = version[..plus];
        string copyright = asm.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? "";
        string avalonia = typeof(Application).Assembly.GetName().Version?.ToString(3) ?? "";

        await MessageDialog.Show(this,
            $"PromptLoupe\nバージョン {version}\n{copyright}(MIT License)\n\n" +
            "AI生成画像のメタデータを元に、並び替え・絞り込みして閲覧できる画像ビューア\n\n" +
            $"使用ライブラリ: Avalonia {avalonia}、SkiaSharp(どちらも MIT License)、LibTiff.Net(BSD License)など\n" +
            "ライセンス文は、メニューの「ヘルプ → ライセンス」で読めます。\n\n" +
            $"{RuntimeInformation.FrameworkDescription}  ・  {RuntimeInformation.OSDescription}",
            "バージョン情報");
    }

    void Window_DragOver(object? sender, DragEventArgs e)
    {
        // 一覧から持ち出すドラッグが、自分のウィンドウの上にあるときは、落とせない印にする
        e.DragEffects = !draggingOut && e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    void Window_Drop(object? sender, DragEventArgs e)
    {
        if (draggingOut) return; // 一覧から持ち出すドラッグが、自分のウィンドウに落ちたときは開き直さない
        var first = e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).FirstOrDefault(p => p != null);
        if (first != null) OpenPath(first);
    }

    void CopyPrompt_Click(object? sender, RoutedEventArgs e) => CopyText(PromptBox.Text ?? "");
    void CopyNegative_Click(object? sender, RoutedEventArgs e) => CopyText(NegativeBox.Text ?? "");

    async void CopyText(string text)
    {
        if (string.IsNullOrEmpty(text) || Clipboard == null) return;
        try
        {
            await Clipboard.SetTextAsync(text);
            Status.Text = "クリップボードにコピーしました";
        }
        catch (Exception ex)
        {
            Status.Text = "コピーに失敗しました: " + ex.Message;
        }
    }

    void Move(int delta)
    {
        int count = shown.Count;
        if (count == 0) return;
        int from = currentItem != null ? shown.IndexOf(currentItem) : FileList.SelectedIndex;
        int idx = (int)Math.Clamp((long)Math.Max(from, 0) + delta, 0, count - 1);
        FileList.SelectedIndex = idx;
    }

    void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        var mod = e.KeyModifiers;
        var cmd = CommandKey;

        if (e.Key == Key.F1)
        {
            ShowHelp();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.F11)
        {
            ToggleFullscreen();
            e.Handled = true;
            return;
        }
        if (mod == cmd && e.Key == Key.T)
        {
            ShowTagStats();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape && isFullscreen)
        {
            ExitFullscreen();
            e.Handled = true;
            return;
        }

        var focused = FocusManager?.GetFocusedElement();
        if (focused is TextBox) return;
        // 右パネルの文字を選んでいるときの Ctrl+C は、その文字のコピーに任せる
        if (focused is SelectableTextBlock { SelectedText.Length: > 0 } && mod == cmd && e.Key == Key.C) return;

        // ファイル操作
        bool handled = true;
        if (mod == cmd && e.Key == Key.C) CopyFiles(cut: false);
        else if (mod == cmd && e.Key == Key.X) CopyFiles(cut: true);
        else if (mod == cmd && e.Key == Key.V) PasteFiles();
        else if (mod == (cmd | KeyModifiers.Shift) && e.Key == Key.C) CopyPaths();
        else if (PlatformShell.CanShowProperties && mod == KeyModifiers.Alt && e.Key == Key.Enter) ShowProperties();
        else if (mod == KeyModifiers.None && e.Key == Key.F2) RenameSelected();
        else if (mod == KeyModifiers.None && e.Key == Key.Delete) DeleteSelected();
        else if (OperatingSystem.IsMacOS() && mod == KeyModifiers.Meta && e.Key == Key.Back) DeleteSelected(); // Mac の「ゴミ箱に入れる」
        else handled = false;
        if (handled)
        {
            e.Handled = true;
            return;
        }

        // Shift / Ctrl / Alt を押しているときは、一覧の範囲選択などに任せる
        if (mod != KeyModifiers.None) return;

        switch (e.Key)
        {
            case Key.Right:
            case Key.PageDown:
            case Key.Space:
                Move(1); break;
            case Key.Left:
            case Key.PageUp:
            case Key.Back:
                Move(-1); break;
            case Key.Home:
                Move(-int.MaxValue / 2); break;
            case Key.End:
                Move(int.MaxValue / 2); break;
            case Key.F:
                SetFitMode(); break;
            case Key.D1:
            case Key.NumPad1:
                SetActualMode(); break;
            default:
                return;
        }
        e.Handled = true;
    }
}
