using System.Text;

namespace DifFuse.Core.Device;

public enum PacketDirection
{
    ToAmp,
    FromAmp,
    Note,
}

public sealed record PacketLogEntry(DateTime Time, PacketDirection Direction, string Text);

/// <summary>Keeps the most recent packets for troubleshooting and can mirror them to a file.</summary>
public sealed class PacketLog : IDisposable
{
    private readonly object _lock = new();
    private readonly LinkedList<PacketLogEntry> _entries = new();
    private readonly int _capacity;
    private StreamWriter? _file;

    public PacketLog(int capacity = 4000, string? filePath = null)
    {
        _capacity = capacity;
        if (filePath is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            _file = new StreamWriter(filePath, append: true, Encoding.UTF8) { AutoFlush = true };
            FilePath = filePath;
        }
    }

    public string? FilePath { get; }

    public void Sent(ReadOnlySpan<byte> packet) => Add(PacketDirection.ToAmp, Format(packet));

    public void Received(ReadOnlySpan<byte> packet) => Add(PacketDirection.FromAmp, Format(packet));

    public void Note(string text) => Add(PacketDirection.Note, text);

    public IReadOnlyList<PacketLogEntry> Snapshot()
    {
        lock (_lock) return _entries.ToList();
    }

    private void Add(PacketDirection direction, string text)
    {
        var entry = new PacketLogEntry(DateTime.Now, direction, text);
        lock (_lock)
        {
            _entries.AddLast(entry);
            while (_entries.Count > _capacity) _entries.RemoveFirst();
            _file?.WriteLine(FormatLine(entry));
        }
    }

    public static string FormatLine(PacketLogEntry e)
    {
        string arrow = e.Direction switch
        {
            PacketDirection.ToAmp => "->",
            PacketDirection.FromAmp => "<-",
            _ => "##",
        };
        return $"{e.Time:HH:mm:ss.fff} {arrow} {e.Text}";
    }

    /// <summary>Header and payload as spaced hex, trailing zeros trimmed.</summary>
    public static string Format(ReadOnlySpan<byte> packet)
    {
        int end = packet.Length;
        while (end > 16 && packet[end - 1] == 0) end--;
        var sb = new StringBuilder(end * 3);
        for (int i = 0; i < end; i++)
        {
            if (i == 16) sb.Append("| ");
            sb.Append(packet[i].ToString("x2")).Append(' ');
        }
        return sb.ToString().TrimEnd();
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _file?.Dispose();
            _file = null;
        }
    }
}
