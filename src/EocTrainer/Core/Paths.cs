using System.Text.Json;

namespace EocTrainer.Core;

/// <summary>Well known locations used by the trainer.</summary>
public static class Paths
{
    public const string ModName = "EocTrainer";

    /// <summary>Fixed UUID of the mod; the folder the game loads is name + "_" + uuid.</summary>
    public const string ModUuid = "b7a1e0c7-1f2a-4b3c-9d4e-5f6a7b8c9d0e";

    public const string ModFolder = ModName + "_" + ModUuid;

    /// <summary>Relative path inside the bridge directory, must match the Lua side.</summary>
    public const string BridgeFolderName = "EocTrainer";

    public static string Documents
    {
        get
        {
            var path = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (string.IsNullOrWhiteSpace(path))
            {
                path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents");
            }
            return path;
        }
    }

    /// <summary>Directory the game uses for user data (saves, profiles, extender logs).</summary>
    public static string GameStorage =>
        Path.Combine(Documents, "Larian Studios", "Divinity Original Sin 2 Definitive Edition");

    /// <summary>Where the Script Extender redirects Ext.IO file access.</summary>
    public static string OsirisData => Path.Combine(GameStorage, "Osiris Data");

    /// <summary>
    /// Bridge directory. The extender resolves <c>Ext.IO</c> paths against
    /// <c>&lt;GameStorage&gt;\Osiris Data\</c>; if a future build ever puts it
    /// somewhere else the first existing candidate wins instead.
    /// </summary>
    public static string BridgeDir
    {
        get
        {
            _bridgeDir ??= ResolveBridgeDirectory();
            return _bridgeDir;
        }
    }

    private static string? _bridgeDir;

    /// <summary>Forgets the cached bridge directory (used by the tests / diagnostics).</summary>
    public static void ResetBridgeCache() => _bridgeDir = null;

    private static string ResolveBridgeDirectory()
    {
        var expected = Path.Combine(OsirisData, BridgeFolderName);

        var candidates = new List<string> { expected };
        try
        {
            if (Directory.Exists(GameStorage))
            {
                foreach (var directory in Directory.GetDirectories(GameStorage))
                {
                    candidates.Add(Path.Combine(directory, BridgeFolderName));
                    candidates.Add(Path.Combine(directory, "Osiris Data", BridgeFolderName));
                }
            }
        }
        catch
        {
            // Fall through to the default location.
        }

        foreach (var candidate in candidates)
        {
            if (File.Exists(Path.Combine(candidate, "state.json"))
                || File.Exists(Path.Combine(candidate, "log.txt"))
                || File.Exists(Path.Combine(candidate, "hello.txt")))
            {
                return candidate;
            }
        }

        return expected;
    }

    public static string BridgeDirExpected => Path.Combine(OsirisData, BridgeFolderName);

    public static string CommandFile => Path.Combine(BridgeDir, "command.json");
    public static string StateFile => Path.Combine(BridgeDir, "state.json");
    public static string BridgeLogFile => Path.Combine(BridgeDir, "log.txt");
    public static string BridgeHelloFile => Path.Combine(BridgeDir, "hello.txt");

    public static string PlayerProfiles => Path.Combine(GameStorage, "PlayerProfiles");

    public static string ExtenderCache =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DOS2ScriptExtender", "ScriptExtender");

    public static string AppData
    {
        get
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EocTrainer");
            Directory.CreateDirectory(path);
            return path;
        }
    }

    public static string SettingsFile => Path.Combine(AppData, "settings.json");

    /// <summary>Mods directory inside the game installation.</summary>
    public static string ModsDirectory(string gameDir) =>
        Path.Combine(gameDir, "DefEd", "Data", "Mods");

    /// <summary>Mods directory in the user's game data folder (same place the workshop paks use).</summary>
    public static string UserModsDirectory => Path.Combine(GameStorage, "Mods");

    public static string ModDirectory(string gameDir) =>
        Path.Combine(ModsDirectory(gameDir), ModFolder);

    public static string UserModDirectory =>
        Path.Combine(UserModsDirectory, ModFolder);

    /// <summary>The mod package the game actually loads.</summary>
    public static string UserModPak => Path.Combine(UserModsDirectory, ModFolder + ".pak");

    public static string ModPak(string gameDir) =>
        Path.Combine(ModsDirectory(gameDir), ModFolder + ".pak");

    /// <summary>Folder name the base campaign module uses.</summary>
    public const string BaseModuleFolder = "DivinityOrigins_1301db3d-1f54-4e98-9be5-5094030916e4";

    /// <summary>
    /// The base campaign's module folder. The Script Extender reads an
    /// <c>OsiToolsConfig.json</c> from it too, which makes it the cleanest place to
    /// switch on extender features (like the NRD_* Osiris calls) without enabling
    /// any mod.
    /// </summary>
    public static string BaseModuleDirectory(string gameDir)
    {
        foreach (var root in new[] { ModsDirectory(gameDir), UserModsDirectory })
        {
            try
            {
                if (!Directory.Exists(root)) continue;
                var match = Directory.GetDirectories(root, "DivinityOrigins_*").FirstOrDefault();
                if (match != null) return match;
            }
            catch
            {
                // Fall through to the default location.
            }
        }

        return Path.Combine(ModsDirectory(gameDir), BaseModuleFolder);
    }
}

/// <summary>Persisted app settings (game path, command sequence counter, ...).</summary>
public sealed class AppSettings
{
    public string? GameDirectory { get; set; }
    public int LastSequence { get; set; }
    public bool ShowLegacyAbilities { get; set; }
    public bool ShowAllTalents { get; set; }
    public bool UseConsoleMode { get; set; } = true;
    public bool HideConsoleWindow { get; set; }

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(Paths.SettingsFile))
            {
                var json = File.ReadAllText(Paths.SettingsFile);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, Options);
                if (loaded != null) return loaded;
            }
        }
        catch
        {
            // A broken settings file is not worth a failure; fall back to defaults.
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(Paths.SettingsFile, JsonSerializer.Serialize(this, Options));
        }
        catch
        {
            // Best effort only.
        }
    }
}
