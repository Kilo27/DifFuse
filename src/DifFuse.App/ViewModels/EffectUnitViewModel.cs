using System.Collections.ObjectModel;
using DifFuse.Core.Models;

namespace DifFuse.App.ViewModels;

public enum EffectChange
{
    Knob,
    Model,
    Power,
    Placement,
}

/// <summary>One effect unit (stomp, modulation, delay or reverb) in the rack.</summary>
public sealed class EffectUnitViewModel : ObservableObject
{
    private static readonly ModelOption NoneOption = new(0, "None", false);

    private ModelOption _selectedModel = NoneOption;
    private bool _isOn;
    private bool _isPostAmp;
    private byte _slot;
    private bool _supportsV2 = true;
    private bool _loading;
    private int _chainPosition;

    public EffectUnitViewModel(EffectFamily family)
    {
        Family = family;
        Knobs = Enumerable.Range(0, KnobValues.Count).Select(i =>
        {
            var knob = new KnobViewModel($"Knob {i + 1}");
            knob.Changed += _ => RaiseChanged(EffectChange.Knob);
            return knob;
        }).ToList();
        _slot = (byte)family;
        RebuildModels(0);
        ApplyModelToKnobs();
    }

    public event Action<EffectUnitViewModel, EffectChange>? Changed;

    public EffectFamily Family { get; }

    public string Title => Family.DisplayName().ToUpperInvariant();

    public string AccentKey => Family switch
    {
        EffectFamily.Stomp => "StompBrush",
        EffectFamily.Modulation => "ModBrush",
        EffectFamily.Delay => "DelayBrush",
        _ => "ReverbBrush",
    };

    public IReadOnlyList<KnobViewModel> Knobs { get; }

    public ObservableCollection<ModelOption> Models { get; } = new();

    public ModelOption SelectedModel
    {
        get => _selectedModel;
        set
        {
            if (value is null || !SetProperty(ref _selectedModel, value)) return;
            ApplyModelToKnobs();
            OnPropertyChanged(nameof(HasModel));
            if (_loading) return;
            if (value.Id == 0)
            {
                SetOn(false);
            }
            else if (!_isOn && _wasEmpty)
            {
                SetOn(true);
            }
            _wasEmpty = value.Id == 0;
            RaiseChanged(EffectChange.Model);
        }
    }

    private bool _wasEmpty = true;

    public bool HasModel => _selectedModel.Id != 0;

    /// <summary>False when the unit is bypassed.</summary>
    public bool IsOn
    {
        get => _isOn;
        set
        {
            if (value && !HasModel) value = false;
            if (!SetProperty(ref _isOn, value)) return;
            OnPropertyChanged(nameof(IsActive));
            RaiseChanged(EffectChange.Power);
        }
    }

    public bool IsActive => _isOn && HasModel;

    /// <summary>True when the unit sits after the amp (in the FX loop).</summary>
    public bool IsPostAmp
    {
        get => _isPostAmp;
        set
        {
            if (SetProperty(ref _isPostAmp, value)) RaiseChanged(EffectChange.Placement);
        }
    }

    public byte Slot => _slot;

    /// <summary>1-based position among the active units in the signal chain, for display.</summary>
    public int ChainPosition
    {
        get => _chainPosition;
        set => SetProperty(ref _chainPosition, value);
    }

    public bool SupportsV2
    {
        get => _supportsV2;
        set
        {
            if (SetProperty(ref _supportsV2, value)) RebuildModels(_selectedModel.Id);
        }
    }

    private void SetOn(bool on)
    {
        if (SetProperty(ref _isOn, on, nameof(IsOn))) OnPropertyChanged(nameof(IsActive));
    }

    private void RaiseChanged(EffectChange change)
    {
        if (!_loading) Changed?.Invoke(this, change);
    }

    private void RebuildModels(int selectId)
    {
        _loading = true;
        try
        {
            Models.Clear();
            Models.Add(NoneOption);
            foreach (var m in EffectCatalog.ForFamily(Family, includeV2: true).Where(m => _supportsV2 || !m.IsV2 || m.Id == selectId))
            {
                Models.Add(new ModelOption(m.Id, m.Name, m.IsV2));
            }
            var selected = Models.FirstOrDefault(m => m.Id == selectId);
            if (selected is null)
            {
                selected = new ModelOption(selectId, EffectCatalog.NameOf((ushort)selectId), false);
                Models.Add(selected);
            }
            _selectedModel = NoneOption;
            SelectedModel = selected;
        }
        finally
        {
            _loading = false;
        }
    }

    private void ApplyModelToKnobs()
    {
        var model = EffectCatalog.Find((ushort)_selectedModel.Id);
        for (int i = 0; i < Knobs.Count; i++)
        {
            var knob = Knobs[i];
            if (model is not null && i < model.Knobs.Count)
            {
                var def = model.Knobs[i];
                knob.Redefine(def.Label, def.Max, def.Choices);
                knob.IsVisible = true;
                knob.IsEnabled = true;
            }
            else
            {
                knob.Redefine(model is null ? "—" : "", 255, null);
                knob.IsVisible = model is null && _selectedModel.Id != 0;
                knob.IsEnabled = false;
            }
        }
    }

    public void Load(EffectSettings effect)
    {
        _loading = true;
        try
        {
            if (Models.All(m => m.Id != effect.ModelId) || (!_supportsV2 && effect.Model?.IsV2 == true))
            {
                RebuildModels(effect.ModelId);
            }
            SelectedModel = Models.First(m => m.Id == effect.ModelId);
            _wasEmpty = effect.ModelId == 0;
            for (int i = 0; i < Knobs.Count; i++) Knobs[i].Load(effect.Knobs[i]);
            SetOn(effect.Enabled && effect.ModelId != 0);
            _slot = effect.Slot;
            SetProperty(ref _isPostAmp, effect.IsPostAmp, nameof(IsPostAmp));
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>Sets the signal-chain position chosen for this unit, without raising <see cref="Changed"/>.</summary>
    public void SetSlot(byte slot)
    {
        _slot = slot;
        SetProperty(ref _isPostAmp, slot >= 4, nameof(IsPostAmp));
    }

    public EffectSettings ToSettings() => new()
    {
        Family = Family,
        ModelId = (ushort)_selectedModel.Id,
        Knobs = new KnobValues(Knobs[0].ByteValue, Knobs[1].ByteValue, Knobs[2].ByteValue,
            Knobs[3].ByteValue, Knobs[4].ByteValue, Knobs[5].ByteValue),
        Slot = _slot,
        Enabled = _isOn && _selectedModel.Id != 0,
    };
}
