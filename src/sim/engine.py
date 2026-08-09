"""Main simulation engine — game loop, phase management, order resolution."""
import random
from dataclasses import dataclass, field
from typing import Optional

from .city import generate_city, get_blocks_by_owner, Block, Business, PoliceOfficer
from .character import Hood, NPC, generate_starting_hoods
from .crime import (
    resolve_extortion, resolve_intimidation, generate_squeal,
    create_investigation, update_investigations, CrimeEvent, Investigation,
)
from .economy import calculate_gang_finances
from .rival_ai import rival_ai_take_turn
from .events import EventStream


@dataclass
class Gang:
    id: str
    name: str
    money: int
    hoods: list = field(default_factory=list)
    is_player: bool = False


class GameEngine:
    """Core simulation engine. Manages city, gangs, and the weekly loop."""

    def __init__(self, data):
        self.data = data
        self.week = 0
        self.blocks = {}
        self.businesses = {}
        self.npcs = {}
        self.police = []
        self.gangs = {}
        self.investigations = {}
        self.pending_orders = []
        self.event_stream = None
        self._crime_counter = 0
        self._invest_counter = 0

    def setup(self):
        """Generate the city, gangs, and starting conditions."""
        city_template = self.data["city_template"]
        businesses_data = self.data["businesses"]
        constants_data = self.data["constants"]
        archetypes_data = self.data["archetypes"]

        # Generate city
        self.blocks, self.businesses, self.npcs, self.police = generate_city(
            city_template, businesses_data, constants_data
        )

        # Create player gang
        player = Gang(id="player", name="Moretti Family", money=3000, is_player=True)
        player.hoods = generate_starting_hoods("player", 3, archetypes_data)
        self.gangs["player"] = player

        # Assign player HQ block and starting business
        for block in self.blocks.values():
            if block.is_player_hq:
                block.owner_gang = "player"
                block.extortion_strength = 50
                # Give player one business in HQ block
                for biz_id in block.businesses:
                    biz = self.businesses[biz_id]
                    if not biz.is_illegal:
                        biz.owner_gang = "player"
                        break

        # Create rival gang
        rival = Gang(id="rival", name="Falcone Syndicate", money=3000)
        rival.hoods = generate_starting_hoods("rival", 3, archetypes_data)
        self.gangs["rival"] = rival

        # Assign rival HQ block and starting business
        for block in self.blocks.values():
            if block.is_rival_hq:
                block.owner_gang = "rival"
                block.extortion_strength = 50
                for biz_id in block.businesses:
                    biz = self.businesses[biz_id]
                    if not biz.is_illegal:
                        biz.owner_gang = "rival"
                        break

        self.week = 1

    def assign_order(self, hood_id, block_id, order_type, gang_id="player"):
        """Assign an order to a hood."""
        hood = self._find_hood(hood_id)
        if hood and hood.is_available:
            order = {
                "hood_id": hood_id,
                "block_id": block_id,
                "order_type": order_type,
                "gang_id": gang_id,
                "week": self.week,
            }
            self.pending_orders.append(order)
            hood.status = "assigned"
            hood.assigned_order = order
            return True
        return False

    def bribe_officer(self, officer_id, gang_id="player"):
        """Put a police officer on payroll."""
        officer = next((o for o in self.police if o.id == officer_id), None)
        gang = self.gangs.get(gang_id)
        if officer and gang and gang.money >= officer.bribe_cost:
            gang.money -= officer.bribe_cost
            officer.on_payroll = True
            officer.payroll_gang = gang_id
            return True
        return False

    def run_working_week(self):
        """Execute the Working Week — resolve all orders, update world state."""
        self.event_stream = EventStream(self.week)

        # Generate rival AI orders
        rival_orders = rival_ai_take_turn(
            "rival", self.blocks, self.gangs["rival"].hoods,
            self.businesses, self.data["crimes"], self.week
        )
        all_orders = self.pending_orders + rival_orders

        # Resolve each order
        for order in all_orders:
            self._resolve_order(order)
            self.event_stream.advance_time(1.0)

        # Generate squeal for crimes committed this week
        self._process_squeal()

        # Update investigations
        arrests = update_investigations(self.investigations, self.week)
        for arrest in arrests:
            for hood_id in arrest.target_hoods:
                hood = self._find_hood(hood_id)
                if hood and hood.status != "dead":
                    hood.status = "arrested"
                    gang = self.gangs.get(hood.gang_id)
                    self.event_stream.add("arrest", {
                        "hood_name": hood.name,
                        "gang_id": hood.gang_id,
                    })

        # Update economy
        self._process_economy()

        # Update territory strength
        self._update_territory()

        # Clear pending orders
        self.pending_orders.clear()
        for gang in self.gangs.values():
            for hood in gang.hoods:
                if hood.status == "assigned":
                    hood.status = "available"
                    hood.assigned_order = None

        self.week += 1

        return self.event_stream

    def _resolve_order(self, order):
        """Resolve a single order."""
        hood = self._find_hood(order["hood_id"])
        block = self.blocks.get(order["block_id"])
        if not hood or not block:
            return

        gang = self.gangs.get(order["gang_id"])
        hood_name = hood.name
        block_name = block.name

        if order["order_type"] == "extort":
            success, details, target_npc = resolve_extortion(
                hood, block, self.npcs, self.businesses, self.data["constants"]
            )

            if success:
                # Gain territory
                if block.owner_gang is None or block.extortion_strength < 30:
                    block.owner_gang = order["gang_id"]
                    block.extortion_strength = max(block.extortion_strength, 20)
                    self.event_stream.add("territory_change", {
                        "block_name": block_name,
                        "gang_id": order["gang_id"],
                        "strength": block.extortion_strength,
                    })
                else:
                    block.extortion_strength = min(100, block.extortion_strength + 10)

                # Generate crime event for squeal
                self._create_crime_event("extort", block, hood, order["gang_id"])
            else:
                # Refusal — notify
                if order["gang_id"] == "player":
                    self.event_stream.add("notification", {
                        "tier": "yellow",
                        "message": f"{hood_name} reports: {details}",
                    })

            self.event_stream.add("order_result", {
                "hood_name": hood_name,
                "order_type": "extort",
                "block_name": block_name,
                "result": "success" if success else "failure",
                "details": details,
            }, time_offset=0.1)

            if not order["gang_id"] == "player":
                self.event_stream.add("rival_action", {
                    "hood_name": hood_name,
                    "order_type": "extort",
                    "block_name": block_name,
                    "result": "success" if success else "failure",
                })

        elif order["order_type"] == "collect_protection":
            # Collect protection from owned blocks
            if block.owner_gang == order["gang_id"] and block.extortion_strength > 0:
                block.extortion_strength = min(100, block.extortion_strength + 5)
                self.event_stream.add("order_result", {
                    "hood_name": hood_name,
                    "order_type": "collect_protection",
                    "block_name": block_name,
                    "result": "success",
                    "details": f"Collected protection. Strength now {block.extortion_strength}.",
                })
            else:
                self.event_stream.add("order_result", {
                    "hood_name": hood_name,
                    "order_type": "collect_protection",
                    "block_name": block_name,
                    "result": "failure",
                    "details": "We don't control this block.",
                })

        elif order["order_type"] == "patrol":
            # Patrol — maintains territory, may spot rivals
            if block.owner_gang == order["gang_id"]:
                block.extortion_strength = min(100, block.extortion_strength + 3)
                self.event_stream.add("order_result", {
                    "hood_name": hood_name,
                    "order_type": "patrol",
                    "block_name": block_name,
                    "result": "success",
                    "details": f"Patrolled. Strength now {block.extortion_strength}.",
                })
            else:
                self.event_stream.add("order_result", {
                    "hood_name": hood_name,
                    "order_type": "patrol",
                    "block_name": block_name,
                    "result": "neutral",
                    "details": "Patrolled neutral block.",
                })

        elif order["order_type"] == "intimidate":
            success, details, target = resolve_intimidation(hood, block, self.npcs)
            self._create_crime_event("intimidate", block, hood, order["gang_id"])
            self.event_stream.add("order_result", {
                "hood_name": hood_name,
                "order_type": "intimidate",
                "block_name": block_name,
                "result": "success" if success else "failure",
                "details": details,
            })

        elif order["order_type"] == "lie_low":
            # Reduce investigation leads for this hood's crimes
            for inv in self.investigations.values():
                if hood.id in inv.target_hoods and inv.status == "active":
                    inv.leads = max(0, inv.leads - 15)
            self.event_stream.add("order_result", {
                "hood_name": hood_name,
                "order_type": "lie_low",
                "block_name": block_name,
                "result": "success",
                "details": "Laying low. Investigation leads reduced.",
            })

    def _create_crime_event(self, crime_type, block, hood, gang_id):
        """Create a crime event and add squeal check."""
        crimes_data = self.data["crimes"]["crimes"]
        crime_def = next((c for c in crimes_data if c["id"] == crime_type), None)
        if not crime_def:
            return

        self._crime_counter += 1
        crime = CrimeEvent(
            id=f"crime_{self._crime_counter:04d}",
            crime_type=crime_type,
            block_id=block.id,
            hood_id=hood.id,
            gang_id=gang_id,
            suspicion=crime_def["suspicion"],
            sentence=crime_def["sentence"],
            investigation_difficulty=crime_def["investigation"],
            week=self.week,
        )

        # Check if corrupt cop suppresses suspicion
        suppressed = False
        for officer in self.police:
            if officer.on_payroll and officer.payroll_gang == gang_id and block.id in officer.beat:
                suppressed = True
                break

        if not suppressed:
            # Store crime for squeal processing
            if not hasattr(self, '_week_crimes'):
                self._week_crimes = []
            self._week_crimes.append(crime)

    def _process_squeal(self):
        """Process squeal for all crimes committed this week."""
        if not hasattr(self, '_week_crimes'):
            return

        for crime in self._week_crimes:
            block = self.blocks[crime.block_id]
            squealers = generate_squeal(crime, block, self.npcs, self.data["constants"])

            if squealers:
                crime.squeal_generated = True
                self._invest_counter += 1
                inv = create_investigation(f"invest_{self._invest_counter:04d}", crime, squealers, self.npcs)
                self.investigations[inv.id] = inv

                squealer_npc = self.npcs[squealers[0]]
                self.event_stream.add("squeal", {
                    "npc_name": squealer_npc.name,
                    "block_name": block.name,
                    "crime_type": crime.crime_type,
                })
                self.event_stream.add("investigation", {
                    "block_name": block.name,
                    "leads": inv.leads,
                    "threshold": inv.leads_threshold,
                })

                # Notification based on info tier
                player_blocks = get_blocks_by_owner(self.blocks, "player")
                player_block_ids = {b.id for b in player_blocks}
                tier = block.info_tier if block.id in player_block_ids else "blind"

                if tier == "blind":
                    self.event_stream.add("notification", {
                        "tier": "red",
                        "message": f"Police are investigating activity on {block.name}. You don't know who talked.",
                    })
                elif tier in ("aware", "informed"):
                    self.event_stream.add("notification", {
                        "tier": "yellow",
                        "message": f"Someone on {block.name} squealed to the police about {crime.crime_type}.",
                    })
                else:
                    self.event_stream.add("notification", {
                        "tier": "yellow",
                        "message": f"{squealer_npc.name} on {block.name} squealed about the {crime.crime_type}. You know who it was.",
                    })

        self._week_crimes = []

    def _process_economy(self):
        """Process weekly economy for all gangs."""
        for gang_id, gang in self.gangs.items():
            income, expenses, breakdown = calculate_gang_finances(
                gang_id, self.blocks, self.businesses, self.npcs,
                self.data["businesses"], self.police
            )

            # Hood payroll: $50/week per active hood
            hood_payroll = len([h for h in gang.hoods if h.status != "dead"]) * 50
            expenses += hood_payroll
            breakdown["payroll"] = hood_payroll

            net = income - expenses
            gang.money += net

            if gang.is_player:
                self.event_stream.add("economy", {
                    "income": income,
                    "expenses": expenses,
                    "net": net,
                    "balance": gang.money,
                    "breakdown": breakdown,
                })

    def _update_territory(self):
        """Decay territory strength for neglected blocks."""
        for block in self.blocks.values():
            if block.owner_gang and block.extortion_strength > 0:
                # Natural decay if not visited this week
                # (In full version, check if a collect/patrol order was executed here)
                pass  # No decay for now — orders maintain strength

    def _find_hood(self, hood_id):
        """Find a hood by ID across all gangs."""
        for gang in self.gangs.values():
            for hood in gang.hoods:
                if hood.id == hood_id:
                    return hood
        return None

    def get_game_state(self):
        """Return a snapshot of the current game state for UI."""
        return {
            "week": self.week,
            "gangs": {
                gid: {
                    "name": g.name,
                    "money": g.money,
                    "is_player": g.is_player,
                    "hoods": [
                        {
                            "id": h.id, "name": h.name,
                            "intelligence": h.intelligence,
                            "skills": h.skills,
                            "skill_summary": h.skill_summary,
                            "loyalty": h.loyalty,
                            "health": h.health,
                            "status": h.status,
                            "is_available": h.is_available,
                        }
                        for h in g.hoods
                    ],
                }
                for gid, g in self.gangs.items()
            },
            "blocks": {
                bid: {
                    "name": b.name,
                    "row": b.row, "col": b.col,
                    "land_value": b.land_value,
                    "population": b.population,
                    "owner_gang": b.owner_gang,
                    "extortion_strength": b.extortion_strength,
                    "info_tier": b.info_tier,
                    "businesses": [
                        {
                            "id": biz_id,
                            "name": self.businesses[biz_id].name,
                            "type": self.businesses[biz_id].type,
                            "is_illegal": self.businesses[biz_id].is_illegal,
                            "owner_gang": self.businesses[biz_id].owner_gang,
                        }
                        for biz_id in b.businesses
                    ],
                    "npcs": len(b.npcs),
                    "is_player_hq": b.is_player_hq,
                    "is_rival_hq": b.is_rival_hq,
                    "is_police_station": b.is_police_station,
                }
                for bid, b in self.blocks.items()
            },
            "police": [
                {
                    "id": o.id, "name": o.name,
                    "beat": o.beat,
                    "bribe_cost": o.bribe_cost,
                    "on_payroll": o.on_payroll,
                    "payroll_gang": o.payroll_gang,
                }
                for o in self.police
            ],
            "investigations": {
                iid: {
                    "block_id": inv.block_id,
                    "leads": inv.leads,
                    "threshold": inv.leads_threshold,
                    "status": inv.status,
                    "target_hoods": inv.target_hoods,
                }
                for iid, inv in self.investigations.items()
            },
        }
