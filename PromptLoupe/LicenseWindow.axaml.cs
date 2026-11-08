using System.Reflection;
using Avalonia.Controls;
using Avalonia.Input;

namespace PromptLoupe;

/// <summary>
/// ライセンス文の表示。LICENSE と THIRD-PARTY-NOTICES.txt は exe に埋め込んである(csproj の EmbeddedResource)。
/// MIT・BSD などの「ライセンス文をソフトウェアと一緒に配る」条件を、別ファイルを置かずに満たすため。
/// </summary>
public partial class LicenseWindow : Window
{
    public LicenseWindow()
    {
        InitializeComponent();
        LicenseText.Text = Load("PromptLoupe.LICENSE.txt");
        NoticesText.Text = Load("PromptLoupe.THIRD-PARTY-NOTICES.txt");
    }

    static string Load(string name)
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
        if (s == null) return "ライセンス文が見つかりません。";
        using var r = new StreamReader(s);
        return r.ReadToEnd().Replace("\r\n", "\n");
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }
}
