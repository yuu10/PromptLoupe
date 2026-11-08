using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

namespace PromptLoupe;

// 全画面表示(F11 で切り替え、Esc で戻る)。メニュー・パネル・ステータスバーを隠して、画像だけを画面いっぱいに出す。
// Avalonia には全画面の状態(WindowState.FullScreen)があるので、WPF 版のような枠の付け外しはしない。
public partial class MainWindow
{
    bool isFullscreen;
    WindowState prevState;
    GridLength prevLeftWidth, prevRightWidth, prevSplit1Width, prevSplit2Width;
    double prevLeftMin, prevRightMin;
    IBrush? prevViewportBackground;

    // 本体の列: 左パネル / 区切り / 画像 / 区切り / 右パネル
    ColumnDefinition LeftCol => BodyGrid.ColumnDefinitions[0];
    ColumnDefinition SplitCol1 => BodyGrid.ColumnDefinitions[1];
    ColumnDefinition SplitCol2 => BodyGrid.ColumnDefinitions[3];
    ColumnDefinition RightCol => BodyGrid.ColumnDefinitions[4];

    void Fullscreen_Click(object? sender, RoutedEventArgs e) => ToggleFullscreen();

    void ToggleFullscreen()
    {
        if (!isFullscreen) EnterFullscreen();
        else ExitFullscreen();
    }

    void EnterFullscreen()
    {
        prevState = WindowState;
        prevLeftWidth = LeftCol.Width;
        prevRightWidth = RightCol.Width;
        prevSplit1Width = SplitCol1.Width;
        prevSplit2Width = SplitCol2.Width;
        prevLeftMin = LeftCol.MinWidth;
        prevRightMin = RightCol.MinWidth;
        prevViewportBackground = Viewport.Background;
        isFullscreen = true;

        MainMenu.IsVisible = PathRow.IsVisible = FilterRow.IsVisible = StatusBar.IsVisible = false;
        LeftPanel.IsVisible = RightPanel.IsVisible = Split1.IsVisible = Split2.IsVisible = false;
        LeftCol.MinWidth = RightCol.MinWidth = 0;
        LeftCol.Width = RightCol.Width = SplitCol1.Width = SplitCol2.Width = new GridLength(0);
        Viewport.Background = Brushes.Black;
        FullscreenButton.Content = "全画面を終了";

        WindowState = WindowState.FullScreen;
        Dispatcher.UIThread.Post(ApplyViewMode, DispatcherPriority.Loaded);
    }

    void ExitFullscreen()
    {
        isFullscreen = false;
        WindowState = prevState == WindowState.FullScreen ? WindowState.Normal : prevState;

        MainMenu.IsVisible = FilterRow.IsVisible = StatusBar.IsVisible = true;
        PathRow.IsVisible = settings.ShowPathBar;
        LeftPanel.IsVisible = RightPanel.IsVisible = Split1.IsVisible = Split2.IsVisible = true;
        LeftCol.MinWidth = prevLeftMin;
        RightCol.MinWidth = prevRightMin;
        LeftCol.Width = prevLeftWidth;
        RightCol.Width = prevRightWidth;
        SplitCol1.Width = prevSplit1Width;
        SplitCol2.Width = prevSplit2Width;
        Viewport.Background = prevViewportBackground;
        FullscreenButton.Content = "全画面";

        Dispatcher.UIThread.Post(ApplyViewMode, DispatcherPriority.Loaded);
    }
}
