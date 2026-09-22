using System.Text;

namespace EocTrainer.Core;

public enum ChannelMode
{
    /// <summary>The packaged mod handles commands through the bridge files.</summary>
    Mod,
    /// <summary>The app injected the bridge code through the extender's console.</summary>
    Console,
    /// <summary>Nothing is connected yet.</summary>
    None,
}

/// <summary>
/// Activates the trainer without a packaged mod by injecting the bridge code into
/// the game through the Script Extender's debug console.
///
/// The injected chunk is tiny: it loads the same Lua modules the mod uses, but
/// from <c>Osiris Data\EocTrainer\lua\</c> which the app writes beforehand. Once
/// running, the injected code polls the very same command file as the mod, so the
/// rest of the application does not care which channel is in use.
///
/// Requirements: <c>CreateConsole</c> must be enabled in
/// <c>OsirisExtenderSettings.json</c>, and the game must be in a session (the
/// console executes Lua on the server thread, which only runs in game).
/// </summary>
public sealed class ConsoleChannel : IDisposable
{
    private readonly GameConsole _console = new();
    private string _lastInjectionResult = "";

    public ChannelMode Mode { get; private set; } = ChannelMode.None;
    public string LastMessage { get; private set; } = "";

    /// <summary>Where the Lua modules for the injected code are written.</summary>
    public static string LuaDirectory => Path.Combine(Paths.BridgeDir, "lua");

    /// <summary>Enables CreateConsole in the extender settings if needed. Returns true when a restart is required.</summary>
    public static bool EnsureConsoleEnabled(out string message)
    {
        var install = GameInstall.Find();
        if (install == null)
        {
            message = "没有找到游戏目录";
            return false;
        }

        var path = Path.Combine(install.BinDirectory, "OsirisExtenderSettings.json");
        try
        {
            System.Text.Json.Nodes.JsonObject node;
            if (File.Exists(path))
            {
                node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))
                           as System.Text.Json.Nodes.JsonObject
                       ?? new System.Text.Json.Nodes.JsonObject();
            }
            else
            {
                node = new System.Text.Json.Nodes.JsonObject();
            }

            var current = node["CreateConsole"]?.GetValue<bool>() ?? false;
            if (current)
            {
                message = "已启用（CreateConsole = true）";
                return false;
            }

            node["CreateConsole"] = true;
            File.WriteAllText(path, node.ToJsonString(JsonOptions), new UTF8Encoding(false));
            message = $"已把 CreateConsole 打开：{path}（需要重启游戏一次）";
            return true;
        }
        catch (Exception ex)
        {
            message = $"写入扩展器配置失败：{ex.Message}";
            return false;
        }
    }

    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    /// <summary>Copies the Lua modules the injected code needs into the bridge directory.</summary>
    public static void DeployLuaModules()
    {
        Directory.CreateDirectory(LuaDirectory);

        foreach (var (path, contents) in ModDeployer.CollectFiles())
        {
            var name = Path.GetFileName(path);
            if (!name.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)) continue;
            File.WriteAllBytes(Path.Combine(LuaDirectory, name), contents);
        }

        // The packaged mod's bootstrap uses Ext.Require, which is unavailable
        // here; Console.lua does the loading instead.
        var console = Path.Combine(LuaDirectory, "Console.lua");
        if (!File.Exists(console))
        {
            throw new FileNotFoundException("embedded Console.lua missing", console);
        }
    }

    /// <summary>
    /// Attaches to the game console and, when needed, injects the bridge code.
    /// </summary>
    public bool TryActivate(out string message)
    {
        if (!GameConsole.IsPresent())
        {
            message = "游戏没有创建控制台（需要在 OsirisExtenderSettings.json 里设置 \"CreateConsole\": true 后重启游戏）";
            LastMessage = message;
            Mode = ChannelMode.None;
            return false;
        }

        if (!_console.TryAttach(out var error))
        {
            message = error;
            LastMessage = message;
            return false;
        }

        try
        {
            DeployLuaModules();
        }
        catch (Exception ex)
        {
            message = $"写入注入用的 Lua 模块失败：{ex.Message}";
            LastMessage = message;
            return false;
        }

        var result = Inject();
        _lastInjectionResult = result;

        if (result.Contains("packaged mod is active", StringComparison.OrdinalIgnoreCase))
        {
            Mode = ChannelMode.Mod;
            message = "已启用模组的桥接（无需注入）";
            LastMessage = message;
            return true;
        }

        if (result.Contains("console mode active", StringComparison.OrdinalIgnoreCase)
            || result.Contains("already running", StringComparison.OrdinalIgnoreCase))
        {
            Mode = ChannelMode.Console;
            message = "控制台注入模式已激活";
            LastMessage = message;
            return true;
        }

        Mode = ChannelMode.None;
        message = $"注入返回：{result}";
        LastMessage = message;
        return false;
    }

    private string Inject()
    {
        // The extender's console thread first waits for a single Enter that
        // switches it from log mode to input mode; the characters typed before
        // that Enter are discarded. So a bare Enter is sent first, and only then
        // the actual paste.
        _console.SendLine("");
        Thread.Sleep(120);

        // The console supports a multi line paste: "--[[" starts collecting and
        // "]]--" executes the collected chunk. Every line is kept short so it
        // fits the console's own line buffer.
        var lines = new List<string>
        {
            "--[[",
            "local __src = Ext.IO.LoadFile(\"EocTrainer/lua/Console.lua\")",
            "if __src == nil then",
            "  Ext.Print(\"EOCINJECT missing Console.lua\")",
            "else",
            "  local __chunk, __err = Ext.Utils.LoadString(__src, \"@Console.lua\")",
            "  if __chunk == nil then",
            "    Ext.Print(\"EOCINJECT compile error: \" .. tostring(__err))",
            "  else",
            "    local __ok, __result = pcall(__chunk)",
            "    Ext.Print(\"EOCINJECT \" .. tostring(__ok) .. \" -> \" .. tostring(__result))",
            "  end",
            "end",
            "]]--",
        };

        foreach (var line in lines)
        {
            _console.SendLine(line);
            Thread.Sleep(15);
        }

        Thread.Sleep(600);
        var tail = _console.ReadTail(30);
        var marker = tail.LastIndexOf("EOCINJECT", StringComparison.Ordinal);
        if (marker < 0) return $"（没有看到注入结果）\n{tail}";

        var end = tail.IndexOf('\n', marker);
        var lineText = end < 0 ? tail[marker..] : tail[marker..end];
        return lineText["EOCINJECT".Length..].Trim();
    }

    /// <summary>Re-injects the bridge code; used after a savegame reload resets the Lua state.</summary>
    public bool Reinject(out string message) => TryActivate(out message);

    public GameConsole Console => _console;

    public void Dispose() => _console.Dispose();
}
