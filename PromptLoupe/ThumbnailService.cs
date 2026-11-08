using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace PromptLoupe;

/// <summary>
/// サムネイルの生成とメモリキャッシュ(ディスクには保存しない)。WPF 版と同じ仕組み。
/// - 画面に表示された項目だけ、バックグラウンドで縮小画像を作る
/// - 直近に要求されたものを先に処理する(スクロール中も見えている分が先に出る)
/// - キャッシュは最大 Capacity 枚まで。超えたら古いものから捨てる
/// </summary>
public static class ThumbnailService
{
    const int Capacity = 600;      // メモリに持つ最大枚数
    const int Size = 160;          // サムネイルの長辺(ピクセル。一覧の最大表示 96px の高DPI分を含む)
    const int MaxQueue = 300;      // 待ち行列の上限(古い要求から捨てる)

    static readonly object Gate = new();
    static readonly LinkedList<(string Key, Bitmap Img)> Lru = new();
    static readonly Dictionary<string, LinkedListNode<(string Key, Bitmap Img)>> Map = new();
    static readonly HashSet<string> Failed = new();
    static readonly LinkedList<(string Path, Action Done, Action Cancelled)> Queue = new();
    static readonly int MaxWorkers = Math.Max(1, Environment.ProcessorCount / 2);
    static int workers;

    /// <summary>キャッシュにあれば true(画像は img に入る。生成に失敗した画像は true で img が null)。</summary>
    public static bool TryGet(string path, out Bitmap? img)
    {
        lock (Gate)
        {
            if (Map.TryGetValue(path, out var node))
            {
                Lru.Remove(node);
                Lru.AddFirst(node);
                img = node.Value.Img;
                return true;
            }
            if (Failed.Contains(path))
            {
                img = null;
                return true;
            }
        }
        img = null;
        return false;
    }

    /// <summary>
    /// 生成を依頼する。完了したら done を、待ち行列があふれて取り下げられたら cancelled を、UIスレッドで呼ぶ。
    /// (取り下げ時に再要求すると、あふれ続けて止まらなくなるので、cancelled では再要求しないこと)
    /// </summary>
    public static void Request(string path, Action done, Action cancelled)
    {
        var dropped = new List<Action>();
        bool startWorker = false;

        lock (Gate)
        {
            Queue.AddFirst((path, done, cancelled));
            while (Queue.Count > MaxQueue)
            {
                dropped.Add(Queue.Last!.Value.Cancelled);
                Queue.RemoveLast();
            }
            if (workers < MaxWorkers)
            {
                workers++;
                startWorker = true;
            }
        }

        foreach (var d in dropped) Dispatcher.UIThread.Post(d);
        if (startWorker) Task.Run(Worker);
    }

    static void Worker()
    {
        while (true)
        {
            (string Path, Action Done, Action Cancelled) req;
            lock (Gate)
            {
                if (Queue.Count == 0)
                {
                    workers--;
                    return;
                }
                req = Queue.First!.Value;
                Queue.RemoveFirst();
            }

            Bitmap? img = null;
            try { img = ImageLoader.Load(req.Path, Size); }
            catch { /* 壊れた画像・非対応形式 */ }

            lock (Gate)
            {
                if (img != null) Add(req.Path, img);
                else Failed.Add(req.Path);
            }
            Dispatcher.UIThread.Post(req.Done);
        }
    }

    static void Add(string path, Bitmap img)
    {
        if (Map.TryGetValue(path, out var old)) Lru.Remove(old);
        Map[path] = Lru.AddFirst((path, img));
        // 捨てた画像は Dispose しない(一覧の項目がまだ表示しているかもしれないので、GC に任せる)
        while (Lru.Count > Capacity)
        {
            Map.Remove(Lru.Last!.Value.Key);
            Lru.RemoveLast();
        }
    }
}
