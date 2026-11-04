using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using PromptLoupe.Metadata;
using ImageMetadata = PromptLoupe.Metadata.ImageMetadata; // System.Windows.Media.ImageMetadata との曖昧さ回避

namespace PromptLoupe;

public partial class MainWindow : Window
{
    static readonly string[] ImageExts = { ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif", ".tif", ".tiff", ".heic", ".heif" };

    ObservableCollection<FileItem> items = new();
    readonly AppSettings settings = AppSettings.Load();
    ListCollectionView? view;
    CancellationTokenSource? scanCts;
    string? currentFolder;
    int loadSeq;
    int folderSeq;

    FileItem? currentItem;       // 右パネルと中央に表示している項目(複数選択のときも、表示は1枚)
    bool suppressSelection;      // 一覧から項目をまとめて取り除く間、画像の読み込みを止める

    bool dragging;
    Point dragStart;
    Point panOrigin;

    public MainWindow()
    {
        ApplyTheme(); // 画面を作る前に適用して、起動時のちらつきを防ぐ
        InitializeComponent();
        Resources["ThumbSize"] = (double)settings.ThumbnailSize;
        UpdateThemeMenu();
        UpdateThumbSizeMenu();
        UpdateSubfolderMenu();
        ThumbMenu.IsChecked = settings.ShowThumbnails;
        AllowDeleteMenu.IsChecked = settings.AllowDelete;
        ConfirmDeleteMenu.IsChecked = settings.ConfirmDelete;
        TagColorMenu.IsChecked = settings.ColorTags;
        ShowGpsMenu.IsChecked = settings.ShowGps;
        UpdateTagDictStatus();
        UpdateViewModeButtons();
        ApplyListTemplate();
        ApplySections();
        TgtFileName.IsChecked = settings.FilterFileName;
        TgtPrompt.IsChecked = settings.FilterPrompt;
        TgtNegative.IsChecked = settings.FilterNegative;
        TgtCharacters.IsChecked = settings.FilterCharacters;
        TgtCharacterNegative.IsChecked = settings.FilterCharacterNegative;
        TgtModel.IsChecked = settings.FilterModel;

        SetItems(new List<FileItem>());
        LoadSavedTagDictionary();

        Loaded += (_, _) =>
        {
            var args = Environment.GetCommandLineArgs();
            if (args.Length > 1) OpenPath(args[1]);
        };
    }

    // ───────────────────── フォルダ / ファイルを開く ─────────────────────

    /// <summary>一覧を丸ごと差し替える。新しいコレクション/ビューを作るので、ソートは1回で済む。</summary>
    void SetItems(List<FileItem> files)
    {
        currentItem = null;
        items = new ObservableCollection<FileItem>(files);
        view = new ListCollectionView(items);
        view.Filter = FilterItem;
        FileList.ItemsSource = view;
        ApplySort();
        UpdateCount();
        NotifyTagStats();
    }

    /// <summary>
    /// フォルダ内の画像を列挙する。maxDepth: 0 = 直下のみ、1〜 = その階層のサブフォルダまで、負数 = 無制限。
    /// アクセスできないフォルダとシンボリックリンク/ジャンクション(ループ防止)は飛ばす。
    /// </summary>
    static IEnumerable<string> EnumerateImages(string root, int maxDepth)
    {
        var opt = new EnumerationOptions { IgnoreInaccessible = true };
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
        else if (File.Exists(path)) OpenFolder(Path.GetDirectoryName(path)!, path);
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

        var target = select == null
            ? null
            : files.FirstOrDefault(f => string.Equals(f.Path, select, StringComparison.OrdinalIgnoreCase));
        if (target != null) FileList.SelectedItem = target;
        else FileList.SelectedIndex = 0;

        StartScan(files);
    }

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
                        Dispatcher.BeginInvoke(() =>
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

            Dispatcher.BeginInvoke(() =>
            {
                if (token.IsCancellationRequested) return;
                view?.Refresh();
                UpdateCount();
                NotifyTagStats();
                int withMeta = files.Count(f => f.Meta?.HasData == true);
                Status.Text = $"{total} 枚(うちAI生成メタデータあり: {withMeta})";
            });
        });
    }

    // ───────────────────── 並び替え / 絞り込み ─────────────────────

    void Sort_Changed(object sender, RoutedEventArgs e) => ApplySort();

    void ApplySort()
    {
        if (view == null) return;
        var key = (SortBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "Name";
        bool desc = DescBox.IsChecked == true;
        DescBox.Content = desc ? "▼ 降順" : "▲ 昇順";
        view.CustomSort = new FileItemComparer(key, desc);
        if (FileList.SelectedItem != null) FileList.ScrollIntoView(FileList.SelectedItem);
    }

    // カンマ(, と 、)区切りの検索語。各語の前後の空白は除き、語の中のスペースはそのまま使う。
    string[] filterTerms = Array.Empty<string>();

    void Filter_Changed(object sender, TextChangedEventArgs e)
    {
        filterTerms = (FilterBox.Text ?? "")
            .Split(new[] { ',', '、', '，' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => PromptText.Normalize(t).Trim())
            .Where(t => t.Length > 0)
            .ToArray();
        view?.Refresh();
        UpdateCount();
        NotifyTagStats();
    }

    // 検索対象の文字列も語も PromptText.Normalize 済み(小文字・_ はスペース・\ なし)なので、そのまま比べる
    static bool Has(string s, string term) => s.Contains(term, StringComparison.Ordinal);

    bool FilterItem(object o)
    {
        var f = (FileItem)o;
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
        if (view == null) return;
        int total = items.Count;
        int shown = view.Count;
        int selected = FileList.SelectedItems.Count;
        string text = total == 0 ? "" : shown == total ? $"{total} 件" : $"{total} 件中 {shown} 件を表示";
        if (total > 0 && selected > 1) text += $"  ・  {selected} 件を選択中";
        CountText.Text = text;
    }

    void FilterTarget_Click(object sender, RoutedEventArgs e)
    {
        FilterTargetMenu.PlacementTarget = FilterTargetButton;
        FilterTargetMenu.Placement = PlacementMode.Bottom;
        FilterTargetMenu.IsOpen = true;
    }

    void FilterTarget_Changed(object sender, RoutedEventArgs e)
    {
        // 検索対象が1つも無いと何もヒットしなくなるので、最後の1つは外せないようにする
        bool any = TgtFileName.IsChecked == true || TgtPrompt.IsChecked == true ||
                   TgtNegative.IsChecked == true || TgtCharacters.IsChecked == true ||
                   TgtCharacterNegative.IsChecked == true || TgtModel.IsChecked == true;
        if (!any && sender is MenuItem mi) mi.IsChecked = true;

        settings.FilterFileName = TgtFileName.IsChecked == true;
        settings.FilterPrompt = TgtPrompt.IsChecked == true;
        settings.FilterNegative = TgtNegative.IsChecked == true;
        settings.FilterCharacters = TgtCharacters.IsChecked == true;
        settings.FilterCharacterNegative = TgtCharacterNegative.IsChecked == true;
        settings.FilterModel = TgtModel.IsChecked == true;
        settings.Save();

        view?.Refresh();
        UpdateCount();
        NotifyTagStats();
    }

    // ───────────────────── 画像表示 ─────────────────────

    async void FileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (suppressSelection) return;
        UpdateCount();

        // 複数選択のときも、表示するのは1枚。新しく選んだ1枚を表示し、範囲選択や全選択では表示を変えない
        FileItem? item;
        bool currentStillSelected = currentItem != null && FileList.SelectedItems.Contains(currentItem);
        if (e.AddedItems.Count == 1) item = e.AddedItems[0] as FileItem;
        else if (e.AddedItems.Count > 1) item = currentStillSelected ? null : e.AddedItems[0] as FileItem;
        else item = FileList.SelectedItems.Count == 0 || currentStillSelected ? null : FileList.SelectedItems[0] as FileItem;
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

        // 画像
        try
        {
            var bmp = await Task.Run(() => Decode(path));
            if (seq != loadSeq) return;
            Img.Source = bmp;
            EmptyPanel.Visibility = Visibility.Collapsed;
            ApplyViewMode();
            Img.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
            Status.Text = $"{FileList.Items.IndexOf(item) + 1}/{FileList.Items.Count}   {bmp.PixelWidth}×{bmp.PixelHeight}   {item.Size / 1024.0:N0} KB";
        }
        catch (Exception ex)
        {
            if (seq != loadSeq) return;
            Img.Source = null;
            UpdateZoomText();
            EmptyPanel.Visibility = Visibility.Visible;
            Placeholder.Text = "画像を表示できません";
            Status.Text = $"読み込み失敗: {ex.Message}{CodecHint(path)}";
        }
    }

    /// <summary>表示に Windows の拡張機能が必要な形式なら、その案内を返す</summary>
    static string CodecHint(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".webp" => "(WebP は Windows の「WebP 画像拡張機能」が必要です)",
        ".heic" or ".heif" => "(HEIC は Windows の「HEIF 画像拡張機能」と「HEVC ビデオ拡張機能」が必要です)",
        _ => "",
    };

    static BitmapSource Decode(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var orientation = ExifOrientation.Read(
            BitmapFrame.Create(fs, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None), path);
        fs.Position = 0;

        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.StreamSource = fs;
        bmp.EndInit();
        bmp.Freeze();
        // JPEG・WebP の EXIF 回転情報に従って向きを直す
        return ExifOrientation.Apply(bmp, orientation);
    }

    void ClearImage()
    {
        currentItem = null;
        Img.Source = null;
        UpdateZoomText();
        Placeholder.Text = "画像が見つかりません";
        EmptyPanel.Visibility = Visibility.Visible;
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
            PromptCard.Visibility = NegativeCard.Visibility = ParamsCard.Visibility = Visibility.Collapsed;
            ParamsList.ItemsSource = OtherParamsList.ItemsSource = null;
            OtherExpander.Visibility = Visibility.Collapsed;
            ExifList.ItemsSource = null;
            ExifCard.Visibility = Visibility.Collapsed;
            BuildCharacters(null);
            return;
        }

        SourceText.Text = m.Error != null ? $"読み取りエラー: {m.Error}" : m.Source ?? "AI生成メタデータは見つかりません";

        PromptBox.Text = m.Prompt ?? "";
        NegativeBox.Text = m.NegativePrompt ?? "";
        BuildChips(PromptChips, m.Prompt);
        BuildChips(NegativeChips, m.NegativePrompt);
        PromptCard.Visibility = string.IsNullOrWhiteSpace(m.Prompt) ? Visibility.Collapsed : Visibility.Visible;
        NegativeCard.Visibility = string.IsNullOrWhiteSpace(m.NegativePrompt) ? Visibility.Collapsed : Visibility.Visible;

        // 主要なパラメーターと、その他に分ける
        var main = m.Params.Where(kv => MainParamKeys.Contains(kv.Key)).ToList();
        var other = m.Params.Where(kv => !MainParamKeys.Contains(kv.Key)).ToList();
        ParamsList.ItemsSource = main;
        OtherParamsList.ItemsSource = other;
        ParamsCard.Visibility = main.Count > 0 || other.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        OtherExpander.Visibility = other.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        BuildCharacters(m);

        var exif = m.Exif?.Rows(settings.ShowGps) ?? new List<KeyValuePair<string, string>>();
        ExifList.ItemsSource = exif;
        ExifCard.Visibility = exif.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        RawBox.Text = string.Join("\n\n", m.Raw.Select(kv => $"[{kv.Key}]\n{kv.Value}"));
    }

    static readonly HashSet<string> MainParamKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "Model", "Sampler", "Scheduler", "Schedule type", "Steps", "CFG scale", "Seed", "Size",
        "Software", "Title", "Generation time", "LoRA"
    };

    // ───────────────────── カードの最小化 / 展開 ─────────────────────

    /// <summary>最小化できるカード: 設定に保存するキー、本体、見出しの矢印。</summary>
    (string Key, FrameworkElement Body, TextBlock Arrow)[] Sections => new[]
    {
        ("Prompt", (FrameworkElement)PromptBody, PromptArrow),
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
            body.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
            arrow.Text = collapsed ? "▸" : "▾";
        }
    }

    void Section_Toggle(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string key }) return;
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
        var chip = new Border
        {
            Child = text,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(9, 3, 9, 4),
            Margin = new Thickness(0, 0, 5, 5),
            Cursor = Cursors.Hand,
            ToolTip = style is { } s ? $"{s.Label}  ・  {hint}" : hint
        };

        if (style is { } st)
        {
            // 色分けしたタグ: 分類の色の文字と、同じ色の薄い背景
            var normal = new SolidColorBrush(st.Bg);
            var hover = new SolidColorBrush(st.BgHover);
            text.Foreground = new SolidColorBrush(st.Fg);
            chip.Background = normal;
            chip.MouseEnter += (_, _) => chip.Background = hover;
            chip.MouseLeave += (_, _) => chip.Background = normal;
        }
        else
        {
            chip.SetResourceReference(Border.BackgroundProperty, "SystemControlBackgroundBaseLowBrush");
            chip.MouseEnter += (_, _) => chip.SetResourceReference(Border.BackgroundProperty, "SystemControlHighlightListAccentLowBrush");
            chip.MouseLeave += (_, _) => chip.SetResourceReference(Border.BackgroundProperty, "SystemControlBackgroundBaseLowBrush");
        }

        chip.MouseLeftButtonUp += (_, e) =>
        {
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
            CharactersCard.Visibility = Visibility.Collapsed;
            return;
        }

        CharactersCard.Visibility = Visibility.Visible; // 見出しは XAML 側(最小化ボタンを兼ねる)にある

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
        var copy = new Button { Content = "コピー", Style = (Style)FindResource("SmallButton") };
        copy.Click += (_, _) => CopyText(copyText ?? "");
        DockPanel.SetDock(copy, Dock.Right);
        header.Children.Add(copy);
        header.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center
        });
        return header;
    }

    // ───────────────────── ズーム / パン ─────────────────────

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
        FitButton.FontWeight = settings.ActualSizeMode ? FontWeights.Normal : FontWeights.Bold;
        ActualButton.FontWeight = settings.ActualSizeMode ? FontWeights.Bold : FontWeights.Normal;
    }

    void ResetView()
    {
        Scale.ScaleX = Scale.ScaleY = 1;
        Trans.X = Trans.Y = 0;
        RenderOptions.SetBitmapScalingMode(Img, BitmapScalingMode.Fant);
        UpdateZoomText();
    }

    /// <summary>画像の右下に、現在の表示倍率(実際のピクセルに対する割合)を出す。</summary>
    void UpdateZoomText()
    {
        if (Img.Source is not BitmapSource bs || Viewport.ActualWidth <= 0 || Viewport.ActualHeight <= 0 || bs.PixelWidth <= 0)
        {
            ZoomText.Text = "";
            return;
        }

        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        double fit = Math.Min(Viewport.ActualWidth / bs.Width, Viewport.ActualHeight / bs.Height);
        double percent = fit * Scale.ScaleX * bs.Width * dpi / bs.PixelWidth * 100;
        ZoomText.Text = $"{percent:0}%";
    }

    void Viewport_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateZoomText();

    void ActualSize()
    {
        if (Img.Source is not BitmapSource bs || Viewport.ActualWidth <= 0 || Viewport.ActualHeight <= 0) return;

        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        double fit = Math.Min(Viewport.ActualWidth / bs.Width, Viewport.ActualHeight / bs.Height);
        double target = (bs.PixelWidth / dpi) / (fit * bs.Width);

        double cx = Viewport.ActualWidth / 2, cy = Viewport.ActualHeight / 2;
        Scale.ScaleX = Scale.ScaleY = target;
        Trans.X = cx - cx * target;
        Trans.Y = cy - cy * target;
        UpdateScalingMode();
        UpdateZoomText();
    }

    void UpdateScalingMode() =>
        RenderOptions.SetBitmapScalingMode(Img, Scale.ScaleX >= 3 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.Fant);

    void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Img.Source == null) return;
        var p = e.GetPosition(Viewport);
        double old = Scale.ScaleX;
        double ns = Math.Clamp(old * (e.Delta > 0 ? 1.15 : 1 / 1.15), 0.05, 64);
        double f = ns / old;

        Trans.X = p.X - (p.X - Trans.X) * f;
        Trans.Y = p.Y - (p.Y - Trans.Y) * f;
        Scale.ScaleX = Scale.ScaleY = ns;
        UpdateScalingMode();
        UpdateZoomText();
        e.Handled = true;
    }

    void Viewport_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Img.Source == null) return;
        if (e.ClickCount == 2) { ApplyViewMode(); return; } // 手動のズーム・移動を、選択中の表示方式に戻す

        dragging = true;
        dragStart = e.GetPosition(Viewport);
        panOrigin = new Point(Trans.X, Trans.Y);
        Viewport.CaptureMouse();
        Viewport.Cursor = Cursors.SizeAll;
    }

    void Viewport_MouseMove(object sender, MouseEventArgs e)
    {
        if (!dragging) return;
        var p = e.GetPosition(Viewport);
        Trans.X = panOrigin.X + (p.X - dragStart.X);
        Trans.Y = panOrigin.Y + (p.Y - dragStart.Y);
    }

    void Viewport_MouseUp(object sender, MouseButtonEventArgs e)
    {
        dragging = false;
        Viewport.ReleaseMouseCapture();
        Viewport.Cursor = Cursors.Arrow;
    }

    void Fit_Click(object sender, RoutedEventArgs e) => SetFitMode();
    void Actual_Click(object sender, RoutedEventArgs e) => SetActualMode();

    // ───────────────────── 操作 ─────────────────────

    void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "画像フォルダを選択" };
        if (dlg.ShowDialog(this) == true) OpenFolder(dlg.FolderName, null);
    }

    void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "画像を選択",
            Filter = "画像ファイル|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif;*.tif;*.tiff;*.heic;*.heif|すべてのファイル|*.*"
        };
        if (dlg.ShowDialog(this) == true) OpenPath(dlg.FileName);
    }

    // ───────────────────── テーマ / サムネイルの大きさ ─────────────────────

    /// <summary>設定のテーマを適用する。"System" は Windows の設定に合わせる(null)。</summary>
    void ApplyTheme()
    {
        ModernWpf.ApplicationTheme? theme = settings.Theme switch
        {
            "Dark" => ModernWpf.ApplicationTheme.Dark,
            "Light" => ModernWpf.ApplicationTheme.Light,
            _ => null
        };
        ModernWpf.ThemeManager.Current.ApplicationTheme = theme;
    }

    /// <summary>ラジオボタンのように、現在の値に当たる項目だけにチェックを付ける。</summary>
    static void CheckOnly(MenuItem parent, string current)
    {
        foreach (var o in parent.Items)
            if (o is MenuItem mi) mi.IsChecked = mi.Tag?.ToString() == current;
    }

    void UpdateThemeMenu() => CheckOnly(ThemeMenu, settings.Theme);
    void UpdateThumbSizeMenu() => CheckOnly(ThumbSizeMenu, settings.ThumbnailSize.ToString());

    void Theme_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string theme }) return;
        settings.Theme = theme;
        settings.Save();
        ApplyTheme();
        UpdateThemeMenu();
        RefreshMetaView(); // タグの色は、テーマ(ダーク/ライト)に合わせて作り直す
    }

    void ThumbSize_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi || !int.TryParse(mi.Tag?.ToString(), out int size)) return;
        settings.ThumbnailSize = size;
        settings.Save();
        Resources["ThumbSize"] = (double)size;
        UpdateThumbSizeMenu();
    }

    void Thumb_Click(object sender, RoutedEventArgs e)
    {
        settings.ShowThumbnails = ThumbMenu.IsChecked;
        settings.Save();
        ApplyListTemplate();
    }

    void ShowGps_Click(object sender, RoutedEventArgs e)
    {
        settings.ShowGps = ShowGpsMenu.IsChecked;
        settings.Save();
        if (currentItem != null) ShowMeta(currentItem, currentItem.Meta);
    }

    /// <summary>サムネイル設定に合わせて、一覧の表示テンプレートを切り替える。</summary>
    void ApplyListTemplate() =>
        FileList.ItemTemplate = (DataTemplate)FindResource(settings.ShowThumbnails ? "ThumbItemTemplate" : "TextItemTemplate");

    void SubfolderDepth_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi || !int.TryParse(mi.Tag?.ToString(), out int depth)) return;

        settings.SubfolderDepth = depth;
        settings.Save();
        UpdateSubfolderMenu();

        // 開いているフォルダを新しい設定で読み込み直す(選択中の画像は維持)
        if (currentFolder != null)
            OpenFolder(currentFolder, (FileList.SelectedItem as FileItem)?.Path);
    }

    /// <summary>深さのメニューを、現在の設定に合わせて1つだけチェック状態にする。</summary>
    void UpdateSubfolderMenu()
    {
        foreach (var item in SubfolderMenu.Items)
        {
            if (item is MenuItem mi && int.TryParse(mi.Tag?.ToString(), out int d))
                mi.IsChecked = d == settings.SubfolderDepth;
        }
    }

    void Exit_Click(object sender, RoutedEventArgs e) => Close();

    HelpWindow? helpWindow;

    void Help_Click(object sender, RoutedEventArgs e)
    {
        // すでに開いていれば前面に出す(ウィンドウを増やさない)
        if (helpWindow is { IsLoaded: true })
        {
            helpWindow.Activate();
            return;
        }
        helpWindow = new HelpWindow { Owner = this };
        helpWindow.Show();
    }

    void About_Click(object sender, RoutedEventArgs e)
    {
        var asm = typeof(App).Assembly;
        string version = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                         ?? asm.GetName().Version?.ToString() ?? "不明";
        int plus = version.IndexOf('+');
        if (plus >= 0) version = version[..plus];
        string copyright = asm.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? "";

        MessageBox.Show(this,
            $"PromptLoupe\nバージョン {version}\n{copyright}(MIT License)\n\n" +
            "AI生成画像のメタデータを元に、並び替え・絞り込みして閲覧できる画像ビューア\n\n" +
            "使用ライブラリ: ModernWpfUI — Copyright (c) 2019 Yimeng Wu(MIT License)\n" +
            "ライセンス文は、同梱の LICENSE と THIRD-PARTY-NOTICES.txt にあります。\n\n" +
            $"{RuntimeInformation.FrameworkDescription}",
            "バージョン情報", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    void Window_Drop(object sender, DragEventArgs e)
    {
        if (draggingOut) return; // 一覧から持ち出すドラッグが、自分のウィンドウに落ちたときは開き直さない
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } paths) OpenPath(paths[0]);
    }

    void CopyPrompt_Click(object sender, RoutedEventArgs e) => CopyText(PromptBox.Text);
    void CopyNegative_Click(object sender, RoutedEventArgs e) => CopyText(NegativeBox.Text);

    void CopyText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        try
        {
            Clipboard.SetText(text);
            Status.Text = "クリップボードにコピーしました";
        }
        catch (Exception ex)
        {
            Status.Text = "コピーに失敗しました: " + ex.Message;
        }
    }

    void Move(int delta)
    {
        int count = FileList.Items.Count;
        if (count == 0) return;
        int from = currentItem != null ? FileList.Items.IndexOf(currentItem) : FileList.SelectedIndex;
        int idx = Math.Clamp(Math.Max(from, 0) + delta, 0, count - 1);
        FileList.SelectedIndex = idx;
    }

    void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F1)
        {
            Help_Click(this, e);
            e.Handled = true;
            return;
        }

        // 全画面表示(入力欄にいても使える)
        if (e.Key == Key.F11)
        {
            ToggleFullscreen();
            e.Handled = true;
            return;
        }
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.T)
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

        if (Keyboard.FocusedElement is TextBox) return;

        // ファイル操作
        var mod = Keyboard.Modifiers;
        bool handled = true;
        if (mod == ModifierKeys.Control && e.Key == Key.C) CopyFiles(cut: false);
        else if (mod == ModifierKeys.Control && e.Key == Key.X) CopyFiles(cut: true);
        else if (mod == ModifierKeys.Control && e.Key == Key.V) PasteFiles();
        else if (mod == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.C) CopyPaths();
        else if (mod == ModifierKeys.None && e.Key == Key.F2) RenameSelected();
        else if (mod == ModifierKeys.None && e.Key == Key.Delete) DeleteSelected();
        else if (mod == ModifierKeys.Alt && (e.Key == Key.Return || e.SystemKey == Key.Return)) ShowProperties();
        else handled = false;
        if (handled)
        {
            e.Handled = true;
            return;
        }

        // Shift / Ctrl / Alt を押しているときは、一覧の範囲選択などに任せる
        if (mod != ModifierKeys.None) return;

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
