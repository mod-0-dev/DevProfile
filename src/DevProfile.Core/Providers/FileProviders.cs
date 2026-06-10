namespace DevProfile.Core.Providers;

public sealed class GitConfigProvider : SingleFileProvider
{
    public override string Id => "git-config";
    public override string DisplayName => ".gitconfig";
    public override ProviderCategory Category => ProviderCategory.GitAndHosts;
    protected override string LivePath => KnownPaths.GitConfig;
    protected override string StoredFileName => "gitconfig";
}

public sealed class PowerShellProfileProvider : SingleFileProvider
{
    public override string Id => "powershell-profile";
    public override string DisplayName => "PowerShell $PROFILE";
    public override ProviderCategory Category => ProviderCategory.Shell;
    protected override string LivePath => KnownPaths.PwshProfile;
    protected override string StoredFileName => "profile.ps1";
}

public sealed class WindowsTerminalProvider : SingleFileProvider
{
    public override string Id => "windows-terminal";
    public override string DisplayName => "Windows Terminal settings";
    public override ProviderCategory Category => ProviderCategory.Shell;
    protected override string LivePath => KnownPaths.WindowsTerminalSettings;
    protected override string StoredFileName => "settings.json";
}

public sealed class VsCodeSettingsProvider : SingleFileProvider
{
    public override string Id => "vscode-settings";
    public override string DisplayName => "VS Code settings.json";
    public override ProviderCategory Category => ProviderCategory.VsCode;
    protected override string LivePath => KnownPaths.VsCodeSettings;
    protected override string StoredFileName => "settings.json";
}
