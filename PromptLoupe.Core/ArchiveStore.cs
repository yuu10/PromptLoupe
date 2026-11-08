using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace PromptLoupe;

/// <summary>書庫の中の画像1つ(Key は書庫の中の名前。区切りは「/」)。</summary>
public sealed record ArchiveEntryInfo(string Key, long Size, DateTime Modified);

/// <summary>書庫の中身の一覧。Encrypted はパスワード付きで読めない画像の数、Archives は中にあった書庫の数(開かない)。</summary>
public sealed record ArchiveListing(List<ArchiveEntryInfo> Images, int Encrypted, int Archives, bool Solid);

/// <summary>
/// 書庫(zip・rar・7z。SharpCompress で読む)の中の画像を、一時フォルダに書き出して、ふつうのファイルとして読めるようにする。
/// 画像の読み込み・メタデータの読み取り・HEIC(OS の機能はファイルのパスを要る)は、どれも書き出したファイルを読む。
/// - ソリッドでない書庫は、要るときに1つずつ書き出す(どの画像からでもすぐ読める)。
/// - ソリッドの書庫(まとめて圧縮した rar・7z)は、途中の画像だけを取り出すと毎回先頭から展開し直すことになるので、
///   開いたときに裏で先頭から順に全部を書き出し、要る画像はその書き出しを待つ。
/// 開いておくのは最後に開いた書庫だけ。別の書庫やフォルダを開いたら閉じて、一時フォルダを消す。
/// 一時フォルダは %TEMP%/PromptLoupe/archives/<プロセス ID>-<番号>。異常終了で残ったものは、次の起動時に消す。
/// </summary>
public static class ArchiveStore
{
    static readonly object gate = new();
    static Session? current;
    static int sessionSeq;

    static string TempRoot => Path.Combine(Path.GetTempPath(), "PromptLoupe", "archives");

    /// <summary>
    /// 書庫を開いて、中の画像の一覧を返す。前に開いていた書庫は閉じる。
    /// isImage は、画像として扱う名前か(拡張子で判断する)。読めない書庫は例外。
    /// </summary>
    public static ArchiveListing Open(string archive, Func<string, bool> isImage)
    {
        var session = new Session(archive, NewTempDir(), isImage);
        Session? old;
        lock (gate)
        {
            old = current;
            current = session;
        }
        old?.Dispose();
        try { return session.Open(); }
        catch
        {
            lock (gate) if (current == session) current = null;
            session.Dispose();
            throw;
        }
    }

    /// <summary>開いている書庫を閉じる(ふつうのフォルダを開いたとき・終了するとき)。wait なら、一時フォルダを消し終わるまで少し待つ。</summary>
    public static void CloseAll(bool wait = false)
    {
        Session? old;
        lock (gate)
        {
            old = current;
            current = null;
        }
        var closing = old?.Close();
        if (wait) try { closing?.Wait(TimeSpan.FromSeconds(3)); } catch { }
    }

    /// <summary>
    /// 書庫の中の画像のパスなら、一時フォルダに書き出したファイルのパスを返す(書き出し終わるまで待つ)。
    /// ふつうのパスはそのまま返す。開いていない書庫の中を指していたら FileNotFoundException。
    /// </summary>
    public static string Resolve(string path)
    {
        if (!ArchivePath.TrySplit(path, out var archive, out var entry, mustExist: false)) return path;
        Session? s;
        lock (gate) s = current;
        if (s == null || !string.Equals(s.Archive, archive, FolderPath.Comparison))
            throw new FileNotFoundException("書庫を開いていません", path);
        return s.Resolve(ArchivePath.NormalizeEntry(entry));
    }

    /// <summary>前回までに残った一時フォルダ(動いていないプロセスのもの)を消す。起動時に裏で呼ぶ。</summary>
    public static void CleanupStale()
    {
        try
        {
            if (!Directory.Exists(TempRoot)) return;
            foreach (var dir in Directory.GetDirectories(TempRoot))
            {
                string name = Path.GetFileName(dir);
                int dash = name.IndexOf('-');
                if (dash > 0 && int.TryParse(name[..dash], out int pid) && IsRunning(pid)) continue;
                TryDelete(dir);
            }
        }
        catch { /* 消せなくても困らない */ }
    }

    static bool IsRunning(int pid)
    {
        if (pid == Environment.ProcessId) return true;
        try
        {
            using var p = Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch { return false; }
    }

    static string NewTempDir() =>
        Path.Combine(TempRoot, $"{Environment.ProcessId}-{Interlocked.Increment(ref sessionSeq)}");

    static void TryDelete(string dir)
    {
        try { Directory.Delete(dir, recursive: true); }
        catch { /* 使用中など。次の起動時に消す */ }
    }

    // ───── 書庫の中の名前の文字コード ─────

    static readonly Encoding StrictUtf8 = new UTF8Encoding(false, throwOnInvalidBytes: true);
    static readonly Lazy<Encoding> ShiftJis = new(() =>
        CodePagesEncodingProvider.Instance.GetEncoding(932) ?? Encoding.Latin1);

    /// <summary>
    /// 名前のバイト列を文字にする。UTF-8 として正しければ UTF-8、そうでなければ Shift_JIS(日本語の Windows で作った zip は、
    /// UTF-8 の印がなく Shift_JIS で名前が入っていることが多い)。
    /// </summary>
    internal static string DecodeName(byte[] bytes, int index, int count)
    {
        try { return StrictUtf8.GetString(bytes, index, count); }
        catch (DecoderFallbackException) { return ShiftJis.Value.GetString(bytes, index, count); }
    }

    // UTF-8 の印のない zip の名前に使う文字コード(rar・7z は Unicode で名前を持つので、これは使わない)
    static ReaderOptions Options() => new()
    {
        ArchiveEncoding = new ArchiveEncoding { Default = Utf8OrShiftJis.Instance },
    };

    /// <summary>読むときに、UTF-8 として正しければ UTF-8、そうでなければ Shift_JIS として読む文字コード。書き込みには使わない。</summary>
    sealed class Utf8OrShiftJis : Encoding
    {
        public static readonly Utf8OrShiftJis Instance = new();

        public override string GetString(byte[] bytes, int index, int count) => DecodeName(bytes, index, count);
        public override int GetCharCount(byte[] bytes, int index, int count) => DecodeName(bytes, index, count).Length;
        public override int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex)
        {
            string s = DecodeName(bytes, byteIndex, byteCount);
            s.CopyTo(0, chars, charIndex, s.Length);
            return s.Length;
        }
        public override int GetMaxCharCount(int byteCount) => byteCount;

        // 書き込み(名前をバイト列にする)は UTF-8 で
        public override int GetByteCount(char[] chars, int index, int count) => UTF8.GetByteCount(chars, index, count);
        public override int GetBytes(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex) =>
            UTF8.GetBytes(chars, charIndex, charCount, bytes, byteIndex);
        public override int GetMaxByteCount(int charCount) => UTF8.GetMaxByteCount(charCount);
    }

    // ───── 開いている書庫 ─────

    sealed class Item
    {
        public required IArchiveEntry Source;
        public required string Local;
        public readonly TaskCompletionSource<string> Done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    sealed class Session : IDisposable
    {
        public string Archive { get; }
        readonly string dir;
        readonly Func<string, bool> isImage;
        readonly object io = new(); // 書庫の読み出しは同時にできないので、1つずつ
        readonly Dictionary<string, Item> items = new(StringComparer.Ordinal);
        readonly CancellationTokenSource cts = new();
        IArchive? archive;
        bool solid;
        Task? extractAll;
        int disposed;

        public Session(string archive, string dir, Func<string, bool> isImage)
        {
            Archive = archive;
            this.dir = dir;
            this.isImage = isImage;
        }

        public ArchiveListing Open()
        {
            archive = ArchiveFactory.Open(Archive, Options());
            solid = archive.IsSolid;
            Directory.CreateDirectory(dir);

            var images = new List<ArchiveEntryInfo>();
            int encrypted = 0, nested = 0, n = 0;
            DateTime fallback = File.GetLastWriteTime(Archive);
            foreach (var e in archive.Entries)
            {
                if (e.IsDirectory || string.IsNullOrEmpty(e.Key)) continue;
                string key = ArchivePath.NormalizeEntry(e.Key);
                if (ArchivePath.HasArchiveExtension(key)) { nested++; continue; }
                if (!isImage(key)) continue;
                if (e.IsEncrypted) { encrypted++; continue; }
                if (items.ContainsKey(key)) continue; // 同じ名前が2つあるとき(まれ)は先のものだけ

                string local = Path.Combine(dir, (n++).ToString("D6") + Path.GetExtension(key).ToLowerInvariant());
                items[key] = new Item { Source = e, Local = local };
                images.Add(new ArchiveEntryInfo(key, e.Size, e.LastModifiedTime ?? fallback));
            }

            if (solid && items.Count > 0) extractAll = Task.Run(ExtractAll);
            return new ArchiveListing(images, encrypted, nested, solid);
        }

        public string Resolve(string key)
        {
            if (!items.TryGetValue(key, out var item)) throw new FileNotFoundException("書庫の中に見つかりません", key);
            if (item.Done.Task.IsCompleted) return item.Done.Task.GetAwaiter().GetResult();

            if (solid)
            {
                // 裏で先頭から書き出しているので、その画像の番が来るのを待つ
                return item.Done.Task.GetAwaiter().GetResult();
            }

            lock (io)
            {
                if (!item.Done.Task.IsCompleted)
                {
                    try
                    {
                        if (Volatile.Read(ref disposed) != 0) throw new ObjectDisposedException("書庫は閉じられました");
                        WriteEntry(item.Source, item.Local);
                        item.Done.TrySetResult(item.Local);
                    }
                    catch (Exception ex)
                    {
                        item.Done.TrySetException(ex);
                    }
                }
            }
            return item.Done.Task.GetAwaiter().GetResult();
        }

        static void WriteEntry(IArchiveEntry entry, string local)
        {
            using var src = entry.OpenEntryStream();
            using var dst = File.Create(local);
            src.CopyTo(dst);
        }

        /// <summary>ソリッドの書庫: 先頭から順に展開して、画像を書き出す。</summary>
        void ExtractAll()
        {
            var byEntry = items.Values.ToDictionary(i => ArchivePath.NormalizeEntry(i.Source.Key!), StringComparer.Ordinal);
            try
            {
                using var reader = archive!.ExtractAllEntries();
                while (reader.MoveToNextEntry())
                {
                    if (cts.IsCancellationRequested) break;
                    var e = reader.Entry;
                    if (e.IsDirectory || e.Key == null) continue;
                    if (!byEntry.TryGetValue(ArchivePath.NormalizeEntry(e.Key), out var item) || item.Done.Task.IsCompleted) continue;
                    try
                    {
                        using (var dst = File.Create(item.Local)) reader.WriteEntryTo(dst);
                        item.Done.TrySetResult(item.Local);
                    }
                    catch (Exception ex)
                    {
                        item.Done.TrySetException(ex);
                    }
                }
            }
            catch (Exception ex)
            {
                foreach (var i in items.Values) i.Done.TrySetException(ex);
            }
            // 最後まで来ても見つからなかったもの
            foreach (var i in items.Values)
                i.Done.TrySetException(new FileNotFoundException("書庫から取り出せませんでした", i.Local));
        }

        public void Dispose() => Close();

        /// <summary>閉じる。書庫を閉じて一時フォルダを消すのは裏で行い、その終わりを返す。</summary>
        public Task Close()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return Task.CompletedTask;
            cts.Cancel();
            var closed = new ObjectDisposedException("書庫は閉じられました");
            foreach (var i in items.Values) i.Done.TrySetException(closed);

            // 裏の書き出しや、読み出しの途中で閉じないように、終わってから閉じる
            return Task.Run(() =>
            {
                try { extractAll?.Wait(); } catch { }
                lock (io)
                {
                    try { archive?.Dispose(); } catch { }
                }
                TryDelete(dir);
            });
        }
    }
}
