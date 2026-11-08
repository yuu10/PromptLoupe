namespace PromptLoupe.Tests;

// パスの区切りは OS によって違うので、テストは Linux・Mac の書き方(/)のときだけ確かめる(CI のテストは Linux)。
public class FolderPathTests
{
    static bool Unix => !OperatingSystem.IsWindows();

    [Fact]
    public void Segments_FromRoot()
    {
        if (!Unix) return;
        Assert.Equal(new[] { ("/", "/"), ("home", "/home"), ("yuto", "/home/yuto"), ("AI", "/home/yuto/AI") },
            FolderPath.Segments("/home/yuto/AI/"));
    }

    [Fact]
    public void Segments_Root()
    {
        if (!Unix) return;
        Assert.Equal(new[] { ("/", "/") }, FolderPath.Segments("/"));
    }

    [Fact]
    public void Normalize_TrimsTrailingSeparator()
    {
        if (!Unix) return;
        Assert.Equal("/home/yuto", FolderPath.Normalize("/home/yuto/"));
        Assert.Equal("/", FolderPath.Normalize("/"));
        Assert.Equal("/home", FolderPath.Normalize("/home/yuto/.."));
    }

    [Fact]
    public void Parent()
    {
        if (!Unix) return;
        Assert.Equal("/home", FolderPath.Parent("/home/yuto"));
        Assert.Null(FolderPath.Parent("/"));
    }

    [Fact]
    public void RootName_Windows() => Assert.Equal("C:", FolderPath.RootName(@"C:\".Replace('\\', Path.DirectorySeparatorChar)));

    [Fact]
    public void RelativeParts()
    {
        if (!Unix) return;
        var c = StringComparison.Ordinal;
        Assert.Equal(new[] { "Pictures", "AI" }, FolderPath.RelativeParts("/home/yuto", "/home/yuto/Pictures/AI", c));
        Assert.Empty(FolderPath.RelativeParts("/home/yuto", "/home/yuto/", c)!);
        Assert.Equal(new[] { "home" }, FolderPath.RelativeParts("/", "/home", c));
        Assert.Null(FolderPath.RelativeParts("/home/yuto", "/home/yutoX/a", c)); // 名前の前半が同じだけのフォルダは外
        Assert.Null(FolderPath.RelativeParts("/home/Yuto", "/home/yuto/a", c));
        Assert.Equal(new[] { "a" }, FolderPath.RelativeParts("/home/Yuto", "/home/yuto/a", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FromInput()
    {
        Assert.Null(FolderPath.FromInput("   ", "/home/u"));
        Assert.Null(FolderPath.FromInput("\"\"", "/home/u"));
        Assert.Equal(@"C:\Users\a b", FolderPath.FromInput(" \"C:\\Users\\a b\" ", "/home/u"));
        Assert.Equal("/home/u", FolderPath.FromInput("~", "/home/u"));
        Assert.Equal(Path.Combine("/home/u", "Pictures"), FolderPath.FromInput("~/Pictures", "/home/u"));
    }
}
