#!/usr/bin/env python3
"""
Steel City: Mob Sim — Automated end-to-end test.
Runs 5 weeks with scripted player orders to verify all systems.
"""
import sys
import os
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src"))

from data.loader import load_all
from sim.engine import GameEngine


def run_test():
    print("\n" + "=" * 60)
    print("  STEEL CITY: MOB SIM — AUTOMATED E2E TEST (5 weeks)")
    print("=" * 60)

    data = load_all()
    engine = GameEngine(data)
    engine.setup()

    player = engine.gangs["player"]
    rival = engine.gangs["rival"]

    print(f"\n  Setup:")
    print(f"    Player: {player.name} | ${player.money} | {len(player.hoods)} hoods")
    print(f"    Rival:  {rival.name} | ${rival.money} | {len(rival.hoods)} hoods")
    print(f"    City: {len(engine.blocks)} blocks, {len(engine.businesses)} businesses, {len(engine.npcs)} NPCs")
    print(f"    Police: {len(engine.police)} officers")
    print(f"    Player hoods: {[(h.name, h.skill_summary) for h in player.hoods]}")

    # Get hood IDs and available blocks
    hoods = player.hoods
    block_ids = list(engine.blocks.keys())

    # Bribe officer_001 (covers player HQ area)
    print(f"\n  --- Bribing Officer O'Brien ---")
    engine.bribe_officer("officer_001")
    print(f"    Treasury: ${player.money}")

    for week_num in range(1, 6):
        print(f"\n{'='*60}")
        print(f"  WEEK {week_num} — PLANNING")
        print(f"{'='*60}")

        # Assign orders each week
        orders_assigned = 0
        for hood in player.hoods:
            if not hood.is_available:
                continue

            # Week 1-2: Extort adjacent unowned blocks
            # Week 3+: Mix of extort, collect, patrol
            if week_num <= 2:
                # Target West Block (block_4) and South Block (block_8)
                target = "block_4" if hood == hoods[0] else ("block_8" if hood == hoods[1] else "block_7")
                order_type = "extort"
            elif week_num <= 3:
                # Collect from owned + extort new
                if hood == hoods[0]:
                    target, order_type = "block_7", "collect_protection"
                elif hood == hoods[1]:
                    target, order_type = "block_4", "extort"
                else:
                    target, order_type = "block_8", "extort"
            else:
                # Patrol owned + collect
                if hood == hoods[0]:
                    target, order_type = "block_7", "collect_protection"
                elif hood == hoods[1]:
                    target, order_type = "block_4", "patrol"
                else:
                    target, order_type = "block_8", "collect_protection"

            success = engine.assign_order(hood.id, target, order_type)
            if success:
                orders_assigned += 1
                print(f"    {hood.name} -> {order_type} on {engine.blocks[target].name}")

        print(f"    Orders assigned: {orders_assigned}")

        # Run the week
        print(f"\n  >>> EXECUTING WEEK {week_num} <<<\n")
        stream = engine.run_working_week()
        print(stream.get_text_report())

        # Status summary
        print(f"\n  --- STATUS AFTER WEEK {week_num} ---")
        print(f"    Treasury: ${player.money}")
        player_blocks = [b for b in engine.blocks.values() if b.owner_gang == "player"]
        rival_blocks = [b for b in engine.blocks.values() if b.owner_gang == "rival"]
        print(f"    Player territory: {len(player_blocks)} blocks ({', '.join(b.name for b in player_blocks)})")
        print(f"    Rival territory: {len(rival_blocks)} blocks ({', '.join(b.name for b in rival_blocks)})")
        active_inv = [i for i in engine.investigations.values() if i.status == "active"]
        print(f"    Active investigations: {len(active_inv)}")
        for inv in active_inv:
            print(f"      {inv.id}: {engine.blocks[inv.block_id].name} — Leads {inv.leads}/{inv.leads_threshold}")

        arrested = [h for h in player.hoods if h.status == "arrested"]
        if arrested:
            print(f"    ⚠ ARRESTED: {', '.join(h.name for h in arrested)}")

    print(f"\n{'='*60}")
    print(f"  TEST COMPLETE — 5 weeks simulated")
    print(f"  Final treasury: ${player.money}")
    print(f"  Player territory: {len([b for b in engine.blocks.values() if b.owner_gang == 'player'])} blocks")
    print(f"  Rival territory: {len([b for b in engine.blocks.values() if b.owner_gang == 'rival'])} blocks")
    print(f"  Total investigations: {len(engine.investigations)}")
    print(f"  Arrests: {len([h for h in player.hoods if h.status == 'arrested'])}")
    print(f"{'='*60}\n")


if __name__ == "__main__":
    run_test()
