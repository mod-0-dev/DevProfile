using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using DevProfile.Core;

namespace DevProfile.App.ViewModels;

public sealed class ApplyViewModel : ObservableObject
{
    private readonly ProfileService _service;
    private string _source = "";
    private string _log = "";
    private string _passphrase = "";
    private string _summary = "Load a profile to see the plan.";
    private bool _isBusy;
    private CancellationTokenSource? _cts;

    public ApplyViewModel(ProfileService service)
    {
        _service = service;
        IsElevated = Elevation.IsElevated();
        LoadCommand = new RelayCommand(LoadAsync, () => !IsBusy && Directory.Exists(Source));
        ApplyCommand = new RelayCommand(ApplyAsync, () => !IsBusy && Plan.Any(p => p.Include));
        CancelCommand = new RelayCommand(Cancel, () => IsBusy);
    }

    public ObservableCollection<PlanRow> Plan { get; } = new();
    public ICommand LoadCommand { get; }
    public ICommand ApplyCommand { get; }
    public ICommand CancelCommand { get; }

    public string Source { get => _source; set => Set(ref _source, value); }
    public string Log { get => _log; set => Set(ref _log, value); }
    public string Summary { get => _summary; set => Set(ref _summary, value); }
    public string Passphrase { get => _passphrase; set => _passphrase = value; }
    public bool IsBusy { get => _isBusy; private set => Set(ref _isBusy, value); }

    /// <summary>Whether this process is elevated; drives the "needs admin" banner.</summary>
    public bool IsElevated { get; }
    public bool NotElevated => !IsElevated;

    private Task Cancel()
    {
        try { _cts?.Cancel(); } catch (ObjectDisposedException) { /* run just finished */ }
        return Task.CompletedTask;
    }

    private async Task LoadAsync()
    {
        Plan.Clear();
        Log = "";
        Summary = "Building plan… (winget diff can take a moment)";
        IsBusy = true;
        var cts = new CancellationTokenSource();
        _cts = cts;
        try
        {
            var items = await _service.BuildPlanAsync(Source, cts.Token).ConfigureAwait(true);
            foreach (var item in items)
                Plan.Add(new PlanRow
                {
                    Item = item,
                    Include = item.Action is PlanAction.Install or PlanAction.Overwrite or PlanAction.Merge,
                });

            int install = items.Count(i => i.Action == PlanAction.Install);
            int update = items.Count(i => i.Action is PlanAction.Overwrite or PlanAction.Merge);
            int skip = items.Count(i => i.Action == PlanAction.Skip);
            int manual = items.Count(i => i.Action == PlanAction.Manual);
            Summary = $"{install} to install · {update} to update · {skip} already current"
                      + (manual > 0 ? $" · {manual} manual" : "");
        }
        catch (OperationCanceledException)
        {
            Summary = "Load cancelled.";
        }
        catch (Exception ex)
        {
            Summary = $"Failed to read profile: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            _cts = null;
            cts.Dispose();
        }
    }

    private async Task ApplyAsync()
    {
        var items = Plan.Where(p => p.Include).Select(p => p.Item).ToList();
        if (items.Count == 0) return;
        Log = "";
        IsBusy = true;
        var cts = new CancellationTokenSource();
        _cts = cts;

        var needsPass = items.Any(i => i.ProviderId == "secrets");
        var options = new ApplyOptions(needsPass ? Passphrase : null);
        try
        {
            var result = await _service.ApplyAsync(Source, items, options, Append, cts.Token).ConfigureAwait(true);
            Summary = result.Ok
                ? $"Done — {result.Applied} item(s) applied."
                : $"Done — {result.Applied} applied · {result.Failed} failed · {result.SkippedByPreflight} skipped (see log)";
        }
        catch (OperationCanceledException)
        {
            Append("Cancelled.");
        }
        catch (Exception ex)
        {
            Append($"! Apply failed: {ex.Message}");
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
}
