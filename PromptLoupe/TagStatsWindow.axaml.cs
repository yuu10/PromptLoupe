using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace PromptLoupe;

/// <summary>集計の1行(タグ、使われている枚数、割合)。</summary>
public sealed class TagStatRow
{
    public required string Tag { get; init; }
    public required int Count { get; init; }
    public required string Kind { get; init; }
    public required IBrush? Foreground { get; init; }
    public required double BarWidth { get; init; }
    public required string PercentText { get; init; }
    public required bool InFilter { get; init; }

    public string CountText => Count.ToString("N0");
    public string Mark => InFilter ? "✓" : "";
}

/// <summary>
/// タグの集計。一覧の画像のプロンプトから、タグごとに「使われている画像の枚数」を数える。
/// 一覧・絞り込み・メタデータが変わると、MainWindow から ScheduleRefresh で知らされて、数え直す。
/// </summary>
public partial class TagStatsWindow : Window
{
    readonly MainWindow owner = null!;
    readonly AppSettings settings = null!;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    bool initializing = true;

    List<TagStatRow> allRows = new();
    List<TagStatRow> viewRows = new();

    // 並び替えの列(設定に保存する名前、見出しのボタン、見出しの文字)
    readonly (string Key, Button Header, string Title)[] sortColumns = Array.Empty<(string, Button, string)>();

    public TagStatsWindow() => InitializeComponent(); // XAML のプレビュー用

    public TagStatsWindow(MainWindow owner, AppSettings settings)
    {
        this.owner = owner;
        this.settings = settings;
        InitializeComponent();

        sortColumns = new[]
        {
            ("Mark", MarkHeader, "✓"),
            ("Tag", TagHeader, "タグ"),
            ("Kind", KindHeader, "分類"),
            ("Count", CountHeader, "枚数"),
            ("Percent", PercentHeader, "割合"),
        };
        if (!sortColumns.Any(c => c.Key == settings.TagStatsSortKey)) settings.TagStatsSortKey = "Count";
        UpdateHeaders();

        var cmd = PlatformSettings?.HotkeyConfiguration.CommandModifiers ?? KeyModifiers.Control;
        CopyTagsMenu.InputGesture = new KeyGesture(Key.C, cmd);

        SelectByTag(SourceBox, settings.TagStatsSource);
        SelectByTag(ScopeBox, settings.TagStatsFilteredOnly ? "Filtered" : "All");
        initializing = false;

        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Recount();
        };
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
        }, RoutingStrategies.Tunnel);
        Closed += (_, _) => timer.Stop();
        Opened += (_, _) => Recount();
    }

    static void SelectByTag(ComboBox box, string tag)
    {
        box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag as string == tag)
                           ?? box.Items[0];
    }

    static string TagOf(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string ?? "";

    /// <summary>
    /// 少し待ってから数え直す。待っている間に何度呼ばれても、数え直すのは1回だけ
    /// (入力のたびや、メタデータの読み込み中に、何度も数え直さないように)。
    /// </summary>
    public void ScheduleRefresh()
    {
        if (!timer.IsEnabled) timer.Start();
    }

    // ───────────────────── 集計 ─────────────────────

    void Recount()
    {
        string source = settings.TagStatsSource;
        var files = owner.TagStatsItems(settings.TagStatsFilteredOnly);

        var counts = new Dictionary<string, int>();
        int images = 0, pending = 0;
        foreach (var f in files)
        {
            if (f.Meta == null) { pending++; continue; }
            if (!f.Meta.HasData) continue;
            images++;

            var tags = source switch
            {
                "Negative" => f.NegativeTags,
                "Characters" => f.CharacterTags,
                _ => f.PromptTags
            };
            foreach (var t in tags)
                counts[t] = counts.TryGetValue(t, out int n) ? n + 1 : 1;
        }

        // 色分けしないタグは、テーマの文字色(ダーク/ライトで変わる)
        IBrush? normal = this.TryFindResource("SystemControlForegroundBaseHighBrush", ActualThemeVariant, out var b) ? b as IBrush : null;
        allRows = counts.Select(kv =>
        {
            double ratio = images == 0 ? 0 : (double)kv.Value / images;
            var style = owner.ClassifyTag(kv.Key);
            return new TagStatRow
            {
                Tag = kv.Key,
                Count = kv.Value,
                Kind = style?.Label ?? "",
                Foreground = style is { } s ? new SolidColorBrush(s.Fg) : normal,
                BarWidth = Math.Round(50 * ratio, 1),
                PercentText = ratio >= 0.995 ? "100%" : ratio >= 0.1 ? $"{ratio:P0}" : $"{ratio:P1}",
                InFilter = owner.IsInFilter(kv.Key),
            };
        }).ToList();

        string scope = settings.TagStatsFilteredOnly ? "一覧に表示中" : "フォルダ内";
        SummaryText.Text = $"{scope}の、メタデータのある画像 {images:N0} 枚から、{counts.Count:N0} 種類のタグ"
                           + (pending > 0 ? $"(メタデータを読み込み中: 残り {pending:N0} 枚)" : "");
        ApplyView();
    }

    /// <summary>検索と並び順を反映して、一覧に出す。選んでいたタグは、選んだままにする。</summary>
    void ApplyView()
    {
        string? keep = (TagList.SelectedItem as TagStatRow)?.Tag;
        string q = PromptText.Normalize(SearchBox.Text).Trim();

        IEnumerable<TagStatRow> rows = allRows;
        if (q.Length > 0) rows = rows.Where(r => r.Tag.Contains(q, StringComparison.Ordinal));
        viewRows = Sort(rows).ToList();

        TagList.ItemsSource = viewRows;
        if (keep != null && viewRows.FirstOrDefault(r => r.Tag == keep) is { } sel)
        {
            TagList.SelectedItem = sel;
            TagList.ScrollIntoView(sel);
        }
    }

    /// <summary>
    /// 選んだ列で並び替える。同じ値の行は、枚数の多い順 → タグの名前順(この順は、昇順・降順で変えない)。
    /// </summary>
    IEnumerable<TagStatRow> Sort(IEnumerable<TagStatRow> rows)
    {
        bool desc = settings.TagStatsSortDescending;
        IOrderedEnumerable<TagStatRow> sorted = settings.TagStatsSortKey switch
        {
            "Mark" => desc ? rows.OrderByDescending(r => r.InFilter) : rows.OrderBy(r => r.InFilter),
            "Tag" => desc ? rows.OrderByDescending(r => r.Tag, StringComparer.Ordinal)
                          : rows.OrderBy(r => r.Tag, StringComparer.Ordinal),
            "Kind" => desc ? rows.OrderByDescending(r => MainWindow.KindOrder(r.Kind))
                           : rows.OrderBy(r => MainWindow.KindOrder(r.Kind)),
            // 枚数と割合は同じ順になる
            _ => desc ? rows.OrderByDescending(r => r.Count) : rows.OrderBy(r => r.Count),
        };
        return sorted.ThenByDescending(r => r.Count).ThenBy(r => r.Tag, StringComparer.Ordinal);
    }

    /// <summary>並び替えている列の見出しに ▲(昇順)/ ▼(降順)を付ける。</summary>
    void UpdateHeaders()
    {
        foreach (var (key, header, title) in sortColumns)
            header.Content = key == settings.TagStatsSortKey
                ? $"{title} {(settings.TagStatsSortDescending ? "▼" : "▲")}"
                : title;
    }

    // ───────────────────── 操作 ─────────────────────

    /// <summary>
    /// 見出しのクリックで並び替える。同じ列をもう一度クリックすると逆順。
    /// 別の列に移ったときは、数の列(✓・枚数・割合)は降順、文字の列(タグ・分類)は昇順から始める。
    /// </summary>
    void Header_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string key }) return;

        if (key == settings.TagStatsSortKey)
            settings.TagStatsSortDescending = !settings.TagStatsSortDescending;
        else
        {
            settings.TagStatsSortKey = key;
            settings.TagStatsSortDescending = key is "Mark" or "Count" or "Percent";
        }
        settings.Save();
        UpdateHeaders();
        ApplyView();
    }

    void Option_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (initializing) return;
        settings.TagStatsSource = TagOf(SourceBox);
        settings.TagStatsFilteredOnly = TagOf(ScopeBox) != "All";
        settings.Save();
        Recount();
    }

    void Search_Changed(object? sender, TextChangedEventArgs e)
    {
        if (initializing) return;
        ApplyView();
    }

    List<TagStatRow> SelectedRows() => TagList.SelectedItems?.OfType<TagStatRow>().ToList() ?? new List<TagStatRow>();

    /// <summary>選んだタグを、絞り込み欄に追加する(入っていれば外す)。</summary>
    void ToggleSelected()
    {
        foreach (var r in SelectedRows()) owner.ToggleFilterTag(r.Tag);
    }

    void TagList_DoubleTapped(object? sender, TappedEventArgs e)
    {
        // 余白のダブルクリックは無視する
        if (e.Source is Visual v && v.FindAncestorOfType<ListBoxItem>(includeSelf: true) is { DataContext: TagStatRow row })
            owner.ToggleFilterTag(row.Tag);
    }

    void TagList_KeyDown(object? sender, KeyEventArgs e)
    {
        var cmd = PlatformSettings?.HotkeyConfiguration.CommandModifiers ?? KeyModifiers.Control;
        if (e.Key == Key.Return)
        {
            ToggleSelected();
            e.Handled = true;
        }
        else if (e.Key == Key.C && e.KeyModifiers == cmd)
        {
            CopyTags();
            e.Handled = true;
        }
    }

    void ToggleFilter_Click(object? sender, RoutedEventArgs e) => ToggleSelected();
    void CopyTags_Click(object? sender, RoutedEventArgs e) => CopyTags();

    /// <summary>選んだタグを、カンマ区切りでコピーする(プロンプトに貼り付けられる形)。</summary>
    void CopyTags()
    {
        var rows = SelectedRows();
        if (rows.Count == 0) return;
        CopyText(string.Join(", ", rows.Select(r => r.Tag)));
    }

    /// <summary>いま一覧に出ている行を、タブ区切り(タグ・枚数・割合)でコピーする。表計算ソフトに貼り付けられる。</summary>
    void CopyList_Click(object? sender, RoutedEventArgs e)
    {
        var sb = new StringBuilder("タグ\t分類\t枚数\t割合\n");
        foreach (var r in viewRows) sb.Append($"{r.Tag}\t{r.Kind}\t{r.Count}\t{r.PercentText}\n");
        CopyText(sb.ToString());
    }

    async void CopyText(string text)
    {
        try
        {
            if (Clipboard != null) await Clipboard.SetTextAsync(text);
        }
        catch { /* クリップボードが使えないときは何もしない */ }
    }
}
