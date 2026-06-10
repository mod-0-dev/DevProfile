using DevProfile.Core.Providers;
using Xunit;

namespace DevProfile.Core.Tests;

public class WingetReadIdsTests
{
    [Fact]
    public void ReadIds_ParsesPackageIdentifiers()
    {
        const string json = """
        {
          "Sources": [
            {
              "Packages": [
                { "PackageIdentifier": "Git.Git" },
                { "PackageIdentifier": "Microsoft.VisualStudioCode" }
              ],
              "SourceDetails": { "Name": "winget" }
            }
          ]
        }
        """;
        var ids = WingetProvider.ReadIds(json);
        Assert.Equal(2, ids.Count);
        Assert.Contains("Git.Git", ids);
        Assert.Contains("microsoft.visualstudiocode", ids); // case-insensitive set
    }

    [Fact]
    public void ReadIds_NoSources_ReturnsEmpty() =>
        Assert.Empty(WingetProvider.ReadIds("""{ "WinGetVersion": "1.7" }"""));
}

public class DotnetToolParseTests
{
    [Fact]
    public void ParseToolList_SkipsHeaderAndSeparator()
    {
        const string stdout = """
        Package Id      Version      Commands
        -------------------------------------
        dotnet-ef       9.0.0        dotnet-ef
        cake.tool       4.0.0        dotnet-cake
        """;
        var ids = DotnetToolProvider.ParseToolList(stdout);
        Assert.Equal(new[] { "dotnet-ef", "cake.tool" }, ids);
    }

    [Fact]
    public void ParseToolList_EmptyTable_ReturnsEmpty()
    {
        const string stdout = """
        Package Id      Version      Commands
        -------------------------------------
        """;
        Assert.Empty(DotnetToolProvider.ParseToolList(stdout));
    }
}

public class NpmParseTests
{
    [Fact]
    public void ParseList_ReadsDependencyNames_ExcludingNpmItself()
    {
        const string json = """
        {
          "name": "global",
          "dependencies": {
            "npm": { "version": "10.0.0" },
            "typescript": { "version": "5.6.0" },
            "@angular/cli": { "version": "18.0.0" }
          }
        }
        """;
        var list = NpmGlobalProvider.ParseList(json);
        Assert.NotNull(list);
        Assert.Equal(2, list!.Count);
        Assert.Contains("typescript", list);
        Assert.Contains("@angular/cli", list);
    }

    [Fact]
    public void ParseList_NoDependencies_ReturnsEmpty() =>
        Assert.Empty(NpmGlobalProvider.ParseList("{}")!);

    [Fact]
    public void ParseList_InvalidJson_ReturnsNull() =>
        Assert.Null(NpmGlobalProvider.ParseList("npm ERR! something broke"));
}

public class VsCodeParseTests
{
    [Fact]
    public void ParseExtensions_FiltersNonExtensionNoise()
    {
        const string stdout = """
        Update available: please restart to apply
        ms-dotnettools.csharp
        esbenp.prettier-vscode
        [warn] GPU process crashed
        GitHub.copilot
        """;
        var list = VsCodeExtensionsProvider.ParseExtensions(stdout);
        Assert.Equal(new[] { "ms-dotnettools.csharp", "esbenp.prettier-vscode", "GitHub.copilot" }, list);
    }
}
