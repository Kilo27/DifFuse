namespace MustangRack.Core.Models;

public enum MustangGeneration
{
    V1,
    V2,
}

/// <summary>A Mustang model, identified by its USB product id.</summary>
/// <param name="PresetCount">Number of preset slots, or 0 when it has to be worked out from the amp's reply.</param>
public sealed record DeviceInfo(ushort ProductId, string Name, MustangGeneration Generation, int PresetCount)
{
    public const ushort FenderVendorId = 0x1ed8;

    public bool SupportsV2Models => Generation == MustangGeneration.V2;

    public string DisplayName => Generation == MustangGeneration.V2 ? $"{Name} (v2)" : Name;

    public static IReadOnlyList<DeviceInfo> Known { get; } =
    [
        new(0x0004, "Mustang I/II", MustangGeneration.V1, 24),
        new(0x0005, "Mustang III/IV/V", MustangGeneration.V1, 100),
        new(0x000a, "Mustang Bronco", MustangGeneration.V1, 0),
        new(0x0010, "Mustang Mini", MustangGeneration.V1, 0),
        new(0x0012, "Mustang Floor", MustangGeneration.V1, 0),
        new(0x0014, "Mustang I/II", MustangGeneration.V2, 24),
        new(0x0016, "Mustang III/IV/V", MustangGeneration.V2, 100),
    ];

    public static DeviceInfo? FromProductId(ushort productId) =>
        Known.FirstOrDefault(d => d.ProductId == productId);
}
