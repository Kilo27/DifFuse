namespace MustangRack.Core.Models;

/// <summary>All amp-unit settings of a preset. Continuous controls are 0-255.</summary>
public sealed record AmpSettings
{
    public byte ModelId { get; init; } = 0x53;
    public byte Volume { get; init; } = 128;
    public byte Gain { get; init; } = 128;
    public byte Gain2 { get; init; } = 128;
    public byte MasterVolume { get; init; } = 128;
    public byte Treble { get; init; } = 128;
    public byte Middle { get; init; } = 128;
    public byte Bass { get; init; } = 128;
    public byte Presence { get; init; } = 128;
    public byte Depth { get; init; } = 128;
    public byte Bias { get; init; } = 128;
    /// <summary>0-5: Off, Low, Mid, High, Super, Custom.</summary>
    public byte NoiseGate { get; init; }
    /// <summary>0-9, used when the noise gate is Custom.</summary>
    public byte Threshold { get; init; }
    public byte Cabinet { get; init; } = Cabinets.Deluxe65;
    /// <summary>0-2.</summary>
    public byte Sag { get; init; } = 1;
    public bool Brightness { get; init; }
    public byte UsbGain { get; init; } = 128;

    public AmpModel? Model => AmpCatalog.Find(ModelId);

    /// <summary>Same model change rules as FUSE and PLUG: picks the model's cabinet and noise gate.</summary>
    public AmpSettings WithModel(byte modelId)
    {
        var model = AmpCatalog.Find(modelId);
        if (model is null) return this with { ModelId = modelId };
        return this with
        {
            ModelId = modelId,
            Cabinet = model.DefaultCabinet ?? Cabinet,
            NoiseGate = model.DefaultNoiseGate,
        };
    }

    /// <summary>The settings that go into the amp packet, ignoring USB gain (which has its own packet).</summary>
    public bool SameAmpPacketAs(AmpSettings other) => this with { UsbGain = 0 } == other with { UsbGain = 0 };
}

/// <summary>Six knob values of an effect.</summary>
public readonly record struct KnobValues(byte K1, byte K2, byte K3, byte K4, byte K5, byte K6)
{
    public const int Count = 6;

    public byte this[int index] => index switch
    {
        0 => K1,
        1 => K2,
        2 => K3,
        3 => K4,
        4 => K5,
        5 => K6,
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };

    public KnobValues With(int index, byte value) => index switch
    {
        0 => this with { K1 = value },
        1 => this with { K2 = value },
        2 => this with { K3 = value },
        3 => this with { K4 = value },
        4 => this with { K5 = value },
        5 => this with { K6 = value },
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };

    public static KnobValues Uniform(byte value) => new(value, value, value, value, value, value);

    public byte[] ToArray() => [K1, K2, K3, K4, K5, K6];

    public static KnobValues FromSpan(ReadOnlySpan<byte> v) => new(v[0], v[1], v[2], v[3], v[4], v[5]);
}

/// <summary>One effect unit of a preset.</summary>
public sealed record EffectSettings
{
    public required EffectFamily Family { get; init; }

    /// <summary>Effect model id, 0 for none.</summary>
    public ushort ModelId { get; init; }

    public KnobValues Knobs { get; init; } = KnobValues.Uniform(128);

    /// <summary>Signal-chain position, 0-7. Positions 4-7 are after the amp (the FX loop).</summary>
    public byte Slot { get; init; }

    /// <summary>False when the effect is bypassed. A bypassed effect is cleared on the amp.</summary>
    public bool Enabled { get; init; } = true;

    public EffectModel? Model => EffectCatalog.Find(ModelId);

    public bool IsEmpty => ModelId == 0;

    /// <summary>True when the amp should currently run this effect.</summary>
    public bool IsActive => Enabled && ModelId != 0;

    public bool IsPostAmp => Slot >= 4;

    public static EffectSettings Empty(EffectFamily family) =>
        new() { Family = family, ModelId = 0, Slot = (byte)family, Enabled = false };

    /// <summary>Picks a new model, keeping knob values and position.</summary>
    public EffectSettings WithModel(ushort modelId) => this with
    {
        ModelId = modelId,
        Enabled = modelId != 0 && (Enabled || ModelId == 0),
    };
}

/// <summary>A complete sound: name, amp unit and the four effect units.</summary>
public sealed record Preset
{
    public string Name { get; init; } = "";
    public AmpSettings Amp { get; init; } = new();
    public EffectSettings Stomp { get; init; } = EffectSettings.Empty(EffectFamily.Stomp);
    public EffectSettings Modulation { get; init; } = EffectSettings.Empty(EffectFamily.Modulation);
    public EffectSettings Delay { get; init; } = EffectSettings.Empty(EffectFamily.Delay);
    public EffectSettings Reverb { get; init; } = EffectSettings.Empty(EffectFamily.Reverb);

    public EffectSettings Effect(EffectFamily family) => family switch
    {
        EffectFamily.Stomp => Stomp,
        EffectFamily.Modulation => Modulation,
        EffectFamily.Delay => Delay,
        EffectFamily.Reverb => Reverb,
        _ => throw new ArgumentOutOfRangeException(nameof(family)),
    };

    public IEnumerable<EffectSettings> Effects => [Stomp, Modulation, Delay, Reverb];

    public Preset WithEffect(EffectSettings effect) => effect.Family switch
    {
        EffectFamily.Stomp => this with { Stomp = effect },
        EffectFamily.Modulation => this with { Modulation = effect },
        EffectFamily.Delay => this with { Delay = effect },
        EffectFamily.Reverb => this with { Reverb = effect },
        _ => throw new ArgumentOutOfRangeException(nameof(effect)),
    };

    /// <summary>Moves an effect before or after the amp, picking a free signal-chain position.</summary>
    public Preset WithEffectPlacement(EffectFamily family, bool postAmp)
    {
        var effect = Effect(family);
        if (effect.IsPostAmp == postAmp) return this;

        var taken = Effects.Where(e => e.Family != family).Select(e => (int)e.Slot).ToHashSet();
        int baseSlot = postAmp ? 4 : 0;
        int preferred = baseSlot + effect.Slot % 4;
        int slot = !taken.Contains(preferred)
            ? preferred
            : Enumerable.Range(baseSlot, 4).FirstOrDefault(s => !taken.Contains(s), preferred);
        return WithEffect(effect with { Slot = (byte)slot });
    }

    public static Preset Default { get; } = new()
    {
        Name = "New preset",
        Stomp = EffectSettings.Empty(EffectFamily.Stomp),
        Modulation = EffectSettings.Empty(EffectFamily.Modulation) with { Slot = 5 },
        Delay = EffectSettings.Empty(EffectFamily.Delay) with { Slot = 6 },
        Reverb = EffectSettings.Empty(EffectFamily.Reverb) with { Slot = 7 },
    };
}
