using DifFuse.Core.Models;

namespace DifFuse.App.ViewModels;

/// <summary>How a parameter is shown, chosen from what the parameter does.</summary>
public enum KnobKind
{
    /// <summary>A continuous amount, 0-10. A knob.</summary>
    Dial,

    /// <summary>A wet/dry balance, shown as a percentage. A knob.</summary>
    Percent,

    /// <summary>Many fixed positions (more than six). A knob that clicks between detents.</summary>
    Stepped,

    /// <summary>Two to six named positions. Stops along a line.</summary>
    Stops,

    /// <summary>Off or on. A switch.</summary>
    Switch,
}

/// <summary>
/// Picks the control for each parameter and explains what it does in one plain line. The texts describe
/// what a control of that name does on an amp or pedal; they never claim units the amp does not report.
/// </summary>
public static class ControlGuide
{
    public static KnobKind KindFor(string label, double maximum, IReadOnlyList<string>? choices)
    {
        if (maximum >= 255) return label == "Mix" ? KnobKind.Percent : KnobKind.Dial;
        if (choices is ["Off", "On"]) return KnobKind.Switch;
        return maximum <= 5 ? KnobKind.Stops : KnobKind.Stepped;
    }

    public const string AmpModel = "Amp model. Switching models also brings in that model's cabinet and noise gate, as FUSE does.";
    public const string Cabinet = "Speaker cabinet the amp model plays through.";
    public const string NoiseGate = "Cuts hiss and hum when you stop playing. Low to Super close more firmly; Custom unlocks Threshold and Depth.";
    public const string Bright = "Bright switch. Lifts the highs for extra sparkle.";
    public const string EffectModel = "Effect model. Its knobs change to match the model you pick.";
    public const string Power = "Turns the effect on, or bypasses it.";
    public const string Placement = "Where the effect sits: before the amp, like a pedal, or after it, in the effects loop.";

    private static readonly Dictionary<string, string> Amp = new()
    {
        ["Gain"] = "How hard the preamp is driven. Low settings stay clean; turn it up for breakup and distortion.",
        ["Gain 2"] = "A second preamp gain stage. Adds drive on top of Gain on the amp models that use it.",
        ["Volume"] = "Volume of the amp model's preamp channel.",
        ["Master"] = "Master volume of the power amp section.",
        ["Bass"] = "Low end of the tone stack.",
        ["Middle"] = "Midrange of the tone stack. Cut it for a scooped sound, raise it to cut through a band.",
        ["Treble"] = "High end of the tone stack.",
        ["Presence"] = "The upper highs above Treble. Adds bite and air.",
        ["Bias"] = "Power-tube bias. Changes how the power amp feels and how it breaks up.",
        ["Sag"] = "How much the power supply sags when you play hard. Soft feels loose and compressed; Hard stays tight.",
        ["Threshold"] = "Custom gate only. The level below which the gate closes, 0 to 9.",
        ["Depth"] = "Custom gate only. How far the sound is turned down while the gate is closed.",
        ["USB Gain"] = "Level of the amp's USB audio output, for recording on your computer.",
    };

    private static readonly Dictionary<string, string> Effects = new()
    {
        // Model-specific first, then family-specific, then by label alone.
        ["Vintage Tremolo:Attack"] = "How smoothly the volume rises in each pulse.",
        ["Vintage Tremolo:Release"] = "How smoothly the volume falls in each pulse.",
        ["Ducking Delay:Release"] = "How quickly the repeats come back after you stop playing.",
        ["Ducking Delay:Threshold"] = "How loud you must play before the repeats duck out of the way.",
        ["Ring Modulator:Freq"] = "Frequency of the ring modulator's carrier tone.",
        ["Ring Modulator:Phase"] = "Offset between the left and right outputs.",
        ["Pitch Shifter:Pitch"] = "Interval of the shifted voice.",
        ["Diatonic Pitch:Pitch"] = "Harmony interval, position 1 to 22.",
        ["Simple Comp:Type"] = "Compression setting, 1 to 4.",

        ["Reverb:Level"] = "Amount of reverb.",
        ["Reverb:Tone"] = "Brightness of the reverb.",
        ["Delay:Feedback"] = "How many repeats you hear.",
        ["Delay:Level"] = "Level of the repeats.",
        ["Modulation:Depth"] = "Strength of the modulation.",
        ["Modulation:Feedback"] = "Signal fed back into the effect for a stronger, more resonant sound.",

        ["Level"] = "Output level of the effect.",
        ["Mix"] = "Balance between your dry sound and the effect.",
        ["Gain"] = "Amount of drive.",
        ["Low"] = "Low end of the pedal's tone.",
        ["Mid"] = "Midrange of the pedal's tone.",
        ["High"] = "High end of the pedal's tone.",
        ["Tone"] = "Brightness of the effect.",
        ["Freq"] = "Where the filter sits in its sweep.",
        ["Heel Freq"] = "Filter frequency with the wah rocked back, heel down.",
        ["Toe Freq"] = "Filter frequency with the wah rocked forward, toe down.",
        ["Min Freq"] = "Lowest point of the filter sweep.",
        ["Max Freq"] = "Highest point of the filter sweep.",
        ["High Q"] = "Narrows the filter peak for a sharper, more vocal sound.",
        ["Sensitivity"] = "How strongly your picking moves the filter.",
        ["Octave"] = "Amount of octave effect blended into the fuzz.",
        ["Peak"] = "How pronounced the filter peak is.",
        ["Threshold"] = "Level above which compression starts.",
        ["Ratio"] = "How strongly loud notes are squeezed.",
        ["Attack"] = "How quickly compression grabs each note.",
        ["Release"] = "How quickly compression lets go.",
        ["Bright"] = "Treble boost.",
        ["Blend"] = "Mix of clean and boosted signal.",
        ["Dist"] = "Amount of distortion.",
        ["Filter"] = "Tone filter of the pedal.",
        ["Sustain"] = "Amount of fuzz and sustain.",
        ["Rate"] = "Speed of the modulation.",
        ["Rotor"] = "Speed of the rotating speaker.",
        ["Depth"] = "Strength of the effect.",
        ["Avg Delay"] = "Base delay time of the chorus voices.",
        ["Feedback"] = "Signal fed back into the effect.",
        ["LR Phase"] = "Offset between the left and right outputs, for stereo width.",
        ["Duty Cycle"] = "How long the tremolo stays up versus down in each pulse.",
        ["LFO Clip"] = "Squares off the tremolo wave for a choppier pulse.",
        ["Tri Shape"] = "Bends the shape of the tremolo wave.",
        ["Shape"] = "Waveform of the modulation, 1 or 2.",
        ["Phase"] = "Offset between the left and right outputs.",
        ["Resonance"] = "Emphasis at the filter's frequency.",
        ["Pitch"] = "Interval of the shifted voice.",
        ["Detune"] = "A small pitch offset that thickens the sound.",
        ["Predelay"] = "Delay before the shifted voice comes in.",
        ["Key"] = "Key the harmony follows, position 1 to 12.",
        ["Scale"] = "Scale the harmony follows, position 1 to 9.",
        ["Delay"] = "Delay time: the gap between repeats.",
        ["Brightness"] = "Tone of the repeats.",
        ["Attenuation"] = "Damping applied to the repeats.",
        ["Frequency"] = "Centre of the filter on the repeats.",
        ["In Level"] = "Level going into the echo.",
        ["Mode"] = "Tap pattern, 1 to 4.",
        ["Stereo"] = "Stereo spread of the repeats.",
        ["Fwd Fdbk"] = "Repeats of the forward signal.",
        ["Rev Fdbk"] = "Repeats of the reversed signal.",
        ["Flutter"] = "Tape wobble on the repeats.",
        ["Separation"] = "Spread between the left and right repeats.",
        ["Decay"] = "How long the reverb tail rings out.",
        ["Dwell"] = "How hard your signal drives the reverb.",
        ["Diffusion"] = "How dense and smeared the reflections are.",
    };

    public static string ForAmp(string label) => Amp.GetValueOrDefault(label, "");

    public static string ForEffect(EffectFamily family, string? modelName, string label)
    {
        if (string.IsNullOrEmpty(label)) return "";
        if (modelName is not null && Effects.TryGetValue($"{modelName}:{label}", out var text)) return text;
        if (Effects.TryGetValue($"{family}:{label}", out text)) return text;
        return Effects.GetValueOrDefault(label, "");
    }
}
