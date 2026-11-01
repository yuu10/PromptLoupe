using System.Windows;
using System.Windows.Media;

namespace MViewer;

// 全画面表示(F11 で切り替え、Esc で戻る)。メニュー・パネル・ステータスバーを隠して、画像だけを画面いっぱいに出す。
public partial class MainWindow
{
    bool isFullscreen;
    WindowState prevState;
    WindowStyle prevStyle;
    ResizeMode prevResize;
    GridLength prevLeftWidth, prevRightWidth, prevSplit1Width, prevSplit2Width;
    double prevLeftMin, prevRightMin;
    Brush? prevViewportBackground;

    void Fullscreen_Click(object sender, RoutedEventArgs e) => ToggleFullscreen();

    void ToggleFullscreen()
    {
        if (!isFullscreen) EnterFullscreen();
        else ExitFullscreen();
    }

    void EnterFullscreen()
    {
        prevState = WindowState;
        prevStyle = WindowStyle;
        prevResize = ResizeMode;
        prevLeftWidth = LeftCol.Width;
        prevRightWidth = RightCol.Width;
        prevSplit1Width = SplitCol1.Width;
        prevSplit2Width = SplitCol2.Width;
        prevLeftMin = LeftCol.MinWidth;
        prevRightMin = RightCol.MinWidth;
        prevViewportBackground = Viewport.Background;
        isFullscreen = true;

        MainMenu.Visibility = FilterRow.Visibility = StatusBar.Visibility = Visibility.Collapsed;
        LeftPanel.Visibility = RightPanel.Visibility = Split1.Visibility = Split2.Visibility = Visibility.Collapsed;
        LeftCol.MinWidth = RightCol.MinWidth = 0;
        LeftCol.Width = RightCol.Width = SplitCol1.Width = SplitCol2.Width = new GridLength(0);
        Viewport.Background = Brushes.Black;
        FullscreenButton.Content = "全画面を終了";

        // 最大化の状態からでも、タスクバーまで覆うように、いったん通常の状態に戻してから最大化する
        WindowState = WindowState.Normal;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowState = WindowState.Maximized;

        Dispatcher.InvokeAsync(() => ApplyViewMode(), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    void ExitFullscreen()
    {
        isFullscreen = false;

        WindowState = WindowState.Normal;
        WindowStyle = prevStyle;
        ResizeMode = prevResize;
        WindowState = prevState;

        MainMenu.Visibility = FilterRow.Visibility = StatusBar.Visibility = Visibility.Visible;
        LeftPanel.Visibility = RightPanel.Visibility = Split1.Visibility = Split2.Visibility = Visibility.Visible;
        LeftCol.MinWidth = prevLeftMin;
        RightCol.MinWidth = prevRightMin;
        LeftCol.Width = prevLeftWidth;
        RightCol.Width = prevRightWidth;
        SplitCol1.Width = prevSplit1Width;
        SplitCol2.Width = prevSplit2Width;
        Viewport.Background = prevViewportBackground;
        FullscreenButton.Content = "全画面";

        Dispatcher.InvokeAsync(() => ApplyViewMode(), System.Windows.Threading.DispatcherPriority.Loaded);
    }
}
