using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;

namespace MViewer;

/// <summary>
/// サムネイルの生成とメモリキャッシュ(ディスクには保存しない)。
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
    static readonly LinkedList<(string Key, BitmapSource Img)> Lru = new();
    static readonly Dictionary<string, LinkedListNode<(string Key, BitmapSource Img)>> Map =
        new(StringComparer.OrdinalIgnoreCase);
    static readonly HashSet<string> Failed = new(StringComparer.OrdinalIgnoreCase);
    static readonly LinkedList<(string Path, Action Done, Action Cancelled)> Queue = new();
    static readonly int MaxWorkers = Math.Max(1, Environment.ProcessorCount / 2);
    static int workers;

    /// <summary>
    /// キャッシュにあれば true(画像は img に入る。生成に失敗した画像は true で img が null)。
    /// </summary>
    public static bool TryGet(string path, out BitmapSource? img)
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

        // 取り下げた要求は、項目側に「待ちをやめた」と知らせるだけ(次に画面に出たときに改めて要求される)
        foreach (var d in dropped) Dispatch(d);
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

            BitmapSource? img = null;
            try { img = Decode(req.Path); }
            catch { /* 壊れた画像・非対応形式 */ }

            lock (Gate)
            {
                if (img != null) Add(req.Path, img);
                else Failed.Add(req.Path);
            }
            Dispatch(req.Done);
        }
    }

    static void Add(string path, BitmapSource img)
    {
        if (Map.TryGetValue(path, out var old)) Lru.Remove(old);
        Map[path] = Lru.AddFirst((path, img));
        while (Lru.Count > Capacity)
        {
            Map.Remove(Lru.Last!.Value.Key);
            Lru.RemoveLast();
        }
    }

    static void Dispatch(Action a) => Application.Current?.Dispatcher.BeginInvoke(a);

    static BitmapSource Decode(string path)
    {
        int w, h;
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            var frame = BitmapFrame.Create(fs, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            w = frame.PixelWidth;
            h = frame.PixelHeight;
        }

        using var fs2 = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.StreamSource = fs2;
        // 長辺が Size になるように、デコード時点で縮小する(メモリと時間の節約)
        if (w >= h) bmp.DecodePixelWidth = Math.Min(Size, w);
        else bmp.DecodePixelHeight = Math.Min(Size, h);
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }
}
