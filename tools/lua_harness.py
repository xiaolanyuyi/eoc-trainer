"""Runs the Lua mod outside the game against a mock Ext/Osi environment.

This is the fastest way to catch runtime errors in the mod without launching
Divinity: the harness implements the handful of Script Extender functions the
mod uses (Ext.IO, Ext.Json, Ext.RegisterListener, Ext.GetCharacter, tick events)
and records every Osiris call so the test can assert on them.

Usage:
    python tools/lua_harness.py            # run the built-in scenario tests
    python tools/lua_harness.py --keep     # keep the temporary bridge directory
"""

import json
import os
import shutil
import sys
import tempfile

import lupa

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
LUA_DIR = os.path.join(ROOT, "mod", "Story", "RawFiles", "Lua")

ATTRIBUTES = ["Strength", "Finesse", "Intelligence", "Constitution", "Memory", "Wits"]
ABILITIES = ["WarriorLore", "RangerLore", "Barter", "Luck"]
TALENTS = ["Bully", "AttackOfOpportunity", "LoneWolf"]


class MockGame:
    """A tiny stand-in for the game's Lua environment."""

    def __init__(self, bridge=None):
        self.bridge = bridge or tempfile.mkdtemp(prefix="eoctrainer-bridge-")
        self.calls = []
        self.listeners = {}
        self.clock = 0.0
        self.characters = {}
        self.roster = {}
        self.lua = lupa.LuaRuntime(unpack_returned_tuples=True, register_eval=False)
        self._install()

    # ------------------------------------------------------------------ setup

    def to_lua(self, obj):
        """Recursively converts Python containers into real Lua tables."""
        if isinstance(obj, dict):
            table = self.lua.table_from(obj)
            for key, value in list(obj.items()):
                table[key] = self.to_lua(value)
            return table
        if isinstance(obj, (list, tuple)):
            table = self.lua.table()
            for index, value in enumerate(obj, start=1):
                table[index] = self.to_lua(value)
            return table
        return obj

    def _install(self):
        lua = self.lua
        lua.execute("EocTrainer = nil; Ext = {}; Osi = {}; Mods = {}")

        ext = lua.table_from({})
        lua.globals().Ext = ext

        ext["IO"] = self._make_io()
        ext["Json"] = self._make_json()
        ext["Utils"] = self._make_utils()
        ext["L10N"] = self._make_l10n()

        ext["MonotonicTime"] = lambda: self._tick_clock()
        ext["RegisterListener"] = self._register_listener
        ext["Require"] = self._require
        ext["Print"] = lambda *a: print("[lua]", *a)
        ext["PrintError"] = lambda *a: print("[lua error]", *a)
        ext["PrintWarning"] = lambda *a: print("[lua warn]", *a)
        ext["GetCharacter"] = self._get_character

        lua.globals().Osi = self._make_osi()

    def _tick_clock(self):
        self.clock += 100.0
        return self.clock

    def _make_io(self):
        bridge = self.bridge

        def path(name):
            return os.path.join(bridge, *name.split("/"))

        def save(name, contents, *_):
            target = path(name)
            os.makedirs(os.path.dirname(target), exist_ok=True)
            with open(target, "w", encoding="utf-8", newline="\n") as handle:
                handle.write(contents)
            return True

        def load(name, *_):
            target = path(name)
            if not os.path.isfile(target):
                return None
            with open(target, encoding="utf-8") as handle:
                return handle.read()

        def is_file(name):
            return os.path.isfile(path(name))

        def is_dir(name):
            return os.path.isdir(path(name))

        table = self.lua.table_from({"SaveFile": save, "LoadFile": load, "IsFile": is_file, "IsDirectory": is_dir})
        return table

    def _make_json(self):
        # Both directions are handled in Python: Lua tables are converted
        # recursively, which keeps the harness independent of the real
        # (sorting) implementation inside the Script Extender.
        def stringify(value, *_):
            return json.dumps(self.lua_to_py(value), ensure_ascii=False)

        def parse(text, *_):
            if text is None:
                return None
            return self.to_lua(json.loads(text))

        return self.lua.table_from({"Stringify": stringify, "Parse": parse})

    def lua_to_py(self, value):
        """Recursively converts Lua values into Python containers.

        An empty Lua table becomes an empty JSON array: this mirrors the Script
        Extender's own writer, which treats an empty table as an array.
        """
        if lupa.lua_type(value) != "table":
            return value

        items = list(value.items())
        if not items:
            return []

        if all(isinstance(key, (int, float)) for key, _ in items):
            ordered = sorted(items, key=lambda pair: pair[0])
            return [self.lua_to_py(item) for _, item in ordered]

        return {str(key): self.lua_to_py(item) for key, item in items}

    def _make_utils(self):
        def game_version():
            return self.lua.table_from({"Major": 3, "Minor": 6, "Revision": 117, "Build": 3735})

        def load_string(code):
            loader = self.lua.eval("function(code) return load(code) end")
            return loader(code)

        return self.lua.table_from({
            "GameVersion": game_version,
            "LoadString": load_string,
            "Print": lambda *a: print("[lua]", *a),
        })

    def _make_l10n(self):
        return self.lua.table_from({
            "GetTranslatedStringFromKey": lambda key: None,
            "GetTranslatedString": lambda *a: None,
        })

    def _register_listener(self, name, handler):
        self.listeners.setdefault(name, []).append(handler)

    def _require(self, name):
        rel = name.replace("/", os.sep)
        path = os.path.join(LUA_DIR, rel)
        if not os.path.isfile(path):
            raise RuntimeError("Ext.Require: missing %s" % path)
        with open(path, encoding="utf-8") as handle:
            source = handle.read()
        chunk = self.lua.eval("function(src, name) return load(src, '@' .. name) end")(source, rel)
        if chunk is None:
            raise RuntimeError("Ext.Require: syntax error in %s" % rel)
        chunk()
        return 0

    # -------------------------------------------------------------- osiris

    def _make_osi(self):
        class OsiProxy:
            def __init__(self, game):
                self.game = game

            def __getitem__(self, key):
                if key == "DB_IsPlayer":
                    return self.game._make_db("IsPlayer")
                return self.game._make_osi_call(key)

        return OsiProxy(self)

    def _make_db(self, name):
        game = self

        class Db:
            def __getitem__(self, key):
                if key == "Get":
                    def get(*args):
                        rows = game.lua.table()
                        for index, guid in enumerate(game.characters, start=1):
                            rows[index] = game.to_lua([guid])
                        return rows
                    return get
                if key in ("Delete", "Insert"):
                    return lambda *args: None
                raise KeyError(key)

        return Db()

    def _make_osi_call(self, name):
        game = self

        def call(*args):
            game.calls.append((name, args))
            guid = args[0] if args else None
            entry = game.roster.get(guid) if isinstance(guid, str) else None

            if name == "CharacterGetHostCharacter":
                return next(iter(game.roster), None)
            if name == "CharacterGetGold":
                return entry["gold"] if entry else 1234
            if name == "CharacterGetAttributePoints":
                return entry["points"]["attribute"] if entry else 0
            if name == "CharacterGetAbilityPoints":
                return entry["points"]["combatAbility"] if entry else 0
            if name == "CharacterGetCivilAbilityPoints":
                return entry["points"]["civilAbility"] if entry else 0
            if name == "CharacterGetTalentPoints":
                return entry["points"]["talent"] if entry else 0
            if name == "NRD_GetVersion":
                return 60

            game.mutate(name, args)
            return None

        return call

    def mutate(self, name, args):
        """Applies a mutating Osiris call to the roster, like the game would."""
        guid = args[0] if args else None
        entry = self.roster.get(guid) if isinstance(guid, str) else None
        if entry is None or len(args) < 2:
            return

        if name == "CharacterAddAttributePoint":
            entry["points"]["attribute"] += args[1]
        elif name == "CharacterAddAbilityPoint":
            entry["points"]["combatAbility"] += args[1]
        elif name == "CharacterAddCivilAbilityPoint" and args[1] != 0:
            entry["points"]["civilAbility"] += args[1]
        elif name == "CharacterAddTalentPoint":
            entry["points"]["talent"] += args[1]
        elif name == "CharacterAddGold":
            entry["gold"] += args[1]
        elif name == "CharacterAddSourcePoints":
            entry["vitals"]["source"] += args[1]
        elif name == "NRD_PlayerSetBaseAttribute":
            entry["attributes"][args[1]] = args[2]
        elif name == "NRD_PlayerSetBaseAbility":
            entry["abilities"][args[1]] = args[2]
        elif name == "NRD_PlayerSetBaseTalent":
            if args[2] != 0:
                entry["talents"].add(args[1])
            else:
                entry["talents"].discard(args[1])
        elif name == "NRD_CharacterSetStatInt":
            key = {"CurrentVitality": "vitality", "CurrentArmor": "armor",
                   "CurrentMagicArmor": "magicArmor"}[args[1]]
            entry["vitals"][key] = args[2]
        elif name == "CharacterResurrect":
            entry["dead"] = False

    def apply_calls(self):
        """Returns how many Osiris calls have been recorded since the last call."""
        consumed = len(self.calls)
        self.calls.clear()
        return consumed

    def _get_character(self, guid, *args):
        if guid not in self.roster:
            return None
        self.refresh_characters()
        return self.characters[guid]

    # ------------------------------------------------------------ scenarios

    def add_character(self, guid, name, level=3, talents=("Bully",)):
        entry = {
            "name": name,
            "level": level,
            "experience": 1200,
            "gold": 1234,
            "dead": False,
            "attributes": {},
            "abilities": {},
            "talents": set(talents),
            "points": {"attribute": 3, "combatAbility": 1, "civilAbility": 2, "talent": 1},
            "vitals": {
                "vitality": 120, "maxVitality": 150,
                "armor": 10, "maxArmor": 20,
                "magicArmor": 5, "maxMagicArmor": 15,
                "ap": 2, "maxAp": 6,
                "source": 1, "maxSource": 2,
            },
        }
        for index, attribute in enumerate(ATTRIBUTES):
            entry["attributes"][attribute] = 10 + index
        for index, ability in enumerate(ABILITIES):
            entry["abilities"][ability] = index

        self.roster[guid] = entry
        self.refresh_characters()
        return self.characters[guid]

    def refresh_characters(self):
        """Rebuilds the Lua side character objects from the Python roster."""
        self.characters = {}
        for guid, entry in self.roster.items():
            stats = dict(entry["attributes"])
            for key, value in entry["attributes"].items():
                stats["Base" + key] = value
            for key, value in entry["abilities"].items():
                stats["Base" + key] = value
            stats.update(entry["abilities"])
            for talent in TALENTS:
                stats["TALENT_" + talent] = talent in entry["talents"]

            stats.update({
                "Level": entry["level"],
                "Experience": entry["experience"],
                "CurrentVitality": entry["vitals"]["vitality"],
                "MaxVitality": entry["vitals"]["maxVitality"],
                "CurrentArmor": entry["vitals"]["armor"],
                "MaxArmor": entry["vitals"]["maxArmor"],
                "CurrentMagicArmor": entry["vitals"]["magicArmor"],
                "MaxMagicArmor": entry["vitals"]["maxMagicArmor"],
                "CurrentAP": entry["vitals"]["ap"],
                "APMaximum": entry["vitals"]["maxAp"],
                "MagicPoints": entry["vitals"]["source"],
                "MaxMp": entry["vitals"]["maxSource"],
            })

            self.characters[guid] = self.to_lua({
                "MyGuid": guid,
                "IsPlayer": True,
                "InParty": True,
                "Dead": entry["dead"],
                "Stats": stats,
                "PlayerCustomData": {"Name": entry["name"]},
            })

    def apply_calls(self):
        """Returns how many Osiris calls have been recorded since the last call."""
        consumed = len(self.calls)
        self.calls.clear()
        return consumed

    def require_mod(self):
        self.lua.execute('EocTrainer = nil')
        self._require("BootstrapServer.lua")

    def fire(self, event):
        for handler in self.listeners.get(event, []):
            handler()

    def fire_ticks(self, count=6):
        for _ in range(count):
            self.fire("Tick")

    def read_state(self):
        path = os.path.join(self.bridge, "EocTrainer", "state.json")
        if not os.path.isfile(path):
            return None
        with open(path, encoding="utf-8") as handle:
            return json.load(handle)

    def write_command(self, token, seq, ops, host=None):
        doc = {"token": token, "seq": seq, "ops": ops}
        if host:
            doc["host"] = host
        path = os.path.join(self.bridge, "EocTrainer", "command.json")
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, "w", encoding="utf-8") as handle:
            handle.write(json.dumps(doc))

    def read_log(self):
        path = os.path.join(self.bridge, "EocTrainer", "log.txt")
        if not os.path.isfile(path):
            return ""
        with open(path, encoding="utf-8") as handle:
            return handle.read()

    def calls_of(self, name):
        return [args for (call_name, args) in self.calls if call_name == name]


FAILURES = []


def check(label, condition, detail=""):
    if condition:
        print("  PASS  %s" % label)
    else:
        FAILURES.append(label)
        print("  FAIL  %s %s" % (label, detail))


def main():
    keep = "--keep" in sys.argv
    game = MockGame()
    guid_a = "11111111-1111-1111-1111-111111111111"
    guid_b = "22222222-2222-2222-2222-222222222222"
    game.add_character(guid_a, "Red Prince", talents=("Bully",))
    game.add_character(guid_b, "Sebille", level=4, talents=("AttackOfOpportunity", "LoneWolf"))

    print("bridge directory: %s" % game.bridge)
    print("\n== 1. bootstrap ==")
    game.require_mod()
    check("bootstrap loads without errors", True)
    check("Tick listener registered", "Tick" in game.listeners, str(game.listeners.keys()))
    check("SessionLoaded listener registered", "SessionLoaded" in game.listeners)

    print("\n== 2. session loaded -> state.json ==")
    game.fire("SessionLoaded")
    state = game.read_state()
    check("state.json written", state is not None)
    if state:
        check("party has two members", len(state["party"]) == 2, str(state["party"]))
        first = state["party"][0]
        check("character name read", first["name"] == "Red Prince", first.get("name"))
        check("base attribute read", first["baseAttributes"]["Strength"] == 10,
              str(first["baseAttributes"]))
        check("ability read", first["baseAbilities"]["WarriorLore"] == 0, str(first["baseAbilities"]))
        check("talents read", first["talents"] == ["Bully"], str(first["talents"]))
        check("point pools read", first["points"]["attribute"] == 3, str(first["points"]))
        check("gold read", first["gold"] == 1234, str(first.get("gold")))
        check("vitals read", first["vitals"]["maxVitality"] == 150, str(first["vitals"]))

    print("\n== 3. executing a command ==")
    game.calls.clear()
    token = "token-alpha"
    game.write_command(token, 1, [
        {"op": "addPoint", "kind": "attribute", "amount": 2},
        {"op": "addPoint", "kind": "combatAbility", "amount": 1},
        {"op": "addPoint", "kind": "talent", "amount": 1},
        {"op": "setAttribute", "target": guid_b, "name": "Strength", "value": 20},
        {"op": "setAbility", "target": guid_a, "name": "WarriorLore", "value": 5},
        {"op": "setTalent", "target": guid_a, "name": "LoneWolf", "value": 1},
        {"op": "addGold", "target": guid_b, "amount": 5000},
        {"op": "setStat", "target": guid_a, "name": "CurrentVitality", "value": 999},
        {"op": "permanentBoost", "target": guid_a, "name": "DamageBoost", "value": 25},
        {"op": "resetCooldowns"},
        {"op": "resurrect"},
    ])
    game.fire_ticks()
    check("attribute points added", game.calls_of("CharacterAddAttributePoint") == [(guid_a, 2)],
          str(game.calls_of("CharacterAddAttributePoint")))
    check("combat ability points added", game.calls_of("CharacterAddAbilityPoint") == [(guid_a, 1)],
          str(game.calls_of("CharacterAddAbilityPoint")))
    check("talent points added", game.calls_of("CharacterAddTalentPoint") == [(guid_a, 1)],
          str(game.calls_of("CharacterAddTalentPoint")))
    check("base attribute set on target",
          game.calls_of("NRD_PlayerSetBaseAttribute") == [(guid_b, "Strength", 20)],
          str(game.calls_of("NRD_PlayerSetBaseAttribute")))
    check("base ability set",
          game.calls_of("NRD_PlayerSetBaseAbility") == [(guid_a, "WarriorLore", 5)],
          str(game.calls_of("NRD_PlayerSetBaseAbility")))
    check("talent granted",
          game.calls_of("NRD_PlayerSetBaseTalent") == [(guid_a, "LoneWolf", 1)],
          str(game.calls_of("NRD_PlayerSetBaseTalent")))
    check("gold added", game.calls_of("CharacterAddGold") == [(guid_b, 5000)],
          str(game.calls_of("CharacterAddGold")))
    check("vitality set", game.calls_of("NRD_CharacterSetStatInt") == [(guid_a, "CurrentVitality", 999)],
          str(game.calls_of("NRD_CharacterSetStatInt")))
    check("permanent boost set",
          game.calls_of("NRD_CharacterSetPermanentBoostInt") == [(guid_a, "DamageBoost", 25)],
          str(game.calls_of("NRD_CharacterSetPermanentBoostInt")))
    check("cooldowns reset", game.calls_of("CharacterResetCooldowns") == [(guid_a,)],
          str(game.calls_of("CharacterResetCooldowns")))
    check("resurrect called", game.calls_of("CharacterResurrect") == [(guid_a,)],
          str(game.calls_of("CharacterResurrect")))
    check("upgrade sync issued", len(game.calls_of("CharacterAddCivilAbilityPoint")) >= 4,
          str(len(game.calls_of("CharacterAddCivilAbilityPoint"))))

    state = game.read_state()
    check("state acknowledges the command", state and state["seq"] == 1, str(state and state.get("seq")))
    check("no errors reported", state and state["errors"] == [], str(state and state.get("errors")))

    print("\n== 4. the same command must not run twice ==")
    game.calls.clear()
    game.fire_ticks()
    mutations = [call for call in game.calls if not call[0].startswith(("CharacterGet", "NRD_CharacterGet"))]
    check("nothing executed again", mutations == [], str(mutations[:4]))

    print("\n== 5. next sequence number with the same token ==")
    game.calls.clear()
    game.write_command(token, 2, [{"op": "addGold", "amount": 1}])
    game.fire_ticks()
    check("second command executed", game.calls_of("CharacterAddGold") == [(guid_a, 1)],
          str(game.calls_of("CharacterAddGold")))

    print("\n== 6. a restarted app (new token, old sequence) still works ==")
    game.calls.clear()
    game.write_command("token-beta", 1, [{"op": "addGold", "amount": 7}])
    game.fire_ticks()
    check("command from a new app instance executed", game.calls_of("CharacterAddGold") == [(guid_a, 7)],
          str(game.calls_of("CharacterAddGold")))

    print("\n== 7. bad input is reported, not fatal ==")
    game.calls.clear()
    game.write_command("token-beta", 2, [
        {"op": "doesNotExist"},
        {"op": "setAttribute", "name": "Charisma", "value": 5},
        {"op": "setAttribute", "name": "Strength", "value": 12},
    ])
    game.fire_ticks()
    state = game.read_state()
    errors = state["errors"] if state else []
    check("unknown operation reported", any("doesNotExist" in e for e in errors), str(errors))
    check("unknown attribute reported", any("Charisma" in e for e in errors), str(errors))
    check("valid operation still applied",
          game.calls_of("NRD_PlayerSetBaseAttribute") == [(guid_a, "Strength", 12)],
          str(game.calls_of("NRD_PlayerSetBaseAttribute")))

    print("\n== 8. log file ==")
    log = game.read_log()
    check("log written", "EoC Trainer" in log, log[:200])
    check("command logged", "command received" in log, log[-400:])

    print("\n== 9. heartbeat keeps the state fresh ==")
    first_ts = game.read_state()["timestamp"]
    game.fire_ticks(20)
    check("timestamp advanced", game.read_state()["timestamp"] > first_ts)

    if not keep:
        shutil.rmtree(game.bridge, ignore_errors=True)
    else:
        print("\nkept: %s" % game.bridge)

    print("\n%d failure(s)" % len(FAILURES))
    for failure in FAILURES:
        print("  - %s" % failure)
    return 1 if FAILURES else 0


if __name__ == "__main__":
    sys.exit(main())
