#!/usr/bin/env python3
"""
Steel City: Mob Sim — Vertical Slice Prototype
Entry point for the text-based simulation.

Usage: python main.py
"""
import sys
import os

# Add src to path
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from data.loader import load_all
from sim.engine import GameEngine
from ui.organizer import run_organizer


def main():
    print("\n" + "=" * 60)
    print("  STEEL CITY: MOB SIM — VERTICAL SLICE PROTOTYPE")
    print("  9 blocks | 2 factions | 3 hoods each | 2 beat cops")
    print("=" * 60)

    # Load all game data
    print("\n  Loading game data...")
    data = load_all()
    print(f"  ✓ Loaded: {', '.join(data.keys())}")

    # Create and setup game engine
    print("\n  Generating city...")
    engine = GameEngine(data)
    engine.setup()

    player = engine.gangs["player"]
    rival = engine.gangs["rival"]
    print(f"  ✓ Player: {player.name} (${player.money}, {len(player.hoods)} hoods)")
    print(f"  ✓ Rival:  {rival.name} (${rival.money}, {len(rival.hoods)} hoods)")
    print(f"  ✓ City: {len(engine.blocks)} blocks, {len(engine.businesses)} businesses, {len(engine.npcs)} NPCs")
    print(f"  ✓ Police: {len(engine.police)} officers on patrol")

    # Run the Gang Organizer
    run_organizer(engine)


if __name__ == "__main__":
    main()
