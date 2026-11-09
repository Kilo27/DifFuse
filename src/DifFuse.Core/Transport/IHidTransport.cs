namespace DifFuse.Core.Transport;

/// <summary>A channel that moves 64-byte packets to and from one amp.</summary>
public interface IHidTransport : IDisposable
{
    /// <summary>Sends one 64-byte packet.</summary>
    /// <exception cref="DeviceDisconnectedException">The amp is gone.</exception>
    void Write(ReadOnlySpan<byte> packet);

    /// <summary>Waits for the next 64-byte packet from the amp.</summary>
    /// <returns>The packet, or null when nothing arrived within <paramref name="timeout"/>.</returns>
    /// <exception cref="DeviceDisconnectedException">The amp is gone.</exception>
    byte[]? Read(TimeSpan timeout);
}

/// <summary>A Mustang found on the system.</summary>
public sealed record DeviceDescriptor(string Path, ushort VendorId, ushort ProductId, string? ProductName);

/// <summary>Finds Mustang amps and opens a transport to one.</summary>
public interface IDeviceLocator
{
    IReadOnlyList<DeviceDescriptor> FindMustangs();

    IHidTransport Open(DeviceDescriptor device);
}

public sealed class DeviceDisconnectedException : IOException
{
    public DeviceDisconnectedException(string message) : base(message) { }

    public DeviceDisconnectedException(string message, Exception inner) : base(message, inner) { }
}
