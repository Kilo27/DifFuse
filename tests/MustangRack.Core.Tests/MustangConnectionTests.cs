using System.Collections.Concurrent;
using MustangRack.Core.Device;
using MustangRack.Core.Fake;

namespace MustangRack.Core.Tests;

public class MustangConnectionTests
{
    private static readonly ConnectionOptions Fast = new()
    {
        PollInterval = TimeSpan.FromMilliseconds(50),
        RetryDelay = TimeSpan.FromMilliseconds(50),
        StartTimeout = TimeSpan.FromSeconds(5),
        Client = MustangClientTests.FastOptions,
    };

    private static async Task<T> Next<T>(BlockingCollection<T> events)
    {
        return await Task.Run(() =>
            events.TryTake(out var e, TimeSpan.FromSeconds(5)) ? e : throw new TimeoutException("No event within 5 s."));
    }

    [Fact]
    public async Task Connects_and_reports_the_amp()
    {
        var locator = new FakeDeviceLocator();
        using var connection = new MustangConnection(locator, Fast);
        var connected = new BlockingCollection<ConnectedEventArgs>();
        connection.Connected += (_, e) => connected.Add(e);

        connection.Start();
        var e = await Next(connected);

        Assert.Equal(ConnectionState.Connected, connection.State);
        Assert.Equal("Mustang I/II (v2)", connection.Device!.DisplayName);
        Assert.Equal(24, e.Initial.PresetNames.Count);
        Assert.Same(e.Client, connection.Client);
    }

    [Fact]
    public async Task Reconnects_by_itself_after_unplug_and_replug()
    {
        var locator = new FakeDeviceLocator();
        using var connection = new MustangConnection(locator, Fast);
        var connected = new BlockingCollection<ConnectedEventArgs>();
        var disconnected = new BlockingCollection<bool>();
        connection.Connected += (_, e) => connected.Add(e);
        connection.Disconnected += (_, _) => disconnected.Add(true);

        connection.Start();
        await Next(connected);

        locator.Unplug();
        await Next(disconnected);
        Assert.Null(connection.Client);
        Assert.NotEqual(ConnectionState.Connected, connection.State);

        await Task.Delay(200);
        Assert.Equal(1, locator.OpenCount); // nothing to open while unplugged

        locator.Plug();
        var again = await Next(connected);
        Assert.Equal(2, locator.OpenCount);
        Assert.Equal(ConnectionState.Connected, connection.State);
        Assert.Equal(24, again.Initial.PresetNames.Count);
    }

    [Fact]
    public async Task Waits_for_an_amp_that_is_not_plugged_in_yet()
    {
        var locator = new FakeDeviceLocator(pluggedIn: false);
        using var connection = new MustangConnection(locator, Fast);
        var connected = new BlockingCollection<ConnectedEventArgs>();
        connection.Connected += (_, e) => connected.Add(e);

        connection.Start();
        await Task.Delay(200);
        Assert.Equal(ConnectionState.Searching, connection.State);

        locator.Plug();
        await Next(connected);
        Assert.Equal(ConnectionState.Connected, connection.State);
    }

    [Fact]
    public async Task Reconnect_drops_and_reopens_the_connection()
    {
        var locator = new FakeDeviceLocator();
        using var connection = new MustangConnection(locator, Fast);
        var connected = new BlockingCollection<ConnectedEventArgs>();
        connection.Connected += (_, e) => connected.Add(e);

        connection.Start();
        await Next(connected);
        connection.Reconnect();
        await Next(connected);
        Assert.Equal(2, locator.OpenCount);
    }
}
