using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using DevProfile.Core;

namespace DevProfile.App.ViewModels;

public sealed class CreateViewModel : ObservableObject
{
    private readonly ProfileService _service;
    private string _destination = "";
    private string _log = "";
    private string _passphrase = "";
    private bool _isBusy;
    private string? _existingProfileSummary;
    private CancellationTokenSource? _cts;

    public CreateViewModel(ProfileService service)
    {
        _service = service;
        Rows = new ObservableCollection<ProviderRow>(
            _service.Providers.Select(p => new ProviderRow
            {
                Id = p.Id,
                DisplayName = p.DisplayName,
                Category = Categorize(p.Category),
                ContainsSecrets = p.ContainsSecrets,
                IsSelected = p.Category != ProviderCategory.Secrets, // secrets opt-in
            }));

        ExportCommand = new RelayCommand(ExportAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(Destination));
        RefreshCommand = new RelayCommand(RefreshAsync, () => !IsBusy && HasExistingProfile);
        CancelCommand = new RelayCommand(Cancel, () => IsBusy);
    }

    public ObservableCollection<ProviderRow> Rows { get; }
    public ICommand ExportCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand CancelCommand { get; }

    public string Destination
    {
        get => _destination;
        set { if (Set(ref _destination, value)) _ = CheckExistingProfileAsync(value); }
    }

    public string Log { get => _log; set => Set(ref _log, value); }
    public bool IsBusy { get => _isBusy; private set => Set(ref _isBusy, value); }

    /// <summary>Set from the PasswordBox in code-behind (PasswordBox.Password isn't bindable).</summary>
    public string Passphrase { get => _passphrase; set => _passphrase = value; }

    public bool SecretsSelected => Rows.Any(r => r.ContainsSecrets && r.IsSelected);

    /// <summary>One-liner about the profile already in Destination; null when it isn't a bundle.</summary>
    public string? ExistingProfileSummary
    {
        get => _existingProfileSummary;
        private set { if (Set(ref _existingProfileSummary, value)) Raise(nameof(HasExistingProfile)); }
    }

    public bool HasExistingProfile => _existingProfileSummary is not null;

    private Task Cancel()
    {
        try { _cts?.Cancel(); } catch (ObjectDisposedException) { /* run just finished */ }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Run discovery for every provider to fill in the "(47 apps)" detail strings.
    /// Providers are probed concurrently — npm/dotnet/code each shell out and take
    /// seconds; serially that made the Create tab feel frozen on startup.
    /// </summary>
    public async Task DiscoverAsync()
    {
        IsBusy = true;
        try
        {
            // Called on the UI thread; each continuation resumes there, so the row
            // property writes below are dispatcher-safe.
            await Task.WhenAll(Rows.Select(async row =>
            {
                var provider = _service.Find(row.Id)!;
                try
                {
                    var result = await provider.DiscoverAsync().ConfigureAwait(true);
                    row.Available = result.Available;
                    row.Detail = result.Detail ?? "";
                    if (!result.Available) row.IsSelected = false;
                }
                catch (Exception ex)
                {
                    row.Available = false;
                    row.Detail = ex.Message;
                    row.IsSelected = false;
                }
            }));
        }
        finally { IsBusy = false; }
    }

    /// <summary>Peek at Destination for an existing bundle, so Export can offer Refresh instead.</summary>
    private async Task CheckExistingProfileAsync(string destination)
    {
        string? summary = null;
        try
        {
            if (Directory.Exists(destination))
            {
                var manifest = await _service.ReadManifestAsync(destination).ConfigureAwait(true);
                if (manifest is not null)
                {
                    var stamp = manifest.UpdatedUtc ?? manifest.CreatedUtc;
                    summary = DateTime.TryParse(stamp, null, DateTimeStyles.RoundtripKind, out var utc)
                        ? $"“{manifest.Name}” — {manifest.Providers.Count} provider(s), last captured {utc.ToLocalTime():g}"
                        : $"“{manifest.Name}” — {manifest.Providers.Count} provider(s)";
                }
            }
        }
        catch { /* unreadable/unsupported manifest -> treat as not a profile */ }

        // The user may have kept typing while we read; only publish if still current.
        if (destination == Destination) ExistingProfileSummary = summary;
    }

    private async Task ExportAsync()
    {
        var selected = Rows.Where(r => r.IsSelected && r.Available).Select(r => r.Id).ToList();
        if (selected.Count == 0) { Append("Nothing selected."); return; }

        if (SecretsSelected && string.IsNullOrEmpty(Passphrase))
        {
            Append("! Secrets are selected but no passphrase was entered.");
            return;
        }

        Log = "";
        IsBusy = true;
        var cts = new CancellationTokenSource();
        _cts = cts;
        var options = new ExportOptions(SecretsSelected ? Passphrase : null);
        try
        {
            await _service.ExportAsync(Destination, selected, options, Append, cts.Token).ConfigureAwait(true);
            Append($"\nProfile written to: {Destination}");
            await CheckExistingProfileAsync(Destination).ConfigureAwait(true); // it's refreshable now
        }
        catch (OperationCanceledException)
        {
            Append("Cancelled.");
        }
        catch (Exception ex)
        {
            Append($"! Export failed: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
            _cts = null;
            cts.Dispose();
        }
    }

    /// <summary>Re-capture the bundle already in Destination, keeping its provider selection.</summary>
    private async Task RefreshAsync()
    {
        Log = "";
        IsBusy = true;
        var cts = new CancellationTokenSource();
        _cts = cts;
        try
        {
            var manifest = await _service.ReadManifestAsync(Destination, cts.Token).ConfigureAwait(true);
            if (manifest is null) { Append("! No profile.json found — nothing to refresh."); return; }

            if (manifest.Providers.Contains("secrets") && string.IsNullOrEmpty(Passphrase))
            {
                Append("! This profile contains encrypted secrets — enter the passphrase to refresh it.");
                return;
            }

            var options = new ExportOptions(manifest.Providers.Contains("secrets") ? Passphrase : null);
            await _service.RefreshAsync(Destination, options, Append, cts.Token).ConfigureAwait(true);
            await CheckExistingProfileAsync(Destination).ConfigureAwait(true); // pick up the new timestamp
        }
        catch (OperationCanceledException)
        {
            Append("Cancelled.");
        }
        catch (Exception ex)
        {
            Append($"! Refresh failed: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
            _cts = null;
            cts.Dispose();
        }
    }

    private void Append(string line) =>
        Application.Current.Dispatcher.Invoke(() => Log += line + Environment.NewLine);

    private static string Categorize(ProviderCategory c) => c switch
    {
        ProviderCategory.Packages => "Packages",
        ProviderCategory.GitAndHosts => "Git & Hosts",
        ProviderCategory.VsCode => "VS Code",
        ProviderCategory.Shell => "Shell",
        ProviderCategory.Secrets => "Secrets",
        _ => c.ToString(),
    };
}
