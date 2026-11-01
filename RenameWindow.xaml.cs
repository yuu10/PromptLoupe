using System;
using System.IO;
using System.Linq;
using System.Windows;

namespace PromptLoupe;

/// <summary>ファイル名の変更ダイアログ。拡張子はそのまま残し、名前の部分だけを編集する。</summary>
public partial class RenameWindow : Window
{
    readonly string folder;
    readonly string oldFileName;
    readonly string ext;

    /// <summary>入力された新しいファイル名(拡張子つき)。</summary>
    public string NewFileName { get; private set; } = "";

    public RenameWindow(string folder, string oldFileName)
    {
        InitializeComponent();
        this.folder = folder;
        this.oldFileName = oldFileName;
        ext = Path.GetExtension(oldFileName);
        ExtText.Text = ext;
        NameBox.Text = Path.GetFileNameWithoutExtension(oldFileName);

        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        string name = NameBox.Text.Trim();
        string? error = Validate(name);
        if (error != null)
        {
            ErrorText.Text = error;
            ErrorText.Visibility = Visibility.Visible;
            NameBox.Focus();
            return;
        }

        NewFileName = name + ext;
        DialogResult = true;
    }

    string? Validate(string name)
    {
        if (name.Length == 0) return "名前を入力してください。";
        if (name.EndsWith('.')) return "名前の最後に . は使えません。";
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
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
