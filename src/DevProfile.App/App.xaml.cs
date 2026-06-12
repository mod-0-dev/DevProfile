using System.Windows;
using DevProfile.Core;

namespace DevProfile.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Relaunched elevated mid-apply to write the hosts file: do that and exit,
        // never showing a window.
        if (HostsElevation.TryHandle(e.Args, out var exitCode))
        {
            Shutdown(exitCode);
            return;
        }

        base.OnStartup(e);
        new MainWindow().Show();
    }
}
