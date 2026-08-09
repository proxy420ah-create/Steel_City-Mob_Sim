"""Economy — business income, expenses, market share."""
import random


def calculate_business_income(business, businesses_data, land_value, owner_gang):
    """Calculate weekly income for a business."""
    if not business.active or business.owner_gang != owner_gang:
        return 0

    profit_group = str(business.profit_group)
    profit_value = businesses_data["profit_groups"].get(profit_group, 0)

    # Land value modifier: higher LV = more income
    lv_modifier = 1.0 + (land_value * 0.1)

    # Random fluctuation
    fluctuation = random.uniform(0.8, 1.2)

    income = int(profit_value * lv_modifier * fluctuation)
    return income


def calculate_running_costs(business, businesses_data, land_value):
    """Calculate weekly running costs for a business."""
    cost_group = str(business.running_cost_group)
    base_cost = businesses_data["running_cost_groups"].get(cost_group, 0)

    # Land value modifier for costs
    lv_modifier = 1.0 + (land_value * 0.05)

    return int(base_cost * lv_modifier)


def calculate_protection_income(block, npcs):
    """Calculate protection money collected from extorted block."""
    biz_npcs = [npcs[nid] for nid in block.npcs
                if npcs[nid].npc_type == "business_owner" and npcs[nid].alive and npcs[nid].is_compliant]

    # Each compliant business owner pays protection
    total = 0
    for npc in biz_npcs:
        # Protection amount based on fear (more fear = more payment)
        payment = 20 + (npc.fear // 10)
        total += payment

    # Scale by territory strength
    strength_factor = block.extortion_strength / 100.0
    return int(total * strength_factor)


def calculate_market_share_factor(businesses_owned):
    """Market share diminishing returns. Based on original game's decay curve."""
    if businesses_owned <= 1:
        return 1.0
    elif businesses_owned <= 5:
        return 0.80
    elif businesses_owned <= 10:
        return 0.79
    elif businesses_owned <= 15:
        return 0.65
    elif businesses_owned <= 20:
        return 0.57
    elif businesses_owned <= 27:
        return 0.50
    elif businesses_owned <= 35:
        return 0.42
    else:
        return 0.03


def calculate_gang_finances(gang_id, blocks, businesses, npcs, businesses_data, police):
    """Calculate total income and expenses for a gang this week."""
    income = 0
    expenses = 0
    breakdown = {"business_income": 0, "protection_income": 0, "payroll": 0, "running_costs": 0, "bribes": 0}

    # Business income
    owned_businesses = [b for b in businesses.values() if b.owner_gang == gang_id and not b.is_illegal and b.active]
    market_factor = calculate_market_share_factor(len(owned_businesses))

    for biz in owned_businesses:
        block = blocks[biz.block_id]
        gross = calculate_business_income(biz, businesses_data, block.land_value, gang_id)
        net = int(gross * market_factor)
        costs = calculate_running_costs(biz, businesses_data, block.land_value)
        income += net
        expenses += costs
        breakdown["business_income"] += net
        breakdown["running_costs"] += costs

    # Protection income from extorted blocks
    for block in blocks.values():
        if block.owner_gang == gang_id and block.extortion_strength > 0:
            prot = calculate_protection_income(block, npcs)
            income += prot
            breakdown["protection_income"] += prot

    # Police bribes
    for officer in police:
        if officer.on_payroll and officer.payroll_gang == gang_id:
            expenses += officer.bribe_cost
            breakdown["bribes"] += officer.bribe_cost

    # Hood payroll (simple: $50/week per hood)
    # This is handled by the engine which knows the hoods

    return income, expenses, breakdown
