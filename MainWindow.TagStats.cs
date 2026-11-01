using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace MViewer;

// タグの集計ウィンドウとの橋渡し。集計そのものは TagStatsWindow が行う。
public partial class MainWindow
{
    TagStatsWindow? tagStatsWindow;

    void TagStats_Click(object sender, RoutedEventArgs e) => ShowTagStats();

    void ShowTagStats()
    {
        // すでに開いていれば前面に出す(ウィンドウを増やさない)
        if (tagStatsWindow != null)
        {
            tagStatsWindow.Activate();
            return;
        }
        tagStatsWindow = new TagStatsWindow(this, settings) { Owner = this };
        tagStatsWindow.Closed += (_, _) => tagStatsWindow = null;
        tagStatsWindow.Show();
    }

    /// <summary>一覧・絞り込み・メタデータが変わったことを伝える(集計ウィンドウが開いているときだけ、集計し直す)。</summary>
    void NotifyTagStats() => tagStatsWindow?.ScheduleRefresh();

    /// <summary>集計する画像。filteredOnly のときは、絞り込み後に一覧に出ている画像だけ。</summary>
    internal List<FileItem> TagStatsItems(bool filteredOnly) =>
        filteredOnly && view != null ? view.Cast<FileItem>().ToList() : items.ToList();

    /// <summary>そのタグ(正規化済み)が、いま絞り込み欄に入っているか。</summary>
    internal bool IsInFilter(string key) => filterTerms.Contains(key);
}
