"""
Steel City: Mob Sim — HTML Visualizer
Generates visual snapshots of game state.
Supports single-week snapshots and combined multi-week navigable HTML.
"""
import os
import html
from datetime import datetime


class SnapshotCollector:
    """Collects game state snapshots across multiple weeks for combined output."""

    def __init__(self):
        self.snapshots = []

    def collect(self, engine, label=None):
        """Collect current game state + event log."""
        state = engine.get_game_state()
        events = []
        if engine.event_stream:
            for event in engine.event_stream.events:
                events.append({
                    "time": event.time,
                    "type": event.type,
                    "data": event.data,
                })
        self.snapshots.append({
            "week": state["week"],
            "label": label or f"Week {state['week']}",
            "state": state,
            "events": events,
        })

    def generate_combined(self, output_path="snapshots/steel_city_report.html"):
        """Generate a single HTML file with all weeks as navigable slides."""
        os.makedirs(os.path.dirname(output_path) or ".", exist_ok=True)

        slides_html = ""
        nav_dots = ""

        for i, snap in enumerate(self.snapshots):
            slide_html = _render_slide(snap, i)
            slides_html += slide_html
            active = "active" if i == 0 else ""
            nav_dots += f'<button class="nav-dot {active}" data-slide="{i}" onclick="goToSlide({i})">{snap["label"]}</button>\n'

        total = len(self.snapshots)

        full_html = f"""<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8" />
<meta name="viewport" content="width=device-width, initial-scale=1.0" />
<title>Steel City: Mob Sim — Full Report</title>
<style>
*, *::before, *::after {{ box-sizing: border-box; margin: 0; padding: 0; }}
:root {{
  --bg: #0d0f14; --bg2: #141720; --bg3: #1c2030;
  --border: #2a2f45; --accent: #4f8ef7; --accent2: #7c5cfc;
  --green: #2ecc71; --red: #e74c3c; --yellow: #f39c12;
  --orange: #ff7a45; --pink: #f5389d; --purple: #8b5cf6;
  --text: #e8ecf4; --muted: #7a8199; --card: #181c27;
  --radius: 8px; --font: 'Inter', 'Segoe UI', system-ui, sans-serif;
}}
html, body {{ background: var(--bg); color: var(--text); font-family: var(--font); font-size: 14px; }}
body {{ padding: 0; }}

.nav-bar {{
  position: sticky; top: 0; z-index: 100;
  background: rgba(13,15,20,0.95); backdrop-filter: blur(10px);
  border-bottom: 1px solid var(--border);
  padding: 12px 20px; display: flex; align-items: center; gap: 16px; flex-wrap: wrap;
}}
.nav-bar .title {{
  font-size: 18px; font-weight: 700;
  background: linear-gradient(90deg, var(--accent), var(--accent2));
  -webkit-background-clip: text; -webkit-text-fill-color: transparent;
  white-space: nowrap;
}}
.nav-controls {{ display: flex; align-items: center; gap: 8px; margin-left: auto; }}
.nav-btn {{
  padding: 6px 16px; background: var(--bg3); border: 1px solid var(--border);
  border-radius: 6px; color: var(--text); cursor: pointer; font-size: 13px;
  transition: all 0.2s; user-select: none;
}}
.nav-btn:hover {{ background: var(--accent); border-color: var(--accent); }}
.nav-btn:disabled {{ opacity: 0.3; cursor: not-allowed; }}
.nav-btn:disabled:hover {{ background: var(--bg3); border-color: var(--border); }}
.nav-counter {{ font-size: 13px; color: var(--muted); min-width: 80px; text-align: center; }}
.nav-dots {{
  display: flex; gap: 4px; padding: 0 12px; flex-wrap: wrap;
  border-left: 1px solid var(--border); border-right: 1px solid var(--border);
}}
.nav-dot {{
  padding: 4px 10px; background: transparent; border: 1px solid var(--border);
  border-radius: 4px; color: var(--muted); cursor: pointer; font-size: 11px;
  transition: all 0.2s;
}}
.nav-dot:hover {{ color: var(--text); border-color: var(--accent); }}
.nav-dot.active {{ background: var(--accent); color: white; border-color: var(--accent); }}

.slides-container {{ position: relative; }}
.slide {{
  display: none; padding: 20px; max-width: 1400px; margin: 0 auto;
  animation: fadeIn 0.3s ease;
}}
.slide.active {{ display: block; }}
@keyframes fadeIn {{ from {{ opacity: 0; transform: translateY(10px); }} to {{ opacity: 1; transform: translateY(0); }} }}

.slide-header {{
  text-align: center; margin-bottom: 24px; padding: 20px;
  background: linear-gradient(135deg, var(--bg2), var(--bg3));
  border-radius: var(--radius); border: 1px solid var(--border);
}}
.slide-header h1 {{
  font-size: 28px; background: linear-gradient(90deg, var(--accent), var(--accent2));
  -webkit-background-clip: text; -webkit-text-fill-color: transparent;
}}
.slide-header .subtitle {{ color: var(--muted); margin-top: 8px; }}

.stats-strip {{ display: flex; gap: 12px; margin-bottom: 24px; flex-wrap: wrap; }}
.stat-box {{
  flex: 1; min-width: 140px; padding: 16px; background: var(--card);
  border-radius: var(--radius); border: 1px solid var(--border); text-align: center;
}}
.stat-box .label {{ color: var(--muted); font-size: 12px; text-transform: uppercase; }}
.stat-box .value {{ font-size: 24px; font-weight: 700; margin-top: 4px; }}
.stat-box.player .value {{ color: var(--accent); }}
.stat-box.rival .value {{ color: var(--red); }}
.stat-box.money .value {{ color: var(--green); }}
.stat-box.territory .value {{ color: var(--orange); }}

.section {{
  margin-bottom: 24px; background: var(--bg2); border-radius: var(--radius);
  border: 1px solid var(--border); overflow: hidden;
}}
.section-header {{
  padding: 12px 20px; background: var(--bg3); font-size: 16px; font-weight: 600;
  border-bottom: 1px solid var(--border); display: flex; align-items: center; gap: 8px;
}}
.section-body {{ padding: 20px; }}

.grid-row {{ display: flex; gap: 12px; margin-bottom: 12px; }}
.block-card {{
  flex: 1; min-width: 200px; padding: 16px; background: var(--card);
  border-radius: var(--radius); border: 2px solid var(--border);
  transition: transform 0.2s; position: relative;
}}
.block-card:hover {{ transform: translateY(-2px); }}
.block-card.empty {{ background: transparent; border: 1px dashed var(--border); min-height: 60px; }}
.block-card.player {{ border-color: var(--accent); box-shadow: 0 0 12px rgba(79,142,247,0.15); }}
.block-card.rival {{ border-color: var(--red); box-shadow: 0 0 12px rgba(231,76,60,0.15); }}

.block-name {{ font-size: 16px; font-weight: 600; margin-bottom: 8px; }}
.block-owner {{
  display: inline-block; padding: 2px 8px; border-radius: 4px;
  font-size: 11px; font-weight: 600; text-transform: uppercase; margin-bottom: 8px;
}}
.block-owner.player {{ background: rgba(79,142,247,0.2); color: var(--accent); }}
.block-owner.rival {{ background: rgba(231,76,60,0.2); color: var(--red); }}
.block-owner.unowned {{ background: rgba(122,129,153,0.2); color: var(--muted); }}

.strength-bar-container {{ height: 6px; background: var(--bg); border-radius: 3px; margin: 8px 0; overflow: hidden; }}
.strength-bar {{ height: 100%; border-radius: 3px; }}
.strength-bar.high {{ background: var(--green); }}
.strength-bar.mid {{ background: var(--yellow); }}
.strength-bar.low {{ background: var(--orange); }}

.block-meta {{ font-size: 12px; color: var(--muted); display: flex; gap: 12px; flex-wrap: wrap; }}
.block-biz {{ margin-top: 8px; font-size: 12px; }}
.biz-item {{ padding: 2px 6px; background: var(--bg3); border-radius: 3px; margin: 2px 0; }}
.biz-item.owned {{ color: var(--green); }}
.biz-item.illegal {{ color: var(--pink); }}

.info-tier {{
  position: absolute; top: 8px; right: 8px; padding: 2px 6px;
  border-radius: 3px; font-size: 10px; font-weight: 600; text-transform: uppercase;
}}
.info-tier.blind {{ background: rgba(122,129,153,0.2); color: var(--muted); }}
.info-tier.aware {{ background: rgba(243,156,18,0.2); color: var(--yellow); }}
.info-tier.informed {{ background: rgba(79,142,247,0.2); color: var(--accent); }}
.info-tier.connected {{ background: rgba(46,204,113,0.2); color: var(--green); }}

.hq-badge {{
  position: absolute; top: 8px; left: 8px; padding: 2px 6px;
  background: var(--purple); color: white; border-radius: 3px; font-size: 10px; font-weight: 600;
}}

.hood-grid {{ display: flex; gap: 12px; flex-wrap: wrap; }}
.hood-card {{
  flex: 1; min-width: 280px; padding: 16px; background: var(--card);
  border-radius: var(--radius); border: 1px solid var(--border);
}}
.hood-card.rival {{ border-color: rgba(231,76,60,0.3); }}
.hood-name {{ font-size: 16px; font-weight: 600; margin-bottom: 4px; }}
.hood-int {{ color: var(--accent2); font-size: 12px; margin-bottom: 12px; }}
.hood-status {{
  display: inline-block; padding: 2px 8px; border-radius: 4px; font-size: 11px;
  margin-bottom: 12px; text-transform: uppercase; font-weight: 600;
}}
.hood-status.available {{ background: rgba(46,204,113,0.2); color: var(--green); }}
.hood-status.assigned {{ background: rgba(79,142,247,0.2); color: var(--accent); }}
.hood-status.arrested {{ background: rgba(231,76,60,0.2); color: var(--red); }}
.hood-status.dead {{ background: rgba(122,129,153,0.2); color: var(--muted); }}

.skill-bar {{ margin-bottom: 4px; }}
.skill-bar .skill-label {{ display: flex; justify-content: space-between; font-size: 11px; margin-bottom: 2px; }}
.skill-bar .skill-track {{ height: 4px; background: var(--bg); border-radius: 2px; overflow: hidden; }}
.skill-bar .skill-fill {{ height: 100%; border-radius: 2px; }}

.inv-card {{ padding: 12px; background: var(--card); border-radius: var(--radius); margin-bottom: 8px; border: 1px solid var(--border); }}
.inv-header {{ font-weight: 600; margin-bottom: 8px; }}
.inv-bar-container {{ height: 8px; background: var(--bg); border-radius: 4px; overflow: hidden; margin-bottom: 4px; }}
.inv-bar {{ height: 100%; background: var(--red); border-radius: 4px; }}
.inv-meta {{ font-size: 12px; color: var(--muted); }}

.police-grid {{ display: flex; gap: 12px; flex-wrap: wrap; }}
.police-card {{
  flex: 1; min-width: 200px; padding: 16px; background: var(--card);
  border-radius: var(--radius); border: 1px solid var(--border);
}}
.police-card.bribed {{ border-color: var(--green); box-shadow: 0 0 8px rgba(46,204,113,0.1); }}
.police-name {{ font-weight: 600; margin-bottom: 4px; }}
.police-status {{ font-size: 12px; margin-bottom: 4px; }}
.police-card.bribed .police-status {{ color: var(--green); }}
.police-beat {{ font-size: 11px; color: var(--muted); }}
.police-cost {{ font-size: 11px; color: var(--muted); margin-top: 4px; }}

.finance-card {{ padding: 16px; background: var(--card); border-radius: var(--radius); }}
.finance-row {{ display: flex; justify-content: space-between; padding: 4px 0; font-size: 14px; }}
.finance-row.income {{ color: var(--green); }}
.finance-row.expense {{ color: var(--orange); }}
.finance-row.net.positive {{ color: var(--green); font-weight: 600; }}
.finance-row.net.negative {{ color: var(--red); font-weight: 600; }}
.finance-row.balance {{ border-top: 1px solid var(--border); margin-top: 8px; padding-top: 8px; font-size: 16px; }}
.breakdown {{ margin-top: 12px; padding-top: 12px; border-top: 1px solid var(--border); }}
.breakdown-item {{ display: flex; justify-content: space-between; font-size: 12px; color: var(--muted); padding: 2px 0; }}

.event-log {{ max-height: 400px; overflow-y: auto; font-family: 'Consolas', monospace; font-size: 12px; }}
.event-line {{ padding: 4px 0; border-bottom: 1px solid rgba(42,47,69,0.5); }}
.event-line .time {{ color: var(--muted); margin-right: 8px; }}
.event-line.squeal {{ color: var(--yellow); }}
.event-line.investigation {{ color: var(--orange); }}
.event-line.arrest {{ color: var(--red); font-weight: 600; }}
.event-line.economy {{ color: var(--green); }}
.event-line.territory {{ color: var(--accent); }}
.event-line.rival {{ color: var(--pink); }}
.event-line.notification-red {{ color: var(--red); }}
.event-line.notification-yellow {{ color: var(--yellow); }}
.event-line.notification-green {{ color: var(--green); }}

.empty-state {{ text-align: center; color: var(--muted); padding: 24px; }}
.two-col {{ display: flex; gap: 24px; }}
.two-col > div {{ flex: 1; }}

.two-col-main {{ display: flex; gap: 20px; align-items: flex-start; }}
.col-left {{ flex: 1.2; min-width: 0; }}
.col-right {{ flex: 1; min-width: 0; }}
.col-right .event-log {{ max-height: 500px; }}

.footer {{ text-align: center; color: var(--muted); font-size: 12px; margin-top: 24px; padding: 16px; }}
</style>
</head>
<body>

<div class="nav-bar">
  <div class="title">Steel City: Mob Sim</div>
  <div class="nav-dots">
{nav_dots}
  </div>
  <div class="nav-controls">
    <button class="nav-btn" id="prevBtn" onclick="prevSlide()">◀ Prev</button>
    <span class="nav-counter" id="slideCounter">1 / {total}</span>
    <button class="nav-btn" id="nextBtn" onclick="nextSlide()">Next ▶</button>
  </div>
</div>

<div class="slides-container" id="slidesContainer">
{slides_html}
</div>

<div class="footer">
  Steel City: Mob Sim — Vertical Slice Report | {total} snapshots | Generated {datetime.now().strftime("%Y-%m-%d %H:%M")}
</div>

<script>
let currentSlide = 0;
const totalSlides = {total};

function showSlide(index) {{
  if (index < 0 || index >= totalSlides) return;
  currentSlide = index;
  document.querySelectorAll('.slide').forEach((s, i) => {{
    s.classList.toggle('active', i === index);
  }});
  document.querySelectorAll('.nav-dot').forEach((d, i) => {{
    d.classList.toggle('active', i === index);
  }});
  document.getElementById('slideCounter').textContent = (index + 1) + ' / ' + totalSlides;
  document.getElementById('prevBtn').disabled = index === 0;
  document.getElementById('nextBtn').disabled = index === totalSlides - 1;
  window.scrollTo({{ top: 0, behavior: 'smooth' }});
}}

function nextSlide() {{ showSlide(currentSlide + 1); }}
function prevSlide() {{ showSlide(currentSlide - 1); }}
function goToSlide(i) {{ showSlide(i); }}

document.addEventListener('keydown', (e) => {{
  if (e.key === 'ArrowLeft') prevSlide();
  if (e.key === 'ArrowRight') nextSlide();
}});

showSlide(0);
</script>

</body>
</html>"""

        with open(output_path, "w", encoding="utf-8") as f:
            f.write(full_html)

        return output_path


def _render_slide(snap, index):
    """Render a single week as a slide div."""
    state = snap["state"]
    week = state["week"]
    player = state["gangs"]["player"]
    rival = state["gangs"]["rival"]
    label = snap["label"]

    blocks = list(state["blocks"].values())
    max_row = max(b["row"] for b in blocks)
    max_col = max(b["col"] for b in blocks)

    grid_html = ""
    for row in range(max_row + 1):
        grid_html += '<div class="grid-row">\n'
        for col in range(max_col + 1):
            block = next((b for b in blocks if b["row"] == row and b["col"] == col), None)
            if block:
                grid_html += _render_block_card(block)
            else:
                grid_html += '<div class="block-card empty"></div>\n'
        grid_html += '</div>\n'

    hood_cards = ""
    for hood in player["hoods"]:
        hood_cards += _render_hood_card(hood)
    rival_hoods = ""
    for hood in rival["hoods"]:
        rival_hoods += _render_hood_card(hood, is_rival=True)

    inv_html = ""
    active_invs = [(iid, inv) for iid, inv in state["investigations"].items() if inv["status"] == "active"]
    if active_invs:
        for iid, inv in active_invs:
            block_name = state["blocks"][inv["block_id"]]["name"]
            lead_pct = min(100, (inv["leads"] / inv["threshold"]) * 100)
            inv_html += f"""
            <div class="inv-card">
                <div class="inv-header">🔍 {html.escape(block_name)} — {iid}</div>
                <div class="inv-bar-container"><div class="inv-bar" style="width: {lead_pct:.0f}%"></div></div>
                <div class="inv-meta">Leads: {inv['leads']}/{inv['threshold']} | Status: {inv['status']}</div>
            </div>"""
    else:
        inv_html = '<div class="empty-state">No active investigations</div>'

    event_log = ""
    for event in snap["events"]:
        event_log += _render_event_line(event)

    finance_html = '<div class="empty-state">No economy data</div>'
    for event in snap["events"]:
        if event["type"] == "economy":
            d = event["data"]
            breakdown_items = ""
            for k, v in d.get("breakdown", {}).items():
                label_str = k.replace("_", " ").title()
                breakdown_items += f"<div class='breakdown-item'><span>{label_str}</span><span>${v}</span></div>"
            finance_html = f"""
            <div class="finance-card">
                <div class="finance-row income">Income: <strong>${d['income']}</strong></div>
                <div class="finance-row expense">Expenses: <strong>${d['expenses']}</strong></div>
                <div class="finance-row net {'positive' if d['net'] >= 0 else 'negative'}">Net: <strong>${'+' if d['net'] >= 0 else ''}{d['net']}</strong></div>
                <div class="finance-row balance">Treasury: <strong>${d['balance']}</strong></div>
                <div class="breakdown">{breakdown_items}</div>
            </div>"""
            break

    police_html = ""
    for officer in state["police"]:
        on_payroll = officer["on_payroll"]
        payroll_class = "bribed" if on_payroll else "clean"
        payroll_text = f"ON PAYROLL ({officer['payroll_gang']})" if on_payroll else "Not bribed"
        beat_str = ", ".join(officer["beat"])
        police_html += f"""
        <div class="police-card {payroll_class}">
            <div class="police-name">👮 {html.escape(officer['name'])}</div>
            <div class="police-status">{payroll_text}</div>
            <div class="police-beat">Beat: {beat_str}</div>
            <div class="police-cost">Bribe: ${officer['bribe_cost']}/week</div>
        </div>"""

    player_blocks = [b for b in state["blocks"].values() if b["owner_gang"] == "player"]
    rival_blocks = [b for b in state["blocks"].values() if b["owner_gang"] == "rival"]
    unowned = [b for b in state["blocks"].values() if b["owner_gang"] is None]
    active_class = "active" if index == 0 else ""

    return f"""<div class="slide {active_class}" data-slide="{index}">

<div class="slide-header">
  <h1>{html.escape(label)}</h1>
  <div class="subtitle">{html.escape(player['name'])} vs {html.escape(rival['name'])}</div>
</div>

<div class="stats-strip">
  <div class="stat-box money"><div class="label">Treasury</div><div class="value">${player['money']}</div></div>
  <div class="stat-box player"><div class="label">Player Territory</div><div class="value">{len(player_blocks)}</div></div>
  <div class="stat-box rival"><div class="label">Rival Territory</div><div class="value">{len(rival_blocks)}</div></div>
  <div class="stat-box territory"><div class="label">Unowned</div><div class="value">{len(unowned)}</div></div>
  <div class="stat-box"><div class="label">Investigations</div><div class="value">{len(active_invs)}</div></div>
</div>

<div class="two-col-main">

  <!-- LEFT COLUMN: Map + Hoods + Rival -->
  <div class="col-left">
    <div class="section">
      <div class="section-header">🏙️ City Map</div>
      <div class="section-body">{grid_html}</div>
    </div>
    <div class="section">
      <div class="section-header">🎭 Your Hoods</div>
      <div class="section-body"><div class="hood-grid">{hood_cards}</div></div>
    </div>
    <div class="section">
      <div class="section-header">⚔️ Rival Gang: {html.escape(rival['name'])} (${rival['money']})</div>
      <div class="section-body"><div class="hood-grid">{rival_hoods}</div></div>
    </div>
  </div>

  <!-- RIGHT COLUMN: Event Log + Investigations + Police + Finances -->
  <div class="col-right">
    <div class="section">
      <div class="section-header">� Event Log — {html.escape(label)}</div>
      <div class="section-body"><div class="event-log">{event_log if event_log else '<div class="empty-state">No events</div>'}</div></div>
    </div>
    <div class="section">
      <div class="section-header">🔍 Active Investigations</div>
      <div class="section-body">{inv_html}</div>
    </div>
    <div class="section">
      <div class="section-header">� Finances</div>
      <div class="section-body">{finance_html}</div>
    </div>
    <div class="section">
      <div class="section-header">👮 Police & Corruption</div>
      <div class="section-body"><div class="police-grid">{police_html}</div></div>
    </div>
  </div>

</div>

</div>
"""


def _render_block_card(block):
    """Render a single block as an HTML card."""
    owner = block["owner_gang"]
    owner_class = owner if owner in ("player", "rival") else "unowned"
    owner_label = {"player": "YOURS", "rival": "RIVAL"}.get(owner, "UNOWNED")
    strength = block["extortion_strength"]
    strength_class = "high" if strength >= 67 else ("mid" if strength >= 34 else "low")
    tier = block["info_tier"]

    hq_badge = ""
    if block.get("is_player_hq"):
        hq_badge = '<div class="hq-badge">YOUR HQ</div>'
    elif block.get("is_rival_hq"):
        hq_badge = '<div class="hq-badge" style="background:var(--red)">RIVAL HQ</div>'
    elif block.get("is_police_station"):
        hq_badge = '<div class="hq-badge" style="background:var(--yellow);color:#000">POLICE</div>'

    biz_html = ""
    for biz in block["businesses"]:
        owned_class = "owned" if biz.get("owner_gang") == "player" else ""
        illegal_class = "illegal" if biz.get("is_illegal") else ""
        owner_tag = " (YOURS)" if biz.get("owner_gang") == "player" else ""
        biz_html += f'<div class="biz-item {owned_class} {illegal_class}">{html.escape(biz["name"])}{owner_tag}</div>\n'

    return f"""<div class="block-card {owner_class}">
  {hq_badge}
  <div class="info-tier {tier}">{tier}</div>
  <div class="block-name">{html.escape(block['name'])}</div>
  <div class="block-owner {owner_class}">{owner_label}</div>
  <div class="strength-bar-container"><div class="strength-bar {strength_class}" style="width: {strength}%"></div></div>
  <div class="block-meta">
    <span>👥 {block['population']}</span>
    <span>🏢 {len(block['businesses'])}</span>
    <span>📊 LV{block['land_value']}</span>
    <span>💪 {strength}%</span>
  </div>
  <div class="block-biz">{biz_html}</div>
</div>
"""


def _render_hood_card(hood, is_rival=False):
    """Render a hood as an HTML card with skill bars."""
    rival_class = "rival" if is_rival else ""
    status_class = hood["status"]

    skill_colors = {
        "organisation": "#4f8ef7", "business": "#2ecc71", "firearms": "#e74c3c",
        "fists": "#ff7a45", "knives": "#f39c12", "arson": "#ff6b35",
        "explosives": "#f5389d", "intimidation": "#8b5cf6", "driving": "#00d4ff",
        "stealth": "#7c5cfc",
    }

    skill_bars = ""
    for skill_name, value in sorted(hood["skills"].items(), key=lambda x: x[1], reverse=True):
        color = skill_colors.get(skill_name, "#4f8ef7")
        pct = (value / 63) * 100
        skill_bars += f"""
        <div class="skill-bar">
          <div class="skill-label"><span>{skill_name.title()}</span><span>{value}/63</span></div>
          <div class="skill-track"><div class="skill-fill" style="width: {pct:.0f}%; background: {color}"></div></div>
        </div>"""

    return f"""<div class="hood-card {rival_class}">
  <div class="hood-name">{html.escape(hood['name'])}</div>
  <div class="hood-int">INT: {hood['intelligence']}/255 | Loyalty: {hood.get('loyalty', '—')}</div>
  <div class="hood-status {status_class}">{hood['status']}</div>
  {skill_bars}
</div>
"""


def _render_event_line(event):
    """Render a single event as a line in the event log."""
    time_str = f"{event['time']:.1f}"
    etype = event["type"]
    d = event["data"]

    if etype == "order_result":
        return f'<div class="event-line"><span class="time">[{time_str}]</span>{html.escape(str(d.get("hood_name","")))} → {html.escape(str(d.get("order_type","")))} on {html.escape(str(d.get("block_name","")))}: <strong>{html.escape(str(d.get("result","")))}</strong> — {html.escape(str(d.get("details","")))}</div>\n'
    elif etype == "squeal":
        return f'<div class="event-line squeal"><span class="time">[{time_str}]</span>⚠ SQUEAL: {html.escape(str(d.get("npc_name","")))} talked to police about {html.escape(str(d.get("block_name","")))}</div>\n'
    elif etype == "investigation":
        return f'<div class="event-line investigation"><span class="time">[{time_str}]</span>🔍 INVESTIGATION: {html.escape(str(d.get("block_name","")))} — Leads: {d.get("leads",0)}/{d.get("threshold",0)}</div>\n'
    elif etype == "arrest":
        return f'<div class="event-line arrest"><span class="time">[{time_str}]</span>🚔 ARREST: {html.escape(str(d.get("hood_name","")))} arrested!</div>\n'
    elif etype == "economy":
        net = d.get("net", 0)
        return f'<div class="event-line economy"><span class="time">[{time_str}]</span>💰 Income ${d.get("income",0)} | Expenses ${d.get("expenses",0)} | Net ${"+" if net>=0 else ""}{net} | Balance ${d.get("balance",0)}</div>\n'
    elif etype == "territory_change":
        return f'<div class="event-line territory"><span class="time">[{time_str}]</span>🏴 {html.escape(str(d.get("block_name","")))} now controlled by {d.get("gang_id","")} (strength: {d.get("strength",0)}%)</div>\n'
    elif etype == "rival_action":
        return f'<div class="event-line rival"><span class="time">[{time_str}]</span>[RIVAL] {html.escape(str(d.get("hood_name","")))} → {html.escape(str(d.get("order_type","")))} on {html.escape(str(d.get("block_name","")))}: {html.escape(str(d.get("result","")))}</div>\n'
    elif etype == "notification":
        tier = d.get("tier", "green")
        return f'<div class="event-line notification-{tier}"><span class="time">[{time_str}]</span>[{tier.upper()}] {html.escape(str(d.get("message","")))}</div>\n'
    return f'<div class="event-line"><span class="time">[{time_str}]</span>{etype}: {html.escape(str(d))}</div>\n'
