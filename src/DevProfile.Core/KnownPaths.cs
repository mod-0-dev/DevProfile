namespace DevProfile.Core;

/// <summary>Well-known config file locations on a Windows dev machine.</summary>
public static class KnownPaths
{
    public static string UserProfile => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public static string AppData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    public static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    /// <summary>Documents — note this honours OneDrive Known Folder redirection.</summary>
    public static string Documents => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    public static string GitConfig => Path.Combine(UserProfile, ".gitconfig");

    /// <summary>PowerShell 7 current-user profile (all hosts).</summary>
    public static string PwshProfile => Path.Combine(Documents, "PowerShell", "profile.ps1");

    /// <summary>
    /// Windows Terminal settings.json — checked in order: packaged stable (Store/winget),
    /// packaged preview, unpackaged (scoop/chocolatey/portable). Falls back to the
    /// stable packaged path when none exist yet.
    /// </summary>
    public static string WindowsTerminalSettings =>
        WindowsTerminalCandidates.FirstOrDefault(File.Exists) ?? WindowsTerminalCandidates[0];

    private static string[] WindowsTerminalCandidates =>
    [
        Path.Combine(LocalAppData, "Packages", "Microsoft.WindowsTerminal_8wekyb3d8bbwe", "LocalState", "settings.json"),
        Path.Combine(LocalAppData, "Packages", "Microsoft.WindowsTerminalPreview_8wekyb3d8bbwe", "LocalState", "settings.json"),
        Path.Combine(LocalAppData, "Microsoft", "Windows Terminal", "settings.json"),
    ];

    public static string VsCodeSettings => Path.Combine(AppData, "Code", "User", "settings.json");

    public static string Hosts => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts");

    public static string SshDir => Path.Combine(UserProfile, ".ssh");
    public static string NpmRc => Path.Combine(UserProfile, ".npmrc");
}
