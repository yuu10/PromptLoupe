namespace PromptLoupe.Tests;

public class PromptTextTests
{
    [Theory]
    [InlineData("Hatsune_Miku", "hatsune miku")]
    [InlineData(@"Rem \(Re:Zero\)", "rem (re:zero)")]
    [InlineData(null, "")]
    [InlineData("", "")]
    public void Normalize(string? input, string expected) =>
        Assert.Equal(expected, PromptText.Normalize(input));

    [Fact]
    public void SplitTags_KeepsCommasInsideBrackets() =>
        Assert.Equal(new[] { "a", "(b, c:1.2)", "<lora:x, y:0.8>", "d" },
            PromptText.SplitTags("a, (b, c:1.2), <lora:x, y:0.8>,\nd"));

    [Fact]
    public void SplitTags_Unbalanced_IgnoresBrackets() =>
        Assert.Equal(new[] { "a", "(b", "c" }, PromptText.SplitTags("a, (b, c"));

    [Fact]
    public void SplitTags_EscapedBracketsAreNotBrackets() =>
        Assert.Equal(new[] { @"rem \(re:zero\)", "x" }, PromptText.SplitTags(@"rem \(re:zero\), x"));

    [Fact]
    public void SplitTags_DropsEmpty() =>
        Assert.Equal(new[] { "a", "b" }, PromptText.SplitTags(" a ,, ,b,\r\n"));

    [Theory]
    [InlineData("(smile:1.2)", "smile")]
    [InlineData("((masterpiece))", "masterpiece")]
    [InlineData("[blurry]", "blurry")]
    [InlineData("{{best quality}}", "best quality")]
    [InlineData("1.2::red hair::", "red hair")]
    [InlineData("-0.5::hat::", "hat")]
    [InlineData("tag: 0.8", "tag")]
    [InlineData(@"rem \(re:zero\)", @"rem \(re:zero\)")]
    [InlineData("  plain  ", "plain")]
    public void CleanTag(string input, string expected) =>
        Assert.Equal(expected, PromptText.CleanTag(input));

    [Fact]
    public void TagKeys_NormalizesAndDedupes() =>
        Assert.Equal(new[] { "hatsune miku", "smile" },
            PromptText.TagKeys("Hatsune_Miku, hatsune miku, (smile:1.1), smile"));

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void TagKeys_Empty(string? input) => Assert.Empty(PromptText.TagKeys(input));
}
