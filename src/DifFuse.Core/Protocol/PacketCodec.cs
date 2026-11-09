using DifFuse.Core.Models;

namespace DifFuse.Core.Protocol;

/// <summary>
/// Builds command packets and decodes the amp's data packets.
/// Byte layouts follow the reverse-engineered protocol documented by PLUG (https://github.com/offa/plug).
/// </summary>
public static class PacketCodec
{
    /// <summary>Number of packets the amp sends for one preset: name, amp, four effects, USB gain.</summary>
    public const int PresetPacketCount = 7;

    // Amp control indexes (FUSE "ControlIndex"); packet offset is 32 + index.
    public static class AmpControl
    {
        public const int Volume = 0, Gain = 1, Gain2 = 2, MasterVolume = 3, Treble = 4, Middle = 5,
            Bass = 6, Presence = 7, Unknown8 = 8, Depth = 9, Bias = 10, Unknown11 = 11,
            Specific12 = 12, Specific13 = 13, Specific14 = 14, NoiseGate = 15, Threshold = 16,
            Cabinet = 17, Specific18 = 18, Sag = 19, Brightness = 20, Unknown21 = 21, Specific22 = 22;

        public const int Count = 23;
    }

    // ---- Commands ----

    public static Packet[] Init() =>
    [
        Packet.Create(PacketStage.Init0, PacketType.Init0, Dsp.None),
        Packet.Create(PacketStage.Init1, PacketType.Init1, Dsp.None),
    ];

    /// <summary>Asks for every preset name followed by the current preset.</summary>
    public static Packet LoadAll() => Packet.Create(PacketStage.Unknown, PacketType.Load, Dsp.None);

    /// <summary>Makes a slot the amp's active preset and asks for its data.</summary>
    public static Packet LoadSlot(byte slot)
    {
        var p = Packet.Create(PacketStage.Ready, PacketType.Operation, Dsp.SelectMemoryBank);
        p.Slot = slot;
        p.SetHeaderExtras(0x00, 0x01, 0x00);
        return p;
    }

    /// <summary>Commits the data packets sent before it.</summary>
    public static Packet Apply() => Packet.Create(PacketStage.Ready, PacketType.Data, Dsp.None);

    /// <summary>Stores the amp's live sound in <paramref name="slot"/> under <paramref name="name"/>.</summary>
    public static Packet SaveName(byte slot, string name)
    {
        var p = Packet.Create(PacketStage.Ready, PacketType.Operation, Dsp.Save);
        p.Slot = slot;
        p.SetHeaderExtras(0x00, 0x01, 0x01);
        p.Name = name;
        return p;
    }

    public static Packet Amp(AmpSettings a)
    {
        var p = Packet.Create(PacketStage.Ready, PacketType.Data, Dsp.Amp);
        p.SetHeaderExtras(0x00, 0x01, 0x01);

        var model = a.Model;
        byte specific = model?.Specific ?? 0;
        byte specific2 = model?.Specific2 ?? 0;
        byte unknown8And11 = model?.Unknown8And11 ?? 0x80;
        byte gate = Math.Min(a.NoiseGate, NoiseGates.Custom);
        bool custom = gate == NoiseGates.Custom;

        p.AmpModel = a.ModelId;
        p.SetControl(AmpControl.Volume, a.Volume);
        p.SetControl(AmpControl.Gain, a.Gain);
        p.SetControl(AmpControl.Gain2, a.Gain2);
        p.SetControl(AmpControl.MasterVolume, a.MasterVolume);
        p.SetControl(AmpControl.Treble, a.Treble);
        p.SetControl(AmpControl.Middle, a.Middle);
        p.SetControl(AmpControl.Bass, a.Bass);
        p.SetControl(AmpControl.Presence, a.Presence);
        p.SetControl(AmpControl.Unknown8, unknown8And11);
        p.SetControl(AmpControl.Depth, custom ? a.Depth : (byte)0x80);
        p.SetControl(AmpControl.Bias, a.Bias);
        p.SetControl(AmpControl.Unknown11, unknown8And11);
        p.SetControl(AmpControl.Specific12, specific);
        p.SetControl(AmpControl.Specific13, specific);
        p.SetControl(AmpControl.Specific14, specific);
        p.SetControl(AmpControl.NoiseGate, gate);
        p.SetControl(AmpControl.Threshold, custom ? Math.Min(a.Threshold, (byte)9) : (byte)0);
        p.SetControl(AmpControl.Cabinet, a.Cabinet);
        p.SetControl(AmpControl.Specific18, specific);
        p.SetControl(AmpControl.Sag, Math.Min(a.Sag, (byte)2));
        p.SetControl(AmpControl.Brightness, a.Brightness ? (byte)1 : (byte)0);
        p.SetControl(AmpControl.Unknown21, 0x01);
        p.SetControl(AmpControl.Specific22, specific2);
        return p;
    }

    public static Packet UsbGain(byte value)
    {
        var p = Packet.Create(PacketStage.Ready, PacketType.Data, Dsp.UsbGain);
        p.SetHeaderExtras(0x00, 0x01, 0x01);
        p.UsbGain = value;
        return p;
    }

    public static Packet Effect(EffectSettings e)
    {
        var model = e.Model;
        if (e.ModelId == 0 || !e.Enabled)
        {
            return ClearEffect(e.Family);
        }

        var p = Packet.Create(PacketStage.Ready, PacketType.Data, e.Family.ToDsp());
        p.SetHeaderExtras(0x00, 0x01, 0x01);
        p.EffectModel = e.ModelId;
        p.EffectSlot = (byte)(e.Slot & 0x07);
        var extras = model?.Extras ?? (0x00, 0x08, 0x01);
        p.SetEffectExtras(extras.A, extras.B, extras.C);

        int used = model?.Knobs.Count ?? KnobValues.Count;
        for (int i = 0; i < KnobValues.Count; i++)
        {
            byte value;
            if (i < used)
            {
                byte max = model?.Knobs[i].Max ?? 255;
                value = Math.Min(e.Knobs[i], max);
            }
            else
            {
                // PLUG only sends the sixth knob for six-knob models.
                value = i == 5 || model?.ZeroUnusedKnobs == true ? (byte)0 : e.Knobs[i];
            }
            p.SetControl(i, value);
        }
        return p;
    }

    /// <summary>Removes whatever effect the family's DSP runs.</summary>
    public static Packet ClearEffect(EffectFamily family)
    {
        var p = Packet.Create(PacketStage.Ready, PacketType.Data, family.ToDsp());
        p.SetHeaderExtras(0x00, 0x01, 0x01);
        p.SetEffectExtras(0x00, 0x08, 0x01);
        return p;
    }

    // ---- Decoding ----

    public static AmpSettings DecodeAmp(Packet amp, Packet? usbGain)
    {
        return new AmpSettings
        {
            ModelId = amp.AmpModel,
            Volume = amp.GetControl(AmpControl.Volume),
            Gain = amp.GetControl(AmpControl.Gain),
            Gain2 = amp.GetControl(AmpControl.Gain2),
            MasterVolume = amp.GetControl(AmpControl.MasterVolume),
            Treble = amp.GetControl(AmpControl.Treble),
            Middle = amp.GetControl(AmpControl.Middle),
            Bass = amp.GetControl(AmpControl.Bass),
            Presence = amp.GetControl(AmpControl.Presence),
            Depth = amp.GetControl(AmpControl.Depth),
            Bias = amp.GetControl(AmpControl.Bias),
            NoiseGate = Math.Min(amp.GetControl(AmpControl.NoiseGate), NoiseGates.Custom),
            Threshold = Math.Min(amp.GetControl(AmpControl.Threshold), (byte)9),
            Cabinet = amp.GetControl(AmpControl.Cabinet),
            Sag = Math.Min(amp.GetControl(AmpControl.Sag), (byte)2),
            Brightness = amp.GetControl(AmpControl.Brightness) != 0,
            UsbGain = usbGain?.UsbGain ?? 0,
        };
    }

    public static EffectSettings DecodeEffect(Packet p, EffectFamily family)
    {
        var knobs = new byte[KnobValues.Count];
        for (int i = 0; i < knobs.Length; i++) knobs[i] = p.GetControl(i);
        ushort model = p.EffectModel;
        return new EffectSettings
        {
            Family = family,
            ModelId = model,
            Knobs = KnobValues.FromSpan(knobs),
            Slot = (byte)(p.EffectSlot & 0x07),
            Enabled = model != 0,
        };
    }

    /// <summary>Works out which effect family a packet belongs to: from its model, else its DSP byte.</summary>
    public static EffectFamily? FamilyOf(Packet p) =>
        EffectCatalog.Find(p.EffectModel)?.Family ?? EffectFamilies.FromDsp(p.Dsp);

    /// <summary>Decodes the seven packets the amp sends for a preset.</summary>
    public static Preset DecodePreset(IReadOnlyList<Packet> packets)
    {
        if (packets.Count < PresetPacketCount)
        {
            throw new ProtocolException($"Expected {PresetPacketCount} preset packets, got {packets.Count}.");
        }

        var effects = new EffectSettings?[4];
        for (int i = 0; i < 4; i++)
        {
            var packet = packets[2 + i];
            var family = FamilyOf(packet) ?? (EffectFamily)i;
            if (effects[(int)family] is not null)
            {
                family = (EffectFamily)i;
                if (effects[i] is not null) continue;
            }
            effects[(int)family] = DecodeEffect(packet, family);
        }

        EffectSettings EffectOrEmpty(EffectFamily f) => effects[(int)f] ?? EffectSettings.Empty(f);

        return new Preset
        {
            Name = packets[0].Name,
            Amp = DecodeAmp(packets[1], packets[6]),
            Stomp = EffectOrEmpty(EffectFamily.Stomp),
            Modulation = EffectOrEmpty(EffectFamily.Modulation),
            Delay = EffectOrEmpty(EffectFamily.Delay),
            Reverb = EffectOrEmpty(EffectFamily.Reverb),
        };
    }

    /// <summary>
    /// Splits the reply to <see cref="LoadAll"/>: two packets per preset (the name is in the first),
    /// then the current preset's seven packets.
    /// </summary>
    public static (IReadOnlyList<string> Names, Preset Current, byte CurrentSlot) DecodeLoadAll(
        IReadOnlyList<Packet> packets, int presetCount)
    {
        if (presetCount <= 0)
        {
            presetCount = packets.Count > 143 ? 100 : 24;
        }

        int namePackets = presetCount * 2;
        if (packets.Count < namePackets + PresetPacketCount)
        {
            throw new ProtocolException(
                $"The amp sent {packets.Count} packets; expected at least {namePackets + PresetPacketCount} for {presetCount} presets.");
        }

        var names = new List<string>(presetCount);
        for (int i = 0; i < namePackets; i += 2)
        {
            names.Add(packets[i].Name);
        }

        var presetPackets = packets.Skip(namePackets).Take(PresetPacketCount).ToList();
        return (names, DecodePreset(presetPackets), presetPackets[0].Slot);
    }
}

public sealed class ProtocolException(string message) : Exception(message);
