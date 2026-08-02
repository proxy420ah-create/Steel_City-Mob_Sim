"""Rival AI — simple decision-making for AI gangs."""
import random


def rival_ai_take_turn(gang_id, blocks, hoods, businesses, crimes_data, week):
    """Make decisions for a rival gang. Returns list of orders."""
    orders = []
    available_hoods = [h for h in hoods if h.gang_id == gang_id and h.is_available]

    if not available_hoods:
        return orders

    # Find unowned or weakly-held blocks adjacent to rival territory
    owned_blocks = [b for b in blocks.values() if b.owner_gang == gang_id]

    # Target blocks: unowned blocks adjacent to owned blocks, or any unowned block
    target_blocks = []
    for block in blocks.values():
        if block.owner_gang is None and not block.is_police_station:
            target_blocks.append(block)

    # Prioritize blocks adjacent to existing territory
    adjacent_targets = []
    for owned in owned_blocks:
        for r, c in owned.adjacent_blocks:
            for b in blocks.values():
                if b.row == r and b.col == c and b.owner_gang is None and b not in adjacent_targets:
                    adjacent_targets.append(b)

    # Assign hoods to extort target blocks
    targets = adjacent_targets if adjacent_targets else target_blocks[:3]

    for i, hood in enumerate(available_hoods):
        if i < len(targets):
            target = targets[i % len(targets)]
            orders.append({
                "hood_id": hood.id,
                "block_id": target.id,
                "order_type": "extort",
                "gang_id": gang_id,
                "week": week,
            })
        else:
            # Extra hoods patrol owned territory
            if owned_blocks:
                patrol_target = random.choice(owned_blocks)
                orders.append({
                    "hood_id": hood.id,
                    "block_id": patrol_target.id,
                    "order_type": "patrol",
                    "gang_id": gang_id,
                    "week": week,
                })

    return orders
