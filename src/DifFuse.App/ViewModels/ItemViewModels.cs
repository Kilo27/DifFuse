using System.Globalization;
using DifFuse.Core.Models;

namespace DifFuse.App.ViewModels;

/// <summary>One preset slot on the amp.</summary>
public sealed class PresetSlotViewModel(int index, string name) : ObservableObject
{
    private string _name = name;
    private bool _isCurrent;

    public int Index { get; } = index;

    public string Number => Index.ToString("00", CultureInfo.InvariantCulture);

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value)) OnPropertyChanged(nameof(Label));
        }
    }

    public string Label => $"{Number}  {Name}";

    public bool IsCurrent
    {
        get => _isCurrent;
        set => SetProperty(ref _isCurrent, value);
    }
}

public enum ChainItemState
{
    On,
    Bypassed,
    Empty,
}

/// <summary>One station on the signal route above the units.</summary>
/// <param name="Label">Short name of the unit (STOMP, MOD, AMP, ...).</param>
/// <param name="Detail">The model the unit runs, or why it is silent.</param>
/// <param name="Family">The effect family, or null for the amp.</param>
public sealed record ChainItemViewModel(string Label, string AccentKey, bool IsActive, bool IsAmp, string ToolTip,
    string Detail = "", ChainItemState State = ChainItemState.On, EffectFamily? Family = null)
{
    public bool IsBypassed => State == ChainItemState.Bypassed;

    public bool IsEmpty => State == ChainItemState.Empty;
}

public sealed record SaveToAmpChoice(int Slot, string Name);
