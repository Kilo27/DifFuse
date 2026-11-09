using DifFuse.Core.Models;

namespace DifFuse.Core.Tests;

public class ModelTests
{
    [Fact]
    public void Catalogs_have_unique_ids()
    {
        Assert.Equal(AmpCatalog.All.Count, AmpCatalog.All.Select(a => a.Id).Distinct().Count());
        Assert.Equal(EffectCatalog.All.Count, EffectCatalog.All.Select(e => e.Id).Distinct().Count());
        Assert.Equal(EffectCatalog.All.Count, EffectCatalog.All.Select(e => e.FileId).Distinct().Count());
    }

    [Fact]
    public void Catalogs_cover_every_v1_and_v2_model()
    {
        Assert.Equal(12, AmpCatalog.All.Count(a => !a.IsV2));
        Assert.Equal(5, AmpCatalog.All.Count(a => a.IsV2));
        Assert.Equal(7, EffectCatalog.ForFamily(EffectFamily.Stomp, includeV2: false).Count());
        Assert.Equal(12, EffectCatalog.ForFamily(EffectFamily.Stomp, includeV2: true).Count());
        Assert.Equal(11, EffectCatalog.ForFamily(EffectFamily.Modulation, includeV2: false).Count());
        Assert.Equal(14, EffectCatalog.ForFamily(EffectFamily.Modulation, includeV2: true).Count());
        Assert.Equal(9, EffectCatalog.ForFamily(EffectFamily.Delay, includeV2: true).Count());
        Assert.Equal(10, EffectCatalog.ForFamily(EffectFamily.Reverb, includeV2: true).Count());
    }

    [Fact]
    public void Every_effect_labels_between_one_and_six_knobs()
    {
        Assert.All(EffectCatalog.All, e =>
        {
            Assert.InRange(e.Knobs.Count, 1, 6);
            Assert.All(e.Knobs, k => Assert.False(string.IsNullOrWhiteSpace(k.Label)));
        });
        Assert.Equal(6, EffectCatalog.Find(0x2a)!.Knobs.Count);
    }

    [Fact]
    public void Changing_amp_model_picks_its_cabinet_and_gate()
    {
        var amp = new AmpSettings { ModelId = 0x53, Cabinet = Cabinets.Deluxe65, NoiseGate = 0, Gain = 42 };
        var metal = amp.WithModel(0x6d);
        Assert.Equal(Cabinets.G4x12, metal.Cabinet);
        Assert.Equal(2, metal.NoiseGate);
        Assert.Equal(42, metal.Gain);

        var thrift = metal.WithModel(0xf9); // v2 model without a known default cabinet
        Assert.Equal(Cabinets.G4x12, thrift.Cabinet);
    }

    [Fact]
    public void Picking_a_model_for_an_empty_unit_turns_it_on_but_keeps_a_bypassed_unit_off()
    {
        var empty = EffectSettings.Empty(EffectFamily.Delay);
        Assert.True(empty.WithModel(0x16).Enabled);

        var bypassed = new EffectSettings { Family = EffectFamily.Delay, ModelId = 0x16, Enabled = false };
        Assert.False(bypassed.WithModel(0x45).Enabled);
        Assert.False(bypassed.WithModel(0).Enabled);
    }

    [Fact]
    public void Moving_an_effect_after_the_amp_picks_a_free_position()
    {
        var preset = new Preset
        {
            Stomp = new EffectSettings { Family = EffectFamily.Stomp, ModelId = 0x3c, Slot = 1 },
            Modulation = new EffectSettings { Family = EffectFamily.Modulation, ModelId = 0x12, Slot = 5 },
            Delay = new EffectSettings { Family = EffectFamily.Delay, ModelId = 0x16, Slot = 6 },
            Reverb = new EffectSettings { Family = EffectFamily.Reverb, ModelId = 0x24, Slot = 7 },
        };

        var moved = preset.WithEffectPlacement(EffectFamily.Stomp, postAmp: true);
        Assert.True(moved.Stomp.IsPostAmp);
        Assert.Equal(4, moved.Stomp.Slot);
        Assert.Equal(4, moved.Effects.Select(e => e.Slot).Distinct().Count());

        var back = moved.WithEffectPlacement(EffectFamily.Stomp, postAmp: false);
        Assert.False(back.Stomp.IsPostAmp);
        Assert.Equal(0, back.Stomp.Slot);
    }

    [Fact]
    public void Device_models_follow_product_ids()
    {
        var m2 = DeviceInfo.FromProductId(0x0014)!;
        Assert.Equal("Mustang I/II (v2)", m2.DisplayName);
        Assert.Equal(24, m2.PresetCount);
        Assert.True(m2.SupportsV2Models);
        Assert.Equal(100, DeviceInfo.FromProductId(0x0005)!.PresetCount);
        Assert.False(DeviceInfo.FromProductId(0x0005)!.SupportsV2Models);
        Assert.Null(DeviceInfo.FromProductId(0x9999));
    }
}
