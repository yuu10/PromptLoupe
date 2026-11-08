using System.IO.Compression;
using System.Text;

namespace PromptLoupe.Tests;

// 書庫の中の画像(zip だけ。rar・7z はテストの中で作れないので、手で確かめる)。
// ArchiveStore は開いている書庫を1つだけ持つので、同時に走らないように1つのクラスにまとめる。
public class ArchiveTests
{
    static bool IsImage(string p) => p.EndsWith(".png", StringComparison.OrdinalIgnoreCase);

    static string MakeZip(Encoding? names, params (string Name, byte[] Data)[] files)
    {
        string path = Path.Combine(Path.GetTempPath(), "pl-test-" + Guid.NewGuid().ToString("N") + ".zip");
        using (var fs = File.Create(path))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create, false, names))
        {
            foreach (var (name, data) in files)
            {
                var e = zip.CreateEntry(name);
                using var s = e.Open();
                s.Write(data);
            }
        }
        return path;
    }

    [Fact]
    public void ArchivePath_SplitAndCombine()
    {
        string zip = MakeZip(null, ("a.png", new byte[] { 1 }));
        try
        {
            string p = ArchivePath.Combine(zip, "dir\\a.png");
            Assert.Equal(zip + "|dir/a.png", p);
            Assert.True(ArchivePath.TrySplit(p, out var a, out var e));
            Assert.Equal(zip, a);
            Assert.Equal("dir/a.png", e);
            Assert.False(ArchivePath.TrySplit("/no/such.zip|a.png", out _, out _));          // 書庫がない
            Assert.True(ArchivePath.TrySplit("/no/such.zip|a.png", out _, out _, mustExist: false));
            Assert.False(ArchivePath.TrySplit("/tmp/a|b.png", out _, out _, mustExist: false)); // 書庫の拡張子でない
            Assert.True(ArchivePath.HasArchiveExtension("x.CBZ"));
            Assert.False(ArchivePath.HasArchiveExtension("x.png"));
        }
        finally { File.Delete(zip); }
    }

    [Fact]
    public void Store_ListsAndExtracts()
    {
        var sjis = CodePagesEncodingProvider.Instance.GetEncoding(932)!;
        string zip = MakeZip(sjis,
            ("001.png", new byte[] { 1, 2, 3 }),
            ("人物/002.png", new byte[] { 4, 5 }),
            ("readme.txt", new byte[] { 9 }),
            ("inner.zip", new byte[] { 0 }),
            ("dir/", Array.Empty<byte>()));
        try
        {
            var list = ArchiveStore.Open(zip, IsImage);
            Assert.Equal(new[] { "001.png", "人物/002.png" }, list.Images.Select(i => i.Key)); // Shift_JIS の名前も読める
            Assert.Equal(1, list.Archives);
            Assert.False(list.Solid);

            string local = ArchiveStore.Resolve(ArchivePath.Combine(zip, "人物/002.png"));
            Assert.Equal(new byte[] { 4, 5 }, File.ReadAllBytes(local));
            Assert.Equal(local, ArchiveStore.Resolve(ArchivePath.Combine(zip, "人物/002.png"))); // 2回目は同じファイル
            Assert.Equal("/x/y.png", ArchiveStore.Resolve("/x/y.png")); // ふつうのパスはそのまま
            Assert.Throws<FileNotFoundException>(() => ArchiveStore.Resolve(ArchivePath.Combine(zip, "none.png")));

            ArchiveStore.CloseAll();
            Assert.Throws<FileNotFoundException>(() => ArchiveStore.Resolve(ArchivePath.Combine(zip, "001.png")));
        }
        finally
        {
            ArchiveStore.CloseAll();
            File.Delete(zip);
        }
    }

    [Fact]
    public void DecodeName_Utf8OrShiftJis()
    {
        var utf8 = Encoding.UTF8.GetBytes("人物/a.png");
        Assert.Equal("人物/a.png", ArchiveStore.DecodeName(utf8, 0, utf8.Length));
        var sjis = CodePagesEncodingProvider.Instance.GetEncoding(932)!.GetBytes("人物/a.png");
        Assert.Equal("人物/a.png", ArchiveStore.DecodeName(sjis, 0, sjis.Length));
    }
}
