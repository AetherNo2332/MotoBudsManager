using Windows.ApplicationModel.Resources;

namespace MotoBudsManager.Services;

public static class LocalizationService
{
    private static readonly ResourceLoader Loader = ResourceLoader.GetForViewIndependentUse();

    public static string Get(string key)
    {
        var value = Loader.GetString(key);
        return string.IsNullOrEmpty(value) ? key : value;
    }
}
