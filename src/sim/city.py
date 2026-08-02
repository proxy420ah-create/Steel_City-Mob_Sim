"""City generation — blocks, businesses, districts."""
import random
from dataclasses import dataclass, field
from typing import Optional
from .character import generate_block_npcs, NPC


@dataclass
class Business:
    id: str
    block_id: str
    type: str  # butcher, bakery, barber, diner, garage, apartments, empty_land, casino, etc.
    name: str
    is_illegal: bool = False
    owner_gang: Optional[str] = None  # who owns the business
    profit_group: int = 0
    running_cost_group: int = 0
    capacity: int = 1
    active: bool = True


@dataclass
class Block:
    id: str
    name: str
    row: int
    col: int
    land_value: int
    population: int
    businesses: list = field(default_factory=list)
    npcs: list = field(default_factory=list)
    owner_gang: Optional[str] = None  # who controls territory (extortion)
    extortion_strength: int = 0  # 0-100
    squeal_risk: int = 0  # derived
    active_investigations: list = field(default_factory=list)
    is_player_hq: bool = False
    is_rival_hq: bool = False
    is_police_station: bool = False

    @property
    def info_tier(self):
        """Derived information tier based on ownership and strength."""
        if self.extortion_strength >= 67:
            return "connected"
        elif self.extortion_strength >= 34:
            return "informed"
        elif self.extortion_strength > 0:
            return "aware"
        else:
            return "blind"

    @property
    def adjacent_blocks(self):
        """Returns list of (row, col) offsets for adjacent blocks."""
        return [
            (self.row - 1, self.col), (self.row + 1, self.col),
            (self.row, self.col - 1), (self.row, self.col + 1),
        ]


@dataclass
class PoliceOfficer:
    id: str
    name: str
    beat: list  # list of block_ids
    bribe_cost: int
    on_payroll: bool = False
    payroll_gang: Optional[str] = None


def generate_city(city_template, businesses_data, constants_data):
    """Generate the full city from template + data files."""
    blocks = {}
    all_businesses = {}
    all_npcs = {}

    biz_defs = {b["id"]: b for b in businesses_data["legal_businesses"]}
    illegal_defs = {b["id"]: b for b in businesses_data["illegal_businesses"]}

    for block_data in city_template["blocks"]:
        block = Block(
            id=block_data["id"],
            name=block_data["name"],
            row=block_data["row"],
            col=block_data["col"],
            land_value=block_data["land_value"],
            population=block_data["population"],
            is_player_hq=block_data.get("player_hq", False),
            is_rival_hq=block_data.get("rival_hq", False),
            is_police_station=block_data.get("police_station", False),
        )

        biz_count = 0
        for biz_entry in block_data["businesses"]:
            biz_type = biz_entry["type"]
            is_illegal = biz_entry.get("illegal", False)
            count = biz_entry.get("count", 1)

            for i in range(count):
                biz_id = f"biz_{block.id}_{biz_type}_{i}"
                if is_illegal and biz_type in illegal_defs:
                    defn = illegal_defs[biz_type]
                    biz = Business(
                        id=biz_id, block_id=block.id, type=biz_type,
                        name=defn["name"], is_illegal=True,
                        profit_group=defn["profit_group"],
                        running_cost_group=0,
                        capacity=defn["capacity"],
                    )
                elif biz_type in biz_defs:
                    defn = biz_defs[biz_type]
                    biz = Business(
                        id=biz_id, block_id=block.id, type=biz_type,
                        name=defn["name"], is_illegal=False,
                        profit_group=defn["profit_group"],
                        running_cost_group=defn["running_cost_group"],
                        capacity=defn["capacity"],
                    )
                else:
                    continue

                all_businesses[biz_id] = biz
                block.businesses.append(biz_id)
                biz_count += 1

        # Generate NPCs for this block
        npcs = generate_block_npcs(block.id, block.population, constants_data, biz_count)
        for npc in npcs:
            all_npcs[npc.id] = npc
        block.npcs = [npc.id for npc in npcs]

        blocks[block.id] = block

    # Generate police officers
    police = []
    for beat_data in city_template["police_beats"]:
        officer = PoliceOfficer(
            id=beat_data["officer_id"],
            name=beat_data["name"],
            beat=beat_data["beat"],
            bribe_cost=beat_data["bribe_cost"],
        )
        police.append(officer)

    return blocks, all_businesses, all_npcs, police


def get_blocks_by_owner(blocks, gang_id):
    """Return list of blocks owned by a gang."""
    return [b for b in blocks.values() if b.owner_gang == gang_id]


def get_adjacent_blocks(blocks, block):
    """Return adjacent Block objects that exist."""
    result = []
    for r, c in block.adjacent_blocks:
        for b in blocks.values():
            if b.row == r and b.col == c:
                result.append(b)
    return result
