using static PromptLoupe.FileChanges;

namespace PromptLoupe.Tests;

public class FileChangesTests
{
    static readonly string Dir = Path.Combine(Path.GetTempPath(), "pl");
    static string P(params string[] parts) => Path.Combine(new[] { Dir }.Concat(parts).ToArray());
    static bool IsImage(string p) => p.EndsWith(".png", StringComparison.OrdinalIgnoreCase);

    static List<Result> Run(string[] items, Change[] changes, params string[] existing)
    {
        var set = new HashSet<string>(existing);
        return Map(items, changes, set.Contains, IsImage, StringComparison.Ordinal);
    }

    [Fact]
    public void Deleted_IsRemoved() =>
        Assert.Equal(new[] { new Result(P("a.png"), null) },
            Run(new[] { P("a.png"), P("b.png") }, new[] { new Change(P("a.png"), null) }, P("b.png")));

    [Fact]
    public void Renamed_IsReplaced() =>
        Assert.Equal(new[] { new Result(P("a.png"), P("c.png")) },
            Run(new[] { P("a.png") }, new[] { new Change(P("a.png"), P("c.png")) }, P("c.png")));

    [Fact]
    public void RenamedToNonImage_IsRemoved() =>
        Assert.Equal(new[] { new Result(P("a.png"), null) },
            Run(new[] { P("a.png") }, new[] { new Change(P("a.png"), P("a.txt")) }, P("a.txt")));

    [Fact]
    public void FolderDeleted_RemovesItemsInside() =>
        Assert.Equal(new[] { new Result(P("sub", "x.png"), null), new Result(P("sub", "deep", "y.png"), null) },
            Run(new[] { P("sub", "x.png"), P("sub", "deep", "y.png"), P("subway.png") },
                new[] { new Change(P("sub"), null) }, P("subway.png")));

    [Fact]
    public void FolderRenamed_MovesItemsInside() =>
        Assert.Equal(new[] { new Result(P("sub", "deep", "y.png"), P("new", "deep", "y.png")) },
            Run(new[] { P("sub", "deep", "y.png") }, new[] { new Change(P("sub"), P("new")) }, P("new", "deep", "y.png")));

    /// <summary>保存のときに、元のファイルを消して一時ファイルの名前を元に戻すアプリ。ファイルは残っているので、何もしない。</summary>
    [Fact]
    public void DeleteThenRestore_IsIgnored() =>
        Assert.Empty(Run(new[] { P("a.png") },
            new[] { new Change(P("a.png"), null), new Change(P("a.tmp"), P("a.png")) }, P("a.png")));

    [Fact]
    public void RenamedTwice_UsesLastName() =>
        Assert.Equal(new[] { new Result(P("a.png"), P("c.png")) },
            Run(new[] { P("a.png") }, new[] { new Change(P("a.png"), P("b.png")), new Change(P("b.png"), P("c.png")) }, P("c.png")));

    [Fact]
    public void Unrelated_IsIgnored() =>
        Assert.Empty(Run(new[] { P("a.png") }, new[] { new Change(P("z.png"), null) }, P("a.png")));

    [Fact]
    public void CaseInsensitive_OnWindows() =>
        Assert.Single(Map(new[] { P("A.png") }, new[] { new Change(P("a.png"), null) },
            _ => false, IsImage, StringComparison.OrdinalIgnoreCase));
}
