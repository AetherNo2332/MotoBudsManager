using MotoBudsManager.Core.Models;

namespace MotoBudsManager.Tests;

public sealed class ModelCatalogTests
{
    [Fact]
    public void ManufacturerData_Parse_KnownPayload()
    {
        var payload = ProtocolCodecTests.Hex("1A 29 01 02 11 AA BB CC DD EE FF");

        var parsed = ManufacturerDataParser.TryParse(payload, out var model, out var color,
            out var capability, out var address, out var transport);

        Assert.True(parsed);
        Assert.Equal("XT2641-1", model);
        Assert.Equal(2, color);
        Assert.Equal(0x11, capability);
        Assert.Equal(0xAABBCCDDEEFFul, address);
        Assert.Equal(TransportKind.BleGatt, transport);
    }

    [Fact]
    public void ModelUuid_Parse_KnownXt2443Uuid()
    {
        var parsed = ModelCatalog.TryParseModelUuid(
            Guid.Parse("182b0103-4899-11ee-be56-0242ac120002"), out var model, out var color);

        Assert.True(parsed);
        Assert.Equal("XT2443-1", model);
        Assert.Equal(3, color);
    }

    [Theory]
    [InlineData("fc9d9fe0-4899-11ee-be56-0242ac120002")]
    [InlineData("00009fe0-4899-11ee-be56-0242ac120002")]
    public void ModelUuid_Parse_ControlServiceUuid_IsRejected(string uuid)
    {
        var parsed = ModelCatalog.TryParseModelUuid(Guid.Parse(uuid), out var model, out _);

        Assert.False(parsed);
        Assert.Empty(model);
    }

    [Theory]
    [InlineData("XT2443-1", true)]
    [InlineData("XT2641-1", true)]
    [InlineData("XT00-159", false)]
    [InlineData("Unknown", false)]
    public void ProductCatalog_RecognizesOnlySupportedModels(string model, bool expected)
    {
        Assert.Equal(expected, ProductCatalog.IsKnownModel(model));
    }

    [Fact]
    public void FeatureCatalog_RejectsAncForLoopModel()
    {
        Assert.False(ModelCatalog.Supports("XT2545-1", ExperimentalFeature.Anc));
        Assert.Throws<NotSupportedException>(() =>
            ModelCatalog.CreateCommand("XT2545-1", ExperimentalFeature.Anc, 1));
    }

    [Theory]
    [InlineData("XT2443-1", TransportKind.Rfcomm, true)]
    [InlineData("XT2443-1", TransportKind.BleGatt, false)]
    [InlineData("XT2641-1", TransportKind.Rfcomm, false)]
    public void Xt2443ControlTarget_RequiresVerifiedModelAndTransport(
        string model,
        TransportKind transport,
        bool expected)
    {
        var device = new MotoBudsDevice("test", "buds", 0, model, 0, 0x10,
            transport, EvidenceLevel.StaticAnalysis);

        Assert.Equal(expected, device.IsXt2443ControlTarget);
    }

    [Theory]
    [InlineData(3, "Moto Buds", "xt2443-1-color3.webp")]
    [InlineData(4, "Moto Buds Bass", "xt2443-1-color4.webp")]
    public void ProductCatalog_DistinguishesXt2443Variants(byte color, string name, string image)
    {
        var product = ProductCatalog.Get("XT2443-1", color);

        Assert.True(product.IsKnown);
        Assert.Equal(name, product.ProductName);
        Assert.Equal(image, product.ImageFileName);
    }

    [Fact]
    public void ProductCatalog_UsesFallbackForUnknownCombination()
    {
        var product = ProductCatalog.Get("Unknown", 9);

        Assert.False(product.IsKnown);
        Assert.Equal("default-tws.webp", product.ImageFileName);
    }

    [Theory]
    [InlineData("XT2441-1", true)]
    [InlineData("XT2545-1", true)]
    [InlineData("XT2641-1", true)]
    [InlineData("XT2443-1", false)]
    [InlineData("XT2643-1", false)]
    [InlineData("Unknown", false)]
    public void ProductCatalog_UsesDocumentedSoundByBoseWhitelist(string model, bool expected)
    {
        Assert.Equal(expected, ProductCatalog.SupportsSoundByBose(model));
        Assert.Equal(expected, ProductCatalog.Get(model, 0).SoundByBose);
    }

    [Theory]
    [InlineData("XT2641-1", BoseLogoDisplayMode.Off, false)]
    [InlineData("XT2641-1", BoseLogoDisplayMode.ByModel, true)]
    [InlineData("XT2443-1", BoseLogoDisplayMode.ByModel, false)]
    [InlineData("XT2443-1", BoseLogoDisplayMode.Always, true)]
    [InlineData(null, BoseLogoDisplayMode.Always, false)]
    public void ProductCatalog_AppliesBoseLogoDisplayMode(
        string? model,
        BoseLogoDisplayMode mode,
        bool expected) =>
        Assert.Equal(expected, ProductCatalog.ShouldShowSoundByBose(model, mode));

    [Theory]
    [InlineData(0, "00 00")]
    [InlineData(1, "01 00")]
    [InlineData(2, "01 01")]
    public void SpatialAudio_MapsKnownPayloads(int value, string expected)
    {
        var command = ModelCatalog.CreateCommand("XT2641-1", ExperimentalFeature.SpatialAudio, value);

        Assert.Equal((ushort)0x030B, command.Opcode);
        Assert.Equal(ProtocolCodecTests.Hex(expected), command.Payload);
    }

    [Theory]
    [InlineData("XT2545-1", ExperimentalFeature.AutomaticVolume, true)]
    [InlineData("XT2545-1", ExperimentalFeature.Anc, false)]
    [InlineData("XT2641-1", ExperimentalFeature.SpatialAudio, true)]
    [InlineData("XT2641-1", ExperimentalFeature.VolumeBoost, false)]
    [InlineData("XT2643-1", ExperimentalFeature.VolumeBoost, true)]
    [InlineData("XT2441-1", ExperimentalFeature.AdaptiveHearing, false)]
    [InlineData("XT2443-1", ExperimentalFeature.AdaptiveHearing, true)]
    [InlineData("XT2545-1", ExperimentalFeature.FitTest, false)]
    [InlineData("XT2545-1", ExperimentalFeature.DualConnection, true)]
    public void FeatureCatalog_UsesDocumentedModelWhitelist(
        string model,
        ExperimentalFeature feature,
        bool expected) =>
        Assert.Equal(expected, ModelCatalog.Supports(model, feature));
}
