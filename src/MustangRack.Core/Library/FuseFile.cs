using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using MustangRack.Core.Models;
using MustangRack.Core.Protocol;

namespace MustangRack.Core.Library;

/// <summary>The result of reading a FUSE .fuse file.</summary>
/// <param name="Preset">The sound, or null when the file holds no amp settings (FUSE's AmpData files).</param>
public sealed record FuseDocument(string Name, string Author, Preset? Preset, IReadOnlyList<string> Warnings)
{
    public bool HasSound => Preset is not null;
}

/// <summary>
/// Reads and writes FUSE preset files:
/// <c>&lt;Preset&gt;&lt;Amplifier&gt;&lt;Module ID POS BypassState&gt;&lt;Param ControlIndex&gt;</c>, then
/// <c>&lt;FX&gt;</c>, <c>&lt;FUSE&gt;&lt;Info name author/&gt;</c> and <c>&lt;UsbGain&gt;</c>.
/// Continuous controls are stored as 16-bit values (the 8-bit value in both bytes).
/// </summary>
public static class FuseFile
{
    // Amp controls stored as 16-bit values; the rest are plain small integers.
    private static readonly HashSet<int> WideAmpControls = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 22];

    public static FuseDocument Load(string path)
    {
        using var stream = File.OpenRead(path);
        var doc = Parse(stream);
        if (string.IsNullOrWhiteSpace(doc.Name))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            doc = doc with { Name = name, Preset = doc.Preset is null ? null : doc.Preset with { Name = name } };
        }
        return doc;
    }

    public static FuseDocument Parse(Stream stream)
    {
        XDocument xml;
        try
        {
            xml = XDocument.Load(stream, LoadOptions.None);
        }
        catch (XmlException ex)
        {
            throw new InvalidDataException($"Not a valid FUSE file: {ex.Message}", ex);
        }

        var root = xml.Root ?? throw new InvalidDataException("Empty FUSE file.");
        var warnings = new List<string>();

        var info = root.Name.LocalName == "FUSE" ? root.Element("Info") : root.Element("FUSE")?.Element("Info");
        string name = (string?)info?.Attribute("name") ?? "";
        string author = (string?)info?.Attribute("author") ?? "";

        var ampModule = root.Element("Amplifier")?.Element("Module");
        if (ampModule is null)
        {
            warnings.Add("The file has no amp settings (FUSE keeps only names and notes in AmpData files).");
            return new FuseDocument(name, author, null, warnings);
        }

        var amp = ParseAmp(ampModule, warnings);
        if (root.Element("UsbGain") is { } usb && TryInt(usb.Value, out int usbGain))
        {
            amp = amp with { UsbGain = ClampByte(usbGain) };
        }

        var preset = new Preset { Name = name, Amp = amp };
        var fx = root.Element("FX");
        if (fx is not null)
        {
            foreach (var family in EffectFamilies.All)
            {
                var container = fx.Element(family.FuseElementName());
                var module = container?.Element("Module");
                if (module is not null)
                {
                    var effect = ParseEffect(module, family, warnings);
                    preset = preset.WithEffect(effect);
                }
            }

            // Older files may list modules directly under <FX>.
            foreach (var module in fx.Elements("Module"))
            {
                var model = EffectCatalog.FindByFileId((ushort)AttrInt(module, "ID"));
                if (model is null) continue;
                preset = preset.WithEffect(ParseEffect(module, model.Family, warnings));
            }
        }

        return new FuseDocument(name, author, NormalizeSlots(preset), warnings);
    }

    private static AmpSettings ParseAmp(XElement module, List<string> warnings)
    {
        int id = AttrInt(module, "ID");
        if (id is < 0 or > 255 || AmpCatalog.Find((byte)id) is null)
        {
            warnings.Add($"Unknown amp model {id}; using the '65 Deluxe Reverb.");
            id = 0x53;
        }

        var values = new Dictionary<int, int>();
        foreach (var param in module.Elements("Param"))
        {
            if (TryInt((string?)param.Attribute("ControlIndex"), out int index) && TryInt(param.Value, out int value))
            {
                values[index] = value;
            }
        }

        byte Wide(int index, byte fallback) => values.TryGetValue(index, out int v) ? FromWide(v) : fallback;
        byte Small(int index, byte fallback, byte max) =>
            values.TryGetValue(index, out int v) ? Math.Min(FromSmall(v, max), max) : fallback;

        var defaults = new AmpSettings().WithModel((byte)id);
        return defaults with
        {
            Volume = Wide(0, defaults.Volume),
            Gain = Wide(1, defaults.Gain),
            Gain2 = Wide(2, defaults.Gain2),
            MasterVolume = Wide(3, defaults.MasterVolume),
            Treble = Wide(4, defaults.Treble),
            Middle = Wide(5, defaults.Middle),
            Bass = Wide(6, defaults.Bass),
            Presence = Wide(7, defaults.Presence),
            Depth = Wide(9, defaults.Depth),
            Bias = Wide(10, defaults.Bias),
            NoiseGate = Small(15, defaults.NoiseGate, NoiseGates.Custom),
            Threshold = Small(16, defaults.Threshold, 9),
            Cabinet = Small(17, defaults.Cabinet, Cabinets.SuperSonic112),
            Sag = Small(19, defaults.Sag, 2),
            Brightness = values.TryGetValue(20, out int b) && b != 0,
        };
    }

    private static EffectSettings ParseEffect(XElement module, EffectFamily family, List<string> warnings)
    {
        int id = AttrInt(module, "ID");
        int pos = Math.Clamp(AttrInt(module, "POS", (int)family), 0, 7);
        var bypassAttr = (string?)module.Attribute("BypassState");
        bool enabled = bypassAttr is null || bypassAttr.Trim() != "0";

        if (id == 0)
        {
            return EffectSettings.Empty(family) with { Slot = (byte)pos };
        }

        var model = EffectCatalog.FindByFileId((ushort)id);
        if (model is null)
        {
            warnings.Add($"Unknown {family.DisplayName().ToLowerInvariant()} effect {id}; left empty.");
            return EffectSettings.Empty(family) with { Slot = (byte)pos };
        }
        if (model.Family != family)
        {
            warnings.Add($"{model.Name} was filed under {family.FuseElementName()}; moved to {model.Family.FuseElementName()}.");
        }

        var knobs = new byte[KnobValues.Count];
        foreach (var param in module.Elements("Param"))
        {
            if (!TryInt((string?)param.Attribute("ControlIndex"), out int index) || index is < 0 or >= KnobValues.Count) continue;
            if (!TryInt(param.Value, out int value)) continue;
            byte max = index < model.Knobs.Count ? model.Knobs[index].Max : (byte)255;
            knobs[index] = max < 255 ? Math.Min(FromSmall(value, max), max) : FromWide(value);
        }

        return new EffectSettings
        {
            Family = model.Family,
            ModelId = model.Id,
            Knobs = KnobValues.FromSpan(knobs),
            Slot = (byte)pos,
            Enabled = enabled,
        };
    }

    /// <summary>Gives every effect a distinct signal-chain position, keeping each one before or after the amp.</summary>
    public static Preset NormalizeSlots(Preset preset)
    {
        var used = new HashSet<int>();
        var result = preset;
        foreach (var effect in preset.Effects.OrderBy(e => e.Slot).ThenBy(e => e.Family))
        {
            int slot = effect.Slot;
            if (used.Contains(slot))
            {
                int baseSlot = slot >= 4 ? 4 : 0;
                slot = Enumerable.Range(baseSlot, 4).Concat(Enumerable.Range(0, 8)).First(s => !used.Contains(s));
            }
            used.Add(slot);
            if (slot != effect.Slot) result = result.WithEffect(effect with { Slot = (byte)slot });
        }
        return result;
    }

    // ---- Writing ----

    public static void Save(string path, Preset preset, string author = "")
    {
        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            NewLineChars = "\r\n",
        };
        var temp = path + ".tmp";
        using (var writer = XmlWriter.Create(temp, settings))
        {
            ToXml(preset, author).Save(writer);
        }
        File.Move(temp, path, overwrite: true);
    }

    public static XDocument ToXml(Preset preset, string author = "")
    {
        var amp = preset.Amp;
        var model = amp.Model;
        int specific = model?.Specific ?? 0;
        int specific2 = model?.Specific2 ?? 0;
        int unknown8And11 = model?.Unknown8And11 ?? 0x80;

        static XElement Param(int index, int value) =>
            new("Param", new XAttribute("ControlIndex", index), value.ToString(CultureInfo.InvariantCulture));
        static int Wide(int value) => (value << 8) | value;

        var ampModule = new XElement("Module",
            new XAttribute("ID", amp.ModelId),
            new XAttribute("POS", 0),
            new XAttribute("BypassState", 1),
            Param(0, Wide(amp.Volume)),
            Param(1, Wide(amp.Gain)),
            Param(2, Wide(amp.Gain2)),
            Param(3, Wide(amp.MasterVolume)),
            Param(4, Wide(amp.Treble)),
            Param(5, Wide(amp.Middle)),
            Param(6, Wide(amp.Bass)),
            Param(7, Wide(amp.Presence)),
            Param(8, Wide(unknown8And11)),
            Param(9, Wide(amp.Depth)),
            Param(10, Wide(amp.Bias)),
            Param(11, Wide(unknown8And11)),
            Param(12, specific),
            Param(13, specific),
            Param(14, specific),
            Param(15, amp.NoiseGate),
            Param(16, amp.Threshold),
            Param(17, amp.Cabinet),
            Param(18, specific),
            Param(19, amp.Sag),
            Param(20, amp.Brightness ? 1 : 0),
            Param(21, 1),
            Param(22, Wide(specific2)));

        var fx = new XElement("FX");
        foreach (var family in EffectFamilies.All)
        {
            var effect = preset.Effect(family);
            var effectModel = effect.Model;
            var module = new XElement("Module",
                new XAttribute("ID", effect.ModelId == 0 ? 0 : effectModel?.FileId ?? effect.ModelId),
                new XAttribute("POS", effect.Slot),
                new XAttribute("BypassState", effect.Enabled || effect.ModelId == 0 ? 1 : 0));
            if (effectModel is not null)
            {
                int count = effectModel.Knobs.Count;
                // Like FUSE and PLUG: five knobs unless the model has one or six.
                int written = count == 1 ? 1 : count == 6 ? 6 : 5;
                for (int i = 0; i < written; i++) module.Add(Param(i, Wide(effect.Knobs[i])));
            }
            fx.Add(new XElement(family.FuseElementName(), new XAttribute("ID", (int)family + 1), module));
        }

        var fuse = new XElement("FUSE",
            new XElement("Info",
                new XAttribute("name", preset.Name),
                new XAttribute("author", author),
                new XAttribute("rating", 0),
                new XAttribute("genre1", -1),
                new XAttribute("genre2", -1),
                new XAttribute("genre3", -1),
                new XAttribute("tags", ""),
                new XAttribute("fenderid", 0)));

        return new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement("Preset",
                new XAttribute("amplifier", "Mustang I/II"),
                new XAttribute("ProductId", 1),
                new XElement("Amplifier", ampModule),
                fx,
                fuse,
                new XElement("UsbGain", amp.UsbGain)));
    }

    // ---- Helpers ----

    private static byte FromWide(int value) => ClampByte(value >> 8);

    /// <summary>Selector values: plain integers in FUSE, 16-bit in PLUG-written files.</summary>
    private static byte FromSmall(int value, byte max) => ClampByte(value > max ? value >> 8 : value);

    private static byte ClampByte(int value) => (byte)Math.Clamp(value, 0, 255);

    private static bool TryInt(string? text, out int value) =>
        int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    private static int AttrInt(XElement element, string name, int fallback = 0) =>
        TryInt((string?)element.Attribute(name), out int v) ? v : fallback;
}
