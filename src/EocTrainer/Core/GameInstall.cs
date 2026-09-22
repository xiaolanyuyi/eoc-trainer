using System.Diagnostics;
using System.Text.RegularExpressions;

namespace EocTrainer.Core;

public enum ExtenderState
{
    /// <summary>No Script Extender found at all.</summary>
    Missing,
    /// <summary>Updater dll is in place but the payload has not been downloaded yet.</summary>
    UpdaterOnly,
    /// <summary>Payload downloaded; the extender will/has been loaded by the game.</summary>
    Ready,
}

public sealed class ExtenderInfo
{
    public ExtenderState State { get; init; }
    public string? UpdaterPath { get; init; }
    public string? UpdaterVersion { get; init; }
    public string? PayloadPath { get; init; }
    public string? PayloadVersion { get; init; }
    public bool HasRunBefore { get; init; }

    public string Describe() => State switch
    {
        ExtenderState.Missing => "未安装 Script Extender",
        ExtenderState.UpdaterOnly => "已装更新器，但扩展器还没下载完成（需要先启动一次游戏）",
        _ => $"已就绪（v{PayloadVersion ?? "?"}）",
    };
}

public sealed class ModInfo
{
    public bool Deployed { get; init; }
    public bool RegisteredAnywhere { get; set; }
    public bool PakPresent { get; init; }
    public bool PakUpToDate { get; init; }
    public bool PakInGameFolder { get; init; }
    public List<string> MissingRegistration { get; } = new();
    public string Directory { get; init; } = "";
    public string UserDirectory { get; init; } = "";
    public string PakPath { get; init; } = "";
}

/// <summary>Checks whether the mod package is present.</summary>
public static class ModPresence
{
    public static bool IsDeployed(string pakPath) => File.Exists(pakPath);
}

public sealed class GameInstall
{
    public required string Root { get; init; }
    public required string BinDirectory { get; init; }
    public required string AppPath { get; init; }
    public required ExtenderInfo Extender { get; init; }
    public required ModInfo Mod { get; init; }

    public string Edition => "Definitive Edition";

    public static IEnumerable<string> CandidateGameDirectories()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var library in SteamLibraries())
        {
            foreach (var folder in new[] { "Divinity Original Sin 2" })
            {
                var candidate = Path.Combine(library, "steamapps", "common", folder);
                if (seen.Add(candidate)) yield return candidate;
            }
        }
    }

    private static IEnumerable<string> SteamLibraries()
    {
        var libraries = new List<string>();

        void AddLibrary(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            if (!libraries.Contains(path, StringComparer.OrdinalIgnoreCase)) libraries.Add(path);
        }

        foreach (var root in new[]
                 {
                     @"HKEY_CURRENT_USER\Software\Valve\Steam",
                     @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam",
                     @"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam",
                 })
        {
            foreach (var name in new[] { "SteamPath", "InstallPath" })
            {
                var value = TryGetRegistryValue(root, name);
                if (!string.IsNullOrWhiteSpace(value)) AddLibrary(value);
            }
        }

        foreach (var library in libraries.ToList())
        {
            var vdf = Path.Combine(library, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) continue;

            try
            {
                foreach (Match match in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s*\"([^\"]+)\""))
                {
                    AddLibrary(match.Groups[1].Value.Replace("\\\\", "\\"));
                }
            }
            catch
            {
                // Ignore unreadable library files.
            }
        }

        return libraries;
    }

    private static string? TryGetRegistryValue(string key, string name)
    {
        try
        {
            return Microsoft.Win32.Registry.GetValue(key, name, null) as string;
        }
        catch
        {
            return null;
        }
    }

    public static GameInstall? Inspect(string root)
    {
        var bin = Path.Combine(root, "DefEd", "bin");
        var app = Path.Combine(bin, "EoCApp.exe");
        if (!File.Exists(app)) return null;

        return new GameInstall
        {
            Root = root,
            BinDirectory = bin,
            AppPath = app,
            Extender = InspectExtender(bin),
            Mod = InspectMod(root),
        };
    }

    public static GameInstall? Find()
    {
        var configured = AppSettings.Load().GameDirectory;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var install = Inspect(configured!);
            if (install != null) return install;
        }

        foreach (var candidate in CandidateGameDirectories())
        {
            var install = Inspect(candidate);
            if (install != null) return install;
        }

        return null;
    }

    private static ExtenderInfo InspectExtender(string binDirectory)
    {
        var updaterPath = new[] { "dxgi.dll", "d3d11.dll", "version.dll", "winmm.dll" }
            .Select(name => Path.Combine(binDirectory, name))
            .FirstOrDefault(path => File.Exists(path) && IsExtenderUpdater(path));

        var payload = FindPayload();
        var hasRun = Directory.Exists(Path.Combine(Paths.GameStorage, "Extender Logs"));

        var state = ExtenderState.Missing;
        if (payload != null) state = ExtenderState.Ready;
        else if (updaterPath != null) state = ExtenderState.UpdaterOnly;

        return new ExtenderInfo
        {
            State = state,
            UpdaterPath = updaterPath,
            UpdaterVersion = updaterPath == null ? null : VersionOf(updaterPath),
            PayloadPath = payload?.Path,
            PayloadVersion = payload?.Version,
            HasRunBefore = hasRun,
        };
    }

    private static bool IsExtenderUpdater(string path)
    {
        var description = VersionOf(path, "FileDescription") ?? "";
        var company = VersionOf(path, "CompanyName") ?? "";
        return description.Contains("Script Extender", StringComparison.OrdinalIgnoreCase)
               || company.Contains("Norbyte", StringComparison.OrdinalIgnoreCase);
    }

    private static (string Path, string Version)? FindPayload()
    {
        try
        {
            if (!Directory.Exists(Paths.ExtenderCache)) return null;

            foreach (var dir in Directory.GetDirectories(Paths.ExtenderCache)
                         .OrderByDescending(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase))
            {
                var dll = Path.Combine(dir, "OsiExtenderEoCApp.dll");
                if (!File.Exists(dll)) continue;

                var name = Path.GetFileName(dir);
                var version = name.Split('_')[0];
                return (dll, version);
            }
        }
        catch
        {
            // Ignore and report as missing.
        }

        return null;
    }

    private static string? VersionOf(string path, string field = "FileVersion")
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            var value = field switch
            {
                "FileDescription" => info.FileDescription,
                "CompanyName" => info.CompanyName,
                _ => info.FileVersion,
            };
            return string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
        }
        catch
        {
            return null;
        }
    }

    private static ModInfo InspectMod(string root)
    {
        var userPak = Paths.UserModPak;
        var gamePak = Paths.ModPak(root);
        var exists = File.Exists(userPak);

        var upToDate = false;
        if (exists)
        {
            try
            {
                // The package is built from the embedded Lua, so a simple byte
                // comparison tells whether the deployed mod is still current.
                upToDate = File.ReadAllBytes(userPak).AsSpan().SequenceEqual(ModDeployer.BuildPackage());
            }
            catch
            {
                upToDate = false;
            }
        }

        var info = new ModInfo
        {
            Deployed = exists || File.Exists(gamePak),
            Directory = Paths.ModDirectory(root),
            UserDirectory = Paths.UserModDirectory,
            PakPath = userPak,
            PakPresent = exists,
            PakUpToDate = upToDate,
            PakInGameFolder = File.Exists(gamePak),
        };

        var profiles = Paths.PlayerProfiles;
        if (Directory.Exists(profiles))
        {
            foreach (var profile in Directory.GetDirectories(profiles))
            {
                var settings = Path.Combine(profile, "modsettings.lsx");
                if (!File.Exists(settings)) continue;

                if (ModSettings.IsRegistered(settings)) info.RegisteredAnywhere = true;
                else info.MissingRegistration.Add(Path.GetFileName(profile));
            }
        }

        return info;
    }
}
