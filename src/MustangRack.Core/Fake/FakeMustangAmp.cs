using System.Collections.Concurrent;
using MustangRack.Core.Models;
using MustangRack.Core.Protocol;
using MustangRack.Core.Transport;

namespace MustangRack.Core.Fake;

/// <summary>
/// A simulated Mustang that answers the same packets as the real amp, so the app and tests
/// run without hardware. Its replies follow the layout PLUG documents.
/// </summary>
public sealed class FakeMustangAmp
{
    private readonly object _lock = new();
    private readonly List<Preset> _presets;
    private readonly List<byte[]> _received = new();
    private AmpSettings? _pendingAmp;
    private byte? _pendingUsbGain;
    private readonly EffectSettings?[] _pendingEffects = new EffectSettings?[4];

    public FakeMustangAmp(DeviceInfo device, IEnumerable<Preset>? presets = null)
    {
        Device = device;
        int count = device.PresetCount > 0 ? device.PresetCount : 24;
        _presets = (presets ?? DemoPresets(count)).Take(count).ToList();
        while (_presets.Count < count) _presets.Add(Preset.Default with { Name = $"Empty {_presets.Count:00}" });
        Live = _presets[0];
    }

    public static FakeMustangAmp CreateMustangIIv2() => new(DeviceInfo.FromProductId(0x0014)!);

    public DeviceInfo Device { get; }

    /// <summary>The sound the simulated amp is playing.</summary>
    public Preset Live { get; private set; }

    public int CurrentSlot { get; private set; }

    public IReadOnlyList<Preset> Presets
    {
        get { lock (_lock) return _presets.ToList(); }
    }

    /// <summary>Every packet the amp has received, oldest first.</summary>
    public IReadOnlyList<byte[]> Received
    {
        get { lock (_lock) return _received.ToList(); }
    }

    /// <summary>Number of save operations (writes to a slot) the amp has received.</summary>
    public int SaveCount { get; private set; }

    /// <summary>Raised when the amp sends packets nobody asked for (front-panel changes).</summary>
    public event Action<IReadOnlyList<byte[]>>? Unsolicited;

    public void ClearReceived()
    {
        lock (_lock) _received.Clear();
    }

    /// <summary>Simulates turning the preset selector on the amp itself.</summary>
    public void SelectPresetOnAmp(int slot)
    {
        List<byte[]> packets;
        lock (_lock)
        {
            CurrentSlot = slot;
            Live = _presets[slot];
            packets = EncodePreset(Live, (byte)slot);
        }
        Unsolicited?.Invoke(packets);
    }

    /// <summary>Handles one packet from the host and returns the amp's replies.</summary>
    public IReadOnlyList<byte[]> Handle(ReadOnlySpan<byte> data)
    {
        var p = new Packet(data);
        lock (_lock)
        {
            _received.Add(p.ToArray());

            if (p.Stage == PacketStage.Unknown && p.Type == PacketType.Load)
            {
                return LoadAllReply();
            }

            if (p.Stage == PacketStage.Ready && p.Type == PacketType.Operation)
            {
                switch (p.Dsp)
                {
                    case Dsp.SelectMemoryBank:
                        int slot = Math.Min(p.Slot, (byte)(_presets.Count - 1));
                        CurrentSlot = slot;
                        Live = _presets[slot];
                        ClearPending();
                        return EncodePreset(Live, (byte)slot);

                    case Dsp.Save:
                        int target = p.Slot;
                        if (target < _presets.Count)
                        {
                            _presets[target] = Live with { Name = p.Name };
                            SaveCount++;
                        }
                        return [Ack()];
                }
            }

            if (p.Stage == PacketStage.Ready && p.Type == PacketType.Data)
            {
                switch (p.Dsp)
                {
                    case Dsp.None:
                        Commit();
                        break;
                    case Dsp.Amp:
                        _pendingAmp = PacketCodec.DecodeAmp(p, null);
                        break;
                    case Dsp.UsbGain:
                        _pendingUsbGain = p.UsbGain;
                        break;
                    case >= Dsp.Stomp and <= Dsp.Reverb:
                        var family = EffectFamilies.FromDsp(p.Dsp)!.Value;
                        _pendingEffects[(int)family] = PacketCodec.DecodeEffect(p, family);
                        break;
                }
            }

            return [Ack()];
        }
    }

    private void Commit()
    {
        var live = Live;
        if (_pendingAmp is not null) live = live with { Amp = _pendingAmp with { UsbGain = live.Amp.UsbGain } };
        if (_pendingUsbGain is not null) live = live with { Amp = live.Amp with { UsbGain = _pendingUsbGain.Value } };
        foreach (var effect in _pendingEffects)
        {
            if (effect is not null) live = live.WithEffect(effect);
        }
        Live = live;
        ClearPending();
    }

    private void ClearPending()
    {
        _pendingAmp = null;
        _pendingUsbGain = null;
        Array.Clear(_pendingEffects);
    }

    private List<byte[]> LoadAllReply()
    {
        var reply = new List<byte[]>();
        for (int i = 0; i < _presets.Count; i++)
        {
            var name = Packet.Create(PacketStage.Ready, PacketType.Operation, Dsp.SaveEffectName);
            name.Slot = (byte)i;
            name.SetHeaderExtras(0x00, 0x01, 0x01);
            name.Name = _presets[i].Name;
            reply.Add(name.ToArray());

            var second = Packet.Create(PacketStage.Ready, PacketType.Operation, Dsp.SaveEffectName);
            second.Slot = (byte)i;
            reply.Add(second.ToArray());
        }
        reply.AddRange(EncodePreset(Live, (byte)CurrentSlot));
        return reply;
    }

    /// <summary>The seven packets the amp sends for a preset.</summary>
    public static List<byte[]> EncodePreset(Preset preset, byte slot)
    {
        var name = Packet.Create(PacketStage.Ready, PacketType.Operation, Dsp.SaveEffectName);
        name.Slot = slot;
        name.SetHeaderExtras(0x00, 0x01, 0x01);
        name.Name = preset.Name;

        var amp = PacketCodec.Amp(preset.Amp);
        amp.Slot = slot;

        var packets = new List<byte[]> { name.ToArray(), amp.ToArray() };
        foreach (var effect in preset.Effects)
        {
            var e = effect.IsActive
                ? PacketCodec.Effect(effect)
                : PacketCodec.ClearEffect(effect.Family);
            if (!effect.IsActive) e.EffectSlot = effect.Slot;
            e.Slot = slot;
            packets.Add(e.ToArray());
        }

        var usb = PacketCodec.UsbGain(preset.Amp.UsbGain);
        usb.Slot = slot;
        packets.Add(usb.ToArray());
        return packets;
    }

    private static byte[] Ack()
    {
        var ack = new byte[Packet.Size];
        ack[0] = PacketStage.Ready;
        return ack;
    }

    /// <summary>A set of made-up presets for the simulated amp.</summary>
    public static IEnumerable<Preset> DemoPresets(int count)
    {
        string[] names =
        [
            "Glass Clean", "Sunday Blues", "Crunch Box", "Lead Highway", "Surf Spring", "Tremolo Night",
            "Chime Sixties", "Big Room Rhythm", "Metal Chug", "Fuzz Garage", "Slapback", "Ambient Wash",
            "Phase Funk", "Chorus Pad", "Tape Echo Solo", "Studio Direct", "Seventies Stack", "Eighties Shred",
            "Nineties Grind", "Thrift Store", "Colour Burst", "Watts Up", "Octave Fuzz", "Quiet Practice",
        ];
        byte[] amps = [0x53, 0x67, 0x61, 0x5e, 0x75, 0x6a, 0x61, 0x72, 0x6d, 0x7c, 0x64, 0x53];
        ushort[] stomps = [0, 0x3c, 0x3c, 0x1a, 0, 0, 0x07, 0, 0x3c, 0x1c, 0, 0];
        ushort[] mods = [0x12, 0, 0, 0, 0, 0x40, 0x2d, 0x18, 0, 0x4f, 0, 0x13];
        ushort[] delays = [0, 0x16, 0, 0x2b, 0, 0, 0x45, 0x16, 0, 0, 0x16, 0x46];
        ushort[] reverbs = [0x24, 0x21, 0x26, 0x3a, 0x0b, 0x21, 0x4e, 0x3b, 0, 0x26, 0x26, 0x4c];

        for (int i = 0; i < count; i++)
        {
            int k = i % amps.Length;
            byte gain = (byte)(40 + (i * 37) % 200);
            yield return new Preset
            {
                Name = i < names.Length ? names[i] : $"Preset {i:00}",
                Amp = new AmpSettings().WithModel(amps[k]) with
                {
                    Gain = gain,
                    Volume = (byte)(150 + i % 60),
                    Treble = (byte)(110 + (i * 13) % 100),
                    Middle = (byte)(90 + (i * 29) % 120),
                    Bass = (byte)(100 + (i * 17) % 90),
                    UsbGain = 128,
                },
                Stomp = Effect(EffectFamily.Stomp, stomps[k], 0, i),
                Modulation = Effect(EffectFamily.Modulation, mods[k], 5, i),
                Delay = Effect(EffectFamily.Delay, delays[k], 6, i),
                Reverb = Effect(EffectFamily.Reverb, reverbs[k], 7, i),
            };
        }

        static EffectSettings Effect(EffectFamily family, ushort model, byte slot, int seed) => new()
        {
            Family = family,
            ModelId = model,
            Slot = slot,
            Enabled = model != 0,
            Knobs = new KnobValues(
                (byte)(100 + seed * 7 % 100), (byte)(60 + seed * 11 % 150), (byte)(80 + seed * 5 % 120),
                (byte)(50 + seed * 3 % 150), (byte)(90 + seed * 13 % 100),
                EffectCatalog.Find(model)?.Knobs.Count == 6 ? (byte)128 : (byte)0),
        };
    }
}

/// <summary>A transport connected to a <see cref="FakeMustangAmp"/>.</summary>
public sealed class FakeHidTransport : IHidTransport
{
    private readonly FakeMustangAmp _amp;
    private readonly BlockingCollection<byte[]> _replies = new(new ConcurrentQueue<byte[]>());
    private volatile bool _unplugged;
    private volatile bool _disposed;

    public FakeHidTransport(FakeMustangAmp amp)
    {
        _amp = amp;
        _amp.Unsolicited += OnUnsolicited;
    }

    /// <summary>Delay before each reply, to mimic USB latency.</summary>
    public TimeSpan ReplyDelay { get; set; } = TimeSpan.Zero;

    public void Write(ReadOnlySpan<byte> packet)
    {
        if (_unplugged) throw new DeviceDisconnectedException("The simulated amp was unplugged.");
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (ReplyDelay > TimeSpan.Zero) Thread.Sleep(ReplyDelay);
        foreach (var reply in _amp.Handle(packet)) _replies.Add(reply);
    }

    public byte[]? Read(TimeSpan timeout)
    {
        if (_unplugged) throw new DeviceDisconnectedException("The simulated amp was unplugged.");
        ObjectDisposedException.ThrowIf(_disposed, this);
        try
        {
            if (_replies.TryTake(out var packet, timeout)) return packet;
        }
        catch (ObjectDisposedException)
        {
        }
        if (_unplugged) throw new DeviceDisconnectedException("The simulated amp was unplugged.");
        return null;
    }

    internal void Unplug()
    {
        _unplugged = true;
        _replies.CompleteAdding();
    }

    private void OnUnsolicited(IReadOnlyList<byte[]> packets)
    {
        if (_unplugged || _disposed) return;
        foreach (var p in packets) _replies.Add(p);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _amp.Unsolicited -= OnUnsolicited;
        if (!_replies.IsAddingCompleted) _replies.CompleteAdding();
    }
}

/// <summary>Finds the simulated amp and lets tests plug and unplug it.</summary>
public sealed class FakeDeviceLocator : IDeviceLocator
{
    private FakeHidTransport? _current;

    public FakeDeviceLocator(FakeMustangAmp? amp = null, bool pluggedIn = true)
    {
        Amp = amp ?? FakeMustangAmp.CreateMustangIIv2();
        IsPluggedIn = pluggedIn;
    }

    public FakeMustangAmp Amp { get; }

    public bool IsPluggedIn { get; private set; }

    public int OpenCount { get; private set; }

    public TimeSpan ReplyDelay { get; set; } = TimeSpan.Zero;

    public IReadOnlyList<DeviceDescriptor> FindMustangs() => IsPluggedIn
        ? [new DeviceDescriptor("fake://mustang", DeviceInfo.FenderVendorId, Amp.Device.ProductId, "Mustang (simulated)")]
        : [];

    public IHidTransport Open(DeviceDescriptor device)
    {
        if (!IsPluggedIn) throw new DeviceDisconnectedException("The simulated amp is unplugged.");
        OpenCount++;
        _current = new FakeHidTransport(Amp) { ReplyDelay = ReplyDelay };
        return _current;
    }

    public void Unplug()
    {
        IsPluggedIn = false;
        _current?.Unplug();
    }

    public void Plug() => IsPluggedIn = true;
}
