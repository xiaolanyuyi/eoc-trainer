# 架构与逆向笔记

这份文档记录的是「为什么这样做」以及从游戏 / 脚本扩展器里挖出来的接口细节，
后续要扩展功能时基本只需要看这里。

---

## 1. 注入链路

```
EoCApp.exe
 ├─ 导入 d3d11.dll → 系统 d3d11.dll 再导入 dxgi.dll
 │     └─ 解析顺序优先应用目录 → 命中 DefEd\bin\dxgi.dll
 │           └─ 这是 Norbyte 的 "D:OS2 Script Extender Auto-Updater DLL" (v3.0.0.0)
 │                 ├─ 读取 https://dbn4nit5dt5fw.cloudfront.net/Channels/Release/Manifest2.json
 │                 ├─ 下载资源包到 %LOCALAPPDATA%\DOS2ScriptExtender\ScriptExtender\<版本>_<摘要>\
 │                 │     内容：OsiExtenderEoCApp.dll / libprotobuf-lite.dll / CrashReporter.exe
 │                 └─ LoadLibraryExW("...\OsiExtenderEoCApp.dll")
 └─ OsiExtenderEoCApp.dll（Script Extender 本体，本机为 v60）
       ├─ 读取同目录（DefEd\bin）的 OsirisExtenderSettings.json
       ├─ 读取各 Mods\<名字_UUID>\OsiToolsConfig.json（FeatureFlags / ModTable）
       ├─ 加载 Mods\<名字_UUID>\Story\RawFiles\Lua\BootstrapServer.lua（服务端）
       └─ 提供 Ext.* Lua API 与 NRD_* 扩展 Osiris 调用
```

要点：

- 扩展器**只有 dxgi.dll 一个文件需要放进游戏目录**，其余自动下载。
- 真正的扩展器 DLL 不在游戏目录，而在用户缓存目录；`extender logs` 目录
  （`文档\Larian Studios\...\Extender Logs`）出现即说明它至少加载过一次。
- 本机验证：`dxgi.dll` 创建于 2026-09-17，缓存里是 v60（`Manifest-Release.json` 里 `MinGameVersion: 3.6.117.0`）。

### 模组配置格式

`Mods/<名字>_<UUID>/OsiToolsConfig.json`：

```json
{ "RequiredExtensionVersion": 60, "ModTable": "EocTrainer",
  "FeatureFlags": ["Lua", "OsirisExtensions"] }
```

- `Lua`：启用 `BootstrapServer.lua` / `BootstrapClient.lua` 加载。
- `OsirisExtensions`：**必须**。`NRD_*` 那批扩展调用受此开关控制
  （源码 `CustomFunctions.cpp` 里 `HasFeatureFlag("OsirisExtensions")`）。
- 模组必须出现在 `modsettings.lsx` 的 `ModOrder` 里，扩展器才会扫描它的配置
  （`ExtensionState::LoadConfigs` 遍历 `BaseModule.LoadOrderedModules`）。

`modsettings.lsx` 每个存档配置一份：`PlayerProfiles\<配置名>\modsettings.lsx`，
需要同时新增 `Mods/ModuleShortDesc`（Folder/Name/UUID/Version）与 `ModOrder/Module`。

---

## 2. 通信协议

Script Extender 的 Lua 沙箱**移除了 `io` / `os` / `package` 库**，但保留了：

```lua
Ext.IO.SaveFile(path, contents)   -- 相对路径，自动建目录
Ext.IO.LoadFile(path, context)    -- context = nil/"user"（默认）或 "data"
Ext.IO.IsFile(path) / IsDirectory(path) / Enumerate(path)
```

根目录为 **GameStorage** = `文档\Larian Studios\Divinity Original Sin 2 Definitive Edition`，
文件实际落在 `<GameStorage>\Osiris Data\<相对路径>`（`Extender Logs` 同理）。
路径只允许 `[A-Za-z0-9-_./ ]`，且不能含 `..`。

于是协议就是两个 JSON 文件（`<GameStorage>\Osiris Data\EocTrainer\`）：

| 文件 | 方向 | 内容 |
|---|---|---|
| `command.json` | 程序 → 游戏 | `{token, seq, host, ops:[{op, target, kind, name, value, amount, ...}]}` |
| `state.json` | 游戏 → 程序 | `{version, token, seq, inGame, ok, errors[], results[], party[], debug{}}` |
| `log.txt` | 游戏 → 程序 | 人类可读的诊断日志（模组侧缓冲后整体覆写） |
| `hello.txt` | 游戏 → 程序 | 会话加载时的探针，用于确认桥接目录可达 |

### 命令去重（很重要）

Lua 状态在每次读档后会被重置，所以「最后执行的命令」不能只存在内存里，否则读档会把
最后一条命令**再执行一次**（例如又加 1000 金币）。做法：

- 程序每次启动生成一个随机 `token`，`seq` 单独递增并持久化在 `%LOCALAPPDATA%\EocTrainer\settings.json`。
- 模组把最后执行的 `(token, seq)` **写回 state.json**。
- 模组启动 / 读档后从 state.json 恢复 `(token, seq)`：
  - `token` 不同 → 新程序实例，照常执行；
  - `token` 相同且 `seq <= ackSeq` → 忽略。
- 程序确认到 ack 后删除 `command.json`（双保险）。

### 写入原子性

- 程序：先写 `command.json.tmp` 再 `File.Move(..., overwrite: true)`。
- 模组：`SaveFile` 是 `ofstream` 直接截断覆写，所以程序侧必须容忍解析失败
  （解析失败时保留上一份状态，250ms 后重试）。

### JSON 细节

- 空 Lua table 会被序列化成 `[]`（`Json.inl: JsonCanStringifyAsArray` 对空表返回 true）。
- 数值字段仍可能以字符串出现（Lua 数字不区分整数/浮点），C# 侧用 `LenientNumberConverter`。
- 字符串字段可能收到数字或表，用 `LenientStringConverter`。
- 列表字段可能收到 `{}`，用 `LenientListConverter`。

程序侧还有一个 JSON 相关的历史坑：`XDocument` 保存时若先 `XmlWriter.WriteRaw`
再 `doc.Save(writer)`，会丢掉根元素（写入 40 字节的空文档）。现在改为先渲染到
内存再拼接 `<?xml ...?>` 声明。

---

## 3. 可用的游戏接口（本机 3.6.117.3735 验证）

### 内置 Osiris 调用（扫描 EoCApp.exe 符号得到）

```
CharacterAddAttributePoint(char, n)        CharacterGetAttributePoints(char)
CharacterAddAbilityPoint(char, n)          CharacterGetAbilityPoints(char)
CharacterAddCivilAbilityPoint(char, n)     CharacterGetCivilAbilityPoints(char)
CharacterAddTalentPoint(char, n)           CharacterGetTalentPoints(char)
CharacterAddSourcePoints(char, n)          CharacterGetSourcePoints(char)
CharacterAddActionPoints(char, n)          CharacterGetBaseSourcePoints(char)
CharacterAddGold(char, n)                  CharacterGetGold(char)
CharacterAddAttribute(char, name, n)       CharacterGetMaxSourcePoints(char)
CharacterResurrect(char)                   CharacterSetHitpointsPercentage(char, pct)
CharacterResetCooldowns(char)              CharacterAddSkill / CharacterApplyStatus
```

> 这些调用的**参数个数**在不同版本可能不同。模组里的 `applyDelta()` 先按
> `(guid, amount)` 调用，失败则退化为逐个 `(guid, 1)` / `(guid)` 调用，
> 并在点数没有实际变化时把警告写回 `state.json.errors`。

### 扩展器新增调用（`NRD_*`）

| 调用 | 作用 |
|---|---|
| `NRD_PlayerSetBaseAttribute(char, "Strength"…, value)` | 设置 6 项属性的等级 |
| `NRD_PlayerSetBaseAbility(char, "WarriorLore"…, value)` | 设置能力等级 |
| `NRD_PlayerSetBaseTalent(char, "Bully"…, 0/1)` | 添加 / 移除天赋 |
| `NRD_CharacterSetStatInt(char, "CurrentVitality"/"CurrentArmor"/"CurrentMagicArmor", v)` | 当前值 |
| `NRD_CharacterSetPermanentBoostInt(char, stat, v)` | 永久增益（写存档） |
| `NRD_CharacterSetPermanentBoostTalent(char, talent, 0/1)` | 永久天赋增益 |
| `NRD_CharacterGetInt/Real/String(char, attr, out)` | 读角色属性 |
| `NRD_CharacterGetStatInt(char, stat, out)` | 读 Stats（Level / Experience / CurrentAP …） |
| `NRD_CharacterGetComputedStat(char, stat, excludeBoosts, out)` | 读计算后数值 |
| `NRD_GetVersion(out)` | 扩展器版本（本机 60） |

**关键坑**：`NRD_PlayerSetBase*` 改完基础值后，客户端不会立即同步，
必须在同一批里补一个空操作的 `CharacterAddCivilAbilityPoint(char, 0)` 强制同步
（扩展器文档明确要求）。模组里封装为 `forceUpgradeSync()`。

### Lua 侧读取角色

```lua
local char = Ext.GetCharacter(guid)          -- 服务端 Character 对象
char.Stats.Strength                          -- 计算后（含装备）
char.Stats.BaseStrength                      -- 基础 + 永久增益 + 天赋加成
char.Stats.WarriorLore / BaseWarriorLore
char.Stats.TALENT_Bully                      -- 天赋用 TALENT_ 前缀
char.PlayerCustomData.Name                   -- 角色名
```

队伍成员枚举用 `Osi.DB_IsPlayer:Get(nil)`（每行 `row[1]` 是 GUID），
主机角色用 `Osi.CharacterGetHostCharacter()`。

### 枚举数据来源

`ExtIdeHelpers.lua`（扩展器内置的 IDE 补全文件，`ScriptExtender\Misc\`）
是按游戏自身枚举生成的，含：

- `CDivinityStatsCharacter` 类字段表（384 项，含全部可读能力 / 天赋字段）
- `--- @alias StatsAbilityType`（41 个能力）
- `--- @alias StatsTalentType`（130 个天赋）

`tools/generate_enums.py` 取两者的交集，生成 `Enums.lua` 与 `GameEnums.g.cs`，
保证两边用的是同一份名单。注意 130 个天赋里有大量内部 / 旧版条目，
界面默认只显示有中文标签的常用天赋。

### 时间与节流

- `Ext.MonotonicTime()` → int64 毫秒（返回整数，`string.format("%d")` 安全）。
- `Ext.RegisterListener("Tick", fn)` 每帧触发（`LuaBinding.cpp: OnUpdate`），
  模组按 120ms 节流轮询；心跳 1s 必写一次状态。
- 事件：`SessionLoading` / `SessionLoaded` / `ModuleLoading` / `ModuleLoaded` /
  `ResetCompleted` / `Tick`。

### 其它已确认但暂未使用的通道

- `Ext.RegisterConsoleCommand("cmd", fn)` + 控制台（需 `CreateConsole: true` 才会
  在游戏进程里 `AllocConsole`），外部程序可用 `AttachConsole(pid)` +
  `WriteConsoleInput` 注入命令，延迟更低但会多一个控制台窗口。
- Lua 调试器（`EnableLuaDebugger`, 端口 9998）支持 `DbgEvaluate` protobuf 协议，
  可以做真正的 RPC，但需要实现 protobuf 客户端且依赖暂停语义。
- `Ext.L10N.GetTranslatedStringFromKey(key)` 可在游戏内解析本地化文本，
  界面上的「探测游戏内显示名」按钮就是发这个命令（用于补全中文名）。

---

## 4. 文件格式备注

`modsettings.lsx` 是 Larian 的 LSX（XML）格式，UTF-8 带 BOM，声明写作
`<?xml version="1.0" encoding="UTF-8" ?>`。写入时保留 BOM、4 空格缩进、
`\n` 换行，并先备份为 `<文件>.eoctrainer.bak`（只在第一次写入时创建，不会覆盖已有备份）。

存档是 `.lsv`（zlib + LSF/LSX 混合容器），本次没有用上；如果以后要做离线存档修改器，
可以从 `Norbyte/lslib` 的 `LSV`/`LSF` 实现入手。

---

## 5. 测试策略

| 层次 | 工具 | 覆盖内容 |
|---|---|---|
| Lua 单元测试 | `tools/lua_harness.py`（lupa） | 用 mock 的 `Ext`/`Osi` 跑真 Lua：命令执行、参数、去重、错误处理、日志、心跳 |
| 协议联调 | `tools/fake_game.py` + `EocTrainer.exe bridge-test` | 真实 JSON 文件往返、C# 反序列化（含中文）、命令队列与 ack |
| 界面 | `EocTrainer.exe --tab=N` + Win32 `PrintWindow` 截图 | 各标签页布局与数据绑定 |
| 真机 | 启动游戏读取存档 | 扩展器加载、`Ext.IO` 路径、`NRD_*` 调用是否生效 |

mock 的注意点：lupa 的 `table_from` 不递归转换嵌套容器，harness 里用
`to_lua()` / `lua_to_py()` 手动递归；空表按扩展器的行为转成 `[]`。

---

## 6. 免模组模式（控制台注入）—— 实现细节

### 6.1 为什么可以"没有模组"

Script Extender 的 \Ext.*\ API 与 Osiris 调用都存在于**游戏进程的 Lua 状态**里，
模组只是"加载 Lua 代码的容器"。控制台注入把同样的代码直接送进那个 Lua 状态：

1. \EocApp.exe\ 启动时，若 \OsirisExtenderSettings.json\ 里 \CreateConsole: true\，
   扩展器会 \AllocConsole()\ 并在游戏进程里起一个 REPL 线程（\Console.cpp\）。
2. REPL 逻辑：先等一个单独的 Enter 从"日志模式"切到"输入模式"，
   之后每行用 \std::getline\ 读入并 \luaL_loadstring\ 执行；
   输入 \--[[\ 进入多行模式，\]]--\ 时把累积的内容整体执行。
   **注意：切换模式那一下会丢掉该行内容，所以第一次必须只发一个空行。**
3. 程序用 \AttachConsole(pid)\ + \WriteConsoleInputW\ 把命令"打字"进去；
   读取用 \ReadConsoleOutputCharacterW\。

### 6.2 GUI 程序的坑

\AttachConsole\ 之后 \GetStdHandle(STD_INPUT/OUTPUT_HANDLE)\ 在 **GUI 进程里拿到的句柄是无效的**
（表现为：读出来全空、写入报"缓冲区满"——实际是 ERROR_INVALID_HANDLE）。
必须直接 \CreateFileW("CONIN$" / "CONOUT$")\。这是这次踩到的最大的坑。

另外 \WriteConsoleInputW\ 可能**部分写入**，必须按返回值推进偏移重试，否则会死循环。

### 6.3 注入的代码

注入内容只有十几行：从 \Osiris Data\EocTrainer\lua\ 读 \Console.lua\、
\Ext.Utils.LoadString\ 编译后执行。\Console.lua\ 再依次加载
\Enums/Bridge/State/Ops/Main.lua\——**和模组用的是同一份源码**，所以两边行为天然一致。
\Console.lua\ 会先检查 \Osi.NRD_IsModLoaded(MOD_UUID)\，模组在跑就让位。

因为读档会重建 Lua 状态，程序每 5 秒检查一次 \state.json\ 是否过期，过期就重新注入。

### 6.4 OsirisExtensions 与 NRD_* 调用

\NRD_PlayerSetBaseAttribute\ 这类精确写入接口**只有在某个模块的 \OsiToolsConfig.json\
里声明了 \OsirisExtensions\ 时才会注册**（\CustomFunctions.cpp\）。
模组模式下由模组自己的配置提供；免模组模式下程序把这行写进**基础模块**的
\Data\Mods\DivinityOrigins_<uuid>/OsiToolsConfig.json\——基础模块永远在加载顺序里，
所以不需要启用任何模组。写入后**需要重启一次游戏**才会注册。

> 这一点是实测发现的：第一版免模组模式里加钱/加点正常，
> 但设置属性报 \No function named 'NRD_PlayerSetBaseAttribute' exists that can be called with 3 parameters.\

### 6.5 游戏内部原始值（为什么界面有两列）

\EocPlayerUpgrade\ 通过 Lua 属性映射可以读到：

| 字段 | 含义 |
|---|---|
| \Attributes int32[]\ | 六个属性的**原始设置值**（[1..6] = 力量,敏捷,智力,体质,记忆,智慧，实测确认） |
| \Abilities int32[]\ | 能力原始值（[1] = WarriorLore，实测确认） |
| \AttributePoints\ / \CombatAbilityPoints\ / \CivilAbilityPoints\ / \TalentPoints\ | 未分配点数池 |
| \IsCustom\ | 被修改器写过之后为 true |

角色面板显示的是 \Stats.Base*\（基础 + 永久增益 + 天赋加成）。
无加成时两者相等（实测 15→15、25→25、33→33）；
开了**独狼**后属性满足 \显示 = 2×设置 − 10\（上限 40），能力也会被放大。
所以界面同时显示"设置值"和"面板显示"，避免误判设置失败。

---

## 7. DOS2 的 .pak 格式（自己实现打包器时逆向所得）

`
[文件数据，每个文件按 64 字节对齐（填充 0xAD）]
[文件列表]  uint32 数量 + LZ4 块（每条目 280 字节）
[头部 32 字节] version(u32)=13 fileListOffset(u32) fileListSize(u32)
              numParts(u16) flags(u8) priority(u8) md5(16)
[uint32]    头部总长（含本字段与魔数）= 40
[4 字节]    "LSPK"      ← 魔数在文件末尾！
`

条目（280 字节）：256 字节 NUL 结尾路径 + \offset/sizeOnDisk/uncompressedSize/archivePart/flags/crc32\。
\lags = 0x42\ 表示 LZ4 + "max" 标记；\crc32\ 是**压缩后**数据的 CRC32（与 zlib.crc32 一致）。

- pak 内部路径必须是 \Mods/<文件夹名>/...\，且 pak 文件名必须是 \<文件夹名>.pak\。
- 模组是否被游戏识别取决于 pak 里的 \meta.lsx\：
  - 缺 \TargetModes → Target → Object="Story"\ 时，游戏**不会**把它列进模组列表（就是最初的失败原因）；
  - \Dependencies\ 用空节点（\<node id="Dependencies" />\）；
  - \Type\ 的 \	ype="22"\、\NumPlayers\ 的 \	ype="1"\ 要和游戏自家的一致。
- 头部 md5 写全 0 即可（游戏自带 pak 也是全 0；模块校验已被 \DisableModValidation\ 关掉）。
- 打包器用"纯字面量 LZ4 块"编码压缩数据，零第三方依赖（\Core/ModPackage.cs\）；
  \	ools/mod_pak.py\ 是独立的 Python 读写/校验实现，可用来交叉验证。
- **松散文件夹不行**：\Mods/<文件夹>/meta.lsx\ 不会被游戏枚举（实测：装了合法 meta.lsx
  的目录在模组列表里不出现，而同样内容的 pak 立刻出现）。
