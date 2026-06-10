using System.Collections;

namespace DevProfile.Core;

/// <summary>
/// Re-reads machine + user environment (including PATH) from the registry into the
/// current process, so tools installed earlier in the same Apply run become callable
/// without relaunching. Mirrors the Update-SessionEnvironment trick from dev_setup.ps1.
/// New child processes (winget/code/npm) inherit the refreshed environment.
/// </summary>
public static class EnvironmentRefresher
{
    public static void Refresh()
    {
        foreach (var scope in new[] { EnvironmentVariableTarget.Machine, EnvironmentVariableTarget.User })
        {
            foreach (DictionaryEntry e in Environment.GetEnvironmentVariables(scope))
            {
                var key = (string)e.Key;
                if (string.Equals(key, "Path", StringComparison.OrdinalIgnoreCase)) continue;
                Environment.SetEnvironmentVariable(key, e.Value?.ToString(), EnvironmentVariableTarget.Process);
            }
        }

        // PATH is the union of machine + user, expanded (registry values may hold %VARS%).
        var machine = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine) ?? "";
        var user = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User) ?? "";
        var combined = string.Join(';', new[] { machine, user }.Where(s => s.Length > 0));
        Environment.SetEnvironmentVariable(
            "Path", Environment.ExpandEnvironmentVariables(combined), EnvironmentVariableTarget.Process);
    }
}
