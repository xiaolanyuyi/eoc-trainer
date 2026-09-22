using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace EocTrainer.Core;

/// <summary>
/// Minimal reader/writer for the game's <c>modsettings.lsx</c> files, which list
/// every mod the player has enabled for a profile.
/// </summary>
public static class ModSettings
{
    private static readonly XNamespace None = XNamespace.None;

    private static XElement? FindNode(XElement parent, string childName) =>
        parent.Elements("children").Elements("node")
            .FirstOrDefault(n => (string?)n.Attribute("id") == childName);

    private static string? AttributeOf(XElement node, string id) =>
        node.Elements("attribute")
            .FirstOrDefault(a => (string?)a.Attribute("id") == id)
            ?.Attribute("value")?.Value;

    private static XElement MakeAttribute(string id, string value, string type)
        => new XElement("attribute",
            new XAttribute("id", id),
            new XAttribute("value", value),
            new XAttribute("type", type));

    public static bool IsRegistered(string settingsPath)
    {
        try
        {
            var doc = Load(settingsPath);
            var root = doc?.Root?.Element("region")?.Element("node");
            var mods = root == null ? null : FindNode(root, "Mods");
            if (mods == null) return false;

            return mods.Elements("children").Elements("node")
                .Any(n => (string?)n.Attribute("id") == "ModuleShortDesc"
                          && (AttributeOf(n, "UUID") == Paths.ModUuid
                              || AttributeOf(n, "Folder") == Paths.ModFolder));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Adds the trainer to the profile's mod list. Returns false and sets <paramref name="error"/> on failure.</summary>
    public static bool Register(string settingsPath) => Register(settingsPath, out _);

    public static bool Register(string settingsPath, out string? error)
    {
        try
        {
            var doc = Load(settingsPath);
            if (doc?.Root?.Element("region")?.Element("node") is not { } root)
            {
                error = "文件结构不是预期的 modsettings.lsx";
                return false;
            }

            var mods = FindNode(root, "Mods");
            if (mods == null)
            {
                mods = new XElement("node", new XAttribute("id", "Mods"));
                root.Add(mods);
            }

            var modsChildren = mods.Element("children");
            if (modsChildren == null)
            {
                modsChildren = new XElement("children");
                mods.Add(modsChildren);
            }

            var order = FindNode(root, "ModOrder");
            if (order == null)
            {
                order = new XElement("node", new XAttribute("id", "ModOrder"));
                root.AddFirst(order);
            }

            var orderChildren = order.Element("children");
            if (orderChildren == null)
            {
                orderChildren = new XElement("children");
                order.Add(orderChildren);
            }

            var alreadyListed = modsChildren.Elements("node")
                .Any(n => (string?)n.Attribute("id") == "ModuleShortDesc"
                          && (AttributeOf(n, "UUID") == Paths.ModUuid
                              || AttributeOf(n, "Folder") == Paths.ModFolder));

            if (!alreadyListed)
            {
                modsChildren.Add(new XElement("node",
                    new XAttribute("id", "ModuleShortDesc"),
                    MakeAttribute("Folder", Paths.ModFolder, "30"),
                    MakeAttribute("MD5", "", "23"),
                    MakeAttribute("Name", Paths.ModName, "22"),
                    MakeAttribute("UUID", Paths.ModUuid, "22"),
                    MakeAttribute("Version", "268435456", "4")));
            }

            var orderContains = orderChildren.Elements("node")
                .Any(n => (string?)n.Attribute("id") == "Module"
                          && AttributeOf(n, "UUID") == Paths.ModUuid);

            if (!orderContains)
            {
                orderChildren.Add(new XElement("node",
                    new XAttribute("id", "Module"),
                    MakeAttribute("UUID", Paths.ModUuid, "22")));
            }

            Save(settingsPath, doc!);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Removes the trainer from the profile's mod list.</summary>
    public static bool Unregister(string settingsPath)
    {
        try
        {
            var doc = Load(settingsPath);
            var root = doc?.Root?.Element("region")?.Element("node");
            if (root == null) return false;

            var mods = FindNode(root, "Mods");
            mods?.Elements("children").Elements("node")
                .Where(n => (string?)n.Attribute("id") == "ModuleShortDesc"
                            && (AttributeOf(n, "UUID") == Paths.ModUuid
                                || AttributeOf(n, "Folder") == Paths.ModFolder))
                .Remove();

            var order = FindNode(root, "ModOrder");
            order?.Elements("children").Elements("node")
                .Where(n => (string?)n.Attribute("id") == "Module"
                            && AttributeOf(n, "UUID") == Paths.ModUuid)
                .Remove();

            Save(settingsPath, doc!);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static XDocument? Load(string path)
    {
        if (!File.Exists(path)) return null;
        // Whitespace is not preserved; the file is re-indented when it is written
        // back, which keeps the diff small and the file readable.
        return XDocument.Load(path);
    }

    private static void Save(string path, XDocument doc)
    {
        var hadBom = File.ReadAllBytes(path).Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF });
        var backup = path + ".eoctrainer.bak";
        if (!File.Exists(backup)) File.Copy(path, backup, false);

        var body = new StringBuilder();
        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "    ",
            OmitXmlDeclaration = true,
            NewLineChars = "\n",
            NewLineHandling = NewLineHandling.Replace,
        };

        using (var writer = XmlWriter.Create(body, settings))
        {
            doc.Save(writer);
        }

        // Larian writes the declaration with this exact casing; the document
        // itself is written without one, so it is prepended here.
        var text = "<?xml version=\"1.0\" encoding=\"UTF-8\" ?>\n" + body;
        File.WriteAllText(path, text, new UTF8Encoding(hadBom));
    }
}
