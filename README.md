# EoC Trainer · 神界：原罪 2 精确数值修改器

针对《神界：原罪 2 决定版》(Divinity: Original Sin 2 Definitive Edition) 的运行时数值修改器。
可在游戏运行中**精确设置 / 增减**队伍角色的属性、能力、天赋、点数、金币等。

支持**两种接入方式**，界面和功能完全一致：

| 方式 | 需要模组 | 需要重启游戏 | 需要新档案登记 | 稳定性 |
|---|---|---|---|---|
| **① 模组包（.pak）** | ✅ 需要 | 首次需要 | 需要（程序自动做） | 最稳 |
| **② 控制台注入（免模组）** | ❌ 不需要 | ❌ 不需要 | ❌ 不需要 | 依赖 Script Extender 控制台 |

程序会自动选择：检测到模组在自己干活时就暂停注入；否则（没装模组 / 模组被停用 / 新建档案没登记）
自动通过控制台把桥接代码注入游戏。默认两种都尝试。

```
┌─────────────────────┐   command.json    ┌──────────────────────────────────┐
│   EocTrainer.exe    │ ────────────────► │  EoCApp.exe                      │
│   C# WinForms 控制端 │                   │   Script Extender（v60，必需）    │
│                     │ ◄──────────────── │    ├─ ① Lua 模组（pak）            │
└─────────────────────┘    state.json     │    └─ ② 控制台注入的同一份 Lua      │
                                          └──────────────────────────────────┘
```

---

## 一、前置条件（本机已全部满足）

1. **游戏**：`...\Divinity Original Sin 2\DefEd\bin\EoCApp.exe`（3.6.117.3735）
2. **Norbyte Script Extender**：`DefEd\bin\dxgi.dll` 是它的自动更新器；启动一次游戏就会
   自动下载主体到 `%LOCALAPPDATA%\DOS2ScriptExtender\ScriptExtender\60.0.0.0_...\OsiExtenderEoCApp.dll`
3. **.NET 10 SDK**（编译控制端用）
4. 免模组模式额外需要：`DefEd\bin\OsirisExtenderSettings.json` 里 `"CreateConsole": true`
   —— 程序里点「启用控制台」会自动写入（之后重启一次游戏）。程序还会把
   `OsirisExtensions` 写进基础模块的 `OsiToolsConfig.json`，
   这样**不启用任何模组**也能使用扩展器的 `NRD_*` 精确写入接口。

---

## 二、使用步骤

1. 运行 `src\EocTrainer\bin\Debug\net10.0-windows\EocTrainer.exe`（或 `dist\EocTrainer.exe`）
2. 首次使用：
   - 想用**模组模式** → 点「安装 / 更新模组」（写入 pak + 在每个存档配置里启用）
   - 想用**免模组模式** → 点「启用控制台」，然后重启一次游戏
3. 启动游戏并**读取存档**。顶部状态栏会出现：
   - `连接：已连接（模组）` 或 `连接：已连接（控制台注入）`
   - 左侧列出队伍角色
4. 开始修改。所有操作即时生效并写入存档。

> 模组包更新后需要重启游戏才生效；控制台注入模式每次读档后程序会自动重新注入
> （读档会重建游戏的 Lua 状态）。

---

## 三、能改什么

| 分类 | 操作 | 底层调用 |
|---|---|---|
| 未分配点数 | 属性点 / 战斗能力点 / 民事能力点 / 天赋点 的增减与设定 | `CharacterAddAttributePoint` 等 |
| 属性 | 力量、敏捷、智力、体质、记忆、智慧 精确设置（0–40），支持批量 ±1 | `NRD_PlayerSetBaseAttribute` |
| 战斗 / 民事能力 | 41 项能力等级精确设置 | `NRD_PlayerSetBaseAbility` |
| 天赋 | 勾选式添加 / 移除任意天赋 | `NRD_PlayerSetBaseTalent` |
| 金币 | 增加 / 减少 / 设定 | `CharacterAddGold` |
| 生命 / 物理护甲 / 魔法护甲 | 设定或回满 | `NRD_CharacterSetStatInt` |
| 源力点 / 行动点 | 增减 | `CharacterAddSourcePoints` / `CharacterAddActionPoints` |
| 永久增益 | 力量、抗性、伤害加成等 40 余项，写进存档 | `NRD_CharacterSetPermanentBoostInt` |
| 其他 | 复活角色、重置技能冷却 | `CharacterResurrect` / `CharacterResetCooldowns` |
| 调试 | 在服务器 Lua 里执行任意片段 | `Ext.Utils.LoadString` |

**关于「设置值」和「面板显示」**：属性页有两列。`设置值` 是游戏内部保存的原始值
（也是修改器写入的值，精确到字节）；`面板显示` 是角色面板上的数值，会被装备 / 天赋加成——
例如**独狼**会把 10 以上的属性翻倍（上限仍是 40）：设 15 显示 20，设 25 显示 40。

---

## 四、项目结构

```
EoC/
├─ mod/                                  ← 桥接实现（同时用于模组和注入两种方式）
│  ├─ OsiToolsConfig.json                ← 扩展器配置（Lua + OsirisExtensions）
│  ├─ meta.lsx                           ← 模组元数据（必须含 TargetModes → Story）
│  └─ Story/RawFiles/Lua/
│     ├─ BootstrapServer.lua             ← 模组方式入口（Ext.Require）
│     ├─ Console.lua                     ← 注入方式入口（Ext.IO.LoadFile + LoadString）
│     └─ EocTrainer/
│        ├─ Enums.lua                    ← 生成：属性/能力/天赋枚举
│        ├─ Bridge.lua                   ← 文件桥、JSON、日志、ack
│        ├─ State.lua                    ← 采集状态（含游戏内部 PlayerUpgrade 原始值）
│        ├─ Ops.lua                      ← 所有具体操作
│        └─ Main.lua                     ← Tick 轮询、命令去重、会话激活
├─ src/EocTrainer/                       ← C# 控制端
│  ├─ Core/
│  │  ├─ Paths.cs                        ← 路径、设置持久化
│  │  ├─ GameInstall.cs                  ← Steam 探测、扩展器/模组状态
│  │  ├─ ModPackage.cs                   ← 自己实现的 DOS2 .pak 打包器（零依赖）
│  │  ├─ ModDeployer.cs                  ← 打包+部署+登记 modsettings
│  │  ├─ ModSettings.cs                  ← modsettings.lsx 读写（自动备份）
│  │  ├─ ConsoleApi.cs / GameConsole.cs  ← Win32 控制台读写
│  │  ├─ ConsoleChannel.cs               ← 注入激活、Lua 模块投放
│  │  ├─ Bridge.cs                       ← 命令队列、状态轮询、超时重发
│  │  ├─ Models.cs / LenientJson.cs      ← 协议 DTO + 宽容 JSON 解析
│  │  ├─ GameEnums.g.cs / Labels.cs      ← 枚举表 + 中文标签
│  │  └─ Cli.cs                          ← 无界面命令
│  └─ MainForm.cs
├─ tools/
│  ├─ generate_enums.py                  ← 生成枚举表
│  ├─ lua_harness.py                     ← 用 lupa 在游戏外跑 Lua 单元测试
│  ├─ fake_game.py                       ← 假游戏端（联调界面/协议）
│  ├─ mod_pak.py                         ← 独立的 pak 读写/校验工具
│  ├─ probe-*.lua                        ← 游戏内调试探针
│  └─ publish.ps1                        ← 打包单文件 exe
└─ docs/architecture.md                  ← 逆向细节、协议、排错
```

---

## 五、开发与测试

```powershell
# 编译
dotnet build src\EocTrainer\EocTrainer.csproj

# Lua 模组单元测试（不需要游戏）
python tools\lua_harness.py

# 环境自检
EocTrainer.exe diagnose
EocTrainer.exe console-status      # 控制台通道状态
EocTrainer.exe console-probe       # 控制台窗口/模式/内容
EocTrainer.exe console-enable      # 打开 CreateConsole
EocTrainer.exe console-activate    # 立即注入
EocTrainer.exe dump-state          # 打印游戏侧状态

# 部署 / 停用 / 卸载模组
EocTrainer.exe deploy              # 打包 + 写入 + 在所有配置里启用
EocTrainer.exe disable             # 只从配置里移除（保留 pak，便于测免模组模式）
EocTrainer.exe uninstall           # 完全卸载

# 单条操作（开发用）
EocTrainer.exe op setAttribute name=Strength value=20
EocTrainer.exe op addGold amount=5000
EocTrainer.exe op eval "codefile=tools/probe-upgrade.lua"

# 端到端联调（不需要游戏）
python tools\fake_game.py --seconds 120
EocTrainer.exe bridge-test

# 打包单文件版
powershell -File tools\publish.ps1
```

> 编译前先关闭正在运行的 `EocTrainer.exe`；部署 pak 前先关闭游戏（运行中的游戏会锁定 pak）。

---

## 六、常见问题

**Q: 提示「包需要更新」**
游戏运行时 pak 被锁定，关掉游戏再点「安装 / 更新模组」。界面模式下不受影响——
注入用的是 `Osiris Data\EocTrainer\lua\` 里的最新源码。

**Q: 免模组模式下「连接」一直不亮**
1. 确认 `OsirisExtenderSettings.json` 里 `CreateConsole: true`（程序里点「启用控制台」）并**重启过游戏**；
2. 游戏里必须**已经读取存档**（控制台只能在游戏内执行 Lua）；
3. 看游戏控制台窗口（标题 `D:OS2 Script Extender Debug Console`）里有没有
   `EOCINJECT true -> console mode active`；界面「日志 / 调试」页也会显示注入结果。

**Q: 数值改了但角色面板没变**
面板需要重新打开才刷新；数值已经生效并会写进存档。属性页的「面板显示」列可以直接对照。

**Q: 会影响成就吗？**
`OsirisExtenderSettings.json` 里 `EnableAchievements: true`，扩展器会重新启用成就。

**Q: 会和模组冲突吗？**
不会。桥接代码只在 `Tick` 里轮询一个小 JSON 文件，不碰游戏逻辑。
两种接入方式互斥运行（程序会自动判断）。

---

## 八、依赖与致谢

- **[Norbyte's Divinity Script Extender (ositools)](https://github.com/Norbyte/ositools)** — MIT 协议。
  本项目的全部能力都建立在它之上：`Ext.*` Lua API、`NRD_*` 扩展 Osiris 调用、
  以及负责下载扩展器主体的 `dxgi.dll` 自动更新器。没有它这个修改器不可能存在。
- **[Norbyte's LSLib](https://github.com/Norbyte/lslib)** — 用于对照验证 DOS2 的 pak 容器格式
  与 `meta.lsx` 模块元数据结构。本项目自己实现了打包器（`Core/ModPackage.cs`），
  没有链接 LSLib 的代码；`tools/mod_pak.py` 是用于交叉验证的独立实现。
- **[lupa](https://github.com/scoder/lupa)** — 让 `tools/lua_harness.py` 能在游戏外
  用真实 Lua 解释器跑模组的单元测试。
- **《神界：原罪 2》© Larian Studios** — 本项目不包含任何游戏资源文件。
  属性 / 能力 / 天赋的枚举名单是游戏自身的标识符，通过 Script Extender 生成的
  补全文件提取，仅用于调用游戏本身的接口。

## 九、免责声明

- 仅供**单机**学习与个人使用；请勿在联机对战中影响他人体验。
- 修改存档有风险，建议先备份：`文档\Larian Studios\Divinity Original Sin 2 Definitive Edition\PlayerProfiles\<配置>\Savegames`。
- 本项目与 Larian Studios、Norbyte 均无隶属关系。
