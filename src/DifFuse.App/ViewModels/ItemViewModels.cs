using System.Globalization;

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

/// <summary>One stage of the signal-chain strip above the rack.</summary>
public sealed record ChainItemViewModel(string Label, string AccentKey, bool IsActive, bool IsAmp, string ToolTip);

public sealed record SaveToAmpChoice(int Slot, string Name);
