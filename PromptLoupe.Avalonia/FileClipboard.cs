using System.Text;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

namespace PromptLoupe;

/// <summary>
/// ファイルと画像のクリップボード(Avalonia 11.3 の DataTransfer)。WPF 版の Clipboard.SetFileDropList などの代わり。
///
/// OS ごとにできること(Avalonia 11.3.22 の中身を読んで確かめた。2026-11-05):
/// - ファイル(DataFormat.File): Windows は CF_HDROP、Linux(X11)は text/uri-list、Mac は public.file-url になる。
///   どの OS でも、ファイルマネージャーとの間でコピー・貼り付けができる。
/// - 切り取り: クリップボードに「移動」の印を一緒に入れて伝える。印の形式は OS ごとに違う。
///   Windows は「Preferred DropEffect」(エクスプローラーと同じ。1 = コピー、2 = 移動)。
///   Linux は GNOME(Nautilus など)の x-special/gnome-copied-files と、KDE(Dolphin)の application/x-kde-cutselection。
///   Mac の Finder には、クリップボードで「移動」を伝える決まりがないので、Finder に貼り付けるとコピーになる
///   (このアプリの中で貼り付けたときだけ移動する。アプリ用の印 <see cref="AppCut"/> で見分ける)。
/// - 画像(DataFormat.Bitmap): Windows は DIB・DIBV5・PNG、Linux は image/png、Mac は PNG になる。
/// - Windows では、アプリを閉じたあとも貼り付けられるように、入れたあとで FlushAsync する(WPF 版の SetDataObject(data, true) と同じ)。
///   Linux・Mac では FlushAsync は何もしない(アプリを閉じると、クリップボードの中身も消えることがある)。
/// </summary>
public static class FileClipboard
{
    /// <summary>Windows のエクスプローラーの形式(4 バイトの整数。1 = コピー、2 = 移動)。</summary>
    static readonly DataFormat<byte[]> PreferredDropEffect = DataFormat.CreateBytesPlatformFormat("Preferred DropEffect");

    /// <summary>GNOME のファイルマネージャーの形式("cut" または "copy" の行のあとに、file:// の URI を1行ずつ)。</summary>
    static readonly DataFormat<byte[]> GnomeCopiedFiles = DataFormat.CreateBytesPlatformFormat("x-special/gnome-copied-files");

    /// <summary>KDE のファイルマネージャーの形式("1" なら切り取り)。</summary>
    static readonly DataFormat<byte[]> KdeCutSelection = DataFormat.CreateBytesPlatformFormat("application/x-kde-cutselection");

    /// <summary>このアプリが切り取ったことの印(どの OS でも使う)。</summary>
    static readonly DataFormat<byte[]> AppCut = DataFormat.CreateBytesApplicationFormat("PromptLoupe.cut");

    /// <summary>ファイルをクリップボードに入れる。cut なら、貼り付けたときに移動するように伝える。</summary>
    public static async Task SetFilesAsync(IClipboard clipboard, IStorageProvider storage, IReadOnlyList<string> paths, bool cut)
    {
        var data = new DataTransfer();
        bool first = true;
        foreach (string path in paths)
        {
            var file = await storage.TryGetFileFromPathAsync(path);
            if (file == null) continue;
            var item = new DataTransferItem();
            item.SetFile(file);
            if (first)
            {
                // 切り取り・コピーの印は、最初の項目に一緒に入れる
                if (OperatingSystem.IsWindows())
                {
                    item.Set(PreferredDropEffect, BitConverter.GetBytes(cut ? 2 : 1));
                }
                else if (OperatingSystem.IsLinux())
                {
                    var sb = new StringBuilder(cut ? "cut" : "copy");
                    foreach (string p in paths) sb.Append('\n').Append(new Uri(Path.GetFullPath(p)).AbsoluteUri);
                    item.Set(GnomeCopiedFiles, Encoding.UTF8.GetBytes(sb.ToString()));
                    if (cut) item.Set(KdeCutSelection, Encoding.ASCII.GetBytes("1"));
                }
                if (cut) item.Set(AppCut, new byte[] { 1 });
                first = false;
            }
            data.Add(item);
        }
        if (first) throw new IOException("ファイルが見つかりません");

        await clipboard.SetDataAsync(data);
        await FlushAsync(clipboard);
    }

    /// <summary>クリップボードに入っているファイルのパスと、切り取り(移動)かどうか。ファイルがなければ null。</summary>
    public static async Task<(List<string> Paths, bool Move)?> GetFilesAsync(IClipboard clipboard)
    {
        var files = await clipboard.TryGetFilesAsync();
        if (files == null || files.Length == 0) return null;
        var paths = files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
        if (paths.Count == 0) return null;

        bool move = false;
        var formats = await clipboard.GetDataFormatsAsync();

        if (formats.Contains(AppCut)) move = true;
        else if (OperatingSystem.IsWindows() && formats.Contains(PreferredDropEffect))
        {
            var b = await clipboard.TryGetValueAsync(PreferredDropEffect);
            if (b is { Length: >= 4 })
            {
                int effect = BitConverter.ToInt32(b, 0);
                move = (effect & 2) != 0 && (effect & 1) == 0; // WPF 版と同じ判定
            }
        }
        else if (OperatingSystem.IsLinux())
        {
            if (formats.Contains(GnomeCopiedFiles) && await clipboard.TryGetValueAsync(GnomeCopiedFiles) is { } g)
            {
                string text = Encoding.UTF8.GetString(g);
                int nl = text.IndexOf('\n');
                move = (nl >= 0 ? text[..nl] : text).Trim() == "cut";
            }
            else if (formats.Contains(KdeCutSelection) && await clipboard.TryGetValueAsync(KdeCutSelection) is { } k)
            {
                move = Encoding.ASCII.GetString(k).Trim() == "1";
            }
        }
        return (paths, move);
    }

    /// <summary>クリップボードにファイルが入っているか(貼り付けのメニューを使えるようにするか)。</summary>
    public static async Task<bool> HasFilesAsync(IClipboard clipboard)
    {
        try
        {
            var formats = await clipboard.GetDataFormatsAsync();
            return formats.Contains(DataFormat.File);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>画像をクリップボードに入れる(他のアプリに貼り付ける用)。</summary>
    public static async Task SetBitmapAsync(IClipboard clipboard, Bitmap bitmap)
    {
        await clipboard.SetBitmapAsync(bitmap);
        await FlushAsync(clipboard);
    }

    /// <summary>Windows では、アプリを閉じても中身が残るようにする(ほかの OS では何もしない)。</summary>
    static async Task FlushAsync(IClipboard clipboard)
    {
        if (!OperatingSystem.IsWindows()) return;
        try { await clipboard.FlushAsync(); }
        catch { /* 残せなくても、アプリを開いている間は貼り付けられる */ }
    }
}
