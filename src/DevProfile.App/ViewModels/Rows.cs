using DevProfile.Core;

namespace DevProfile.App.ViewModels;

/// <summary>A selectable provider on the Create screen.</summary>
public sealed class ProviderRow : ObservableObject
{
    private bool _isSelected = true;
    private bool _available = true;
    private string _detail = "…";

    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string Category { get; init; }
    public bool ContainsSecrets { get; init; }

    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
    public bool Available { get => _available; set { if (Set(ref _available, value)) Raise(nameof(Enabled)); } }
    public string Detail { get => _detail; set => Set(ref _detail, value); }

    /// <summary>Disabled (and unchecked) when nothing was discovered to capture.</summary>
    public bool Enabled => _available;
}

/// <summary>A row in the Apply preview.</summary>
public sealed class PlanRow : ObservableObject
{
    private bool _include;

    public required PlanItem Item { get; init; }
    public bool Include { get => _include; set => Set(ref _include, value); }

    public string ProviderId => Item.ProviderId;
    public string Label => Item.Label;
    public string Status => Item.Status;
    public string Action => Item.Action.ToString();
    public string? Detail => Item.Detail;
}
