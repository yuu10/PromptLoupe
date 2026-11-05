using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;

namespace PromptLoupe;

/// <summary>
/// 使い方マニュアル(WPF 版の HelpWindow の代わり)。本文は、アセンブリに埋め込んだ Help.txt。
/// 書き方は Help.txt の先頭に書いてある(見出し・リード文・注意の枠・箇条書き・字下げした段落、太字・コード・改行)。
/// </summary>
public partial class HelpWindow : Window
{
    /// <summary>コード(ファイル名やタグなど)の文字。</summary>
    static readonly FontFamily CodeFont = new("Consolas,Menlo,DejaVu Sans Mono,monospace");

    public HelpWindow()
    {
        InitializeComponent();
        Build(LoadText());
    }

    /// <summary>埋め込んだ Help.txt を読み、OS によって違う所(設定の保存先)を埋める。</summary>
    static string LoadText()
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("PromptLoupe.Help.txt");
        if (s == null) return "# 使い方\n使い方の文章が見つかりません。";
        using var r = new StreamReader(s);
        string settingsPath = Path.Combine(AppSettings.DataDir, "settings.json");
        return r.ReadToEnd().Replace("{settings}", $"`{settingsPath}`");
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

    void Build(string text)
    {
        foreach (string raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            string line = raw.TrimEnd();
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal)) continue;

            if (line.StartsWith("## ", StringComparison.Ordinal))
                Body.Children.Add(new Border { Classes = { "h2" }, Child = Para(line[3..]) });
            else if (line.StartsWith("# ", StringComparison.Ordinal))
                Body.Children.Add(Para(line[2..], "h1"));
            else if (line.StartsWith(">> ", StringComparison.Ordinal))
                Body.Children.Add(Para(line[3..], "lead"));
            else if (line.StartsWith("! ", StringComparison.Ordinal))
                Body.Children.Add(new Border { Classes = { "note" }, Child = Para(line[2..]) });
            else if (line.StartsWith("- ", StringComparison.Ordinal))
                Body.Children.Add(Bullet(line[2..]));
            else if (line.StartsWith("  ", StringComparison.Ordinal))
                Body.Children.Add(Para(line.Trim(), "indent"));
            else
                Body.Children.Add(Para(line, "body"));
        }
    }

    static Control Bullet(string text)
    {
        var grid = new Grid { Classes = { "bullet" }, ColumnDefinitions = new ColumnDefinitions("16,*") };
        grid.Children.Add(new TextBlock { Text = "•", LineHeight = 22 });
        var p = Para(text);
        Grid.SetColumn(p, 1);
        grid.Children.Add(p);
        return grid;
    }

    /// <summary>1つの段落。**太字**、`コード`、{br}(改行)を、文字の飾りに変える。</summary>
    static SelectableTextBlock Para(string text, string? cls = null)
    {
        var tb = new SelectableTextBlock();
        if (cls != null) tb.Classes.Add(cls);
        var inlines = new InlineCollection();
        bool bold = false;
        int i = 0;
        var buf = new System.Text.StringBuilder();

        void Flush()
        {
            if (buf.Length == 0) return;
            var run = new Run(buf.ToString());
            if (bold) run.FontWeight = FontWeight.Bold;
            inlines.Add(run);
            buf.Clear();
        }

        while (i < text.Length)
        {
            if (text[i] == '`')
            {
                int end = text.IndexOf('`', i + 1);
                if (end > i)
                {
                    Flush();
                    var code = new Run(text[(i + 1)..end]) { FontFamily = CodeFont };
                    if (bold) code.FontWeight = FontWeight.Bold;
                    inlines.Add(code);
                    i = end + 1;
                    continue;
                }
            }
            if (string.CompareOrdinal(text, i, "**", 0, 2) == 0)
            {
                Flush();
                bold = !bold;
                i += 2;
                continue;
            }
            if (string.CompareOrdinal(text, i, "{br}", 0, 4) == 0)
            {
                Flush();
                inlines.Add(new LineBreak());
                i += 4;
                continue;
            }
            buf.Append(text[i]);
            i++;
        }
        Flush();
        tb.Inlines = inlines;
        return tb;
    }
}
