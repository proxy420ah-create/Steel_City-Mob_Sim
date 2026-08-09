"""Gang Organizer — text-based planning interface."""
import sys


def display_header(week, gang):
    print(f"\n{'='*60}")
    print(f"  STEEL CITY: MOB SIM — GANG ORGANIZER")
    print(f"  Week {week} | {gang.name} | Treasury: ${gang.money}")
    print(f"{'='*60}\n")


def display_hoods(gang):
    print(f"  YOUR HOODS:")
    print(f"  {'ID':<20} {'Name':<20} {'INT':>4} {'Top Skills':<30} {'Status':<12}")
    print(f"  {'-'*90}")
    for hood in gang.hoods:
        print(f"  {hood.id:<20} {hood.name:<20} {hood.intelligence:>4} {hood.skill_summary:<30} {hood.status:<12}")
    print()


def display_blocks(engine, gang_id="player"):
    print(f"  CITY MAP (3×3):")
    print(f"  {'Block':<15} {'Owner':<12} {'Strength':>8} {'Info Tier':<12} {'Pop':>4} {'Businesses':<30}")
    print(f"  {'-'*85}")
    for block in sorted(engine.blocks.values(), key=lambda b: (b.row, b.col)):
        owner = block.owner_gang or "—"
        biz_list = ", ".join(
            f"{engine.businesses[bid].name}{'(YOURS)' if engine.businesses[bid].owner_gang == gang_id else ''}"
            for bid in block.businesses
        )
        hq = ""
        if block.is_player_hq:
            hq = " [HQ]"
        elif block.is_rival_hq:
            hq = " [RIVAL]"
        elif block.is_police_station:
            hq = " [POLICE]"
        print(f"  {block.name+':'+block.id:<15} {owner:<12} {block.extortion_strength:>8} {block.info_tier:<12} {block.population:>4} {biz_list:<30}{hq}")
    print()


def display_police(engine):
    print(f"  POLICE OFFICERS:")
    print(f"  {'ID':<15} {'Name':<25} {'Bribe Cost':>10} {'On Payroll':<12} {'Beat':<30}")
    print(f"  {'-'*95}")
    for officer in engine.police:
        payroll = f"YES ({officer.payroll_gang})" if officer.on_payroll else "No"
        beat_str = ", ".join(officer.beat)
        print(f"  {officer.id:<15} {officer.name:<25} ${officer.bribe_cost:>9} {payroll:<12} {beat_str:<30}")
    print()


def display_investigations(engine):
    active = [inv for inv in engine.investigations.values() if inv.status == "active"]
    if not active:
        print(f"  INVESTIGATIONS: None active\n")
        return
    print(f"  ACTIVE INVESTIGATIONS:")
    print(f"  {'ID':<15} {'Block':<15} {'Leads':>6}/{'':<5} {'Status':<12} {'Target Hoods':<20}")
    print(f"  {'-'*75}")
    for inv in active:
        block_name = engine.blocks[inv.block_id].name
        hood_names = [engine._find_hood(h).name for h in inv.target_hoods if engine._find_hood(h)]
        print(f"  {inv.id:<15} {block_name:<15} {inv.leads:>6}/{inv.leads_threshold:<5} {inv.status:<12} {', '.join(hood_names):<20}")
    print()


def display_finances(engine, gang_id="player"):
    gang = engine.gangs[gang_id]
    print(f"  FINANCES:")
    print(f"    Treasury: ${gang.money}")
    print()


def display_orders_help():
    print(f"  AVAILABLE ORDERS:")
    print(f"    extort <hood_id> <block_id>    — Extort a block for protection money")
    print(f"    collect <hood_id> <block_id>   — Collect protection from owned block")
    print(f"    patrol <hood_id> <block_id>    — Patrol owned territory")
    print(f"    intimidate <hood_id> <block_id> — Intimidate a block (raises fear)")
    print(f"    lielow <hood_id> <block_id>    — Lie low (reduce investigation leads)")
    print(f"    bribe <officer_id>             — Bribe a police officer")
    print(f"    run                            — Execute Working Week")
    print(f"    status                         — Show full game state")
    print(f"    quit                           — Exit game")
    print()


def run_organizer(engine):
    """Main Gang Organizer loop — text interface."""
    gang = engine.gangs["player"]

    while True:
        display_header(engine.week, gang)
        display_hoods(gang)
        display_blocks(engine)
        display_police(engine)
        display_investigations(engine)
        display_orders_help()

        try:
            cmd = input("  > ").strip().lower()
        except (EOFError, KeyboardInterrupt):
            print("\n  Exiting...")
            break

        if not cmd:
            continue

        parts = cmd.split()

        if parts[0] == "quit":
            print("  Goodbye.")
            break

        elif parts[0] == "status":
            import json
            state = engine.get_game_state()
            print(json.dumps(state, indent=2, default=str))

        elif parts[0] == "extort" and len(parts) >= 3:
            hood_id, block_id = parts[1], parts[2]
            if engine.assign_order(hood_id, block_id, "extort"):
                hood = engine._find_hood(hood_id)
                print(f"  ✓ {hood.name} assigned to extort {engine.blocks[block_id].name}")
            else:
                print(f"  ✗ Could not assign order. Hood may be unavailable.")

        elif parts[0] == "collect" and len(parts) >= 3:
            hood_id, block_id = parts[1], parts[2]
            if engine.assign_order(hood_id, block_id, "collect_protection"):
                hood = engine._find_hood(hood_id)
                print(f"  ✓ {hood.name} assigned to collect protection from {engine.blocks[block_id].name}")
            else:
                print(f"  ✗ Could not assign order.")

        elif parts[0] == "patrol" and len(parts) >= 3:
            hood_id, block_id = parts[1], parts[2]
            if engine.assign_order(hood_id, block_id, "patrol"):
                hood = engine._find_hood(hood_id)
                print(f"  ✓ {hood.name} assigned to patrol {engine.blocks[block_id].name}")
            else:
                print(f"  ✗ Could not assign order.")

        elif parts[0] == "intimidate" and len(parts) >= 3:
            hood_id, block_id = parts[1], parts[2]
            if engine.assign_order(hood_id, block_id, "intimidate"):
                hood = engine._find_hood(hood_id)
                print(f"  ✓ {hood.name} assigned to intimidate {engine.blocks[block_id].name}")
            else:
                print(f"  ✗ Could not assign order.")

        elif parts[0] == "lielow" and len(parts) >= 3:
            hood_id, block_id = parts[1], parts[2]
            if engine.assign_order(hood_id, block_id, "lie_low"):
                hood = engine._find_hood(hood_id)
                print(f"  ✓ {hood.name} lying low at {engine.blocks[block_id].name}")
            else:
                print(f"  ✗ Could not assign order.")

        elif parts[0] == "bribe" and len(parts) >= 2:
            officer_id = parts[1]
            if engine.bribe_officer(officer_id):
                officer = next(o for o in engine.police if o.id == officer_id)
                print(f"  ✓ {officer.name} is now on your payroll (${officer.bribe_cost}/week)")
            else:
                print(f"  ✗ Could not bribe. Not enough money or officer not found.")

        elif parts[0] == "run":
            print(f"\n  >>> EXECUTING WORKING WEEK {engine.week} <<<\n")
            stream = engine.run_working_week()
            print(stream.get_text_report())
            print(f"\n  >>> WEEK COMPLETE — Treasury: ${gang.money} <<<\n")

        else:
            print(f"  Unknown command. Type 'run' to execute week, 'quit' to exit.")
