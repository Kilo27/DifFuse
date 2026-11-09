using MustangRack.Core.Device;
using MustangRack.Core.Fake;
using MustangRack.Core.Models;
using MustangRack.Core.Protocol;
using MustangRack.Core.Transport;

namespace MustangRack.Core.Tests;

public sealed class MustangClientTests : IDisposable
{
    internal static readonly ClientOptions FastOptions = new()
    {
        AckTimeout = TimeSpan.FromMilliseconds(200),
        FirstReplyTimeout = TimeSpan.FromMilliseconds(500),
        ReplyIdleTimeout = TimeSpan.FromMilliseconds(60),
        LiveUpdateInterval = TimeSpan.FromMilliseconds(30),
    };

    private readonly FakeDeviceLocator _locator = new();
    private readonly IHidTransport _transport;
    private readonly MustangClient _client;

    public MustangClientTests()
    {
        _transport = _locator.Open(_locator.FindMustangs()[0]);
        _client = new MustangClient(_transport, _locator.Amp.Device, FastOptions);
    }

    public void Dispose()
    {
        _client.Dispose();
        _transport.Dispose();
    }

    private FakeMustangAmp Amp => _locator.Amp;

    private IEnumerable<Packet> Received(Func<Packet, bool> where) => Amp.Received.Select(b => new Packet(b)).Where(where);

    private static bool IsAmp(Packet p) => p.Type == PacketType.Data && p.Dsp == Dsp.Amp;

    private static bool IsEffect(Packet p, EffectFamily family) => p.Type == PacketType.Data && p.Dsp == family.ToDsp();

    private static bool IsClear(Packet p, EffectFamily family) => IsEffect(p, family) && p.EffectModel == 0;

    [Fact]
    public async Task Start_reads_names_and_the_current_preset()
    {
        var initial = await _client.StartAsync();

        Assert.Equal(24, initial.PresetNames.Count);
        Assert.Equal(Amp.Presets.Select(p => p.Name), initial.PresetNames);
        Assert.Equal(0, initial.CurrentSlot);
        Assert.Equal(Amp.Live.Amp, initial.Current.Amp);
        Assert.Equal(Amp.Live.Name, initial.Current.Name);

        var first = Amp.Received.Take(3).Select(b => new Packet(b)).ToList();
        Assert.Equal(PacketType.Init0, first[0].Type);
        Assert.Equal(PacketStage.Init1, first[1].Stage);
        Assert.Equal(PacketStage.Unknown, first[2].Stage);
    }

    [Fact]
    public async Task Load_slot_switches_the_amp_and_returns_the_preset()
    {
        await _client.StartAsync();
        var preset = await _client.LoadSlotAsync(5);

        Assert.Equal(5, Amp.CurrentSlot);
        Assert.Equal(Amp.Presets[5].Name, preset.Name);
        Assert.Equal(Amp.Presets[5].Amp, preset.Amp);
        Assert.Equal(preset, _client.LiveState);
    }

    [Fact]
    public async Task Load_slot_rejects_slots_the_amp_does_not_have()
    {
        await _client.StartAsync();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _client.LoadSlotAsync(24));
        await _client.LoadSlotAsync(23); // the client keeps working
    }

    [Fact]
    public async Task Knob_moves_are_coalesced_and_the_last_value_wins()
    {
        await _client.StartAsync();
        Amp.ClearReceived();

        var amp = _client.LiveState.Amp;
        for (int i = 0; i < 50; i++)
        {
            _client.QueueAmp(amp with { Gain = (byte)i });
        }
        await _client.FlushAsync();

        Assert.Equal(49, Amp.Live.Amp.Gain);
        int sent = Received(IsAmp).Count();
        Assert.InRange(sent, 1, 10);
        Assert.Empty(Received(p => p.Dsp == Dsp.UsbGain));
    }

    [Fact]
    public async Task Usb_gain_change_sends_only_the_usb_gain_packet()
    {
        await _client.StartAsync();
        Amp.ClearReceived();

        _client.QueueAmp(_client.LiveState.Amp with { UsbGain = 10 });
        await _client.FlushAsync();

        Assert.Equal(10, Amp.Live.Amp.UsbGain);
        Assert.Empty(Received(IsAmp));
        Assert.Single(Received(p => p.Dsp == Dsp.UsbGain));
    }

    [Fact]
    public async Task Effect_knob_change_resends_the_effect_without_clearing_it()
    {
        await _client.StartAsync();
        var reverb = _client.LiveState.Reverb;
        Assert.True(reverb.IsActive);
        Amp.ClearReceived();

        _client.QueueEffect(reverb with { Knobs = reverb.Knobs.With(1, 7) });
        await _client.FlushAsync();

        Assert.Equal(7, Amp.Live.Reverb.Knobs[1]);
        Assert.Empty(Received(p => IsClear(p, EffectFamily.Reverb)));
        Assert.Single(Received(p => IsEffect(p, EffectFamily.Reverb)));
    }

    [Fact]
    public async Task Effect_model_change_clears_then_sets()
    {
        await _client.StartAsync();
        var reverb = _client.LiveState.Reverb;
        Amp.ClearReceived();

        _client.QueueEffect(reverb.WithModel(0x4d)); // Arena
        await _client.FlushAsync();

        var packets = Received(p => IsEffect(p, EffectFamily.Reverb)).ToList();
        Assert.Equal(2, packets.Count);
        Assert.Equal(0, packets[0].EffectModel);
        Assert.Equal(0x4d, packets[1].EffectModel);
        Assert.Equal(0x4d, Amp.Live.Reverb.ModelId);
    }

    [Fact]
    public async Task Bypassing_an_effect_clears_it_on_the_amp_and_turning_it_on_restores_it()
    {
        await _client.StartAsync();
        var reverb = _client.LiveState.Reverb;

        _client.QueueEffect(reverb with { Enabled = false });
        await _client.FlushAsync();
        Assert.True(Amp.Live.Reverb.IsEmpty);

        _client.QueueEffect(reverb);
        await _client.FlushAsync();
        Assert.Equal(reverb.ModelId, Amp.Live.Reverb.ModelId);
        Assert.Equal(reverb.Knobs, Amp.Live.Reverb.Knobs);
    }

    [Fact]
    public async Task Moving_an_effect_after_the_amp_sends_the_new_position()
    {
        await _client.StartAsync();
        var live = _client.LiveState;
        var stomp = live.Stomp.IsActive ? live.Stomp : live.Stomp.WithModel(0x3c);
        var moved = live.WithEffect(stomp).WithEffectPlacement(EffectFamily.Stomp, postAmp: true).Stomp;

        _client.QueueEffect(moved);
        await _client.FlushAsync();

        Assert.True(Amp.Live.Stomp.IsPostAmp);
        Assert.Equal(moved.Slot, Amp.Live.Stomp.Slot);
    }

    [Fact]
    public async Task Apply_preset_sends_every_unit()
    {
        await _client.StartAsync();
        var target = Amp.Presets[11] with { Name = "From library" };

        await _client.ApplyPresetAsync(target);

        Assert.Equal(target.Amp, Amp.Live.Amp);
        foreach (var family in EffectFamilies.All)
        {
            Assert.Equal(target.Effect(family).ModelId, Amp.Live.Effect(family).ModelId);
        }
        Assert.Equal(0, Amp.SaveCount);
    }

    [Fact]
    public async Task Save_writes_the_live_sound_to_the_slot()
    {
        await _client.StartAsync();
        _client.QueueAmp(_client.LiveState.Amp with { Treble = 33 });

        var saved = await _client.SaveToSlotAsync(20, "My Save");

        Assert.Equal(1, Amp.SaveCount);
        Assert.Equal("My Save", Amp.Presets[20].Name);
        Assert.Equal(33, Amp.Presets[20].Amp.Treble);
        Assert.Equal("My Save", saved.Name);
        Assert.Equal(20, Amp.CurrentSlot);
    }

    [Fact]
    public async Task Preset_change_on_the_amp_is_reported()
    {
        await _client.StartAsync();
        var changed = new TaskCompletionSource<AmpPresetChangedEventArgs>();
        _client.AmpPresetChanged += (_, e) => changed.TrySetResult(e);

        Amp.SelectPresetOnAmp(7);
        var e = await changed.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(7, e.Slot);
        Assert.Equal(Amp.Presets[7].Name, e.Preset.Name);
        Assert.Equal(Amp.Presets[7].Amp, _client.LiveState.Amp);
    }

    [Fact]
    public async Task Unplugging_faults_the_client_and_fails_later_operations()
    {
        await _client.StartAsync();
        _locator.Unplug();

        await Assert.ThrowsAnyAsync<IOException>(() => _client.Completion.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.IsType<DeviceDisconnectedException>(_client.Fault);
        await Assert.ThrowsAnyAsync<IOException>(() => _client.LoadSlotAsync(1));
    }

    [Fact]
    public void Preset_block_is_found_by_shape_when_a_stray_packet_shifts_it()
    {
        var block = FakeMustangAmp.EncodePreset(Amp.Presets[3], 3).Select(b => new Packet(b)).ToList();
        var packets = new List<Packet> { new() };
        packets.AddRange(block);

        Assert.Equal(1, MustangClient.FindPresetBlockStart(packets, 0));
        Assert.Equal(-1, MustangClient.FindPresetBlockStart(block.Take(5).ToList(), 0));
    }
}
