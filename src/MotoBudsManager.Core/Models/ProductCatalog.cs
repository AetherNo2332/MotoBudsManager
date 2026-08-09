namespace MotoBudsManager.Core.Models;

public sealed record ProductPresentation(
    string ProductName,
    string ColorName,
    string ImageFileName,
    bool IsKnown,
    bool SoundByBose);

public static class ProductCatalog
{
    private static readonly IReadOnlyDictionary<string, byte> MaximumKnownColor =
        new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase)
        {
            ["XT2441-1"] = 1,
            ["XT2443-1"] = 6,
            ["XT2545-1"] = 2,
            ["XT2641-1"] = 2,
            ["XT2643-1"] = 2,
        };

    public static bool IsKnownModel(string modelId) => MaximumKnownColor.ContainsKey(modelId);

    public static bool SupportsSoundByBose(string modelId) => modelId.ToUpperInvariant() is
        "XT2441-1" or "XT2545-1" or "XT2641-1";

    public static ProductPresentation Get(string modelId, byte colorId)
    {
        var known = MaximumKnownColor.TryGetValue(modelId, out var maximumColor) && colorId <= maximumColor;
        var productName = modelId.ToUpperInvariant() switch
        {
            "XT2441-1" => "Moto Buds Plus",
            "XT2443-1" when colorId <= 3 => "Moto Buds",
            "XT2443-1" when colorId <= 6 => "Moto Buds Bass",
            "XT2545-1" => "Moto Buds Loop / Clip",
            "XT2641-1" => "Moto Buds 2 Plus",
            "XT2643-1" => "Moto Buds 2",
            _ => "Moto Buds",
        };
        var colorName = (modelId.ToUpperInvariant(), colorId) switch
        {
            ("XT2641-1", 0) => "Silhouette",
            ("XT2641-1", 1) => "Bright White",
            ("XT2641-1", 2) => "Majesty",
            ("XT2643-1", 0) => "Carbon",
            ("XT2643-1", 1) => "Gray Mist",
            ("XT2643-1", 2) => "Violet Ice",
            _ => $"Color {colorId}",
        };
        var imageFileName = known
            ? $"{modelId.ToLowerInvariant()}-color{colorId}.webp"
            : "default-tws.webp";

        return new ProductPresentation(
            productName,
            colorName,
            imageFileName,
            known,
            SupportsSoundByBose(modelId));
    }
}
