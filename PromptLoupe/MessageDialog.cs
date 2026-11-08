using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;

namespace PromptLoupe;

/// <summary>メッセージの小さなダイアログ(WPF の MessageBox の代わり。Avalonia には標準のものがない)。</summary>
public static class MessageDialog
{
    /// <summary>メッセージを出す(OK だけ)。</summary>
    public static Task Show(Window owner, string message, string title) =>
        ShowCore(owner, message, title, yesNo: false);

    /// <summary>はい / いいえ を聞く。既定(Enter・Esc)は「いいえ」。</summary>
    public static Task<bool> Confirm(Window owner, string message, string title) =>
        ShowCore(owner, message, title, yesNo: true);

    static async Task<bool> ShowCore(Window owner, string message, string title, bool yesNo)
    {
        var dlg = new Window
        {
            Title = title,
            Width = 440,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Icon = owner.Icon,
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 18, 0, 0),
        };

        Button Add(string text, bool result, bool isDefault, bool isCancel)
        {
            var b = new Button { Content = text, MinWidth = 88, IsDefault = isDefault, IsCancel = isCancel,
                HorizontalContentAlignment = HorizontalAlignment.Center };
            b.Click += (_, _) => dlg.Close(result);
            buttons.Children.Add(b);
            return b;
        }

        Button focus;
        if (yesNo)
        {
            Add("はい", true, false, false);
            focus = Add("いいえ", false, true, true);
        }
        else
        {
            focus = Add("OK", true, true, true);
        }

        dlg.Content = new StackPanel
        {
            Margin = new Thickness(20, 16, 20, 20),
            Children =
            {
                new SelectableTextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                buttons,
            },
        };
        dlg.Opened += (_, _) => focus.Focus(NavigationMethod.Tab);

        bool r = await dlg.ShowDialog<bool>(owner);
        return r;
    }
}
