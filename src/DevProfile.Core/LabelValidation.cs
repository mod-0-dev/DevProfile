using System.Text.RegularExpressions;

namespace DevProfile.Core;

/// <summary>
/// Strict shape checks for identifiers read out of a profile bundle before they are
/// passed to a CLI. A profile folder is untrusted input (it travels via OneDrive/USB),
/// so a tampered packages.txt must not be able to smuggle shell operators or extra
/// arguments into winget/npm/code invocations.
/// </summary>
public static partial class LabelValidation
{
    // winget package id: dot-separated segments, e.g. "Microsoft.VisualStudioCode",
    // store ids like "9NBLGGH4NNS1", plus the occasional '+' ("Notepad++").
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._+-]*$")]
    private static partial Regex WingetId();

    // npm package, optionally scoped and/or versioned: "@scope/name@1.2.3".
    [GeneratedRegex(@"^(@[a-z0-9][a-z0-9._-]*/)?[a-z0-9][a-z0-9._-]*(@[A-Za-z0-9.^~=<>*-]+)?$")]
    private static partial Regex NpmPackage();

    // VS Code extension id: "publisher.name".
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9-]*\.[A-Za-z0-9][A-Za-z0-9._-]*$")]
    private static partial Regex VsCodeExtension();

    // .NET tool package id: NuGet id rules.
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]*$")]
    private static partial Regex DotnetToolId();

    public static bool IsWingetId(string s) => WingetId().IsMatch(s);
    public static bool IsNpmPackage(string s) => NpmPackage().IsMatch(s);
    public static bool IsVsCodeExtension(string s) => VsCodeExtension().IsMatch(s);
    public static bool IsDotnetToolId(string s) => DotnetToolId().IsMatch(s);
}
