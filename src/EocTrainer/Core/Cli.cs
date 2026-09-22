using System.Runtime.InteropServices;
using System.Text;

namespace EocTrainer.Core;

/// <summary>
/// Head-less entry points, handy for verification and for scripting the
/// installation. Output is written to the console when one is attached and to
/// <c>%LOCALAPPDATA%\EocTrainer\cli-output.txt</c> in every case.
/// </summary>
public static class Cli
{
    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);

    private const int AttachParentProcess = -1;

    public static int Run(string[] args)
    {
        var command = args[0].TrimStart('-', '/').ToLowerInvariant();
        var output = new StringBuilder();

        void Write(string text)
        {
            output.AppendLine(text);
            Console.WriteLine(text);
        }

        var exitCode = 0;
        try
        {
            AttachConsole(AttachParentProcess);

            switch (command)
            {
                case "diagnose":
                    Diagnose(Write);
                    break;

                case "resources":
                    Resources(Write);
                    break;

                case "deploy":
                    Deploy(Write);
                    break;

                case "build-pak":
                    BuildPak(Write, args.Length > 1 ? args[1] : null);
                    break;

                case "bridge-test":
                    BridgeTest(Write);
                    break;

                case "op":
                    SendSingleOp(Write, args);
                    break;

                case "console-status":
                    ConsoleStatus(Write);
                    break;

                case "console-activate":
                    ConsoleActivate(Write);
                    break;

                case "console-probe":
                    ConsoleProbe(Write);
                    break;

                case "console-enable":
                    ConsoleEnable(Write);
                    break;

                case "dump-state":
                    DumpState(Write);
                    break;

                case "uninstall":
                    Uninstall(Write);
                    break;

                case "disable":
                    Disable(Write);
                    break;

                case "help":
                case "--help":
                    Write("用法: EocTrainer.exe [diagnose|resources|deploy|uninstall]");
                    break;

                default:
                    Write($"未知命令：{command}");
                    exitCode = 1;
                    break;
            }
        }
        catch (Exception ex)
        {
            Write("异常：" + ex);
            exitCode = 1;
        }

        try
        {
            File.WriteAllText(Path.Combine(Paths.AppData, "cli-output.txt"), output.ToString(), new UTF8Encoding(false));
        }
        catch
        {
            // Nothing else to do.
        }

        return exitCode;
    }

    private static void Resources(Action<string> write)
    {
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        write("嵌入的模组资源：");
        foreach (var name in assembly.GetManifestResourceNames().OrderBy(n => n))
        {
            write("  " + name);
        }
    }

    private static void Diagnose(Action<string> write)
    {
        write("== 环境 ==");
        write($"文档目录      : {Paths.Documents}");
        write($"游戏用户目录  : {Paths.GameStorage}（{(Directory.Exists(Paths.GameStorage) ? "存在" : "不存在")}）");
        write($"桥接目录      : {Paths.BridgeDir}（{(Directory.Exists(Paths.BridgeDir) ? "存在" : "不存在")}）");
        write($"状态文件      : {Paths.StateFile}（{(File.Exists(Paths.StateFile) ? "存在" : "不存在")}）");
        write($"扩展器缓存    : {Paths.ExtenderCache}");
        write("");

        write("== Steam 库候选目录 ==");
        foreach (var candidate in GameInstall.CandidateGameDirectories())
        {
            write($"  {candidate}（{(File.Exists(Path.Combine(candidate, "DefEd", "bin", "EoCApp.exe")) ? "可用" : "无游戏")}）");
        }
        write("");

        var install = GameInstall.Find();
        if (install == null)
        {
            write("== 游戏 ==");
            write("未找到游戏，请用界面上的「选择游戏目录」手动指定。");
            return;
        }

        write("== 游戏 ==");
        write($"根目录        : {install.Root}");
        write($"可执行文件    : {install.AppPath}");
        write($"扩展器        : {install.Extender.Describe()}");
        write($"  更新器      : {install.Extender.UpdaterPath ?? "（无）"}");
        write($"  主体        : {install.Extender.PayloadPath ?? "（未下载）"}");
        write($"  曾经运行过  : {(install.Extender.HasRunBefore ? "是" : "否")}");
        write("");
        write("== 模组 ==");
        write($"模组包          : {install.Mod.PakPath}（{(install.Mod.PakPresent ? "存在" : "不存在")}）");
        write($"包内容是最新的  : {(install.Mod.PakUpToDate ? "是" : "否（关闭游戏后重新部署）")}");
        write($"游戏目录内      : {Paths.ModPak(install.Root)}（{(install.Mod.PakInGameFolder ? "存在" : "无")}）");
        write($"已在配置中启用  : {(install.Mod.RegisteredAnywhere ? "是" : "否")}");
        foreach (var profile in install.Mod.MissingRegistration)
        {
            write($"  未启用的存档配置: {profile}");
        }
        write("");
        write("== 存档配置 ==");
        foreach (var profile in ModDeployer.Profiles())
        {
            write($"  {profile}（已启用: {(ModSettings.IsRegistered(profile) ? "是" : "否")}）");
        }

        Resources(write);
    }

    private static void Deploy(Action<string> write)
    {
        var install = GameInstall.Find();
        if (install == null)
        {
            write("未找到游戏目录。");
            return;
        }

        var result = ModDeployer.Deploy(install);
        foreach (var message in result.Messages) write(message);
        foreach (var error in result.Errors) write("错误：" + error);
    }

    /// <summary>Writes the mod package to a file so it can be inspected externally.</summary>
    private static void BuildPak(Action<string> write, string? destination)
    {
        try
        {
            var contents = ModDeployer.BuildPackage();
            var path = destination ?? Path.Combine(Paths.AppData, Paths.ModFolder + ".pak");
            File.WriteAllBytes(path, contents);
            write($"已生成模组包：{path}（{contents.Length} 字节）");
            foreach (var (name, bytes) in ModDeployer.CollectFiles())
            {
                write($"  {name}  {bytes.Length} 字节");
            }
        }
        catch (Exception ex)
        {
            write("生成失败：" + ex.Message);
        }
    }

    /// <summary>
    /// Sends a batch of operations through the real bridge files and waits for the
    /// mod to acknowledge it. Used together with tools/fake_game.py or a running game.
    /// </summary>
    private static void BridgeTest(Action<string> write)
    {
        var bridge = new BridgeClient(AppSettings.Load());

        write($"桥接目录: {Paths.BridgeDir}");
        write($"命令文件: {(File.Exists(Paths.CommandFile) ? "存在" : "不存在")}");
        write($"状态文件: {(File.Exists(Paths.StateFile) ? "存在" : "不存在")}");

        bridge.Poll();
        if (bridge.State == null)
        {
            write("还没有收到状态文件；请先让游戏（或 tools/fake_game.py）跑起来。");
            return;
        }

        write($"初始状态: seq={bridge.State.Seq} inGame={bridge.State.InGame} 角色数={bridge.State.Party.Count}");
        foreach (var member in bridge.State.Party)
        {
            write($"  {member.Guid} {member.Display} 等级={member.Level} 金币={member.Gold} " +
                  $"属性点={Lookup(member.Points, "attribute")} 天赋={string.Join("/", member.Talents)}");
        }

        var target = bridge.State.Party.FirstOrDefault(p => p.IsHost)?.Guid
                     ?? bridge.State.Party.FirstOrDefault()?.Guid;

        bridge.Enqueue(new Op { OpName = "addPoint", Target = target, Kind = "attribute", Amount = 2 });
        bridge.Enqueue(new Op { OpName = "setAttribute", Target = target, Name = "Strength", Value = 15 });
        bridge.Enqueue(new Op { OpName = "addGold", Target = target, Amount = 1000 });
        bridge.Enqueue(new Op { OpName = "setTalent", Target = target, Name = "LoneWolf", Value = 1 });
        bridge.Flush();
        write($"已发送序号 {bridge.LastSentSequence}");

        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            bridge.Poll();
            bridge.Flush();
            if (bridge.State != null && bridge.State.Seq >= bridge.LastSentSequence) break;
            Thread.Sleep(200);
        }

        var state = bridge.State;
        if (state == null || state.Seq < bridge.LastSentSequence)
        {
            write("没有收到确认（超时）。");
            return;
        }

        write($"已确认: seq={state.Seq} ok={state.Ok}");
        foreach (var error in state.Errors) write("  模组错误: " + error);
        foreach (var result in state.Results) write($"  结果: {result.Op} -> {result.Value}");

        foreach (var member in state.Party)
        {
            write($"  {member.Display} 金币={member.Gold} 力量={Lookup(member.BaseAttributes, "Strength")} " +
                  $"属性点={Lookup(member.Points, "attribute")} 天赋={string.Join("/", member.Talents)}");
        }

        write("游戏侧日志末尾：");
        var log = bridge.ReadGameLog().Split('\n');
        foreach (var line in log.TakeLast(12)) write("  " + line);
    }

    /// <summary>Diagnostics for the console channel.</summary>
    private static void ConsoleStatus(Action<string> write)
    {
        write("== 控制台注入模式 ==");
        write($"控制台窗口存在 : {(GameConsole.IsPresent() ? "是" : "否（需要 CreateConsole: true 并重启游戏）")}");
        write($"EoCApp 进程    : {GameConsole.FindGameProcessId()?.ToString() ?? "未运行"}");

        var install = GameInstall.Find();
        if (install != null)
        {
            var settings = Path.Combine(install.BinDirectory, "OsirisExtenderSettings.json");
            write($"扩展器配置     : {settings}");
            if (File.Exists(settings))
            {
                var text = File.ReadAllText(settings);
                var createConsole = text.Contains("\"CreateConsole\": true", StringComparison.OrdinalIgnoreCase);
                write($"CreateConsole  : {(createConsole ? "已开启" : "未开启")}");
            }
            else
            {
                write("扩展器配置     : 不存在");
            }
        }

        write($"注入用 Lua 目录: {ConsoleChannel.LuaDirectory}（{(Directory.Exists(ConsoleChannel.LuaDirectory) ? "存在" : "不存在")}）");

        using var channel = new ConsoleChannel();
        var ok = channel.TryActivate(out var message);
        write($"激活结果       : {(ok ? "成功" : "失败")} · {message}");

        if (ok)
        {
            write("控制台末尾输出：");
            foreach (var line in channel.Console.ReadTail(20).Split('\n')) write("  " + line.TrimEnd());
        }
    }

    private static void ConsoleActivate(Action<string> write)
    {
        using var channel = new ConsoleChannel();
        var ok = channel.TryActivate(out var message);
        write($"{(ok ? "已激活" : "未激活")}：{message}（模式：{channel.Mode}）");
    }

    /// <summary>Low level console diagnostics: window, modes, screen content.</summary>
    private static void ConsoleProbe(Action<string> write)
    {
        var pid = GameConsole.FindGameProcessId();
        write($"EoCApp pid      : {pid?.ToString() ?? "未运行"}");
        write($"控制台窗口      : {(GameConsole.IsPresent() ? "存在" : "不存在")}");

        using var console = new GameConsole();
        if (!console.TryAttach(out var error))
        {
            write("附加失败：" + error);
            return;
        }

        write($"已附加到控制台（pid={console.ProcessId}，窗口句柄=0x{console.WindowHandle:X}）");

        try
        {
            var processes = new uint[16];
            var count = ConsoleApi.GetConsoleProcessList(processes, (uint)processes.Length);
            write($"附加到该控制台的进程数：{count}（第一个通常是游戏）");

            if (ConsoleApi.GetConsoleMode(ConsoleApi.GetStdHandle(ConsoleApi.StdInputHandle), out var mode))
            {
                write($"输入模式        : 0x{mode:X4}" +
                      $"（行输入={(mode & ConsoleApi.EnableLineInput) != 0}，" +
                      $"回显={(mode & ConsoleApi.EnableEchoInput) != 0}，" +
                      $"处理Ctrl={(mode & ConsoleApi.EnableProcessedInput) != 0}）");
            }
        }
        catch (Exception ex)
        {
            write("查询控制台模式失败：" + ex.Message);
        }

        write("--- 控制台末尾内容（40 行）---");
        try
        {
            foreach (var line in console.ReadTail(40).Split('\n')) write("  |" + line.TrimEnd());
        }
        catch (Exception ex)
        {
            write("读取控制台内容失败：" + ex.Message);
        }
    }

    private static void ConsoleEnable(Action<string> write)
    {
        var needsRestart = ConsoleChannel.EnsureConsoleEnabled(out var message);
        write(message);
        if (needsRestart) write("请完全退出游戏后重新启动，然后运行 console-activate。");
    }

    /// <summary>
    /// Sends a single operation, eg.
    /// <c>EocTrainer.exe op setAttribute name=Strength value=25</c>.
    /// Useful for probing the game while developing.
    /// </summary>
    private static void SendSingleOp(Action<string> write, string[] args)
    {
        if (args.Length < 2)
        {
            write("用法: EocTrainer.exe op <op> [key=value ...]");
            write("     键: target / kind / name / value / amount / code / fields");
            return;
        }

        var bridge = new BridgeClient(AppSettings.Load());
        bridge.Poll();

        var op = new Op { OpName = args[1] };
        if (bridge.State != null && op.Target == null)
        {
            op.Target = bridge.State.Party.FirstOrDefault(p => p.IsHost)?.Guid;
        }

        foreach (var argument in args.Skip(2))
        {
            var split = argument.Split('=', 2);
            var key = split[0].ToLowerInvariant();
            var raw = split.Length > 1 ? split[1] : "";
            switch (key)
            {
                case "target": op.Target = raw; break;
                case "kind": op.Kind = raw; break;
                case "name": op.Name = raw; break;
                case "value": op.Value = double.Parse(raw); break;
                case "amount": op.Amount = double.Parse(raw); break;
                case "code": op.Code = raw; break;
                case "codefile": op.Code = File.ReadAllText(raw.Trim('"')); break;
                case "fields": op.Fields = raw.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(); break;
                case "keys": op.Keys = raw.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(); break;
                default: write($"未知参数：{argument}"); break;
            }
        }

        write($"发送 {op.OpName} target={op.Target} name={op.Name} value={op.Value} amount={op.Amount}");
        bridge.Enqueue(op);
        bridge.Flush();

        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            bridge.Poll();
            bridge.Flush();
            if (bridge.State != null && bridge.State.Seq >= bridge.LastSentSequence) break;
            Thread.Sleep(200);
        }

        var state = bridge.State;
        if (state == null || state.Seq < bridge.LastSentSequence)
        {
            write("超时：没有收到确认。");
            return;
        }

        write($"已确认 seq={state.Seq}");
        foreach (var error in state.Errors) write("  错误: " + error);
        foreach (var result in state.Results) write($"  结果: {result.Op} -> {result.Value}");

        var member = state.Party.FirstOrDefault(p => p.IsHost);
        if (member != null)
        {
            write($"  {member.Display} 金币={member.Gold} 力量={Lookup(member.BaseAttributes, "Strength")} " +
                  $"属性点={Lookup(member.Points, "attribute")} 天赋={string.Join("/", member.Talents)}");
        }
    }

    private static void DumpState(Action<string> write)
    {
        var bridge = new BridgeClient(AppSettings.Load());
        bridge.Poll();

        var state = bridge.State;
        if (state == null)
        {
            write($"没有可用的状态文件：{Paths.StateFile}");
            if (bridge.LastError != null) write("错误: " + bridge.LastError);
            return;
        }

        write($"seq={state.Seq} version={state.Version} inGame={state.InGame} ok={state.Ok} 角色数={state.Party.Count}");
        foreach (var error in state.Errors) write("  错误: " + error);

        foreach (var member in state.Party)
        {
            write($"{member.Display} ({member.Guid}) 等级={member.Level} 金币={member.Gold} 倒地={member.Dead}");
            write("  点数: " + string.Join(", ", member.Points.Select(kv => $"{kv.Key}={kv.Value}")));
            write("  属性: " + string.Join(", ", member.BaseAttributes.Select(kv => $"{kv.Key}={kv.Value}")));
            write("  能力: " + string.Join(", ", member.BaseAbilities.Select(kv => $"{kv.Key}={kv.Value}")));
            write("  天赋: " + string.Join(", ", member.Talents));
            write("  状态: " + string.Join(", ", member.Vitals.Select(kv => $"{kv.Key}={kv.Value}")));
        }
    }

    private static string Lookup(Dictionary<string, double?> map, string key) =>
        map.TryGetValue(key, out var value) && value.HasValue ? value.Value.ToString("0.##") : "—";

    private static void Uninstall(Action<string> write)
    {
        var install = GameInstall.Find();
        if (install == null)
        {
            write("未找到游戏目录。");
            return;
        }

        var result = ModDeployer.Uninstall(install);
        foreach (var message in result.Messages) write(message);
        foreach (var error in result.Errors) write("错误：" + error);
    }

    /// <summary>Removes the mod from the load order but keeps the package in place.</summary>
    private static void Disable(Action<string> write)
    {
        var install = GameInstall.Find();
        if (install == null)
        {
            write("未找到游戏目录。");
            return;
        }

        var result = ModDeployer.Disable(install);
        foreach (var message in result.Messages) write(message);
        foreach (var error in result.Errors) write("错误：" + error);
    }
}
