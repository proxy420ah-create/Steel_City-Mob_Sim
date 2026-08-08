# 3D City Rendering — Working Week Visualization

**Created**: August 2, 2026
**Status**: 📐 In Progress

---

## Overview

The game uses a two-phase visual split:

- **Gang Organizer (Planning)**: 2D UI screens — maps, tables, menus, order assignment. Data-focused, fast interaction.
- **Working Week (Execution)**: 3D city with free camera — watch the simulation unfold as a visual spectacle. Citizens walk streets, hoods execute orders, police patrol, crimes happen in real-time.

The 3D city is an **interactive visualization layer**. The simulation crunches numbers and produces events. The 3D renderer visualizes those events. But the player can also issue **tactical overrides** mid-week — limited, urgent orders in response to what they see unfolding. The simulation and renderer are decoupled, but the player bridges them.

---

## Design Principle: Simulation Decides, Renderer Shows, Player Reacts

```
1. Simulation resolves: "Vinny extorts Baker St. butcher. Butcher refuses."
2. 3D renderer plays: Vinny walks to butcher shop. Conversation animation.
   Butcher shakes head (refused). Vinny walks away.

3. Simulation resolves: "Frankie assaults Baker St. butcher. Success.
   Witness spotted."
4. 3D renderer plays: Frankie runs in. Fight animation. Butcher on ground.
   Civilian runs away (witness). Police siren in distance.

5. Player sees rival hoods approaching Frankie's position
6. Player issues tactical override: "Reinforce — send Sal to Frankie"
7. Simulation adjusts: Sal reroutes to Frankie's block
8. 3D renderer shows: Sal running toward Baker St.
```

The simulation decides **what happens**. The 3D renderer shows **how it looks**. The player can **react** to what they see with limited tactical overrides.

### Implications

- Can skip 3D visualization entirely → text reports only (fast mode)
- Can watch at different speeds (1x, 2x, 5x)
- Can pause (spacebar) to issue tactical overrides without time pressure
- Can issue orders in real-time without pausing for tension and immersion
- Can focus camera on specific hoods or blocks to follow their week
- Simulation is **time-sliced**, not atomic — advances in steps, accepts player input between steps

---

## Interactive Working Week — Tactical Overrides

The Working Week is not pure playback. The player can issue **tactical overrides** — limited, urgent orders in response to what they see unfolding in the 3D city. These are not full Gang Organizer orders. They are quick reactions: "get out of there", "send backup", "hold ground".

### Available Tactical Overrides

| Override | When Available | Effect |
|---|---|---|
| **Flee** | Any time | Hood breaks off current action, returns to HQ |
| **Reinforce** | During combat/encounter | Send nearby available hoods to the scene |
| **Abort** | Before crime executes | Cancel current order, hood goes idle |
| **Attack** | Rival hoods spotted in your territory | Engage the rivals |
| **Hold ground** | During combat | Hood fights instead of fleeing (override morale break) |
| **Lie low** | Any time | Hood immediately stops and hides, reduces investigation leads |

### What's NOT Available Mid-Week

- No new extortion assignments (that's planning phase)
- No business purchases
- No recruitment
- No bribery
- No complex multi-step orders

The player is a **boss watching his crew work** — can shout "get out of there!" or "send backup!" but can't redesign the week's plan on the fly.

### UI for Tactical Overrides

- Click a hood in the 3D view → radial menu appears with 2-3 context-appropriate options
- One-tap decisions. No menus, no complex UI.
- Minimal HUD — select hood, see options, tap, done.
- Context filters options: combat state shows "Flee/Hold ground/Reinforce", idle state shows "Lie low", pre-crime shows "Abort".

---

## Pause System

The player can toggle between real-time and paused modes during the Working Week:

- **Spacebar**: Toggles pause/resume
- **Default**: Real-time (immersive, tense)
- **Paused**: Simulation freezes, player can issue tactical overrides without time pressure
- **Speed controls**: 1x, 2x, 5x (adjustable during real-time)
- **Auto-pause**: Optional setting — automatically pauses on major events (combat triggered, hood arrested, rival enters territory)

This gives casual players a deliberate, paused experience and hardcore players a tense, real-time experience. Same game, different pace.

---

## Time-Sliced Simulation Architecture

The simulation is not one atomic tick. It advances in **time slices** (e.g., 1 in-game hour per step). Between slices, the player can react.

```
Simulation advances one time slice
    │
    ├── Events generated this slice are visualized in 3D
    │
    ▼
Player observes (real-time) or pauses
    │
    ├── Player may issue tactical overrides
    │
    ▼
Simulation incorporates overrides into next time slice
    │
    └── continue until week is complete
```

### Data Flow (Updated)

```
Simulation Engine (C#, pure logic)
    │
    ├── advances in time slices, produces Event Stream per slice
    │
    ▼
Event Player + Input Handler (C# controller)
    │
    ├── reads events, triggers 3D visualizations
    ├── accepts player tactical overrides
    ├── feeds overrides back to simulation for next slice
    │
    ▼
3D Renderer (Unity)
    │
    ├── spawns/moves entities
    ├── plays animations
    ├── triggers particle effects
    ├── updates camera
    └── renders radial menu HUD for selected hoods
```

---

## Entity Budget

The city has a fixed population. Entity counts are bounded:

| Entity Type | Count | Visual | Notes |
|---|---|---|---|
| Civilians | ~2000 | Simple 3D models, walking streets | Period-appropriate clothing variations |
| Police | ~400 | Uniformed models, patrol routes | Beat-based movement |
| FBI | ~100 | Suit models, specific locations | Only active during investigations |
| Player Hoods | 20-60 | Named characters, distinct appearance | Visible assignments |
| Rival Hoods | 20-60 per gang × 3-4 | Similar to player hoods | Rival-colored indicators |
| Vehicles | ~100-200 | Period cars on roads | Simple traffic system |
| Buildings | ~400 blocks × 1-3 | 3D building models, no interiors | 20-30 unique models, reused with variation |

**Total active entities: ~3000-4000.** Trivial for modern Unity. No scaling problem — city is fixed size.

---

## What the 3D City Shows

### Routine Activity (ambient)
- Civilians walking sidewalks, entering/exiting businesses
- Cars driving on roads (simple traffic system, not physics-based)
- Police officers walking beats
- Businesses operating (doors opening, people entering)

### Player Orders (highlighted)
- Hoods walking to assigned blocks
- Extortion interactions (hood approaches business owner, conversation, payment or refusal)
- Patrol routes (hoods walking assigned routes)
- Recruitment (hood meeting potential recruit)
- Business management (hood visiting owned business)

### Crimes (dramatic)
- Raid: hoods running into building, smashing effects, goods carried out
- Assault: fight animation, victim on ground
- Torch: fire particle effects, building darkens/smokes
- Bomb: explosion particle, building model swaps to damaged version
- Kill: quick encounter, victim falls, hood flees
- Combat encounters: street battles with cover, shooting, fleeing

### Law Enforcement (reactive)
- Detectives visiting crime scenes
- Police converging on active crimes
- Arrests (officer escorts hood away)
- Patrol changes (increased presence in high-heat areas)

### Rival Activity (if intel tier allows)
- Rival hoods visible in your territory (if information tier ≥ Informed)
- Rival extortion happening on screen
- Territory contests visible at borders

---

## What We DON'T Need

- **No building interiors** — buildings are solid 3D shells. All action at street level.
- **No real-time physics** — simulation playback, not physics engine. Hoods move to points, play animations, dice rolls resolve outcomes.
- **No destructible environment** — fire = particle effect + darkened building. Bomb = explosion particle + damaged model swap. No structural simulation.
- **No complex AI pathfinding** — civilians follow sidewalk waypoints. Hoods follow assigned routes. Basic NavMesh or scripted paths.
- **No projectile physics** — combat is auto-resolved. 3D view plays back the result with animations and effects.

---

## Camera System

- **Default**: Isometric-angle overview (matches original game's feel)
- **Free orbit**: Player can rotate, zoom, pan
- **Follow mode**: Click a hood → camera follows them through their week
- **Block focus**: Click a block → camera centers on it, shows activity
- **Event focus**: Major events (combat, arrests, crimes) can auto-focus camera (toggleable)

---

## Technical Architecture

### Unity Setup

- **Rendering**: Standard Unity 3D (URP recommended for performance)
- **Entities**: ECS optional but recommended for clean entity management (~3000-4000 entities)
- **Camera**: Cinemachine free orbit or custom script
- **Lighting**: Baked lightmaps for buildings + simple dynamic for day/night cycle
- **Models**: Low-poly 3D (think Cities: Skylines level of detail, not photorealistic)
- **Animations**: Simple state machines (walk, talk, fight, flee, fall)

### Data Flow (Replaced by Time-Sliced Architecture above)

See "Time-Sliced Simulation Architecture" section above for the updated bidirectional data flow with player input handling.

### Event Stream Format

```json
{
  "week": 1,
  "events": [
    {
      "time": 0.0,
      "type": "hood_move",
      "hood_id": "hood_001",
      "from_block": "block_hq",
      "to_block": "block_baker_st",
      "duration": 3.5
    },
    {
      "time": 3.5,
      "type": "extortion_attempt",
      "hood_id": "hood_001",
      "block_id": "block_baker_st",
      "business_id": "biz_butcher",
      "result": "refused",
      "duration": 2.0
    },
    {
      "time": 5.5,
      "type": "hood_move",
      "hood_id": "hood_001",
      "from_block": "block_baker_st",
      "to_block": "block_hq",
      "duration": 3.5
    },
    {
      "time": 8.0,
      "type": "squeal_event",
      "block_id": "block_baker_st",
      "npc_id": "npc_baker_003",
      "investigation_id": "invest_001"
    }
  ]
}
```

The simulation produces this stream. The 3D renderer consumes it. Text report mode just prints the events as text instead of visualizing them.

---

## Performance Budget

| Resource | Usage | Notes |
|---|---|---|
| CPU | Light | Turn-based sim, basic movement, no physics |
| GPU | Light-Moderate | 4000 simple models + 400 buildings + lighting |
| RAM | Moderate | Entity data + 3D models + textures (~2-4GB) |
| Target | 60fps @ 1080p/1440p | Very achievable on modern hardware |

City is fixed size. Entity count is bounded. No scaling problem.

---

## Comparison to SteelTide

| Aspect | SteelTide | Steel City 3D |
|---|---|---|
| Entity count | 10,000+ | ~3,000-4,000 |
| Real-time? | Yes, 60fps FPS | Yes, but simulation-driven playback |
| Physics | Full voxel destruction | None (visual effects only) |
| Compute shaders | Yes (raymarching) | No |
| DOTS/ECS | Required | Optional but helpful |
| Camera | First-person player | Free orbit, isometric default |
| Lighting | Dynamic, real-time | Baked + simple dynamic |
| Complexity | Maximum | Moderate |
| Building interiors | No | No |

SteelTide's ECS entity management approach transfers. Everything else is simpler.

---

## Build Order

1. **Simulation engine** (pure C#, no rendering) — JSON in, event stream out. Test with text output.
2. **Gang Organizer UI** (2D Unity UI) — data-driven, order assignment, map view.
3. **3D city renderer** (Unity 3D) — consumes event stream, visualizes on 3D city. Polish layer.

The 3D is built last because it depends on the simulation being correct. But it's the piece that makes the game feel modern.

---

## System Interactions

- **All systems**: The 3D renderer visualizes outputs from every system (extortion, crime, combat, economy, police, rival AI)
- **Intelligence**: What's visible in 3D depends on information tier — rival hoods only render if player can see them
- **Combat**: Auto-resolved by simulation, played back as 3D street battle animation
- **Corruption**: Corrupt cops visible on their beats, can be seen patrolling (or not patrolling) your territory

---

## Debug Path Rendering

Debug pathfinding beams are rendered as instanced box meshes via `CommandBuffer.DrawMeshInstanced`, composited directly into the voxel raymarch render texture. See **`docs/systems/PATH_DEBUG_RENDERING.md`** for the full architecture, camera hookup gotcha, and troubleshooting guide.
