namespace DifFuse.Core.Models;

/// <summary>One amp model the Mustang DSP can emulate.</summary>
/// <param name="Id">Model byte sent in the amp packet and stored in FUSE files.</param>
/// <param name="Specific">Model-specific byte the amp expects in controls 12, 13, 14 and 18.</param>
/// <param name="Specific2">Model-specific byte the amp expects in control 22.</param>
/// <param name="Unknown8And11">Value of controls 8 and 11 (0x80 for every model but the '65 Deluxe Reverb).</param>
public sealed record AmpModel(
    byte Id,
    string Name,
    bool IsV2,
    byte Specific,
    byte Specific2,
    byte Unknown8And11,
    byte? DefaultCabinet,
    byte DefaultNoiseGate);

public static class AmpCatalog
{
    public static IReadOnlyList<AmpModel> All { get; } =
    [
        new(0x67, "Fender '57 Deluxe", false, 0x01, 0x53, 0x80, Cabinets.Deluxe57, 0),
        new(0x64, "Fender '59 Bassman", false, 0x02, 0x67, 0x80, Cabinets.Bassman, 0),
        new(0x7c, "Fender '57 Champ", false, 0x0c, 0x00, 0x80, Cabinets.Champ, 0),
        new(0x53, "Fender '65 Deluxe Reverb", false, 0x03, 0x6a, 0x00, Cabinets.Deluxe65, 0),
        new(0x6a, "Fender '65 Princeton", false, 0x04, 0x61, 0x80, Cabinets.Princeton65, 0),
        new(0x75, "Fender '65 Twin Reverb", false, 0x05, 0x72, 0x80, Cabinets.Twin65, 0),
        new(0x72, "Fender Super-Sonic", false, 0x06, 0x79, 0x80, Cabinets.SuperSonic112, 2),
        new(0x61, "British '60s", false, 0x07, 0x5e, 0x80, Cabinets.Classic2x12, 0),
        new(0x79, "British '70s", false, 0x0b, 0x7c, 0x80, Cabinets.G4x12, 1),
        new(0x5e, "British '80s", false, 0x09, 0x5d, 0x80, Cabinets.M4x12, 1),
        new(0x5d, "American '90s", false, 0x0a, 0x6d, 0x80, Cabinets.V4x12, 3),
        new(0x6d, "Metal 2000", false, 0x08, 0x75, 0x80, Cabinets.G4x12, 2),
        // Mustang v2 models
        new(0xf1, "Studio Preamp", true, 0x0d, 0xf6, 0x80, Cabinets.G4x12, 0),
        new(0xf6, "Fender '57 Twin", true, 0x0e, 0xf9, 0x80, null, 0),
        new(0xf9, "Fender '60s Thrift", true, 0x0f, 0xfc, 0x80, null, 0),
        new(0xfc, "British Colour", true, 0x10, 0xff, 0x80, null, 0),
        new(0xff, "British Watts", true, 0x11, 0x00, 0x80, null, 0),
    ];

    private static readonly Dictionary<byte, AmpModel> ById = All.ToDictionary(a => a.Id);

    public static AmpModel? Find(byte id) => ById.GetValueOrDefault(id);

    public static string NameOf(byte id) => Find(id)?.Name ?? $"Unknown amp (0x{id:X2})";
}

public static class Cabinets
{
    public const byte Off = 0x00;
    public const byte Deluxe57 = 0x01;
    public const byte Bassman = 0x02;
    public const byte Deluxe65 = 0x03;
    public const byte Princeton65 = 0x04;
    public const byte Champ = 0x05;
    public const byte M4x12 = 0x06;
    public const byte Classic2x12 = 0x07;
    public const byte G4x12 = 0x08;
    public const byte Twin65 = 0x09;
    public const byte V4x12 = 0x0a;
    public const byte SuperSonic212 = 0x0b;
    public const byte SuperSonic112 = 0x0c;

    public static IReadOnlyList<string> Names { get; } =
    [
        "Off", "'57 Deluxe", "Bassman", "'65 Deluxe", "'65 Princeton", "Champ",
        "4x12 M", "2x12 C", "4x12 G", "'65 Twin", "4x12 V", "SS 2x12", "SS 1x12",
    ];

    public static string NameOf(byte id) => id < Names.Count ? Names[id] : $"Unknown (0x{id:X2})";
}

public static class NoiseGates
{
    public const byte Custom = 5;

    public static IReadOnlyList<string> Names { get; } = ["Off", "Low", "Mid", "High", "Super", "Custom"];
}

public static class SagLevels
{
    public static IReadOnlyList<string> Names { get; } = ["Soft", "Normal", "Hard"];
}
