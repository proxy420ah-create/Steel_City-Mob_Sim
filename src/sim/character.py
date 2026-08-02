"""Character generation — hoods and NPCs."""
import random
from dataclasses import dataclass, field
from typing import Optional


SKILLS = [
    "organisation", "business", "firearms", "fists", "knives",
    "arson", "explosives", "intimidation", "driving", "stealth",
]


@dataclass
class Hood:
    id: str
    name: str
    intelligence: int
    skills: dict
    loyalty: int = 200
    health: str = "healthy"  # healthy, winded, lightly_wounded, badly_wounded, dead
    status: str = "available"  # available, assigned, arrested, dead
    assigned_order: Optional[dict] = None
    gang_id: str = ""

    @property
    def is_available(self):
        return self.status == "available" and self.health not in ("badly_wounded", "dead")

    @property
    def skill_summary(self):
        top = sorted(self.skills.items(), key=lambda x: x[1], reverse=True)[:3]
        return ", ".join(f"{k}={v}" for k, v in top)


@dataclass
class NPC:
    id: str
    name: str
    npc_type: str  # business_owner, civilian, police
    block_id: str
    business_id: Optional[str] = None
    fear: int = 100
    hostility: int = 50
    squeal: int = 100
    alive: bool = True

    @property
    def is_compliant(self):
        return self.fear > self.hostility


HOOD_NAMES = [
    "Vinny Moretti", "Frankie Russo", "Sal Bianchi", "Tony Caruso",
    "Mikey Falcone", "Nicky Lombardi", "Eddie Greco", "Paulie Vitale",
    "Joey Marino", "Carmine Romano", "Luigi Esposito", "Dominic Ricci",
]

NPC_NAMES = [
    "Tony the Butcher", "Old Man Patterson", "Mrs. O'Sullivan",
    "Jimmy the Baker", "Sal the Barber", "Katherine Doyle",
    "Eddie the Mechanic", "Rose Calabrese", "Pat Flanagan",
    "Angie Morretti", "Tom Kelly", "Maria Costa",
]


def generate_hood(hood_id, gang_id, archetypes_data):
    """Generate a hood from weighted archetypes."""
    archetypes = archetypes_data["archetypes"]
    weights = [a["weight"] for a in archetypes]
    chosen = random.choices(archetypes, weights=weights, k=1)[0]

    intel = chosen["intelligence"]
    intelligence = intel["base"] + random.randint(0, intel["range"])

    skills = {}
    for skill_name in SKILLS:
        s = chosen["skills"][skill_name]
        skills[skill_name] = max(0, min(63, s["base"] + random.randint(0, s["range"])))

    name = random.choice(HOOD_NAMES)
    return Hood(
        id=hood_id,
        name=name,
        intelligence=intelligence,
        skills=skills,
        gang_id=gang_id,
    )


def generate_starting_hoods(gang_id, count, archetypes_data, start_id=0):
    """Generate a roster of starting hoods for a gang."""
    hoods = []
    for i in range(count):
        hood = generate_hood(f"hood_{gang_id}_{start_id + i:03d}", gang_id, archetypes_data)
        hoods.append(hood)
    return hoods


def generate_npc(npc_id, npc_type, block_id, constants_data, business_id=None):
    """Generate an NPC with fear/hostility/squeal based on type."""
    fear_data = constants_data["fear_base"].get(npc_type, constants_data["fear_base"]["civilian"])
    squeal_data = constants_data["squeal"]

    squeal_map = {
        "business_owner": squeal_data["business_owner"],
        "civilian": squeal_data["civilian"],
        "police": squeal_data["police"],
    }

    base_fear = fear_data["base"] + fear_data["modifier"]
    squeal_val = squeal_map.get(npc_type, squeal_data["civilian"])

    return NPC(
        id=npc_id,
        name=random.choice(NPC_NAMES),
        npc_type=npc_type,
        block_id=block_id,
        business_id=business_id,
        fear=max(0, base_fear + random.randint(-20, 20)),
        hostility=max(0, random.randint(30, 70)),
        squeal=squeal_val,
    )


def generate_block_npcs(block_id, population, constants_data, business_count=0):
    """Generate NPCs for a block — business owners + civilians."""
    npcs = []
    for i in range(business_count):
        npcs.append(generate_npc(
            f"npc_{block_id}_biz_{i}", "business_owner", block_id, constants_data
        ))
    for i in range(population - business_count):
        npcs.append(generate_npc(
            f"npc_{block_id}_civ_{i}", "civilian", block_id, constants_data
        ))
    return npcs
