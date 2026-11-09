using DifFuse.Core.Protocol;

namespace DifFuse.Core.Models;

/// <summary>The four effect DSPs of a Mustang. Each preset holds at most one effect per family.</summary>
public enum EffectFamily
{
    Stomp = 0,
    Modulation = 1,
    Delay = 2,
    Reverb = 3,
}

public static class EffectFamilies
{
    public static IReadOnlyList<EffectFamily> All { get; } =
        [EffectFamily.Stomp, EffectFamily.Modulation, EffectFamily.Delay, EffectFamily.Reverb];

    public static byte ToDsp(this EffectFamily family) => (byte)(Dsp.Stomp + (int)family);

    public static EffectFamily? FromDsp(byte dsp) =>
        Dsp.IsEffect(dsp) ? (EffectFamily)(dsp - Dsp.Stomp) : null;

    public static string DisplayName(this EffectFamily family) => family switch
    {
        EffectFamily.Stomp => "Stomp",
        EffectFamily.Modulation => "Modulation",
        EffectFamily.Delay => "Delay",
        EffectFamily.Reverb => "Reverb",
        _ => family.ToString(),
    };

    /// <summary>Element names FUSE uses inside &lt;FX&gt;.</summary>
    public static string FuseElementName(this EffectFamily family) => family switch
    {
        EffectFamily.Stomp => "Stompbox",
        EffectFamily.Modulation => "Modulation",
        EffectFamily.Delay => "Delay",
        EffectFamily.Reverb => "Reverb",
        _ => family.ToString(),
    };
}

/// <summary>One knob of an effect. Knobs with a small <see cref="Max"/> are selectors.</summary>
public sealed record KnobDef(string Label, byte Max = 255, IReadOnlyList<string>? Choices = null)
{
    public bool IsSelector => Max < 255;
}

/// <summary>One effect model.</summary>
/// <param name="Id">Model id used in USB packets.</param>
/// <param name="FileId">Model id used in FUSE preset files (differs for one v2 effect).</param>
/// <param name="Extras">Effect payload bytes 3-5 the amp expects for this model.</param>
/// <param name="ZeroUnusedKnobs">Send 0 for every knob the model does not use.</param>
public sealed record EffectModel(
    ushort Id,
    string Name,
    EffectFamily Family,
    bool IsV2,
    IReadOnlyList<KnobDef> Knobs,
    (byte A, byte B, byte C) Extras,
    ushort FileId,
    bool ZeroUnusedKnobs = false);

public static class EffectCatalog
{
    private static readonly (byte, byte, byte) DefaultExtras = (0x00, 0x08, 0x01);
    private static readonly (byte, byte, byte) ModExtras = (0x01, 0x01, 0x01);
    private static readonly (byte, byte, byte) ModExtras8 = (0x01, 0x08, 0x01);
    private static readonly (byte, byte, byte) DelayExtras = (0x02, 0x01, 0x01);

    private static KnobDef[] K(params string[] labels) => labels.Select(l => new KnobDef(l)).ToArray();

    private static readonly IReadOnlyList<string> OffOn = ["Off", "On"];

    private static EffectModel M(ushort id, string name, EffectFamily family, KnobDef[] knobs,
        (byte, byte, byte)? extras = null, bool v2 = false, ushort? fileId = null, bool zeroUnused = false)
    {
        var x = extras ?? family switch
        {
            EffectFamily.Modulation => ModExtras,
            EffectFamily.Delay => DelayExtras,
            _ => DefaultExtras,
        };
        return new EffectModel(id, name, family, v2, knobs, x, fileId ?? id, zeroUnused);
    }

    private static readonly KnobDef[] ReverbKnobs = K("Level", "Decay", "Dwell", "Diffusion", "Tone");

    public static IReadOnlyList<EffectModel> All { get; } =
    [
        // Stomp
        M(0x3c, "Overdrive", EffectFamily.Stomp, K("Level", "Gain", "Low", "Mid", "High")),
        M(0x49, "Wah", EffectFamily.Stomp, K("Mix", "Freq", "Heel Freq", "Toe Freq", "High Q"), ModExtras8),
        M(0x4a, "Touch Wah", EffectFamily.Stomp, K("Mix", "Sensitivity", "Heel Freq", "Toe Freq", "High Q"), ModExtras8),
        M(0x1a, "Fuzz", EffectFamily.Stomp, K("Level", "Gain", "Octave", "Low", "High")),
        M(0x1c, "Fuzz Touch Wah", EffectFamily.Stomp, K("Level", "Gain", "Sensitivity", "Octave", "Peak")),
        M(0x88, "Simple Comp", EffectFamily.Stomp, [new KnobDef("Type", 3, ["1", "2", "3", "4"])], (0x08, 0x08, 0x01), zeroUnused: true),
        M(0x07, "Compressor", EffectFamily.Stomp, K("Level", "Threshold", "Ratio", "Attack", "Release")),
        M(0x0103, "Ranger Boost", EffectFamily.Stomp, K("Level", "Gain", "Low", "Bright"), v2: true),
        M(0x00ba, "Greenbox", EffectFamily.Stomp, K("Level", "Gain", "Tone", "Blend"), v2: true),
        M(0x0110, "Orangebox", EffectFamily.Stomp, K("Level", "Dist", "Tone"), v2: true),
        M(0x0111, "Blackbox", EffectFamily.Stomp, K("Level", "Dist", "Filter"), v2: true),
        M(0x010f, "Big Fuzz", EffectFamily.Stomp, K("Level", "Tone", "Sustain"), v2: true),

        // Modulation
        M(0x12, "Sine Chorus", EffectFamily.Modulation, K("Level", "Rate", "Depth", "Avg Delay", "LR Phase")),
        M(0x13, "Triangle Chorus", EffectFamily.Modulation, K("Level", "Rate", "Depth", "Avg Delay", "LR Phase")),
        M(0x18, "Sine Flanger", EffectFamily.Modulation, K("Level", "Rate", "Depth", "Feedback", "LR Phase")),
        M(0x19, "Triangle Flanger", EffectFamily.Modulation, K("Level", "Rate", "Depth", "Feedback", "LR Phase")),
        M(0x2d, "Vibratone", EffectFamily.Modulation, K("Level", "Rotor", "Depth", "Feedback", "LR Phase")),
        M(0x40, "Vintage Tremolo", EffectFamily.Modulation, K("Level", "Rate", "Duty Cycle", "Attack", "Release")),
        M(0x41, "Sine Tremolo", EffectFamily.Modulation, K("Level", "Rate", "Duty Cycle", "LFO Clip", "Tri Shape")),
        M(0x22, "Ring Modulator", EffectFamily.Modulation,
            [new("Level"), new("Freq"), new("Depth"), new("Shape", 1, ["1", "2"]), new("Phase")], ModExtras8),
        M(0x29, "Step Filter", EffectFamily.Modulation, K("Level", "Rate", "Resonance", "Min Freq", "Max Freq")),
        M(0x4f, "Phaser", EffectFamily.Modulation,
            [new("Level"), new("Rate"), new("Depth"), new("Feedback"), new("Shape", 1, ["1", "2"])]),
        M(0x1f, "Pitch Shifter", EffectFamily.Modulation, K("Level", "Pitch", "Detune", "Feedback", "Predelay"), ModExtras8),
        M(0xf4, "Wah (Mod)", EffectFamily.Modulation,
            [new("Mix"), new("Freq"), new("Heel Freq"), new("Toe Freq"), new("High Q", 1, OffOn)], ModExtras8, v2: true),
        M(0xf5, "Touch Wah (Mod)", EffectFamily.Modulation,
            [new("Mix"), new("Sensitivity"), new("Min Freq"), new("Max Freq"), new("High Q", 1, OffOn)], ModExtras8, v2: true),
        // USB packets use 0x101f; PLUG's FUSE reader and writer use 0x11f.
        M(0x101f, "Diatonic Pitch", EffectFamily.Modulation,
            [new("Mix"), new("Pitch", 21), new("Key", 11), new("Scale", 8), new("Tone")], (0x00, 0x08, 0x01), v2: true, fileId: 0x11f),

        // Delay
        M(0x16, "Mono Delay", EffectFamily.Delay, K("Level", "Delay", "Feedback", "Brightness", "Attenuation")),
        M(0x43, "Mono Echo Filter", EffectFamily.Delay, K("Level", "Delay", "Feedback", "Frequency", "Resonance", "In Level")),
        M(0x48, "Stereo Echo Filter", EffectFamily.Delay, K("Level", "Delay", "Feedback", "Frequency", "Resonance", "In Level")),
        M(0x44, "Multitap Delay", EffectFamily.Delay,
            [new("Level"), new("Delay"), new("Feedback"), new("Brightness"), new("Mode", 3, ["1", "2", "3", "4"])]),
        M(0x45, "Ping Pong Delay", EffectFamily.Delay, K("Level", "Delay", "Feedback", "Brightness", "Stereo")),
        M(0x15, "Ducking Delay", EffectFamily.Delay, K("Level", "Delay", "Feedback", "Release", "Threshold")),
        M(0x46, "Reverse Delay", EffectFamily.Delay, K("Level", "Delay", "Fwd Fdbk", "Rev Fdbk", "Tone")),
        M(0x2b, "Tape Delay", EffectFamily.Delay, K("Level", "Delay", "Feedback", "Flutter", "Brightness", "Stereo")),
        M(0x2a, "Stereo Tape Delay", EffectFamily.Delay, K("Level", "Delay", "Feedback", "Flutter", "Separation", "Brightness")),

        // Reverb
        M(0x24, "Small Hall", EffectFamily.Reverb, ReverbKnobs),
        M(0x3a, "Large Hall", EffectFamily.Reverb, ReverbKnobs),
        M(0x26, "Small Room", EffectFamily.Reverb, ReverbKnobs),
        M(0x3b, "Large Room", EffectFamily.Reverb, ReverbKnobs),
        M(0x4e, "Small Plate", EffectFamily.Reverb, ReverbKnobs),
        M(0x4b, "Large Plate", EffectFamily.Reverb, ReverbKnobs),
        M(0x4c, "Ambient", EffectFamily.Reverb, ReverbKnobs),
        M(0x4d, "Arena", EffectFamily.Reverb, ReverbKnobs),
        M(0x21, "'63 Spring", EffectFamily.Reverb, ReverbKnobs),
        M(0x0b, "'65 Spring", EffectFamily.Reverb, ReverbKnobs),
    ];

    private static readonly Dictionary<ushort, EffectModel> ById = All.ToDictionary(e => e.Id);
    private static readonly Dictionary<ushort, EffectModel> ByFileId = All.ToDictionary(e => e.FileId);

    public static EffectModel? Find(ushort id) => ById.GetValueOrDefault(id);

    /// <summary>Looks up a FUSE file module id; also accepts the USB id.</summary>
    public static EffectModel? FindByFileId(ushort id) => ByFileId.GetValueOrDefault(id) ?? Find(id);

    public static IEnumerable<EffectModel> ForFamily(EffectFamily family, bool includeV2) =>
        All.Where(e => e.Family == family && (includeV2 || !e.IsV2));

    public static string NameOf(ushort id) =>
        id == 0 ? "None" : Find(id)?.Name ?? $"Unknown (0x{id:X4})";
}
