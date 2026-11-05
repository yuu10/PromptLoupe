using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace PromptLoupe;

/// <summary>
/// OS ごとに違うファイル操作(既定のアプリで開く・ファイルマネージャーで表示・ごみ箱へ移す・プロパティ)。
/// Windows の処理は WPF 版と同じ。Mac・Linux は、OS に付いているコマンドを使う。
/// </summary>
public static class PlatformShell
{
    /// <summary>ファイルマネージャーの名前(メニューや案内に出す)。</summary>
    public static string FileManagerName =>
        OperatingSystem.IsWindows() ? "エクスプローラー" :
        OperatingSystem.IsMacOS() ? "Finder" : "ファイルマネージャー";

    /// <summary>既定のアプリで開く。</summary>
    public static void OpenWithDefaultApp(string path)
    {
        if (OperatingSystem.IsLinux()) Run("xdg-open", path);
        else if (OperatingSystem.IsMacOS()) Run("open", path);
        else Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    /// <summary>ファイルを選んだ状態でファイルマネージャーを開く(file が null ならフォルダを開く)。</summary>
    public static void Reveal(string? file, string? folder)
    {
        if (file != null && File.Exists(file))
        {
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo("explorer.exe") { Arguments = $"/select,\"{file}\"", UseShellExecute = true });
            else if (OperatingSystem.IsMacOS())
                Run("open", "-R", file);
            else if (!TryRun("dbus-send", "--session", "--dest=org.freedesktop.FileManager1", "--type=method_call",
                         "/org/freedesktop/FileManager1", "org.freedesktop.FileManager1.ShowItems",
                         $"array:string:{new Uri(file).AbsoluteUri}", "string:"))
                Run("xdg-open", Path.GetDirectoryName(file)!); // ファイルを選べないときは、フォルダだけ開く
        }
        else if (folder != null)
        {
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo("explorer.exe") { Arguments = $"\"{folder}\"", UseShellExecute = true });
            else Run(OperatingSystem.IsMacOS() ? "open" : "xdg-open", folder);
        }
    }

    /// <summary>ごみ箱に移す(完全には削除しない)。失敗したら例外。</summary>
    public static void MoveToTrash(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
        }
        else if (OperatingSystem.IsMacOS())
        {
            // Finder に頼む(ごみ箱から「戻す」も使える)
            string escaped = path.Replace("\\", "\\\\").Replace("\"", "\\\"");
            RunChecked("osascript", "-e", $"tell application \"Finder\" to delete POSIX file \"{escaped}\"");
        }
        else
        {
            // GNOME などは gio、なければ freedesktop.org の決まりに沿って、自分でごみ箱に移す
            if (!TryRun("gio", "trash", "--", path)) TrashFreedesktop(path);
        }

        if (File.Exists(path)) throw new IOException("ごみ箱に移せませんでした");
    }

    /// <summary>
    /// freedesktop.org のごみ箱(~/.local/share/Trash)に移す。files に本体、info に元の場所と日時を書く。
    /// ホームと別のドライブのファイルは移せない(例外になる)。
    /// </summary>
    static void TrashFreedesktop(string path)
    {
        string dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } x
            ? x
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        string trash = Path.Combine(dataHome, "Trash");
        string files = Path.Combine(trash, "files");
        string info = Path.Combine(trash, "info");
        Directory.CreateDirectory(files);
        Directory.CreateDirectory(info);

        string name = Path.GetFileName(path);
        string baseName = Path.GetFileNameWithoutExtension(name), ext = Path.GetExtension(name);
        string dest = name;
        for (int i = 2; File.Exists(Path.Combine(files, dest)) || File.Exists(Path.Combine(info, dest + ".trashinfo")); i++)
            dest = $"{baseName} ({i}){ext}";

        string infoPath = Path.Combine(info, dest + ".trashinfo");
        var sb = new StringBuilder("[Trash Info]\n");
        sb.Append("Path=").Append(Uri.EscapeDataString(Path.GetFullPath(path)).Replace("%2F", "/")).Append('\n');
        sb.Append("DeletionDate=").Append(DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss")).Append('\n');
        File.WriteAllText(infoPath, sb.ToString());
        try
        {
            File.Move(path, Path.Combine(files, dest));
        }
        catch
        {
            File.Delete(infoPath);
            throw;
        }
    }

    // ───────── プロパティ(Windows のみ) ─────────

    /// <summary>この OS で「プロパティ」が使えるか(Windows だけ。Mac・Linux ではメニューに出さない)。</summary>
    public static bool CanShowProperties => OperatingSystem.IsWindows();

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct ShellExecuteInfo
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public string lpVerb;
        public string lpFile;
        public string? lpParameters;
        public string? lpDirectory;
        public int nShow;
        public IntPtr hInstApp;
        public IntPtr lpIDList;
        public string? lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIcon;
        public IntPtr hProcess;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool ShellExecuteEx(ref ShellExecuteInfo info);

    /// <summary>Windows 標準の「プロパティ」画面を開く(WPF 版と同じ)。開けなかったら false。</summary>
    [SupportedOSPlatform("windows")]
    public static bool ShowProperties(string path)
    {
        var info = new ShellExecuteInfo
        {
            cbSize = Marshal.SizeOf<ShellExecuteInfo>(),
            fMask = 0x0000000C, // SEE_MASK_INVOKEIDLIST
            lpVerb = "properties",
            lpFile = path,
            nShow = 1
        };
        return ShellExecuteEx(ref info);
    }

    // ───────── コマンドの実行 ─────────

    static void Run(string file, params string[] args)
    {
        var psi = new ProcessStartInfo(file) { UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        Process.Start(psi);
    }

    /// <summary>終わるまで待ち、成功(終了コード 0)なら true。コマンドが無いときも false。</summary>
    static bool TryRun(string file, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(file)
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            if (p == null) return false;
            p.StandardOutput.ReadToEnd();
            p.StandardError.ReadToEnd();
            if (!p.WaitForExit(15000)) return false;
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    static void RunChecked(string file, params string[] args)
    {
        var psi = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new IOException($"{file} を実行できません");
        p.StandardOutput.ReadToEnd();
        string err = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new IOException(err.Trim().Length > 0 ? err.Trim() : $"{file} が失敗しました");
    }
}
