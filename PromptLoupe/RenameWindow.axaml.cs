using Avalonia.Controls;
using Avalonia.Interactivity;

namespace PromptLoupe;

/// <summary>
/// ファイル名の変更ダイアログ。拡張子はそのまま残し、名前の部分だけを編集する。
/// ShowDialog の結果は、新しいファイル名(拡張子つき)。キャンセルなら null。
/// </summary>
public partial class RenameWindow : Window
{
    readonly string folder = "";
    readonly string oldFileName = "";
    readonly string ext = "";

    public RenameWindow() => InitializeComponent(); // XAML のプレビュー用

    public RenameWindow(string folder, string oldFileName)
    {
        InitializeComponent();
        this.folder = folder;
        this.oldFileName = oldFileName;
        ext = Path.GetExtension(oldFileName);
        ExtText.Text = ext;
        NameBox.Text = Path.GetFileNameWithoutExtension(oldFileName);

        Opened += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    void Ok_Click(object? sender, RoutedEventArgs e)
    {
        string name = (NameBox.Text ?? "").Trim();
        string? error = Validate(name);
        if (error != null)
        {
            ErrorText.Text = error;
            ErrorText.IsVisible = true;
            NameBox.Focus();
            return;
        }
        Close(name + ext);
    }

    void Cancel_Click(object? sender, RoutedEventArgs e) => Close(null);

    // Windows で使えない文字は、どの OS でも断る(Windows とやり取りするファイルが壊れないように)
    static readonly char[] InvalidChars = Path.GetInvalidFileNameChars()
        .Concat(new[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|' }).Distinct().ToArray();

    string? Validate(string name)
    {
        if (name.Length == 0) return "名前を入力してください。";
        if (name.EndsWith('.')) return "名前の最後に . は使えません。";
        if (name.IndexOfAny(InvalidChars) >= 0)
            return "次の文字は使えません: \\ / : * ? \" < > |";

        string fileName = name + ext;
        if (string.Equals(fileName, oldFileName, StringComparison.Ordinal)) return null; // 変更なし(呼び出し側で何もしない)

        // 大文字小文字だけの変更は許可する。それ以外で同名のファイルがあれば断る
        bool caseOnly = string.Equals(fileName, oldFileName, StringComparison.OrdinalIgnoreCase);
        if (!caseOnly && File.Exists(Path.Combine(folder, fileName)))
            return "同じ名前のファイルがすでにあります。";
        return null;
    }
}
