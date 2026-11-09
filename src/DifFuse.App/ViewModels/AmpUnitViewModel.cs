using System.Collections.ObjectModel;
using System.Globalization;
using DifFuse.Core.Models;

namespace DifFuse.App.ViewModels;

public sealed record ModelOption(int Id, string Name, bool IsV2)
{
    public string DisplayName => IsV2 ? $"{Name}  (v2)" : Name;

    public override string ToString() => DisplayName;
}

/// <summary>
/// The amp unit, grouped the way an amp is built: preamp, tone stack, power amp, noise gate, cabinet,
/// plus the USB output level, which is not part of the tone.
/// </summary>
public sealed class AmpUnitViewModel : ObservableObject
{
    private ModelOption? _selectedModel;
    private int _cabinet;
    private int _noiseGate;
    private bool _brightness;
    private bool _supportsV2 = true;
    private bool _loading;

    public AmpUnitViewModel()
    {
        Gain = Knob("Gain");
        Gain2 = Knob("Gain 2");
        Volume = Knob("Volume");
        Master = Knob("Master");
        Treble = Knob("Treble");
        Middle = Knob("Middle");
        Bass = Knob("Bass");
        Presence = Knob("Presence");
        Bias = Knob("Bias");
        Sag = Knob("Sag", 2, SagLevels.Names);
        Threshold = Knob("Threshold", 9, format: v => v.ToString(CultureInfo.CurrentCulture));
        Depth = Knob("Depth");
        UsbGain = Knob("USB Gain");

        PreampKnobs = [Gain, Gain2, Volume];
        EqKnobs = [Bass, Middle, Treble, Presence];
        PowerKnobs = [Master, Bias, Sag];
        RebuildModels(ModelIdOrDefault);
        Load(new AmpSettings());
    }

    /// <summary>Raised when the user changes anything on the unit.</summary>
    public event Action? Changed;

    public KnobViewModel Gain { get; }
    public KnobViewModel Gain2 { get; }
    public KnobViewModel Volume { get; }
    public KnobViewModel Master { get; }
    public KnobViewModel Treble { get; }
    public KnobViewModel Middle { get; }
    public KnobViewModel Bass { get; }
    public KnobViewModel Presence { get; }
    public KnobViewModel Bias { get; }
    public KnobViewModel Sag { get; }
    public KnobViewModel Threshold { get; }
    public KnobViewModel Depth { get; }
    public KnobViewModel UsbGain { get; }

    /// <summary>Gain stages and channel volume.</summary>
    public IReadOnlyList<KnobViewModel> PreampKnobs { get; }

    /// <summary>Tone stack, low to high.</summary>
    public IReadOnlyList<KnobViewModel> EqKnobs { get; }

    /// <summary>Master volume and how the power amp behaves.</summary>
    public IReadOnlyList<KnobViewModel> PowerKnobs { get; }

    /// <summary>True when the noise gate is Custom, the only setting that uses Threshold and Depth.</summary>
    public bool IsCustomGate => _noiseGate == NoiseGates.Custom;

    public ObservableCollection<ModelOption> Models { get; } = new();

    public IReadOnlyList<string> CabinetNames => Cabinets.Names;

    public IReadOnlyList<string> NoiseGateNames => NoiseGates.Names;

    public ModelOption? SelectedModel
    {
        get => _selectedModel;
        set
        {
            if (value is null || !SetProperty(ref _selectedModel, value)) return;
            if (_loading) return;
            // Like FUSE: a new model brings its own cabinet and noise gate.
            var updated = ToSettings(modelOverride: null).WithModel((byte)value.Id);
            _loading = true;
            try
            {
                Cabinet = updated.Cabinet;
                NoiseGate = updated.NoiseGate;
            }
            finally
            {
                _loading = false;
            }
            RaiseChanged();
        }
    }

    public int Cabinet
    {
        get => _cabinet;
        set
        {
            if (SetProperty(ref _cabinet, value)) RaiseChanged();
        }
    }

    public int NoiseGate
    {
        get => _noiseGate;
        set
        {
            if (!SetProperty(ref _noiseGate, value)) return;
            OnPropertyChanged(nameof(IsCustomGate));
            UpdateGateKnobs();
            RaiseChanged();
        }
    }

    public bool Brightness
    {
        get => _brightness;
        set
        {
            if (SetProperty(ref _brightness, value)) RaiseChanged();
        }
    }

    /// <summary>Whether the connected amp runs the v2 models. Offline, every model is offered.</summary>
    public bool SupportsV2
    {
        get => _supportsV2;
        set
        {
            if (!SetProperty(ref _supportsV2, value)) return;
            RebuildModels(ModelIdOrDefault);
        }
    }

    private int ModelIdOrDefault => _selectedModel?.Id ?? 0x53;

    private KnobViewModel Knob(string label, double max = 255, IReadOnlyList<string>? choices = null, Func<int, string>? format = null)
    {
        var knob = new KnobViewModel(label, max, max >= 255 ? 128 : 0, choices, format, ControlGuide.ForAmp(label));
        knob.Changed += _ => RaiseChanged();
        return knob;
    }

    private void RaiseChanged()
    {
        if (!_loading) Changed?.Invoke();
    }

    private void UpdateGateKnobs()
    {
        bool custom = _noiseGate == NoiseGates.Custom;
        Threshold.IsEnabled = custom;
        Depth.IsEnabled = custom;
    }

    private void RebuildModels(int selectId)
    {
        _loading = true;
        try
        {
            Models.Clear();
            foreach (var m in AmpCatalog.All.Where(m => _supportsV2 || !m.IsV2 || m.Id == selectId))
            {
                Models.Add(new ModelOption(m.Id, m.Name, m.IsV2));
            }
            var selected = Models.FirstOrDefault(m => m.Id == selectId);
            if (selected is null)
            {
                selected = new ModelOption(selectId, AmpCatalog.NameOf((byte)selectId), false);
                Models.Add(selected);
            }
            _selectedModel = null;
            SelectedModel = selected;
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>Shows settings from the amp, a file or the library without sending anything.</summary>
    public void Load(AmpSettings amp)
    {
        _loading = true;
        try
        {
            if (Models.All(m => m.Id != amp.ModelId) || (!_supportsV2 && AmpCatalog.Find(amp.ModelId)?.IsV2 == true))
            {
                RebuildModels(amp.ModelId);
            }
            SetProperty(ref _selectedModel, Models.First(m => m.Id == amp.ModelId), nameof(SelectedModel));
            Gain.Load(amp.Gain);
            Gain2.Load(amp.Gain2);
            Volume.Load(amp.Volume);
            Master.Load(amp.MasterVolume);
            Treble.Load(amp.Treble);
            Middle.Load(amp.Middle);
            Bass.Load(amp.Bass);
            Presence.Load(amp.Presence);
            Bias.Load(amp.Bias);
            Sag.Load(amp.Sag);
            Threshold.Load(amp.Threshold);
            Depth.Load(amp.Depth);
            UsbGain.Load(amp.UsbGain);
            Cabinet = amp.Cabinet;
            NoiseGate = amp.NoiseGate;
            Brightness = amp.Brightness;
            UpdateGateKnobs();
        }
        finally
        {
            _loading = false;
        }
    }

    public AmpSettings ToSettings() => ToSettings(null);

    private AmpSettings ToSettings(byte? modelOverride) => new()
    {
        ModelId = modelOverride ?? (byte)ModelIdOrDefault,
        Gain = Gain.ByteValue,
        Gain2 = Gain2.ByteValue,
        Volume = Volume.ByteValue,
        MasterVolume = Master.ByteValue,
        Treble = Treble.ByteValue,
        Middle = Middle.ByteValue,
        Bass = Bass.ByteValue,
        Presence = Presence.ByteValue,
        Bias = Bias.ByteValue,
        Sag = Sag.ByteValue,
        Threshold = Threshold.ByteValue,
        Depth = Depth.ByteValue,
        UsbGain = UsbGain.ByteValue,
        Cabinet = (byte)Math.Clamp(_cabinet, 0, 255),
        NoiseGate = (byte)Math.Clamp(_noiseGate, 0, NoiseGates.Custom),
        Brightness = _brightness,
    };
}
