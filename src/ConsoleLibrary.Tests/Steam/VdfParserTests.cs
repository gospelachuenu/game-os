using ConsoleLibrary.Steam;

namespace ConsoleLibrary.Tests.Steam;

public class VdfParserTests
{
    private const string SampleAcf = """
        "AppState"
        {
            "appid"		"730"
            "Universe"		"1"
            "name"		"Counter-Strike 2"
            "StateFlags"		"4"
            "installdir"		"Counter-Strike Global Offensive"
            "LastUpdated"		"1700000000"
            "SizeOnDisk"		"41943040000"
            "UserConfig"
            {
                "language"		"english"
            }
        }
        """;

    [Fact]
    public void Parse_ReadsTopLevelKeyAndNestedValues()
    {
        var root = VdfParser.Parse(SampleAcf);
        var appState = root["AppState"];

        Assert.NotNull(appState);
        Assert.Equal("730", appState!.GetString("appid"));
        Assert.Equal("Counter-Strike 2", appState.GetString("name"));
        Assert.Equal("Counter-Strike Global Offensive", appState.GetString("installdir"));
    }

    [Fact]
    public void Parse_ReadsDeeplyNestedBlocks()
    {
        var root = VdfParser.Parse(SampleAcf);
        var userConfig = root["AppState"]?["UserConfig"];

        Assert.NotNull(userConfig);
        Assert.Equal("english", userConfig!.GetString("language"));
    }

    [Fact]
    public void Parse_HandlesEscapedQuotesInsideValues()
    {
        const string vdf = """
            "AppState"
            {
                "name"		"Some \"Quoted\" Title"
            }
            """;

        var root = VdfParser.Parse(vdf);
        Assert.Equal("Some \"Quoted\" Title", root["AppState"]!.GetString("name"));
    }

    [Fact]
    public void Parse_IsCaseInsensitiveForKeys()
    {
        var root = VdfParser.Parse(SampleAcf);
        Assert.Equal("730", root["appstate"]!.GetString("APPID"));
    }

    [Fact]
    public void Parse_SkipsLineComments()
    {
        const string vdf = """
            // top of file comment
            "AppState"
            {
                // inline comment
                "appid"		"440"
            }
            """;

        var root = VdfParser.Parse(vdf);
        Assert.Equal("440", root["AppState"]!.GetString("appid"));
    }
}
