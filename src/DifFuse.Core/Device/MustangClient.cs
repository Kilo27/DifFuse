using System.Collections.Concurrent;
using System.Diagnostics;
using DifFuse.Core.Models;
using DifFuse.Core.Protocol;
using DifFuse.Core.Transport;

namespace DifFuse.Core.Device;

public sealed record ClientOptions
{
    /// <summary>How long to wait for the amp's reply to a single command.</summary>
    public TimeSpan AckTimeout { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>How long to wait for the first packet of a multi-packet reply.</summary>
    public TimeSpan FirstReplyTimeout { get; init; } = TimeSpan.FromMilliseconds(1500);

    /// <summary>A multi-packet reply is over once the amp has been quiet this long.</summary>
    public TimeSpan ReplyIdleTimeout { get; init; } = TimeSpan.FromMilliseconds(300);

    /// <summary>Minimum time between two live updates while knobs move.</summary>
    public TimeSpan LiveUpdateInterval { get; init; } = TimeSpan.FromMilliseconds(35);

    /// <summary>Clear an effect before resending it when only its knobs changed (PLUG always clears).</summary>
    public bool ClearBeforeEffectKnobChange { get; init; }
}

/// <summary>What the amp reports when the app connects.</summary>
public sealed record InitialState(IReadOnlyList<string> PresetNames, Preset Current, int CurrentSlot);

public sealed class AmpPresetChangedEventArgs(Preset preset, int slot) : EventArgs
{
    public Preset Preset { get; } = preset;
    public int Slot { get; } = slot;
}

/// <summary>
/// Talks to one connected amp. All USB traffic runs on one worker thread, so commands never
/// interleave. Knob moves are queued and coalesced, then sent at most every
/// <see cref="ClientOptions.LiveUpdateInterval"/>.
/// </summary>
public sealed class MustangClient : IDisposable
{
    private readonly IHidTransport _transport;
    private readonly ClientOptions _options;
    private readonly PacketLog? _log;
    private readonly Thread _worker;
    private readonly Thread _reader;
    private readonly BlockingCollection<object> _incoming = new(new ConcurrentQueue<object>());
    private readonly BlockingCollection<object> _wake = new(new ConcurrentQueue<object>());
    private readonly ConcurrentQueue<WorkItem> _work = new();
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _pendingLock = new();
    private readonly List<Packet> _unsolicited = new();
    private AmpSettings? _pendingAmp;
    private readonly EffectSettings?[] _pendingEffects = new EffectSettings?[4];
    private readonly List<TaskCompletionSource> _flushWaiters = new();
    private long _nextLiveSend;
    private bool _liveInFlight;
    private volatile bool _stopping;
    private volatile Exception? _fault;
    private Preset _live = Preset.Default;
    private int _liveSlot = -1;

    public MustangClient(IHidTransport transport, DeviceInfo device, ClientOptions? options = null, PacketLog? log = null)
    {
        _transport = transport;
        Device = device;
        _options = options ?? new ClientOptions();
        _log = log;
        _reader = new Thread(ReadLoop) { IsBackground = true, Name = "Mustang reader" };
        _worker = new Thread(WorkLoop) { IsBackground = true, Name = "Mustang worker" };
        _reader.Start();
        _worker.Start();
    }

    public DeviceInfo Device { get; }

    /// <summary>Completes when the client stops: after <see cref="Dispose"/> or when the amp goes away.</summary>
    public Task Completion => _completion.Task;

    public Exception? Fault => _fault;

    /// <summary>The sound the amp is believed to be playing.</summary>
    public Preset LiveState
    {
        get { lock (_pendingLock) return _live; }
    }

    /// <summary>Raised on the worker thread when the amp reports a preset change made on the amp itself.</summary>
    public event EventHandler<AmpPresetChangedEventArgs>? AmpPresetChanged;

    // ---- Public operations ----

    /// <summary>Runs the init handshake and reads all preset names and the current preset.</summary>
    public Task<InitialState> StartAsync() => Enqueue(() =>
    {
        DiscardIncoming(TimeSpan.FromMilliseconds(50));
        foreach (var packet in PacketCodec.Init()) SendAndAck(packet);
        DiscardIncoming(TimeSpan.FromMilliseconds(50));

        var reply = SendAndCollect(PacketCodec.LoadAll());
        var (names, current, slot) = DecodeLoadAll(reply);
        SetLive(current, slot);
        return new InitialState(names, current, slot);
    });

    /// <summary>Makes <paramref name="slot"/> the amp's active preset and returns it.</summary>
    public Task<Preset> LoadSlotAsync(int slot) => Enqueue(() =>
    {
        CheckSlot(slot);
        DropPendingLive();
        var reply = SendAndCollect(PacketCodec.LoadSlot((byte)slot));
        var preset = PacketCodec.DecodePreset(FindPresetBlock(reply, 0));
        SetLive(preset, slot);
        return preset;
    });

    /// <summary>
    /// Writes the amp's current sound into <paramref name="slot"/> with <paramref name="name"/>,
    /// replacing what was there. Callers must have the user's OK first.
    /// </summary>
    public Task<Preset> SaveToSlotAsync(int slot, string name) => Enqueue(() =>
    {
        CheckSlot(slot);
        SendPendingLive();
        SendAndAck(PacketCodec.SaveName((byte)slot, name));
        var reply = SendAndCollect(PacketCodec.LoadSlot((byte)slot));
        var preset = PacketCodec.DecodePreset(FindPresetBlock(reply, 0));
        SetLive(preset, slot);
        return preset;
    });

    /// <summary>Sends a whole preset to the amp's live sound (does not store it in a slot).</summary>
    public Task ApplyPresetAsync(Preset preset) => Enqueue<object?>(() =>
    {
        DropPendingLive();
        SendAmp(preset.Amp, force: true);
        foreach (var effect in preset.Effects) SendEffect(effect, force: true);
        lock (_pendingLock) _live = _live with { Name = preset.Name };
        return null;
    });

    /// <summary>Queues an amp-unit change. Only the newest queued value is sent.</summary>
    public void QueueAmp(AmpSettings amp)
    {
        lock (_pendingLock) _pendingAmp = amp;
        Wake();
    }

    /// <summary>Queues an effect-unit change. Only the newest queued value per unit is sent.</summary>
    public void QueueEffect(EffectSettings effect)
    {
        lock (_pendingLock) _pendingEffects[(int)effect.Family] = effect;
        Wake();
    }

    /// <summary>Completes once every queued live change has been sent.</summary>
    public Task FlushAsync()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_pendingLock)
        {
            if (_fault is not null) return Task.FromException(_fault);
            if (!HasUnsentLive()) return Task.CompletedTask;
            _flushWaiters.Add(tcs);
        }
        Wake();
        return tcs.Task;
    }

    // ---- Worker ----

    private sealed record WorkItem(Func<object?> Run, Action<object?> Complete, Action<Exception> Fail);

    private Task<T> Enqueue<T>(Func<T> run)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (_fault is not null) return Task.FromException<T>(_fault);
        if (_stopping) return Task.FromException<T>(new ObjectDisposedException(nameof(MustangClient)));
        _work.Enqueue(new WorkItem(() => run(), r => tcs.TrySetResult((T)r!), e => tcs.TrySetException(e)));
        Wake();
        return tcs.Task;
    }

    private void Wake()
    {
        if (!_wake.IsAddingCompleted)
        {
            try
            {
                _wake.Add(this);
            }
            catch (InvalidOperationException)
            {
            }
        }
    }

    private void WorkLoop()
    {
        var collections = new[] { _wake, _incoming };
        try
        {
            while (!_stopping)
            {
                if (_work.TryDequeue(out var item))
                {
                    RunItem(item);
                    continue;
                }

                int waitMs = 250;
                if (HasPendingLiveLocked())
                {
                    long now = Stopwatch.GetTimestamp();
                    if (now >= _nextLiveSend)
                    {
                        SendPendingLive();
                        _nextLiveSend = Stopwatch.GetTimestamp() + (long)(_options.LiveUpdateInterval.TotalSeconds * Stopwatch.Frequency);
                        continue;
                    }
                    waitMs = Math.Max(1, (int)((_nextLiveSend - now) * 1000 / Stopwatch.Frequency));
                }
                else
                {
                    CompleteFlushWaiters();
                }

                int index = BlockingCollection<object>.TryTakeFromAny(collections, out var obj, waitMs);
                if (index == 1 && obj is byte[] data)
                {
                    HandleUnsolicited(new Packet(data));
                }
            }
        }
        catch (Exception ex)
        {
            SetFault(ex);
        }
        finally
        {
            Shutdown();
        }
    }

    private void RunItem(WorkItem item)
    {
        try
        {
            item.Complete(item.Run());
        }
        catch (IOException ex)
        {
            item.Fail(ex);
            throw;
        }
        catch (Exception ex)
        {
            _log?.Note($"Operation failed: {ex.Message}");
            item.Fail(ex);
        }
    }

    private void ReadLoop()
    {
        try
        {
            while (!_stopping)
            {
                var data = _transport.Read(TimeSpan.FromMilliseconds(250));
                if (data is null) continue;
                _log?.Received(data);
                _incoming.Add(data);
            }
        }
        catch (Exception ex) when (!_stopping)
        {
            SetFault(ex);
            Wake();
        }
        catch
        {
            // Stopping.
        }
    }

    private void SetFault(Exception ex)
    {
        if (_fault is not null || _stopping) return;
        _fault = ex is DeviceDisconnectedException ? ex : new DeviceDisconnectedException(ex.Message, ex);
        _log?.Note($"Connection lost: {ex.Message}");
        _stopping = true;
    }

    private void Shutdown()
    {
        _stopping = true;
        var error = _fault ?? new ObjectDisposedException(nameof(MustangClient));
        while (_work.TryDequeue(out var item)) item.Fail(error);
        lock (_pendingLock)
        {
            foreach (var waiter in _flushWaiters) waiter.TrySetException(error);
            _flushWaiters.Clear();
        }
        if (_fault is not null) _completion.TrySetException(_fault);
        else _completion.TrySetResult();
    }

    // ---- Live updates ----

    private bool HasPendingLive() => _pendingAmp is not null || _pendingEffects.Any(e => e is not null);

    /// <summary>Queued changes plus changes taken from the queue whose packets are still going out.</summary>
    private bool HasUnsentLive() => HasPendingLive() || _liveInFlight;

    private bool HasPendingLiveLocked()
    {
        lock (_pendingLock) return HasPendingLive();
    }

    private void CompleteFlushWaiters()
    {
        lock (_pendingLock)
        {
            if (_flushWaiters.Count == 0 || HasUnsentLive()) return;
            foreach (var waiter in _flushWaiters) waiter.TrySetResult();
            _flushWaiters.Clear();
        }
    }

    private void DropPendingLive()
    {
        lock (_pendingLock)
        {
            _pendingAmp = null;
            Array.Clear(_pendingEffects);
        }
    }

    private void SendPendingLive()
    {
        AmpSettings? amp;
        EffectSettings?[] effects;
        lock (_pendingLock)
        {
            amp = _pendingAmp;
            effects = (EffectSettings?[])_pendingEffects.Clone();
            _pendingAmp = null;
            Array.Clear(_pendingEffects);
            _liveInFlight = true;
        }

        try
        {
            if (amp is not null) SendAmp(amp, force: false);
            foreach (var effect in effects)
            {
                if (effect is not null) SendEffect(effect, force: false);
            }
        }
        finally
        {
            lock (_pendingLock) _liveInFlight = false;
        }
        CompleteFlushWaiters();
    }

    private void SendAmp(AmpSettings amp, bool force)
    {
        var live = LiveState.Amp;
        if (force || !amp.SameAmpPacketAs(live))
        {
            SendAndAck(PacketCodec.Amp(amp));
            SendAndAck(PacketCodec.Apply());
        }
        if (force || amp.UsbGain != live.UsbGain)
        {
            SendAndAck(PacketCodec.UsbGain(amp.UsbGain));
            SendAndAck(PacketCodec.Apply());
        }
        lock (_pendingLock) _live = _live with { Amp = amp };
    }

    private void SendEffect(EffectSettings effect, bool force)
    {
        var live = LiveState.Effect(effect.Family);
        if (!effect.IsActive)
        {
            if (force || live.IsActive)
            {
                SendAndAck(PacketCodec.ClearEffect(effect.Family));
                SendAndAck(PacketCodec.Apply());
            }
        }
        else
        {
            bool sameUnit = live.IsActive && live.ModelId == effect.ModelId && live.Slot == effect.Slot;
            if (force || !sameUnit || _options.ClearBeforeEffectKnobChange)
            {
                SendAndAck(PacketCodec.ClearEffect(effect.Family));
                SendAndAck(PacketCodec.Apply());
                SendAndAck(PacketCodec.Effect(effect));
                SendAndAck(PacketCodec.Apply());
            }
            else if (live.Knobs != effect.Knobs)
            {
                SendAndAck(PacketCodec.Effect(effect));
                SendAndAck(PacketCodec.Apply());
            }
        }
        lock (_pendingLock) _live = _live.WithEffect(effect);
    }

    private void SetLive(Preset preset, int slot)
    {
        lock (_pendingLock)
        {
            _live = preset;
            _liveSlot = slot;
        }
    }

    // ---- Packet I/O (worker thread only) ----

    private void Write(Packet packet)
    {
        _log?.Sent(packet.Span);
        _transport.Write(packet.Span);
    }

    private Packet? Receive(TimeSpan timeout)
    {
        if (_incoming.TryTake(out var obj, timeout) && obj is byte[] data) return new Packet(data);
        if (_fault is not null) throw _fault;
        return null;
    }

    /// <summary>Sends a command and waits for its single reply. A missing reply is logged, not fatal.</summary>
    private Packet? SendAndAck(Packet packet)
    {
        DrainUnsolicited();
        Write(packet);
        var reply = Receive(_options.AckTimeout);
        if (reply is null) _log?.Note("No reply within the timeout.");
        return reply;
    }

    /// <summary>Sends a command and gathers every packet until the amp goes quiet.</summary>
    private List<Packet> SendAndCollect(Packet packet)
    {
        DrainUnsolicited();
        Write(packet);
        var packets = new List<Packet>();
        var next = Receive(_options.FirstReplyTimeout);
        while (next is not null)
        {
            packets.Add(next);
            next = Receive(_options.ReplyIdleTimeout);
        }
        _log?.Note($"Reply: {packets.Count} packets.");
        return packets;
    }

    private void DiscardIncoming(TimeSpan quietFor)
    {
        while (Receive(quietFor) is not null)
        {
        }
    }

    private void DrainUnsolicited()
    {
        while (_incoming.TryTake(out var obj) && obj is byte[] data)
        {
            HandleUnsolicited(new Packet(data));
        }
    }

    private (IReadOnlyList<string> Names, Preset Current, int Slot) DecodeLoadAll(List<Packet> reply)
    {
        int count = Device.PresetCount > 0 ? Device.PresetCount : (reply.Count > 143 ? 100 : 24);
        int expectedStart = count * 2;
        int start = FindPresetBlockStart(reply, expectedStart);
        if (start < 0) start = expectedStart;
        int offset = start - expectedStart;
        if (offset < 0) offset = 0;
        var aligned = reply.Skip(offset).ToList();
        var (names, current, slot) = PacketCodec.DecodeLoadAll(aligned, count);
        return (names, current, slot < count ? slot : 0);
    }

    private static List<Packet> FindPresetBlock(List<Packet> reply, int expectedStart)
    {
        int start = FindPresetBlockStart(reply, expectedStart);
        return reply.Skip(start < 0 ? expectedStart : start).ToList();
    }

    /// <summary>
    /// Finds the seven-packet preset block by its shape (amp DSP in the second packet, USB gain in the
    /// seventh), preferring the expected position. Returns -1 when no packet run has that shape.
    /// </summary>
    internal static int FindPresetBlockStart(IReadOnlyList<Packet> packets, int expectedStart)
    {
        bool Matches(int k) =>
            k >= 0 && k + PacketCodec.PresetPacketCount <= packets.Count &&
            packets[k + 1].Dsp == Dsp.Amp && packets[k + 6].Dsp == Dsp.UsbGain;

        if (Matches(expectedStart)) return expectedStart;
        int best = -1;
        for (int k = 0; k + PacketCodec.PresetPacketCount <= packets.Count; k++)
        {
            if (Matches(k) && (best < 0 || Math.Abs(k - expectedStart) < Math.Abs(best - expectedStart))) best = k;
        }
        return best;
    }

    /// <summary>
    /// Packets nobody asked for. A preset change on the amp's own controls arrives as a seven-packet
    /// preset block; anything else is only logged.
    /// </summary>
    private void HandleUnsolicited(Packet packet)
    {
        _unsolicited.Add(packet);
        if (_unsolicited.Count > 16) _unsolicited.RemoveAt(0);

        int start = _unsolicited.Count - PacketCodec.PresetPacketCount;
        if (packet.Dsp == Dsp.UsbGain && start >= 0)
        {
            var block = _unsolicited.Skip(start).ToList();
            if (block[1].Dsp == Dsp.Amp && block.Skip(2).Take(4).All(p => Dsp.IsEffect(p.Dsp)))
            {
                _unsolicited.Clear();
                var preset = PacketCodec.DecodePreset(block);
                int slot = block[0].Slot < Math.Max(Device.PresetCount, 1) ? block[0].Slot : _liveSlot;
                DropPendingLive();
                SetLive(preset, slot);
                _log?.Note($"Amp switched to preset {slot}: {preset.Name}");
                AmpPresetChanged?.Invoke(this, new AmpPresetChangedEventArgs(preset, slot));
            }
        }
    }

    private void CheckSlot(int slot)
    {
        int max = Device.PresetCount > 0 ? Device.PresetCount : 100;
        if (slot < 0 || slot >= max) throw new ArgumentOutOfRangeException(nameof(slot), $"Slot must be 0-{max - 1}.");
    }

    public void Dispose()
    {
        if (_stopping && _completion.Task.IsCompleted) return;
        _stopping = true;
        Wake();
        if (Thread.CurrentThread != _worker) _worker.Join(TimeSpan.FromSeconds(3));
        if (Thread.CurrentThread != _reader) _reader.Join(TimeSpan.FromSeconds(1));
        Shutdown();
    }
}
