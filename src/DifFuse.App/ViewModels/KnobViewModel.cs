using System.Globalization;

namespace DifFuse.App.ViewModels;

/// <summary>
/// One control on a unit. Values are 0-<see cref="Maximum"/>, stored as whole numbers. <see cref="Kind"/> says
/// which control fits what the parameter does: a knob, a stepped knob, stops along a line, or a switch.
/// </summary>
public sealed class KnobViewModel : ObservableObject
{
    private string _label;
    private double _maximum;
    private double _value;
    private bool _isEnabled = true;
    private bool _isVisible = true;
    private IReadOnlyList<string>? _choices;
    private Func<int, string>? _format;
    private KnobKind _kind;
    private string _help;
    private bool _silent;

    public KnobViewModel(string label, double maximum = 255, double value = 128, IReadOnlyList<string>? choices = null,
        Func<int, string>? format = null, string help = "")
    {
        _label = label;
        _maximum = maximum;
        _value = Math.Clamp(value, 0, maximum);
        _choices = choices;
        _format = format;
        _help = help;
        _kind = ControlGuide.KindFor(label, maximum, choices);
        DefaultValue = maximum >= 255 ? 128 : 0;
    }

    /// <summary>Raised when the user (not a load) changes the value.</summary>
    public event Action<KnobViewModel>? Changed;

    public string Label
    {
        get => _label;
        set => SetProperty(ref _label, value);
    }

    public double Maximum
    {
        get => _maximum;
        private set
        {
            if (SetProperty(ref _maximum, value)) OnPropertyChanged(nameof(IsSelector));
        }
    }

    public bool IsSelector => Maximum < 255;

    public KnobKind Kind
    {
        get => _kind;
        private set => SetProperty(ref _kind, value);
    }

    /// <summary>One plain line on what the control does.</summary>
    public string Help
    {
        get => _help;
        private set => SetProperty(ref _help, value);
    }

    /// <summary>Names of the positions, for stops and stepped knobs.</summary>
    public IReadOnlyList<string> PositionNames =>
        _choices ?? Enumerable.Range(0, (int)Maximum + 1).Select(Format).ToList();

    public double DefaultValue { get; private set; }

    public double Value
    {
        get => _value;
        set
        {
            value = Math.Round(Math.Clamp(value, 0, Maximum));
            if (!SetProperty(ref _value, value)) return;
            OnPropertyChanged(nameof(DisplayValue));
            OnPropertyChanged(nameof(IsOn));
            if (!_silent) Changed?.Invoke(this);
        }
    }

    /// <summary>For switches: true when the value is 1.</summary>
    public bool IsOn
    {
        get => _value > 0;
        set => Value = value ? 1 : 0;
    }

    public byte ByteValue => (byte)Math.Clamp((int)Math.Round(_value), 0, 255);

    public string DisplayValue => Format((int)Math.Round(_value));

    private string Format(int v)
    {
        if (_format is not null) return _format(v);
        if (_choices is not null && v < _choices.Count) return _choices[v];
        if (IsSelector) return (v + 1).ToString(CultureInfo.CurrentCulture);
        if (_kind == KnobKind.Percent) return Math.Round(v / 2.55).ToString(CultureInfo.CurrentCulture) + "%";
        return (v / 25.5).ToString("0.0", CultureInfo.CurrentCulture);
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }

    /// <summary>Sets the value without raising <see cref="Changed"/> (used when loading a preset).</summary>
    public void Load(double value)
    {
        _silent = true;
        try
        {
            Value = value;
        }
        finally
        {
            _silent = false;
        }
    }

    /// <summary>Changes what the knob controls, e.g. when an effect model changes.</summary>
    public void Redefine(string label, double maximum, IReadOnlyList<string>? choices, Func<int, string>? format = null, string help = "")
    {
        Label = label;
        _choices = choices;
        _format = format;
        Maximum = maximum;
        Kind = ControlGuide.KindFor(label, maximum, choices);
        Help = help;
        DefaultValue = maximum >= 255 ? 128 : 0;
        if (_value > maximum) Load(maximum);
        OnPropertyChanged(nameof(PositionNames));
        OnPropertyChanged(nameof(DisplayValue));
        OnPropertyChanged(nameof(IsOn));
    }
}
