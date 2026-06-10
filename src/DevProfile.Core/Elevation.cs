using System.Security.Principal;

namespace DevProfile.Core;

public static class Elevation
{
    /// <summary>True if the current process is running with Administrator rights.</summary>
    public static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
