using DifFuse.Core.Models;
using DifFuse.Core.Transport;

namespace DifFuse.Core.Device;

public enum ConnectionState
{
    Stopped,
    Searching,
    Connecting,
    Connected,
}

public sealed record ConnectionOptions
{
    /// <summary>How often to look for the amp while it is not connected.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Pause after a failed connection attempt.</summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Longest time the init handshake and preset download may take.</summary>
    public TimeSpan StartTimeout { get; init; } = TimeSpan.FromSeconds(15);

    public ClientOptions Client { get; init; } = new();
}

public sealed class ConnectedEventArgs(MustangClient client, InitialState initial, DeviceDescriptor descriptor) : EventArgs
{
    public MustangClient Client { get; } = client;
    public InitialState Initial { get; } = initial;
    public DeviceDescriptor Descriptor { get; } = descriptor;
}

/// <summary>
/// Finds the amp, connects, and reconnects on its own after an unplug. Events are raised on a
/// background thread.
/// </summary>
public sealed class MustangConnection : IDisposable
{
    private readonly IDeviceLocator _locator;
    private readonly ConnectionOptions _options;
    private readonly PacketLog? _log;
    private readonly CancellationTokenSource _stop = new();
    private readonly object _lock = new();
    private TaskCompletionSource _dropRequest = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? _loop;

    public MustangConnection(IDeviceLocator locator, ConnectionOptions? options = null, PacketLog? log = null)
    {
        _locator = locator;
        _options = options ?? new ConnectionOptions();
        _log = log;
    }

    public ConnectionState State { get; private set; } = ConnectionState.Stopped;

    public DeviceInfo? Device { get; private set; }

    public MustangClient? Client { get; private set; }

    /// <summary>Why the last attempt failed or the last connection ended, if it did.</summary>
    public string? LastError { get; private set; }

    public event EventHandler? StateChanged;

    public event EventHandler<ConnectedEventArgs>? Connected;

    public event EventHandler? Disconnected;

    public event EventHandler<AmpPresetChangedEventArgs>? AmpPresetChanged;

    public void Start()
    {
        lock (_lock)
        {
            _loop ??= Task.Run(() => RunAsync(_stop.Token));
        }
    }

    /// <summary>Drops the current connection; the loop then looks for the amp again.</summary>
    public void Reconnect()
    {
        lock (_lock) _dropRequest.TrySetResult();
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            SetState(ConnectionState.Searching, null);
            DeviceDescriptor? descriptor = null;
            try
            {
                descriptor = _locator.FindMustangs().FirstOrDefault();
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }

            if (descriptor is null)
            {
                await Delay(_options.PollInterval, ct);
                continue;
            }

            var device = DeviceInfo.FromProductId(descriptor.ProductId)
                ?? new DeviceInfo(descriptor.ProductId, descriptor.ProductName ?? "Mustang", MustangGeneration.V1, 0);
            SetState(ConnectionState.Connecting, device);

            IHidTransport? transport = null;
            MustangClient? client = null;
            bool wasConnected = false;
            try
            {
                lock (_lock)
                {
                    if (_dropRequest.Task.IsCompleted) _dropRequest = new(TaskCreationOptions.RunContinuationsAsynchronously);
                }
                _log?.Note($"Connecting to {device.DisplayName} ({descriptor.Path})");
                transport = _locator.Open(descriptor);
                client = new MustangClient(transport, device, _options.Client, _log);
                var initial = await client.StartAsync().WaitAsync(_options.StartTimeout, ct);

                client.AmpPresetChanged += ForwardPresetChanged;
                Client = client;
                LastError = null;
                wasConnected = true;
                SetState(ConnectionState.Connected, device);
                Connected?.Invoke(this, new ConnectedEventArgs(client, initial, descriptor));

                Task drop;
                lock (_lock) drop = _dropRequest.Task;
                await Task.WhenAny(client.Completion, drop, Task.Delay(Timeout.Infinite, ct));
                if (client.Fault is not null) LastError = client.Fault.Message;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
            }
            catch (TimeoutException)
            {
                LastError = "The amp did not answer in time.";
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }
            finally
            {
                if (client is not null) client.AmpPresetChanged -= ForwardPresetChanged;
                Client = null;
                client?.Dispose();
                transport?.Dispose();
                if (wasConnected)
                {
                    _log?.Note("Disconnected.");
                    SetState(ConnectionState.Searching, null);
                    Disconnected?.Invoke(this, EventArgs.Empty);
                }
            }

            await Delay(wasConnected ? _options.PollInterval : _options.RetryDelay, ct);
        }
        SetState(ConnectionState.Stopped, null);
    }

    private void ForwardPresetChanged(object? sender, AmpPresetChangedEventArgs e) => AmpPresetChanged?.Invoke(this, e);

    private void SetState(ConnectionState state, DeviceInfo? device)
    {
        if (State == state && Device == device) return;
        State = state;
        Device = device;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static async Task Delay(TimeSpan delay, CancellationToken ct)
    {
        try
        {
            await Task.Delay(delay, ct);
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        try
        {
            _loop?.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
        }
        _stop.Dispose();
    }
}
