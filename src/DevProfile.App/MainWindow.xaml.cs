using System.Diagnostics;
using System.Windows;
using DevProfile.App.ViewModels;
using Microsoft.Win32;

namespace DevProfile.App;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        Loaded += async (_, _) =>
        {
            HandleStartupArgs();
            await _vm.Create.DiscoverAsync();
        };
    }

    /// <summary>After a "Restart as admin" relaunch we get "--apply &lt;folder&gt;" — jump straight to Apply.</summary>
    private void HandleStartupArgs()
    {
        var args = Environment.GetCommandLineArgs();
        var idx = Array.IndexOf(args, "--apply");
        if (idx < 0 || idx + 1 >= args.Length) return;

        _vm.Apply.Source = args[idx + 1];
        Tabs.SelectedIndex = 1;
        if (_vm.Apply.LoadCommand.CanExecute(null))
            _vm.Apply.LoadCommand.Execute(null);
    }

    private void BrowseExport_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Choose where to export the profile" };
        if (dlg.ShowDialog(this) == true)
            _vm.Create.Destination = dlg.FolderName;
    }

    private void BrowseApply_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Choose a profile folder to apply" };
        if (dlg.ShowDialog(this) == true)
            _vm.Apply.Source = dlg.FolderName;
    }

    private void RestartAsAdmin_Click(object sender, RoutedEventArgs e)
    {
        var exe = Environment.ProcessPath;
        if (exe is null) return;

        var psi = new ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas" };
        if (!string.IsNullOrWhiteSpace(_vm.Apply.Source))
        {
            psi.ArgumentList.Add("--apply");
            psi.ArgumentList.Add(_vm.Apply.Source);
        }

        try
        {
            Process.Start(psi);
            Application.Current.Shutdown();
        }
        catch (Exception)
        {
            // User dismissed the UAC prompt — stay in the current (non-elevated) process.
        }
    }

    // PasswordBox.Password is not bindable, so push it into the VM on change.
    private void CreatePassphrase_Changed(object sender, RoutedEventArgs e) =>
        _vm.Create.Passphrase = CreatePassphrase.Password;

    private void ApplyPassphrase_Changed(object sender, RoutedEventArgs e) =>
        _vm.Apply.Passphrase = ApplyPassphrase.Password;
}
