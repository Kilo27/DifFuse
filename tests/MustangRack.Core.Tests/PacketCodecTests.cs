using MustangRack.Core.Fake;
using MustangRack.Core.Models;
using MustangRack.Core.Protocol;

namespace MustangRack.Core.Tests;

public class PacketCodecTests
{
    private static byte[] Bytes(Packet p) => p.ToArray();

    [Fact]
    public void Init_and_load_commands_match_the_documented_headers()
    {
        var init = PacketCodec.Init();
        Assert.Equal(new byte[] { 0x00, 0xc3, 0x00 }, Bytes(init[0])[..3]);
        Assert.Equal(new byte[] { 0x1a, 0xc1, 0x00 }, Bytes(init[1])[..3]);
        Assert.Equal(new byte[] { 0xff, 0xc1, 0x00 }, Bytes(PacketCodec.LoadAll())[..3]);
        Assert.Equal(new byte[] { 0x1c, 0x03, 0x00, 0x00 }, Bytes(PacketCodec.Apply())[..4]);
        Assert.All(Bytes(PacketCodec.Apply())[3..], b => Assert.Equal(0, b));
    }

    [Fact]
    public void Load_slot_command_carries_the_slot_in_header_byte_4()
    {
        var p = Bytes(PacketCodec.LoadSlot(17));
        Assert.Equal(new byte[] { 0x1c, 0x01, 0x01, 0x00, 17, 0x00, 0x01, 0x00 }, p[..8]);
        Assert.All(p[8..], b => Assert.Equal(0, b));
    }

    [Fact]
    public void Save_command_carries_slot_and_name()
    {
        var p = Bytes(PacketCodec.SaveName(3, "Big Room"));
        Assert.Equal(new byte[] { 0x1c, 0x01, 0x03, 0x00, 3, 0x00, 0x01, 0x01 }, p[..8]);
        Assert.Equal("Big Room"u8.ToArray(), p[16..24]);
        Assert.Equal(0, p[24]);
    }

    [Fact]
    public void Save_command_truncates_names_to_32_bytes_and_replaces_unsupported_characters()
    {
        var packet = PacketCodec.SaveName(0, new string('x', 40));
        Assert.Equal(32, packet.Name.Length);
        Assert.Equal(0, packet[48]);

        var unicode = PacketCodec.SaveName(0, "Tone ★ é");
        Assert.Equal("Tone _ é", unicode.Name);
    }

    [Fact]
    public void Amp_packet_puts_each_control_at_32_plus_its_control_index()
    {
        var amp = new AmpSettings
        {
            ModelId = 0x67, // '57 Deluxe
            Volume = 1, Gain = 2, Gain2 = 3, MasterVolume = 4, Treble = 5, Middle = 6, Bass = 7,
            Presence = 8, Depth = 9, Bias = 10, NoiseGate = 2, Threshold = 4, Cabinet = 0x0b, Sag = 2,
            Brightness = true, UsbGain = 99,
        };
        var p = Bytes(PacketCodec.Amp(amp));

        Assert.Equal(new byte[] { 0x1c, 0x03, 0x05, 0x00, 0x00, 0x00, 0x01, 0x01 }, p[..8]);
        Assert.Equal(0x67, p[16]);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, p[32..40]);
        Assert.Equal(0x80, p[40]); // control 8
        Assert.Equal(0x80, p[41]); // depth is 0x80 unless the gate is Custom
        Assert.Equal(10, p[42]);
        Assert.Equal(0x80, p[43]); // control 11
        Assert.Equal(new byte[] { 0x01, 0x01, 0x01 }, p[44..47]); // model-specific
        Assert.Equal(2, p[47]); // noise gate
        Assert.Equal(0, p[48]); // threshold only with Custom gate
        Assert.Equal(0x0b, p[49]); // cabinet
        Assert.Equal(0x01, p[50]); // model-specific
        Assert.Equal(2, p[51]); // sag
        Assert.Equal(1, p[52]); // brightness
        Assert.Equal(0x01, p[53]);
        Assert.Equal(0x53, p[54]); // model-specific 2
        Assert.All(p[55..], b => Assert.Equal(0, b));
    }

    [Fact]
    public void Amp_packet_sends_depth_and_threshold_only_with_the_custom_gate()
    {
        var amp = new AmpSettings { ModelId = 0x6d, NoiseGate = NoiseGates.Custom, Depth = 77, Threshold = 12 };
        var p = PacketCodec.Amp(amp);
        Assert.Equal(77, p.GetControl(PacketCodec.AmpControl.Depth));
        Assert.Equal(9, p.GetControl(PacketCodec.AmpControl.Threshold)); // clamped to 0-9
    }

    [Fact]
    public void Deluxe_reverb_uses_zero_for_controls_8_and_11()
    {
        var p = PacketCodec.Amp(new AmpSettings { ModelId = 0x53 });
        Assert.Equal(0x00, p.GetControl(8));
        Assert.Equal(0x00, p.GetControl(11));
        Assert.Equal(0x03, p.GetControl(12));
        Assert.Equal(0x6a, p.GetControl(22));
    }

    [Theory]
    [InlineData(0xf1, 0x0d, 0xf6)]
    [InlineData(0xff, 0x11, 0x00)]
    [InlineData(0x7c, 0x0c, 0x00)]
    public void Amp_models_set_their_specific_bytes(byte model, byte specific, byte specific2)
    {
        var p = PacketCodec.Amp(new AmpSettings { ModelId = model });
        Assert.Equal(model, p.AmpModel);
        Assert.Equal(specific, p.GetControl(12));
        Assert.Equal(specific, p.GetControl(18));
        Assert.Equal(specific2, p.GetControl(22));
    }

    [Fact]
    public void Usb_gain_has_its_own_packet()
    {
        var p = Bytes(PacketCodec.UsbGain(200));
        Assert.Equal(new byte[] { 0x1c, 0x03, 0x0d, 0x00, 0x00, 0x00, 0x01, 0x01 }, p[..8]);
        Assert.Equal(200, p[16]);
    }

    [Fact]
    public void Six_knob_delay_packet_layout()
    {
        var e = new EffectSettings
        {
            Family = EffectFamily.Delay,
            ModelId = 0x43, // Mono Echo Filter
            Slot = 6,
            Knobs = new KnobValues(10, 20, 30, 40, 50, 60),
        };
        var p = Bytes(PacketCodec.Effect(e));
        Assert.Equal(new byte[] { 0x1c, 0x03, 0x08, 0x00, 0x00, 0x00, 0x01, 0x01 }, p[..8]);
        Assert.Equal(new byte[] { 0x43, 0x00, 6, 0x02, 0x01, 0x01 }, p[16..22]);
        Assert.Equal(new byte[] { 10, 20, 30, 40, 50, 60 }, p[32..38]);
    }

    [Fact]
    public void Five_knob_effects_send_zero_for_the_sixth_knob()
    {
        var e = new EffectSettings { Family = EffectFamily.Reverb, ModelId = 0x24, Knobs = KnobValues.Uniform(99), Slot = 7 };
        var p = PacketCodec.Effect(e);
        Assert.Equal(0x09, p.Dsp);
        Assert.Equal(99, p.GetControl(4));
        Assert.Equal(0, p.GetControl(5));
        Assert.Equal(new byte[] { 0x00, 0x08, 0x01 }, p.ToArray()[19..22]);
    }

    [Fact]
    public void Simple_comp_clamps_its_type_and_zeroes_the_rest()
    {
        var e = new EffectSettings { Family = EffectFamily.Stomp, ModelId = 0x88, Knobs = KnobValues.Uniform(200) };
        var p = PacketCodec.Effect(e);
        Assert.Equal(3, p.GetControl(0));
        for (int i = 1; i < 6; i++) Assert.Equal(0, p.GetControl(i));
        Assert.Equal(new byte[] { 0x08, 0x08, 0x01 }, p.ToArray()[19..22]);
    }

    [Theory]
    [InlineData(0x22, 3, 1)] // ring modulator shape
    [InlineData(0x4f, 4, 1)] // phaser shape
    [InlineData(0x44, 4, 3)] // multitap mode
    public void Selector_knobs_are_clamped(ushort model, int knob, byte max)
    {
        var family = EffectCatalog.Find(model)!.Family;
        var e = new EffectSettings { Family = family, ModelId = model, Knobs = KnobValues.Uniform(250) };
        Assert.Equal(max, PacketCodec.Effect(e).GetControl(knob));
    }

    [Fact]
    public void V2_effect_ids_use_two_bytes_little_endian()
    {
        var e = new EffectSettings { Family = EffectFamily.Stomp, ModelId = 0x0103 };
        var p = PacketCodec.Effect(e);
        Assert.Equal(0x03, p[16]);
        Assert.Equal(0x01, p[17]);
        Assert.Equal(0x0103, p.EffectModel);
    }

    [Fact]
    public void Disabled_effects_serialize_as_a_clear_packet()
    {
        var e = new EffectSettings { Family = EffectFamily.Modulation, ModelId = 0x12, Enabled = false };
        var p = Bytes(PacketCodec.Effect(e));
        Assert.Equal(new byte[] { 0x1c, 0x03, 0x07, 0x00, 0x00, 0x00, 0x01, 0x01 }, p[..8]);
        Assert.Equal(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x08, 0x01 }, p[16..22]);
        Assert.All(p[22..], b => Assert.Equal(0, b));
    }

    [Fact]
    public void Preset_survives_encode_and_decode()
    {
        var preset = FakeMustangAmp.DemoPresets(24).ElementAt(6);
        Assert.True(preset.Effects.Count(e => e.IsActive) >= 3);

        var packets = FakeMustangAmp.EncodePreset(preset, 6).Select(b => new Packet(b)).ToList();
        var decoded = PacketCodec.DecodePreset(packets);

        Assert.Equal(preset.Name, decoded.Name);
        Assert.Equal(preset.Amp, decoded.Amp);
        foreach (var family in EffectFamilies.All)
        {
            var expected = preset.Effect(family);
            var actual = decoded.Effect(family);
            Assert.Equal(expected.ModelId, actual.ModelId);
            if (expected.IsActive)
            {
                Assert.Equal(expected.Knobs, actual.Knobs);
                Assert.Equal(expected.Slot, actual.Slot);
            }
        }
    }

    [Fact]
    public void Decode_places_effects_by_model_family_even_when_out_of_order()
    {
        var preset = FakeMustangAmp.DemoPresets(24).ElementAt(6);
        var packets = FakeMustangAmp.EncodePreset(preset, 0).Select(b => new Packet(b)).ToList();
        // Swap the stomp and reverb packets.
        (packets[2], packets[5]) = (packets[5], packets[2]);

        var decoded = PacketCodec.DecodePreset(packets);
        Assert.Equal(preset.Stomp.ModelId, decoded.Stomp.ModelId);
        Assert.Equal(preset.Reverb.ModelId, decoded.Reverb.ModelId);
    }

    [Fact]
    public void Load_all_reply_gives_names_and_current_preset()
    {
        var presets = FakeMustangAmp.DemoPresets(24).ToList();
        var packets = new List<Packet>();
        for (int i = 0; i < 24; i++)
        {
            var name = new Packet { Stage = 0x1c, Type = 0x01, Dsp = 0x04, Slot = (byte)i, Name = presets[i].Name };
            packets.Add(name);
            packets.Add(new Packet { Stage = 0x1c, Type = 0x01, Dsp = 0x04, Slot = (byte)i });
        }
        packets.AddRange(FakeMustangAmp.EncodePreset(presets[9], 9).Select(b => new Packet(b)));

        var (names, current, slot) = PacketCodec.DecodeLoadAll(packets, 24);
        Assert.Equal(presets.Select(p => p.Name), names);
        Assert.Equal(presets[9].Name, current.Name);
        Assert.Equal(presets[9].Amp, current.Amp);
        Assert.Equal(9, slot);
    }

    [Fact]
    public void Load_all_reply_that_is_too_short_is_a_protocol_error()
    {
        var packets = Enumerable.Range(0, 20).Select(_ => new Packet()).ToList();
        Assert.Throws<ProtocolException>(() => PacketCodec.DecodeLoadAll(packets, 24));
    }

    [Fact]
    public void Unknown_preset_count_is_inferred_from_reply_length()
    {
        var small = Enumerable.Range(0, 55).Select(_ => new Packet()).ToList();
        Assert.Equal(24, PacketCodec.DecodeLoadAll(small, 0).Names.Count);
        var large = Enumerable.Range(0, 207).Select(_ => new Packet()).ToList();
        Assert.Equal(100, PacketCodec.DecodeLoadAll(large, 0).Names.Count);
    }
}
