using DifFuse.Core.Models;
using DifFuse.Hid;

namespace DifFuse.Core.Tests;

public class WindowsHidTests
{
    [Fact]
    public void Enumerating_hid_devices_finds_only_known_mustangs()
    {
        var found = new WindowsHidLocator().FindMustangs();
        Assert.All(found, d =>
        {
            Assert.Equal(DeviceInfo.FenderVendorId, d.VendorId);
            Assert.NotNull(DeviceInfo.FromProductId(d.ProductId));
            Assert.Contains("vid_1ed8", d.Path, StringComparison.OrdinalIgnoreCase);
        });
    }
}
