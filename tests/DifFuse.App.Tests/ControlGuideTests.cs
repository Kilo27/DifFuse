using DifFuse.App.ViewModels;
using DifFuse.Core.Models;

namespace DifFuse.App.Tests;

/// <summary>Every control's form fits what it does, and every control explains itself.</summary>
public sealed class ControlGuideTests
{
    [Theory]
    [InlineData("Gain", 255, KnobKind.Dial)]
    [InlineData("Mix", 255, KnobKind.Percent)]
    [InlineData("Threshold", 9, KnobKind.Stepped)]
    [InlineData("Key", 11, KnobKind.Stepped)]
    [InlineData("Pitch", 21, KnobKind.Stepped)]
    [InlineData("Type", 3, KnobKind.Stops)]
    [InlineData("Shape", 1, KnobKind.Stops)]
    public void Kind_follows_the_range(string label, int max, KnobKind expected) =>
        Assert.Equal(expected, ControlGuide.KindFor(label, max, null));

    [Fact]
    public void Sag_is_three_stops_and_high_q_is_a_switch()
    {
        var amp = new AmpUnitViewModel();
        Assert.Equal(KnobKind.Stops, amp.Sag.Kind);
        Assert.Equal(["Soft", "Normal", "Hard"], amp.Sag.PositionNames);

        var mod = new EffectUnitViewModel(EffectFamily.Modulation);
        mod.SelectedModel = mod.Models.First(m => m.Name == "Wah (Mod)");
        var highQ = mod.Knobs[4];
        Assert.Equal("High Q", highQ.Label);
        Assert.Equal(KnobKind.Switch, highQ.Kind);
        highQ.IsOn = true;
        Assert.Equal(1, highQ.ByteValue);
        Assert.Equal("On", highQ.DisplayValue);
    }

    [Fact]
    public void A_knob_is_redefined_when_the_model_changes()
    {
        var delay = new EffectUnitViewModel(EffectFamily.Delay);
        delay.SelectedModel = delay.Models.First(m => m.Name == "Multitap Delay");
        Assert.Equal(KnobKind.Stops, delay.Knobs[4].Kind);
        Assert.Equal("Mode", delay.Knobs[4].Label);

        delay.SelectedModel = delay.Models.First(m => m.Name == "Mono Delay");
        Assert.Equal(KnobKind.Dial, delay.Knobs[4].Kind);
        Assert.Equal("Attenuation", delay.Knobs[4].Label);
    }

    [Fact]
    public void Mix_reads_as_a_percentage_and_amounts_as_zero_to_ten()
    {
        var mix = new KnobViewModel("Mix", 255, 255);
        Assert.Equal(KnobKind.Percent, mix.Kind);
        Assert.Equal("100%", mix.DisplayValue);

        var level = new KnobViewModel("Level", 255, 255);
        Assert.Equal((10.0).ToString("0.0"), level.DisplayValue);
    }

    [Fact]
    public void Amp_controls_are_grouped_like_an_amp()
    {
        var amp = new AmpUnitViewModel();
        Assert.Equal(["Gain", "Gain 2", "Volume"], amp.PreampKnobs.Select(k => k.Label));
        Assert.Equal(["Bass", "Middle", "Treble", "Presence"], amp.EqKnobs.Select(k => k.Label));
        Assert.Equal(["Master", "Bias", "Sag"], amp.PowerKnobs.Select(k => k.Label));
    }

    [Fact]
    public void Gate_threshold_and_depth_only_work_with_the_custom_gate()
    {
        var amp = new AmpUnitViewModel();
        amp.NoiseGate = 2;
        Assert.False(amp.IsCustomGate);
        Assert.False(amp.Threshold.IsEnabled);
        Assert.False(amp.Depth.IsEnabled);

        amp.NoiseGate = NoiseGates.Custom;
        Assert.True(amp.IsCustomGate);
        Assert.True(amp.Threshold.IsEnabled);
        Assert.True(amp.Depth.IsEnabled);
    }

    [Fact]
    public void Every_amp_control_explains_itself()
    {
        var amp = new AmpUnitViewModel();
        var knobs = amp.PreampKnobs.Concat(amp.EqKnobs).Concat(amp.PowerKnobs).Append(amp.Threshold).Append(amp.Depth).Append(amp.UsbGain);
        Assert.All(knobs, k => Assert.False(string.IsNullOrWhiteSpace(k.Help), $"{k.Label} has no help text"));
    }

    [Fact]
    public void Every_effect_control_explains_itself()
    {
        var missing = EffectCatalog.All
            .SelectMany(m => m.Knobs.Select(k => (m, k)))
            .Where(x => ControlGuide.ForEffect(x.m.Family, x.m.Name, x.k.Label).Length == 0)
            .Select(x => $"{x.m.Name}: {x.k.Label}")
            .ToList();
        Assert.Empty(missing);
    }
}
