namespace PromptLoupe.Tests;

public class NaturalComparerTests
{
    static int Sign(string a, string b) => Math.Sign(NaturalComparer.Instance.Compare(a, b));

    [Theory]
    [InlineData("img2.png", "img10.png")]
    [InlineData("a1", "ab")]                 // 数字は文字より前
    [InlineData("abc", "ABD")]               // 大文字・小文字は区別しない
    [InlineData("x", "x1")]                  // 短い方が前
    [InlineData("img7", "img007a")]
    [InlineData("1", "01")]                  // 同じ数なら、0 の少ない方が前
    [InlineData("99999999999999999999", "100000000000000000000")] // long に収まらない数
    [InlineData(@"sub\a.png", @"sub\b.png")]
    public void Less(string a, string b)
    {
        Assert.Equal(-1, Sign(a, b));
        Assert.Equal(1, Sign(b, a));
    }

    [Fact]
    public void Equal() => Assert.Equal(0, Sign("img10.png", "img10.png"));

    [Fact]
    public void SortsLikeExplorer()
    {
        var list = new List<string> { "img10.png", "IMG1.png", "img2.png", "img2a.png", "a.png", "img02.png" };
        list.Sort(NaturalComparer.Instance);
        Assert.Equal(new[] { "a.png", "IMG1.png", "img2.png", "img02.png", "img2a.png", "img10.png" }, list);
    }
}
