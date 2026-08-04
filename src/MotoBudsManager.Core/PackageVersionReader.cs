using System.Xml.Linq;

namespace MotoBudsManager.Core;

public static class PackageVersionReader
{
    public static string GetDisplayText(string productName, string manifestPath)
    {
        try
        {
            var document = XDocument.Load(manifestPath);
            var root = document.Root;
            var identity = root?.Element(root.Name.Namespace + "Identity");
            var rawVersion = identity?.Attribute("Version")?.Value;
            if (Version.TryParse(rawVersion, out var version))
            {
                return $"{productName} {version.Major}.{version.Minor}.{version.Build}";
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (System.Xml.XmlException)
        {
        }

        return productName;
    }
}
