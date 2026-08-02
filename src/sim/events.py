"""Event types and event stream for Working Week visualization."""
from dataclasses import dataclass, field
from typing import Any


@dataclass
class GameEvent:
    time: float
    type: str
    data: dict = field(default_factory=dict)

    def __str__(self):
        return f"[{self.time:.1f}] {self.type}: {self.data}"


class EventStream:
    """Collects events during a Working Week for playback and reporting."""

    def __init__(self, week):
        self.week = week
        self.events = []
        self._time = 0.0

    def add(self, event_type, data, time_offset=0.0):
        self.events.append(GameEvent(
            time=self._time + time_offset,
            type=event_type,
            data=data,
        ))

    def advance_time(self, duration):
        self._time += duration

    @property
    def current_time(self):
        return self._time

    def get_text_report(self):
        """Generate a text report of all events."""
        lines = [f"=== Week {self.week} Event Log ===\n"]
        for event in self.events:
            if event.type == "order_result":
                d = event.data
                lines.append(f"  {d['hood_name']} -> {d['order_type']} on {d['block_name']}: {d['result']}")
                if d.get('details'):
                    lines.append(f"    Details: {d['details']}")
            elif event.type == "squeal":
                d = event.data
                lines.append(f"  ⚠ SQUEAL: {d['npc_name']} talked to police about {d['block_name']}")
            elif event.type == "investigation":
                d = event.data
                lines.append(f"  🔍 INVESTIGATION: {d['block_name']} - Leads: {d['leads']}/{d['threshold']}")
            elif event.type == "arrest":
                d = event.data
                lines.append(f"  🚔 ARREST: {d['hood_name']} arrested!")
            elif event.type == "rival_action":
                d = event.data
                lines.append(f"  [RIVAL] {d['hood_name']} -> {d['order_type']} on {d['block_name']}: {d['result']}")
            elif event.type == "economy":
                d = event.data
                lines.append(f"  💰 Economy: Income ${d['income']}, Expenses ${d['expenses']}, Net ${d['net']}")
            elif event.type == "territory_change":
                d = event.data
                lines.append(f"  🏴 Territory: {d['block_name']} now controlled by {d['gang_id'] or 'nobody'} (strength: {d['strength']})")
            elif event.type == "notification":
                d = event.data
                tier = d.get('tier', 'green')
                prefix = {"green": "  ℹ", "yellow": "  ⚠", "red": "  🚨"}.get(tier, "  ℹ")
                lines.append(f"{prefix} [{tier.upper()}] {d['message']}")

        return "\n".join(lines)
