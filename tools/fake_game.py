"""Runs the Lua mod against the mock game and keeps it alive, so the desktop
app can be tested end to end without starting Divinity.

    python tools/fake_game.py --seconds 300

The bridge directory is the real one the app uses, so this behaves exactly like
a running game (only the character data is fake). Stop it with Ctrl+C.
"""

import argparse
import json
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from lua_harness import MockGame  # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


class FakeGame(MockGame):
    """Points the bridge at the real Documents folder instead of a temp dir."""

    def __init__(self, bridge_root):
        super().__init__(bridge_root)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--seconds", type=int, default=120, help="how long to stay alive")
    parser.add_argument("--tick-ms", type=int, default=16, help="delay between ticks")
    parser.add_argument("--once", action="store_true", help="single tick for scripting")
    args = parser.parse_args()

    documents = os.path.join(os.path.expanduser("~"), "Documents")
    game_storage = os.path.join(documents, "Larian Studios", "Divinity Original Sin 2 Definitive Edition")
    bridge = os.path.join(game_storage, "Osiris Data")
    os.makedirs(bridge, exist_ok=True)

    game = FakeGame(bridge)
    game.add_character("1a2b3c4d-0001-4000-8000-000000000001", "红王子", level=5)
    game.add_character("1a2b3c4d-0002-4000-8000-000000000002", "塞比勒", level=5,
                       talents=("AttackOfOpportunity", "LoneWolf"))
    game.add_character("1a2b3c4d-0003-4000-8000-000000000003", "伊凡", level=4,
                       talents=("Bully", "ElementalAffinity"))
    game.add_character("1a2b3c4d-0004-4000-8000-000000000004", "洛丝", level=4)

    game.require_mod()
    game.fire("SessionLoaded")
    print("fake game running, bridge = %s" % os.path.join(bridge, "EocTrainer"))

    deadline = time.time() + (1 if args.once else args.seconds)
    try:
        while time.time() < deadline:
            game.fire("Tick")
            applied = game.apply_calls()
            if applied:
                state = game.read_state()
                print("applied %d call(s) -> gold=%s points=%s" % (
                    applied,
                    [member["gold"] for member in state["party"]],
                    [member["points"]["attribute"] for member in state["party"]]))
            if args.once:
                break
            time.sleep(args.tick_ms / 1000.0)
    except KeyboardInterrupt:
        print("stopped")

    return 0


if __name__ == "__main__":
    sys.exit(main())
