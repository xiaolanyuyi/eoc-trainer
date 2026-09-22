using System.Reflection;
using System.Text;

namespace EocTrainer.Core;

public sealed class DeployResult
{
    public List<string> Messages { get; } = new();
    public List<string> Errors { get; } = new();
    public bool Ok => Errors.Count == 0;

    public void Message(string text) => Messages.Add(text);
    public void Error(string text) => Errors.Add(text);
}

/// <summary>
/// Packages the embedded Lua mod into a .pak the game accepts and registers it in
/// every profile's <c>modsettings.lsx</c>.
///
/// A loose folder is not enough: the game only lists mods it can find as
/// <c>&lt;mods root&gt;/&lt;Folder&gt;.pak</c> (or as a workshop item). That was
/// verified in game - a folder with a valid meta.lsx did not appear in the Mods menu.
/// </summary>
public static class ModDeployer
{
    private const string ResourcePrefix = "mod.";

    /// <summary>Recommended extender configuration, written next to the game executable.</summary>
    private const string ExtenderSettings = """
    {
        "EnableAchievements": true,
        "DisableModValidation": true,
        "EnableLogging": false,
        "LogFailedCompile": true,
        "LogRuntime": false,
        "CreateConsole": false,
        "SendCrashReports": false,
        "SyncNetworkStrings": true,
        "OptimizeHashing": true
    }
    """;

    /// <summary>The mod payload as it should appear inside the package.</summary>
    public static List<(string Path, byte[] Contents)> CollectFiles()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var files = new List<(string, byte[])>();

        foreach (var name in assembly.GetManifestResourceNames())
        {
            if (!name.StartsWith(ResourcePrefix, StringComparison.Ordinal)) continue;

            var relative = name[ResourcePrefix.Length..];
            string target;

            if (relative is "OsiToolsConfig.json" or "meta.lsx")
            {
                target = relative;
            }
            else if (relative is "BootstrapServer.lua" or "Console.lua")
            {
                target = "Story/RawFiles/Lua/" + relative;
            }
            else if (relative.StartsWith("EocTrainer.", StringComparison.Ordinal))
            {
                target = "Story/RawFiles/Lua/EocTrainer/" + relative["EocTrainer.".Length..];
            }
            else
            {
                continue;
            }

            using var stream = assembly.GetManifestResourceStream(name)!;
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            files.Add((target, memory.ToArray()));
        }

        if (files.Count == 0)
        {
            throw new InvalidOperationException("程序内没有嵌入任何模组文件（编译问题？）");
        }

        return files;
    }

    /// <summary>Builds the package in memory.</summary>
    public static byte[] BuildPackage() => ModPackage.Build(CollectFiles(), Paths.ModFolder);

    /// <summary>
    /// Extender features the trainer relies on, written into the base campaign's
    /// module folder. <c>OsirisExtensions</c> is what registers the NRD_* Osiris
    /// calls (exact attribute/ability/talent writes); without it those calls do not
    /// exist, which is exactly what happened when the console channel was first
    /// tested with no mod enabled.
    /// </summary>
    private const string BaseModuleConfig = """
    {
        "RequiredExtensionVersion": 60,
        "FeatureFlags": [
            "OsirisExtensions"
        ]
    }
    """;

    public static DeployResult Deploy(GameInstall install)
    {
        var result = new DeployResult();
        var pak = Paths.UserModPak;

        try
        {
            var contents = BuildPackage();
            Directory.CreateDirectory(Path.GetDirectoryName(pak)!);
            File.WriteAllBytes(pak, contents);
            result.Message($"已写入模组包：{pak}（{contents.Length} 字节）");
        }
        catch (Exception ex)
        {
            // A running game keeps the package memory mapped; the rest of the
            // deployment is still useful, so carry on and report it.
            result.Error($"写入模组包失败（游戏运行时会占用该文件，关掉游戏再试）：{ex.Message}");
        }

        // Loose folders are not loaded by the game and would shadow the package
        // contents in the virtual file system, so make sure they are gone.
        foreach (var stale in new[] { Paths.ModDirectory(install.Root), Paths.UserModDirectory })
        {
            try
            {
                if (Directory.Exists(stale))
                {
                    Directory.Delete(stale, recursive: true);
                    result.Message($"已清理旧的松散模组目录：{stale}");
                }
            }
            catch (Exception ex)
            {
                result.Error($"删除旧目录失败（{stale}）：{ex.Message}");
            }
        }

        RegisterInProfiles(result);

        // Feature flags for the game itself, so the extender's Osiris extensions
        // are available even when no mod is enabled (console / no-mod mode).
        try
        {
            var baseModule = Paths.BaseModuleDirectory(install.Root);
            Directory.CreateDirectory(baseModule);
            var config = Path.Combine(baseModule, "OsiToolsConfig.json");
            var needsWrite = !File.Exists(config)
                             || !File.ReadAllText(config).Contains("OsirisExtensions", StringComparison.Ordinal);
            if (needsWrite)
            {
                File.WriteAllText(config, BaseModuleConfig, new UTF8Encoding(false));
                result.Message($"已启用扩展器的 Osiris 扩展接口：{config}（首次生效需重启游戏）");
            }
        }
        catch (Exception ex)
        {
            result.Error($"写入基础模块配置失败：{ex.Message}");
        }

        var extenderSettings = Path.Combine(install.BinDirectory, "OsirisExtenderSettings.json");
        if (!File.Exists(extenderSettings))
        {
            try
            {
                File.WriteAllText(extenderSettings, ExtenderSettings, new UTF8Encoding(false));
                result.Message($"已写入 Script Extender 推荐配置：{extenderSettings}");
            }
            catch (Exception ex)
            {
                result.Error($"写入扩展器配置失败：{ex.Message}");
            }
        }

        return result;
    }

    public static DeployResult Uninstall(GameInstall install)
    {
        var result = Disable(install);
        RemoveBaseModuleConfig(install);

        foreach (var pak in new[] { Paths.UserModPak, Paths.ModPak(install.Root) })
        {
            try
            {
                if (File.Exists(pak))
                {
                    File.Delete(pak);
                    result.Message($"已删除模组包：{pak}");
                }
            }
            catch (Exception ex)
            {
                result.Error($"删除 {pak} 失败：{ex.Message}");
            }
        }

        result.Messages.Add("提示：Script Extender 与 OsirisExtenderSettings.json 未做改动。");
        return result;
    }

    /// <summary>Removes the extender feature flags written into the base module folder.</summary>
    public static void RemoveBaseModuleConfig(GameInstall install)
    {
        try
        {
            var config = Path.Combine(Paths.BaseModuleDirectory(install.Root), "OsiToolsConfig.json");
            if (File.Exists(config) && File.ReadAllText(config).Contains("OsirisExtensions", StringComparison.Ordinal))
            {
                File.Delete(config);
            }
        }
        catch
        {
            // Best effort.
        }
    }

    /// <summary>Removes the mod from every profile's load order but keeps the files.</summary>
    public static DeployResult Disable(GameInstall install)
    {
        var result = new DeployResult();

        foreach (var profile in Profiles())
        {
            try
            {
                if (ModSettings.Unregister(profile))
                {
                    result.Message($"已停用：{Path.GetFileName(Path.GetDirectoryName(profile))}");
                }
            }
            catch (Exception ex)
            {
                result.Error($"更新 {profile} 失败：{ex.Message}");
            }
        }

        return result;
    }

    private static void RegisterInProfiles(DeployResult result)
    {
        var profiles = Profiles().ToList();
        if (profiles.Count == 0)
        {
            result.Error($"没有找到任何存档配置目录：{Paths.PlayerProfiles}");
            return;
        }

        foreach (var profile in profiles)
        {
            var label = Path.GetFileName(Path.GetDirectoryName(profile));
            try
            {
                if (ModSettings.Register(profile, out var error)) result.Message($"已启用模组：{label}");
                else result.Error($"无法修改 {profile}：{error}");
            }
            catch (Exception ex)
            {
                result.Error($"更新 {label} 失败：{ex.Message}");
            }
        }
    }

    /// <summary>All <c>modsettings.lsx</c> files that belong to a player profile.</summary>
    public static IEnumerable<string> Profiles()
    {
        var root = Paths.PlayerProfiles;
        if (!Directory.Exists(root)) yield break;

        foreach (var directory in Directory.GetDirectories(root))
        {
            var settings = Path.Combine(directory, "modsettings.lsx");
            if (File.Exists(settings)) yield return settings;
        }
    }
}
