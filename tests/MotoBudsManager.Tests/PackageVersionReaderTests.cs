using MotoBudsManager.Core;

namespace MotoBudsManager.Tests;

public sealed class PackageVersionReaderTests
{
    [Fact]
    public void GetDisplayText_ReadsThreePartVersionFromManifest()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, """
                <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">
                  <Identity Name="MotoBudsManager" Publisher="CN=Test" Version="1.4.3.0" />
                </Package>
                """);

            var result = PackageVersionReader.GetDisplayText("Moto Buds Manager", path);

            Assert.Equal("Moto Buds Manager 1.4.3", result);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void GetDisplayText_MissingManifest_ReturnsProductName()
    {
        var result = PackageVersionReader.GetDisplayText(
            "Moto Buds Manager",
            Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.xml"));

        Assert.Equal("Moto Buds Manager", result);
    }
}
