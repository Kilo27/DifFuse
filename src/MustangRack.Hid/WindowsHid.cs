using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using MustangRack.Core.Models;
using MustangRack.Core.Protocol;
using MustangRack.Core.Transport;

namespace MustangRack.Hid;

/// <summary>Finds Mustangs through SetupAPI and talks to them through the Windows HID class driver.</summary>
public sealed class WindowsHidLocator : IDeviceLocator
{
    public IReadOnlyList<DeviceDescriptor> FindMustangs()
    {
        var found = new List<DeviceDescriptor>();
        HidNative.HidD_GetHidGuid(out var hidGuid);
        var set = HidNative.SetupDiGetClassDevsW(ref hidGuid, IntPtr.Zero, IntPtr.Zero,
            HidNative.DIGCF_PRESENT | HidNative.DIGCF_DEVICEINTERFACE);
        if (set == HidNative.InvalidHandleValue) return found;

        try
        {
            for (uint index = 0; ; index++)
            {
                var data = new HidNative.SP_DEVICE_INTERFACE_DATA
                {
                    cbSize = Marshal.SizeOf<HidNative.SP_DEVICE_INTERFACE_DATA>(),
                };
                if (!HidNative.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hidGuid, index, ref data)) break;

                var path = GetDevicePath(set, ref data);
                if (path is null || !path.Contains("vid_1ed8", StringComparison.OrdinalIgnoreCase)) continue;

                var descriptor = Describe(path);
                if (descriptor is not null) found.Add(descriptor);
            }
        }
        finally
        {
            HidNative.SetupDiDestroyDeviceInfoList(set);
        }

        return found;
    }

    public IHidTransport Open(DeviceDescriptor device) => new WindowsHidTransport(device.Path);

    private static DeviceDescriptor? Describe(string path)
    {
        // Access 0 lets us read attributes even while another program has the device open.
        using var handle = HidNative.CreateFileW(path, 0, HidNative.FILE_SHARE_READ | HidNative.FILE_SHARE_WRITE,
            IntPtr.Zero, HidNative.OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle.IsInvalid) return null;

        var attributes = new HidNative.HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HidNative.HIDD_ATTRIBUTES>() };
        if (!HidNative.HidD_GetAttributes(handle, ref attributes)) return null;
        if (attributes.VendorID != DeviceInfo.FenderVendorId || DeviceInfo.FromProductId(attributes.ProductID) is null)
        {
            return null;
        }

        string? product = null;
        var buffer = new byte[256];
        if (HidNative.HidD_GetProductString(handle, buffer, buffer.Length))
        {
            product = System.Text.Encoding.Unicode.GetString(buffer).Split('\0')[0];
        }

        return new DeviceDescriptor(path, attributes.VendorID, attributes.ProductID, product);
    }

    private static string? GetDevicePath(IntPtr set, ref HidNative.SP_DEVICE_INTERFACE_DATA data)
    {
        HidNative.SetupDiGetDeviceInterfaceDetailW(set, ref data, IntPtr.Zero, 0, out uint required, IntPtr.Zero);
        if (required == 0) return null;

        IntPtr detail = Marshal.AllocHGlobal((int)required);
        try
        {
            // cbSize of SP_DEVICE_INTERFACE_DETAIL_DATA_W: DWORD + WCHAR[1], padded.
            Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
            if (!HidNative.SetupDiGetDeviceInterfaceDetailW(set, ref data, detail, required, out _, IntPtr.Zero))
            {
                return null;
            }
            return Marshal.PtrToStringUni(detail + 4);
        }
        finally
        {
            Marshal.FreeHGlobal(detail);
        }
    }
}

/// <summary>
/// An open HID connection. Reports are 65 bytes on Windows: report id 0, then the 64-byte packet.
/// A background thread keeps reading so the HID driver's input buffer never overflows.
/// </summary>
public sealed class WindowsHidTransport : IHidTransport
{
    private static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(2);

    private readonly SafeFileHandle _handle;
    private readonly FileStream _stream;
    private readonly int _inputLength;
    private readonly int _outputLength;
    private readonly BlockingCollection<byte[]> _incoming = new(new ConcurrentQueue<byte[]>());
    private readonly CancellationTokenSource _stop = new();
    private readonly Thread _reader;
    private readonly object _writeLock = new();
    private volatile Exception? _failure;
    private volatile bool _disposed;

    public WindowsHidTransport(string path)
    {
        _handle = HidNative.CreateFileW(path, HidNative.GENERIC_READ | HidNative.GENERIC_WRITE,
            HidNative.FILE_SHARE_READ | HidNative.FILE_SHARE_WRITE, IntPtr.Zero, HidNative.OPEN_EXISTING,
            HidNative.FILE_FLAG_OVERLAPPED, IntPtr.Zero);
        if (_handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            throw new IOException($"Could not open the amp: {new Win32Exception(error).Message}");
        }

        (_inputLength, _outputLength) = GetReportLengths(_handle);
        HidNative.HidD_SetNumInputBuffers(_handle, 256);

        _stream = new FileStream(_handle, FileAccess.ReadWrite, bufferSize: 0, isAsync: true);
        _reader = new Thread(ReadLoop) { IsBackground = true, Name = "Mustang HID reader" };
        _reader.Start();
    }

    public void Write(ReadOnlySpan<byte> packet)
    {
        ThrowIfFailed();
        var report = new byte[_outputLength];
        int offset = _outputLength > Packet.Size ? 1 : 0;
        packet[..Math.Min(packet.Length, _outputLength - offset)].CopyTo(report.AsSpan(offset));

        lock (_writeLock)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                timeout.CancelAfter(WriteTimeout);
                _stream.WriteAsync(report, timeout.Token).AsTask().GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) when (!_disposed)
            {
                throw new DeviceDisconnectedException("The amp did not accept data in time.");
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException && ex is not DeviceDisconnectedException)
            {
                throw new DeviceDisconnectedException("The amp was disconnected.", ex);
            }
        }
    }

    public byte[]? Read(TimeSpan timeout)
    {
        if (_incoming.TryTake(out var packet, timeout)) return packet;
        if (_incoming.IsCompleted) ThrowIfFailed(always: true);
        return null;
    }

    private void ReadLoop()
    {
        var buffer = new byte[_inputLength];
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                int read = _stream.ReadAsync(buffer, _stop.Token).AsTask().GetAwaiter().GetResult();
                if (read <= 0) throw new DeviceDisconnectedException("The amp closed the connection.");

                int offset = _inputLength > Packet.Size ? 1 : 0;
                var packet = new byte[Packet.Size];
                Array.Copy(buffer, offset, packet, 0, Math.Clamp(read - offset, 0, Packet.Size));
                _incoming.Add(packet);
            }
        }
        catch (Exception ex)
        {
            if (!_disposed)
            {
                _failure = ex as DeviceDisconnectedException ?? new DeviceDisconnectedException("The amp was disconnected.", ex);
            }
        }
        finally
        {
            _incoming.CompleteAdding();
        }
    }

    private void ThrowIfFailed(bool always = false)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(WindowsHidTransport));
        var failure = _failure;
        if (failure is not null) throw new DeviceDisconnectedException(failure.Message, failure);
        if (always) throw new DeviceDisconnectedException("The amp was disconnected.");
    }

    private static (int Input, int Output) GetReportLengths(SafeFileHandle handle)
    {
        int input = Packet.Size + 1, output = Packet.Size + 1;
        if (HidNative.HidD_GetPreparsedData(handle, out var preparsed))
        {
            try
            {
                if (HidNative.HidP_GetCaps(preparsed, out var caps) == HidNative.HIDP_STATUS_SUCCESS)
                {
                    if (caps.InputReportByteLength > 0) input = caps.InputReportByteLength;
                    if (caps.OutputReportByteLength > 0) output = caps.OutputReportByteLength;
                }
            }
            finally
            {
                HidNative.HidD_FreePreparsedData(preparsed);
            }
        }
        return (input, output);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stop.Cancel();
        try
        {
            HidNative.CancelIoEx(_handle, IntPtr.Zero);
        }
        catch (ObjectDisposedException)
        {
        }
        _reader.Join(TimeSpan.FromSeconds(2));
        _stream.Dispose();
        _handle.Dispose();
        _stop.Dispose();
    }
}

internal static class HidNative
{
    public const uint DIGCF_PRESENT = 0x02;
    public const uint DIGCF_DEVICEINTERFACE = 0x10;
    public const uint GENERIC_READ = 0x80000000;
    public const uint GENERIC_WRITE = 0x40000000;
    public const uint FILE_SHARE_READ = 0x01;
    public const uint FILE_SHARE_WRITE = 0x02;
    public const uint OPEN_EXISTING = 3;
    public const uint FILE_FLAG_OVERLAPPED = 0x40000000;
    public const int HIDP_STATUS_SUCCESS = 0x00110000;
    public static readonly IntPtr InvalidHandleValue = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    public struct SP_DEVICE_INTERFACE_DATA
    {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDD_ATTRIBUTES
    {
        public int Size;
        public ushort VendorID;
        public ushort ProductID;
        public ushort VersionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct HIDP_CAPS
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        public fixed ushort Reserved[17];
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }

    [DllImport("hid.dll")]
    public static extern void HidD_GetHidGuid(out Guid hidGuid);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool HidD_GetAttributes(SafeFileHandle device, ref HIDD_ATTRIBUTES attributes);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool HidD_GetProductString(SafeFileHandle device, byte[] buffer, int bufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool HidD_GetPreparsedData(SafeFileHandle device, out IntPtr preparsedData);

    [DllImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool HidD_SetNumInputBuffers(SafeFileHandle device, int numberBuffers);

    [DllImport("hid.dll")]
    public static extern int HidP_GetCaps(IntPtr preparsedData, out HIDP_CAPS capabilities);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr SetupDiGetClassDevsW(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData,
        ref Guid interfaceClassGuid, uint memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr deviceInfoSet,
        ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, IntPtr deviceInterfaceDetailData,
        uint deviceInterfaceDetailDataSize, out uint requiredSize, IntPtr deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode,
        IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CancelIoEx(SafeFileHandle file, IntPtr overlapped);
}
