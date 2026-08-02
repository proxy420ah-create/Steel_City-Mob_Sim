"""Crime resolution — extortion, squeal, investigations."""
import random
from dataclasses import dataclass, field
from typing import Optional


@dataclass
class Investigation:
    id: str
    block_id: str
    crimes: list = field(default_factory=list)
    leads: int = 0
    leads_threshold: int = 100
    target_hoods: list = field(default_factory=list)
    status: str = "active"  # active, closed, resulted_in_arrest
    detective_id: Optional[str] = None


@dataclass
class CrimeEvent:
    id: str
    crime_type: str
    block_id: str
    hood_id: str
    gang_id: str
    suspicion: int = 0
    sentence: int = 0
    investigation_difficulty: int = 0
    result: str = ""  # success, failure, partial
    details: str = ""
    squeal_generated: bool = False
    week: int = 0


def resolve_extortion(hood, block, npcs, businesses, constants_data):
    """Attempt to extort a block. Returns (success, details, npc_affected)."""
    # Find a business owner NPC to extort
    biz_npcs = [npcs[nid] for nid in block.npcs if npcs[nid].npc_type == "business_owner" and npcs[nid].alive]

    if not biz_npcs:
        return False, "No business owners to extort in this block.", None

    target = random.choice(biz_npcs)
    hood_intimidation = hood.skills.get("intimidation", 0)

    # Compliance check: fear + intimidation pressure vs hostility
    pressure = hood_intimidation + random.randint(0, 20)
    resistance = target.hostility + random.randint(0, 20)

    if target.fear > target.hostility:
        # Already afraid enough — complies
        target.fear = min(255, target.fear + 5)  # fear maintained
        return True, f"{target.name} paid up without trouble (fear {target.fear} > hostility {target.hostility}).", target

    if pressure > resistance:
        # Intimidation wins — raises fear
        fear_gain = random.randint(15, 35)
        target.fear = min(255, target.fear + fear_gain)
        hostility_gain = random.randint(0, 10)  # some resentment
        target.hostility = min(255, target.hostility + hostility_gain)
        return True, f"{target.name} paid after pressure (fear +{fear_gain}, now {target.fear}). Some resentment (hostility +{hostility_gain}).", target
    else:
        # Refusal
        return False, f"{target.name} refused to pay (pressure {pressure} vs resistance {resistance}). Fear {target.fear}, Hostility {target.hostility}.", target


def resolve_intimidation(hood, block, npcs, target_npc=None):
    """Intimidate a block or specific NPC. Raises fear, may raise hostility."""
    if target_npc is None:
        biz_npcs = [npcs[nid] for nid in block.npcs if npcs[nid].npc_type == "business_owner" and npcs[nid].alive]
        if not biz_npcs:
            return False, "No one to intimidate in this block.", None
        target = random.choice(biz_npcs)
    else:
        target = target_npc

    hood_intimidation = hood.skills.get("intimidation", 0)
    pressure = hood_intimidation + random.randint(10, 30)
    resistance = target.hostility + random.randint(0, 15)

    fear_gain = random.randint(20, 50)
    hostility_gain = random.randint(5, 20)

    target.fear = min(255, target.fear + fear_gain)
    target.hostility = min(255, target.hostility + hostility_gain)

    if target.fear > target.hostility:
        return True, f"{target.name} is now compliant (fear {target.fear} > hostility {target.hostility}). But remembers the threat.", target
    else:
        return False, f"{target.name} still refusing (fear {target.fear}, hostility {target.hostility}). The threat made them angrier.", target


def generate_squeal(crime_event, block, npcs, constants_data):
    """Check if any NPC squeals about the crime. Returns list of squealer NPC IDs."""
    squealers = []
    squeal_base = constants_data["squeal"]

    for npc_id in block.npcs:
        npc = npcs[npc_id]
        if not npc.alive or npc.npc_type == "police":
            continue

        # Squeal roll: NPC's squeal value, modified by fear (terrified people talk more)
        squeal_value = npc.squeal
        if npc.fear > 150:
            squeal_value = int(squeal_value * 1.3)  # terrified people more likely to talk

        # Roll: random(0, 255) must be < squeal_value to squeal
        roll = random.randint(0, 255)
        if roll < squeal_value:
            squealers.append(npc.id)

    return squealers


def create_investigation(invest_id, crime_event, squealers, npcs):
    """Create an investigation from a crime + squealers."""
    leads = crime_event.investigation_difficulty * 10
    return Investigation(
        id=invest_id,
        block_id=crime_event.block_id,
        crimes=[crime_event.id],
        leads=leads,
        target_hoods=[crime_event.hood_id],
    )


def update_investigations(investigations, week):
    """Tick investigations — decay leads, check for arrests."""
    arrests = []
    for inv in investigations.values():
        if inv.status != "active":
            continue

        # Leads decay over time
        inv.leads = max(0, inv.leads - 5)

        # Check if leads reach threshold
        if inv.leads >= inv.leads_threshold:
            inv.status = "resulted_in_arrest"
            arrests.append(inv)

    return arrests
