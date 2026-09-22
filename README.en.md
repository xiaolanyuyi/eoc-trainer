# EoC Trainer · Divinity: Original Sin 2 Runtime Trainer

[中文](README.md) | [English](README.en.md)

A **runtime stat trainer** for *Divinity: Original Sin 2 - Definitive Edition*: edit your party's
attributes, abilities, talents, unspent points and gold while the game is running. Changes apply
immediately and are stored in the savegame.

**[⬇ Download the latest release](https://github.com/xiaolanyuyi/eoc-trainer/releases/latest)** ·
[Usage](#install--usage) · [Troubleshooting](#troubleshooting) · [Internals](docs/architecture.md)

- **Exact writes, not guesswork** — it calls the game's own Osiris functions, so setting Strength
  to 15 stores 15 in the game's internal field. No memory scanning, no address hunting.
- **Two ways to attach** — install a tiny mod package, or **install no mod at all** and let the app
  inject the bridge code through the Script Extender's console.
- **Self-contained packer** — the DOS2 `.pak` writer is implemented from scratch, no third party
  dependency.
- **Testable** — Lua unit tests that run outside the game (real Lua via lupa), an independent pak
  verification tool, and a fake game backend for UI work.

![Points tab](docs/screenshots/01-points.png)

> The UI is currently **Chinese only**. The screenshots above show the layout: party list on the
> left, feature tabs on the right.

---

## Table of contents

- [Screenshots](#screenshots)
- [What it can change](#what-it-can-change)
- [How it works](#how-it-works)
- [Install & usage](#install--usage)
- [Troubleshooting](#troubleshooting)
- [Building from source](#building-from-source)
- [Project layout](#project-layout)
- [Credits](#credits)
- [Disclaimer](#disclaimer)

---

## Screenshots

![Attributes tab](docs/screenshots/02-attributes.png)

The **attributes** tab shows two columns on purpose:

- `设置值` (stored value) — what the game keeps internally, and what the trainer writes.
- `面板显示` (sheet value) — what the character sheet shows, including equipment and talent bonuses.

They are usually identical. With the **Lone Wolf** talent the game doubles attributes above 10
(capped at 40), so setting 15 shows 20 and setting 25 shows 40 — having both columns makes it
obvious the write succeeded.

![Abilities tab](docs/screenshots/03-abilities.png)

**Abilities** covers all 41 combat / civil abilities, with bulk ±1 and exact per-row values.

![Other tab](docs/screenshots/05-other.png)

**Other** holds gold, health / armour / magic armour, source points, action points, permanent
boosts, resurrection and cooldown reset.

> Also included: a **talents** tab ([screenshot](docs/screenshots/04-talents.png), checkbox list with
> a filter box) and a **log / debug** tab ([screenshot](docs/screenshots/06-log.png) with the
> in-game log and an arbitrary-Lua console).
>
> The characters in the screenshots are fake test data (`tools/fake_game.py`), so the UI can be
> explored without starting the game.

## What it can change

| Category | Operation | Underlying call |
|---|---|---|
| Unspent points | add / remove / set attribute, combat ability, civil ability and talent points | `CharacterAddAttributePoint` etc. |
| Attributes | set Strength, Finesse, Intelligence, Constitution, Memory, Wits exactly (1–40) | `NRD_PlayerSetBaseAttribute` |
| Combat / civil abilities | set any of the 41 ability levels exactly | `NRD_PlayerSetBaseAbility` |
| Talents | add / remove any talent (including racial ones) | `NRD_PlayerSetBaseTalent` |
| Gold | add / subtract / set | `CharacterAddGold` |
| Health / physical armour / magic armour | set or refill | `NRD_CharacterSetStatInt` |
| Source points / action points | add / subtract | `CharacterAddSourcePoints` / `CharacterAddActionPoints` |
| Permanent boosts | 40+ stats such as resistances or damage bonuses (saved to the savegame) | `NRD_CharacterSetPermanentBoostInt` |
| Misc | revive a character, reset skill cooldowns | `CharacterResurrect` / `CharacterResetCooldowns` |
| Debug | run arbitrary Lua in the game's server state | `Ext.Utils.LoadString` |

Only **player characters** (your four party members) can be edited; NPCs are not supported.

## How it works

```
┌─────────────────────┐   command.json    ┌──────────────────────────────────┐
│   EocTrainer.exe    │ ────────────────► │  EoCApp.exe (game process)       │
│   C# WinForms app   │                   │   └─ Script Extender (required)  │
│                     │ ◄──────────────── │        ├─ ① Lua mod (.pak)       │
└─────────────────────┘    state.json     │        └─ ② same Lua, injected   │
                                          └──────────────────────────────────┘
```

The desktop app never touches the game process. It renders the UI, turns button presses into
commands, and reads/writes a small JSON file. The code that actually edits stats is **Lua running
inside the game process**, which calls the game's own Osiris API.

| Attach mode | Needs a mod | Needs config changes | New profiles | When to pick it |
|---|---|---|---|---|
| **① Mod package** | ✅ one `.pak` (installed by the app) | yes (`modsettings.lsx`, done automatically) | needs one button press | most robust |
| **② Console injection** | ❌ none | one-time `CreateConsole` flag | works immediately | no mod / no restart wanted |

The app picks automatically: if the mod is doing the work it stays out of the way, otherwise it
injects the bridge code through the console. Both poll the command file every 120 ms, so edits feel
instant.

> Implementation details (DOS2 `.pak` layout, console injection pitfalls, the game's internal
> `PlayerUpgrade` structure, the command de-duplication protocol) live in
> [`docs/architecture.md`](docs/architecture.md) (Chinese).

## Install & usage

### 0. Requirements

| Requirement | Notes |
|---|---|
| The game | Steam version, **Definitive Edition**. The Classic edition is not supported |
| [Norbyte's Script Extender](https://github.com/Norbyte/ositools/releases/latest) | required, see below |
| .NET | **not** needed for the released exe; .NET 10 SDK is needed to build from source |

#### Installing the Script Extender

1. Download the latest release from [ositools releases](https://github.com/Norbyte/ositools/releases/latest).
2. Put `dxgi.dll` from the archive into the game's `DefEd\bin\` folder (next to `EoCApp.exe`,
   usually `steamapps\common\Divinity Original Sin 2\DefEd\bin\`).
3. Launch the game once — the extender downloads its payload automatically. Nothing else to do.

The app checks this for you and shows `Script Extender：已就绪 (v60.0.0.0)` in its header.

### 1. Start the trainer

Download from [Releases](https://github.com/xiaolanyuyi/eoc-trainer/releases/latest) and run it (or
build it yourself). It locates the game through the Steam library folders; if it can't, use
「选择游戏目录…」 and point it at the `Divinity Original Sin 2` root.

### 2. Pick an attach mode

- **Mod mode** → click 「安装 / 更新模组」. The app packages the mod into a `.pak`, writes it to
  `Documents\Larian Studios\Divinity Original Sin 2 Definitive Edition\Mods\`, enables it in every
  profile's `modsettings.lsx` (backing the file up first) and enables the extender's
  `OsirisExtensions` feature. Then **restart the game once**.
- **No-mod mode** → click 「启用控制台」 and **restart the game once**. The Script Extender's debug
  console window will appear when the game starts (you can hide it from the app).

### 3. Play

Start the game, **load a save**, switch back to the trainer: the header shows
`连接：已连接（模组）` or `连接：已连接（控制台注入）` and the party list appears on the left. Edit away.

> - After updating the mod package the game must be restarted; in no-mod mode the app re-injects
>   automatically after every savegame load (loading a save recreates the game's Lua state).
> - The character sheet may only refresh when you reopen it — the values themselves are already
>   applied and will be saved.
> - Only player characters can be edited.

### Uninstalling

「卸载模组」 removes the mod from `modsettings.lsx`, deletes the `.pak` and cleans up the extender
feature flags. Original `modsettings.lsx` backups are kept as `*.eoctrainer.bak` next to the file.

## Troubleshooting

**The header keeps saying it is waiting for the bridge**

- Mod mode: make sure the mod is installed, the game was **restarted** afterwards, and a save is loaded.
- No-mod mode: make sure 「启用控制台」 was clicked and the game restarted; a save must be loaded
  (the console can only execute Lua while in game).
- Check the 「日志 / 调试」 tab: it shows the in-game log and the result of every operation. A
  successful no-mod injection reports `console mode active`.

**A button did nothing visible**
Reopen the character sheet — it only redraws on open. The log tab shows per-operation errors, e.g.
"called CharacterAddAttributePoint but the point count did not change" means the character is not a
player character or the value is capped.

**"Package needs updating"**
The `.pak` is locked while the game runs; close the game and click 「安装 / 更新模组」 again.
No-mod mode is unaffected (it uses the latest source).

**Does it affect achievements?**
The extender config written by the app sets `EnableAchievements: true`, so the Script Extender
re-enables them.

**Will it conflict with other mods?**
No. The bridge only polls a small JSON file from the game's `Tick` handler; it does not touch game
logic or modify game data files.

**I broke my save?**
Back up saves first: `Documents\Larian Studios\Divinity Original Sin 2 Definitive Edition\PlayerProfiles\<profile>\Savegames`.
The trainer only writes stat fields and never changes the savegame structure.

## Building from source

```powershell
dotnet build src\EocTrainer\EocTrainer.csproj          # build
powershell -File tools\publish.ps1                    # single-file exe (needs .NET runtime)
powershell -File tools\publish.ps1 -SelfContained      # fully standalone (~60 MB)

python tools\lua_harness.py                           # Lua unit tests (real Lua via lupa, no game needed)
python tools\fake_game.py --seconds 300                # fake game backend for UI/protocol work
powershell -File tools\capture-screenshots.ps1         # regenerate the README screenshots

EocTrainer.exe diagnose            # environment check (game path / extender / mod status)
EocTrainer.exe dump-state          # print the in-game state
EocTrainer.exe op setAttribute name=Strength value=20   # send a single operation
EocTrainer.exe deploy | disable | uninstall            # mod lifecycle
```

After changing the Lua sources, run `deploy` again (mod mode); no-mod mode always deploys the latest
sources on injection.

<details>
<summary>If your network blocks git traffic</summary>

Some networks reset the git smart-HTTP endpoint on `github.com:443` while the web UI and the API
still work. In that case `tools\publish-via-api.ps1` uploads the current commit through the REST
API:

```powershell
$env:GH_TOKEN = '<token with repo scope>'
powershell -File tools\publish-via-api.ps1 -Owner <user> -Repo <repo>
```

It uploads every file straight from the local object database (`git cat-file`), so the remote files
are byte identical to the local ones (same tree SHA). Only the commit object's own SHA may differ,
because GitHub trims leading/trailing whitespace from commit messages.
</details>

## Project layout

```
├─ mod/                                  ← Lua bridge injected into the game (shared by both modes)
│  ├─ OsiToolsConfig.json                ← extender config (Lua + OsirisExtensions)
│  ├─ meta.lsx                           ← module metadata (without TargetModes the game ignores it)
│  └─ Story/RawFiles/Lua/
│     ├─ BootstrapServer.lua             ← mod-mode entry point (Ext.Require)
│     ├─ Console.lua                     ← injection-mode entry point (Ext.IO.LoadFile + LoadString)
│     └─ EocTrainer/
│        ├─ Enums.lua                    ← generated attribute/ability/talent enums
│        ├─ Bridge.lua                   ← file bridge, JSON, logging, command de-duplication
│        ├─ State.lua                    ← state collection (raw PlayerUpgrade values)
│        ├─ Ops.lua                      ← all stat operations
│        └─ Main.lua                     ← tick polling, session activation
├─ src/EocTrainer/                       ← C# WinForms front end
│  └─ Core/
│     ├─ GameInstall.cs                  ← Steam detection, extender/mod status, package freshness
│     ├─ ModPackage.cs                   ← from-scratch DOS2 .pak writer (no dependencies)
│     ├─ ModDeployer.cs / ModSettings.cs ← mod installation, modsettings.lsx editing (with backups)
│     ├─ ConsoleApi.cs / GameConsole.cs  ← Win32 console access (injection channel)
│     ├─ ConsoleChannel.cs               ← injection activation and Lua deployment
│     ├─ Bridge.cs                       ← command queue, state polling, retries
│     └─ Models.cs / LenientJson.cs      ← protocol DTOs and tolerant JSON parsing
├─ tools/                                ← development and verification tools
└─ docs/architecture.md                  ← reverse engineering notes (Chinese)
```

## Credits

- **[Norbyte's Divinity Script Extender (ositools)](https://github.com/Norbyte/ositools)** (MIT) —
  everything here builds on it: the `Ext.*` Lua API, the `NRD_*` Osiris extensions and the
  `dxgi.dll` auto-updater that fetches the extender payload. Without it this trainer could not exist.
- **[Norbyte's LSLib](https://github.com/Norbyte/lslib)** — used to cross-check the DOS2 pak
  container format and the `meta.lsx` module metadata. The packer here is an independent
  implementation (`Core/ModPackage.cs`); `tools/mod_pak.py` is a separate verifier.
- **[lupa](https://github.com/scoder/lupa)** — lets `tools/lua_harness.py` run the mod's Lua unit
  tests outside the game.
- **Divinity: Original Sin 2 © Larian Studios** — this project contains no game assets. The
  attribute / ability / talent name lists are the game's own identifiers, extracted from the helper
  file the Script Extender generates, and are only used to call the game's own API.

## Disclaimer

- For **single player** learning and personal use only; please don't spoil other players' sessions
  in multiplayer.
- Editing saves carries risk — keep a backup.
- Not affiliated with Larian Studios or Norbyte.
- License: [MIT](LICENSE)
