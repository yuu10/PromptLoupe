using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace PromptLoupe;

/// <summary>
/// 表示用の画像の先読み。表示中の画像と、一覧でその前後にある画像を、裏で読んでおく。
/// 画像を送ったとき、読み込みを待たずに表示できるようにするため(WPF 版より画像の切り替えが重かったことへの対応)。
/// - ここで読んだ画像は、このクラスが持ち主。表示に使っていても、手放すのは <see cref="Keep"/> と <see cref="Clear"/> だけ
/// - ファイルの更新日時が変わっていたら、読み直す(ほかのアプリで上書きされたとき)
/// - UI スレッドから呼ぶ
/// </summary>
public sealed class ImagePreloader
{
    sealed class Entry
    {
        public required Task<Bitmap> Task;
        public required DateTime Stamp;
    }

    readonly Dictionary<string, Entry> entries = new(StringComparer.OrdinalIgnoreCase);

    // 【調査用・一時的】画像ごとの展開にかかった時間(ミリ秒)。ステータスバーに出して、重さの原因を切り分ける
    readonly System.Collections.Concurrent.ConcurrentDictionary<string, double> decodeMs = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>【調査用】その画像の展開にかかった時間(ミリ秒)。まだ読み終わっていなければ null。</summary>
    public double? DecodeMilliseconds(string path) => decodeMs.TryGetValue(path, out var ms) ? ms : null;

    /// <summary>画像を返す。先読み済みならすぐ終わる。読めなければ例外。</summary>
    public Task<Bitmap> Get(string path)
    {
        var stamp = Stamp(path);
        if (entries.TryGetValue(path, out var e))
        {
            if (e.Stamp == stamp && !e.Task.IsFaulted) return e.Task;
            Release(e);
            entries.Remove(path);
        }

        var task = Task.Run(() =>
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var bmp = ImageLoader.Load(path);
            decodeMs[path] = sw.Elapsed.TotalMilliseconds;
            return bmp;
        });
        entries[path] = new Entry { Task = task, Stamp = stamp };
        return task;
    }

    /// <summary>先読みを始める(結果は待たない。読めない画像は、表示するときに改めて例外になる)。</summary>
    public void Prefetch(string path)
    {
        try { _ = Get(path); }
        catch { /* 先読みの失敗は無視する */ }
    }

    /// <summary>paths にない画像を手放す。表示から外した画像は、ここで捨てる。</summary>
    public void Keep(IEnumerable<string> paths)
    {
        var keep = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
        foreach (var key in entries.Keys.Where(k => !keep.Contains(k)).ToList())
        {
            Release(entries[key]);
            entries.Remove(key);
        }
    }

    /// <summary>すべて手放す(フォルダを閉じたとき)。表示から外してから呼ぶ。</summary>
    public void Clear() => Keep(Array.Empty<string>());

    /// <summary>読み終わっていればすぐ、読み込み中なら読み終わったときに、画像を捨てる(捨てるのは UI スレッドで)。</summary>
    static void Release(Entry e) =>
        e.Task.ContinueWith(t =>
        {
            if (t.Status == TaskStatus.RanToCompletion) Dispatcher.UIThread.Post(t.Result.Dispose);
        }, TaskScheduler.Default);

    static DateTime Stamp(string path)
    {
        try { return File.GetLastWriteTimeUtc(path); }
        catch { return DateTime.MinValue; }
    }
}
