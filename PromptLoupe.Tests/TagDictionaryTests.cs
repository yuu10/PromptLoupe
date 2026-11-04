using System.Text;

namespace PromptLoupe.Tests;

public class TagDictionaryTests
{
    [Fact]
    public void Load_TagsAndAliases()
    {
        var csv = "1girl,0,5000000,\"\"\n" +
                  "hatsune_miku,4,100000,\"miku,初音ミク\"\n" +
                  "vocaloid,3,90000\n" +
                  "broken line\n" +
                  "badcat,9,1\n";
        var path = TestImages.Save(Encoding.UTF8.GetBytes(csv), ".csv");

        var (dict, error) = TagDictionary.Load(path);
        Assert.Null(error);
        Assert.NotNull(dict);
        Assert.Equal(0, dict!.Lookup("1girl"));
        Assert.Equal(4, dict.Lookup("hatsune miku"));   // _ はスペースに正規化して引く
        Assert.Equal(4, dict.Lookup("miku"));
        Assert.Equal(4, dict.Lookup("初音ミク"));
        Assert.Equal(3, dict.Lookup("vocaloid"));
        Assert.Equal(-1, dict.Lookup("badcat"));         // 分類が 0〜5 でない行は読まない
        Assert.Equal(-1, dict.Lookup("unknown"));
    }

    [Fact]
    public void Load_WrongFormat_ReturnsError()
    {
        var path = TestImages.Save(Encoding.UTF8.GetBytes("hello\nworld\n"), ".csv");
        var (dict, error) = TagDictionary.Load(path);
        Assert.Null(dict);
        Assert.NotNull(error);
    }

    [Fact]
    public void Load_MissingFile_ReturnsError()
    {
        var (dict, error) = TagDictionary.Load(Path.Combine(Path.GetTempPath(), "no-such-" + Guid.NewGuid() + ".csv"));
        Assert.Null(dict);
        Assert.NotNull(error);
    }
}
