using System.Text;

namespace DifFuse.Core.Protocol;

/// <summary>Values of header byte 0.</summary>
public static class PacketStage
{
    public const byte Init0 = 0x00;
    public const byte Init1 = 0x1a;
    public const byte Ready = 0x1c;
    public const byte Unknown = 0xff;
}

/// <summary>Values of header byte 1.</summary>
public static class PacketType
{
    public const byte Operation = 0x01;
    public const byte Data = 0x03;
    public const byte Init0 = 0xc3;
    public const byte Init1 = 0xc1;
    public const byte Load = 0xc1;
}

/// <summary>Values of header byte 2: which DSP (or operation) a packet addresses.</summary>
public static class Dsp
{
    public const byte None = 0x00;
    public const byte SelectMemoryBank = 0x01;
    public const byte Save = 0x03;
    public const byte SaveEffectName = 0x04;
    public const byte Amp = 0x05;
    public const byte Stomp = 0x06;
    public const byte Modulation = 0x07;
    public const byte Delay = 0x08;
    public const byte Reverb = 0x09;
    public const byte UsbGain = 0x0d;

    public static bool IsEffect(byte dsp) => dsp is >= Stomp and <= Reverb;
}

/// <summary>
/// One 64-byte Mustang packet: a 16-byte header followed by a 48-byte payload.
/// Controls ("ControlIndex" in FUSE files) live at payload offset 16 + index,
/// which is packet offset 32 + index, for both amp and effect packets.
/// </summary>
public sealed class Packet
{
    public const int Size = 64;
    public const int PayloadOffset = 16;
    public const int ControlOffset = PayloadOffset + 16;
    public const int NameLength = 32;

    private readonly byte[] _bytes;

    public Packet()
    {
        _bytes = new byte[Size];
    }

    public Packet(ReadOnlySpan<byte> data)
    {
        _bytes = new byte[Size];
        data[..Math.Min(data.Length, Size)].CopyTo(_bytes);
    }

    public static Packet Create(byte stage, byte type, byte dsp)
    {
        var p = new Packet { Stage = stage, Type = type, Dsp = dsp };
        return p;
    }

    public byte this[int index]
    {
        get => _bytes[index];
        set => _bytes[index] = value;
    }

    public ReadOnlySpan<byte> Span => _bytes;

    public byte[] ToArray() => (byte[])_bytes.Clone();

    // ---- Header ----

    public byte Stage { get => _bytes[0]; set => _bytes[0] = value; }
    public byte Type { get => _bytes[1]; set => _bytes[1] = value; }
    public byte Dsp { get => _bytes[2]; set => _bytes[2] = value; }

    /// <summary>Header byte 4: the preset slot for save/load operations.</summary>
    public byte Slot { get => _bytes[4]; set => _bytes[4] = value; }

    /// <summary>Header bytes 3, 6 and 7, whose meaning is not fully known.</summary>
    public void SetHeaderExtras(byte b3, byte b6, byte b7)
    {
        _bytes[3] = b3;
        _bytes[6] = b6;
        _bytes[7] = b7;
    }

    // ---- Payload: controls ----

    public byte GetControl(int controlIndex) => _bytes[ControlOffset + controlIndex];

    public void SetControl(int controlIndex, byte value) => _bytes[ControlOffset + controlIndex] = value;

    // ---- Payload: name ----

    /// <summary>Preset name, up to 32 bytes at the start of the payload.</summary>
    public string Name
    {
        get
        {
            var raw = _bytes.AsSpan(PayloadOffset, NameLength);
            int end = raw.IndexOf((byte)0);
            if (end < 0) end = raw.Length;
            return Encoding.Latin1.GetString(raw[..end]).TrimEnd();
        }
        set
        {
            var raw = _bytes.AsSpan(PayloadOffset, NameLength);
            raw.Clear();
            var encoded = Encoding.Latin1.GetBytes(SanitizeName(value));
            encoded.AsSpan(0, Math.Min(encoded.Length, NameLength)).CopyTo(raw);
        }
    }

    /// <summary>Replaces characters the amp cannot store (non-Latin-1, control characters).</summary>
    public static string SanitizeName(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (char c in name)
        {
            sb.Append(c is >= ' ' and <= '~' or >= ' ' and <= 'ÿ' ? c : '_');
        }
        var s = sb.ToString();
        return s.Length > NameLength ? s[..NameLength] : s;
    }

    // ---- Payload: amp ----

    public byte AmpModel { get => _bytes[PayloadOffset]; set => _bytes[PayloadOffset] = value; }

    /// <summary>USB gain packets carry the value in the first payload byte.</summary>
    public byte UsbGain { get => _bytes[PayloadOffset]; set => _bytes[PayloadOffset] = value; }

    // ---- Payload: effect ----

    public ushort EffectModel
    {
        get => (ushort)(_bytes[PayloadOffset] | (_bytes[PayloadOffset + 1] << 8));
        set
        {
            _bytes[PayloadOffset] = (byte)(value & 0xff);
            _bytes[PayloadOffset + 1] = (byte)(value >> 8);
        }
    }

    /// <summary>Signal-chain position of the effect, 0-7. 4 and up are after the amp.</summary>
    public byte EffectSlot { get => _bytes[PayloadOffset + 2]; set => _bytes[PayloadOffset + 2] = value; }

    /// <summary>Effect payload bytes 3-5, whose meaning is not fully known.</summary>
    public void SetEffectExtras(byte a, byte b, byte c)
    {
        _bytes[PayloadOffset + 3] = a;
        _bytes[PayloadOffset + 4] = b;
        _bytes[PayloadOffset + 5] = c;
    }

    public override string ToString() => Convert.ToHexString(_bytes);
}
