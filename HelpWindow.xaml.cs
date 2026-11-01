using System.Windows;
using System.Windows.Input;

namespace MViewer;

/// <summary>使い方マニュアル(内容は HelpWindow.xaml に埋め込み)。</summary>
public partial class HelpWindow : Window
{
    public HelpWindow()
    {
        InitializeComponent();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
        };
    }
}
