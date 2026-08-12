# Steel City UI Design Research & Improvement Plan

**Version**: 1.0 | **Date**: Aug 12, 2026 | **Status**: 📋 Research → Planning

---

## Table of Contents

1. [Current State Assessment](#1-current-state-assessment)
2. [UI Polish & Feedback (Juice)](#2-ui-polish--feedback-juice)
3. [Accordion / Collapsible Sections](#3-accordion--collapsible-sections)
4. [Advanced Scrolling Techniques](#4-advanced-scrolling-techniques)
5. [Map Overlay Systems](#5-map-overlay-systems)
6. [Design System & Consistency](#6-design-system--consistency)
7. [Tab Architecture Improvements](#7-tab-architecture-improvements)
8. [Implementation Priority Matrix](#8-implementation-priority-matrix)
9. [Reference Links](#9-reference-links)

---

## 1. Current State Assessment

### What We Have
- **7 tabs**: Hoods, Block, Editor, Finance, Police, Invest, Log
- **uGUI (Canvas)** — programmatically built via `GameUIAutoBuilder.cs`
- **Top bar**: Week, Phase, Treasury, Character Status
- **Bottom bar**: RUN WEEK button
- **3D city map**: Orthographic camera, voxel blocks, character on street

### Known Issues
| Issue | Severity | Tab |
|---|---|---|
| No button hover/press/disabled states | High | All |
| Editor tab: giant flat scroll, no section grouping | High | Editor |
| Editor tab: ScrollRect + ContentSizeFitter causes layout lag | Medium | Editor |
| Event log: no virtualization, will chug at 500+ entries | Medium | Log |
| Tab switches are instant SetActive — no transition | Medium | All |
| Treasury/numbers snap instead of counting | Low | Top bar |
| No map overlay system (territory, land value, etc.) | High | Map |
| Inconsistent font sizes (11,12,13,14,16,18 with no system) | Medium | All |
| No disabled state styling (just interactable=false) | Medium | All |
| Order buttons were in wrong tab (FIXED — now in Hoods) | ~~Done~~ | ~~Hoods~~ |
| Camera focused on assigned block, not character (FIXED) | ~~Done~~ | ~~Map~~ |

---

## 2. UI Polish & Feedback (Juice)

*Source: [GameJuice UI Feedback Guide](https://gamejuice.co.uk/articles/ui-feedback-design), [Lost Continent Games](https://lostcontinentgames.com/color-vs-movement-in-ui/)*

### Button States (ALL buttons need these)

| State | Visual | Trigger | Notes |
|---|---|---|---|
| **Normal** | 100% scale, base color | Default | Resting state |
| **Hover** | 105% scale, slight color brighten | Pointer enter | Immediate, no delay. Tells player "this is clickable" |
| **Press** | 95% scale, slight darkening | Pointer down (NOT up!) | Tactile confirmation. 30ms faster than waiting for release |
| **Release** | Bounce 95% → 102% → 100% with elastic ease | Pointer up | Overshoot is critical — linear return feels dead |
| **Disabled** | 40% opacity, no hover response | `interactable = false` | Player should see at a glance it won't respond |

**Implementation approach for uGUI**:
- Custom `ButtonHoverEffect` MonoBehaviour on each button
- Use `RectTransform.localScale` tweening (DOTween or manual)
- Listen to `IPointerEnterHandler`, `IPointerExitHandler`, `IPointerDownHandler`, `IPointerUpHandler`
- No external dependency needed for basic version

### Health/Status Bars
- **Ghost bar**: Main bar jumps instantly, ghost bar holds old value 400ms then tweens
- **Color interpolation**: Green → yellow → red across range
- **Pulse at critical**: Scale oscillation draws peripheral attention

### Number Animations
- Treasury money changes: count up/down over 0.5s
- Week counter: slide in from right
- Phase text: fade out → swap → fade in

### Tab Transitions
- Current: `SetActive(true/false)` — instant, jarring
- Better: Fade out old page (0.1s) → swap → fade in new (0.15s)
- Or: Slide pages left/right like a carousel
- Use `CanvasGroup.alpha` for fade, `RectTransform.anchoredPosition` for slide

---

## 3. Accordion / Collapsible Sections

*Source: [Unity UI Extensions Accordion](https://unity-ui-extensions.github.io/ugui/controls/accordion/), [Unity Foldout](https://docs.unity3d.com/Manual/UIE-uxml-element-Foldout.html)*

### What It Is
Vertically stacked sections with clickable headers. Click header → content expands/collapses below. Only one open at a time (or multiple if `AllowMultiple = true`).

### Perfect For Editor Tab
Currently the Editor tab dumps ALL debug controls in one giant scroll. With accordions:

```
▼ ROAD & CAMERA
   Road Width [slider]      Sidewalk Width [slider]
   Camera Zoom [slider]     [RESET CAMERA]
   Buildings/Block Row [stepper]
   ☑ Show Road Names       ☑ Show Block Labels
   ☑ Split Terrain         ☐ Proxy Render
   [REBUILD CITY]
▶ SHADOW DEBUG
▶ LIGHTING DEBUG
▶ MATERIAL BRIGHTNESS
```

### Implementation Options

| Option | Effort | Dependency | Animation |
|---|---|---|---|
| **Custom simple** | Low | None | `SetActive` toggle, optional size tween |
| **Unity UI Extensions** | Low | Package install | Built-in tween transitions |
| **UI Toolkit Foldout** | Medium | Migration to UI Toolkit | Built-in |

### Custom Accordion Spec (No Dependency)
```
AccordionSection:
  - Header (Button, 28px height, gold text, bg color)
    - Arrow icon (▶ when collapsed, ▼ when expanded)
    - Label text
  - Content (VerticalLayoutGroup, animated height)
    - Child controls...

AccordionGroup:
  - List<AccordionSection>
  - AllowMultiple: bool
  - ToggleOn: bool (click open section to close it)
  - When section opens, close others (unless AllowMultiple)
```

### Animation
- Animate `ContentRectTransform.sizeDelta.y` from 0 → preferredHeight
- Use `Mathf.Lerp` in coroutine over 0.2s with `EaseOutQuad`
- Or use `LayoutElement.preferredHeight` tween (works with VerticalLayoutGroup)

---

## 4. Advanced Scrolling Techniques

*Source: [Recyclable Scroll Rect](https://github.com/Migzro/Recyclable-Scroll-Rect), [InfinityScroll](https://github.com/Mr-sB/InfinityScroll), [Unity UI Extensions](https://unity-ui-extensions.github.io/ugui/)*

### Current Problems

**Editor tab**: Uses `ScrollRect` + `VerticalLayoutGroup` + `ContentSizeFitter`. This works but:
- All items instantiated upfront (no recycling)
- `ContentSizeFitter` causes layout rebuild every frame if content changes
- No sticky headers
- No scroll-to-section

**Event log**: Same basic ScrollRect. Will degrade with 500+ entries.

### Virtual Scrolling (For Future)
Only instantiate visible items + a few buffer. Recycle off-screen items.

**When we need it**:
- Event log > 100 entries
- Hood list > 20 hoods
- Business/property lists
- Any list that could grow large

**Options**:
- [Recyclable Scroll Rect](https://github.com/Migzro/Recyclable-Scroll-Rect) — most feature-complete
- [InfinityScroll](https://github.com/Mr-sB/InfinityScroll) — simpler, uses ObjectPool
- [VirtualList](https://github.com/disruptorbeaminc/VirtualList) — clean API, Beam team

### Other Scroll Techniques

| Technique | Use Case | Source |
|---|---|---|
| **Scroll Snap** | Page-style navigation (hood cards?) | Unity UI Extensions |
| **Scroll Rect Tweener** | Programmatic scroll-to with easing | Unity UI Extensions |
| **Scroll To Selection** | Auto-scroll to keep selected item visible | Unity UI Extensions |
| **Infinite Loop** | Marquee tickers, news feeds | Unity UI Extensions |
| **Fancy Scroll View** | High-performance with custom layouts | Unity UI Extensions |

### Editor Tab Scrolling Fix (Near-term)
- Replace flat scroll with **accordion** (sections collapse, reducing scroll need)
- Add **scroll-to-section** when accordion header clicked (if section is off-screen)
- Use `LayoutRebuilder.ForceRebuildLayoutImmediate` only on expand/collapse, not every frame

---

## 5. Map Overlay Systems

*Source: [Gangsters 1998 Manual](https://d2.xp.myabandonware.com/f/lzff/Gangsters-Organized-Crime_Manual_Win_EN.pdf), [Archon Engine](https://forgottenhistory.github.io/Archon-Engine/Docs/Engine/master-architecture-document.html), [Territory Shader](https://discussions.unity.com/t/how-to-optimize-and-improve-this-jump-flood-and-distance-field-shader-grand-strategy-faction-borders-shader/1694427)*

### Gangsters 1998 Map System (Reference)

**3 zoom levels**:
- **City Plan (L)** — Full city overview, territory overlays
- **Rooftop View (M)** — Medium zoom, business locations
- **Street View (H)** — Close-up, character-level view

**Map overlay buttons** (right side of map):
- **Territory** — Red outline around blocks you own/protect
- **Land Value** — Color-coded by property value
- **Business Ownership** — Shows who controls each business
- **Gang Locations** — Shows rival positions (once discovered)

**Two-phase gameplay**:
- **Gang Organizer** (planning) — organize teams, view city info, give orders
- **Working Week** (execution) — real-time, watch orders carried out, intervene

### Planned Overlays for Steel City

| Overlay | Data Source | Visual | When |
|---|---|---|---|
| **Territory** | `block.ownerGang` | Gang-colored tint on block tops + outline | Planning |
| **Land Value** | `block.landValue` | Green (high) → yellow → red (low) heat map | Planning |
| **Business Control** | `business.ownerGang` | Icon/color on business blocks | Planning |
| **Police Presence** | `block.policeStrength` | Blue intensity overlay | Planning |
| **Heat Level** | `block.heatLevel` | Red gradient (high heat = hot) | Planning |
| **Rival Activity** | `block.rivalActivity` | Pulsing markers | Execution |

### Implementation Approaches

**A. Simple (Block Tinting)** — For our block count (~20-40 blocks):
- Each block already has a mesh + material
- Use `MaterialPropertyBlock` to override block top color per-block
- Toggle overlay mode → set color override on each block
- No shader work needed, just C# property blocks
- **Effort**: Low. **Performance**: Fine for <100 blocks.

**B. GPU Shader (Territory Borders)** — For smooth territory lines:
- Render `RenderTexture` where each pixel = block ID encoded as color
- Fragment shader reads block ID → looks up owner → tints with gang color
- **Jump-Flood algorithm** for smooth border lines between territories
- Single draw call, scales to thousands of blocks
- **Effort**: High. **Performance**: Excellent at scale.

**C. Hybrid (Recommended)**:
- Use approach A for block tinting (immediate)
- Use approach B later for smooth territory borders (when we have 100+ blocks)
- Overlay toggle bar above 3D map with icon buttons

### Overlay Toggle UI
```
[ TERRITORY ] [ LAND VALUE ] [ POLICE ] [ HEAT ] [ BUSINESS ] [ OFF ]
```
- Row of toggle buttons above the 3D map view
- Only one overlay active at a time (click another to switch)
- Click active overlay again to turn off → normal view
- Active overlay button highlighted with accent color
- Each button has icon + label

### Zoom Levels (Gangsters-Style)
We already have camera zoom in the Editor tab. Consider exposing zoom presets:
- **City Plan** — Orthographic size 30, top-down angle
- **Street View** — Orthographic size 5, slight angle
- **Follow Character** — Camera locks to Vinny

---

## 6. Design System & Consistency

*Source: [drawcode Design System](https://github.com/drawcode/unity-ui-document-design-system), [Game UI Design Best Practices](https://generalistprogrammer.com/tutorials/game-ui-design-best-practices)*

### Spacing Scale
Use ONLY these values for padding, margins, gaps:

```
4px  — tight (between icon and text)
8px  — compact (between related elements)
16px — standard (between sections)
24px — loose (major section breaks)
32px — very loose (panel padding)
```

### Color Palette (Defined)
```
ACCENT:     #c69e33 (Gold)       — headers, highlights, active states
POSITIVE:   #4d9c5c (Green)      — treasury positive, confirm, run week
NEGATIVE:   #c75c5c (Red)        — errors, danger, extort
INFO:       #5a8ac4 (Blue)       — patrol, info, police
WARNING:    #c9a84c (Yellow)     — planning phase, caution
SPECIAL:    #b873c4 (Purple)     — intimidate, special

NEUTRALS:
  BgPanel:   #1a1a2e             — main panel background
  BgCard:    #252538             — card/element background
  BgInput:   #2a2a40             — input fields
  TextBright:#dedede             — primary text
  TextMuted: #8a8a9a             — secondary text
  TextDim:   #5a5a6a             — disabled/placeholder
```

### Typography Hierarchy
```
H1 Header:    16px Bold Gold     — section titles
H2 Subheader: 14px Bold Bright   — subsection titles
Body:         13px Regular Bright— normal text
Caption:      11px Regular Muted — hints, descriptions
Stat Number:  14px Bold Bright   — treasury, counts
Tab Label:    12px Bold          — tab buttons
Button Label: 13px Bold          — buttons
```

### Consistency Rules
- Same control = same look everywhere (buttons, toggles, sliders)
- Corner radius: consistent (we use 0px — fine for brutalist aesthetic)
- Button heights: 32px standard, 28px compact
- Tab labels: 1-2 words, no ALL CAPS (Unity best practice)
- Disabled: 40% opacity + no hover, always

---

## 7. Tab Architecture Improvements

### Current → Proposed

| # | Current | Proposed | Changes |
|---|---|---|---|
| 0 | Hoods | **Hoods** | ✅ Order buttons moved in (done) |
| 1 | Block | **Block** | No change |
| 2 | Editor | **Editor** | Add accordion sections, fix scrolling |
| 3 | Finance | **Finance** | No change |
| 4 | Police | **Police** | No change |
| 5 | Invest | **Invest** | No change |
| 6 | Log | **Log** | Add virtual scrolling when needed |

### Future Tabs (When Game Systems Expand)
- **Map** tab — overlay controls + zoom presets (or overlay bar above 3D map)
- **Businesses** tab — list of owned/available businesses with filters
- **Diplomacy** tab — gang relations, treaties, rival status
- **Recruitment** tab — hire new hoods, manage roster

### Tab Best Practices (from research)
- Tab labels: 1-2 words, short and scannable
- Don't use ALL CAPS
- Avoid disabled tabs — remove if not available
- Don't use tabs when users need to compare content between panels
- Max 4 tabs in narrow spaces (we have 7 — consider grouping)

---

## 8. Implementation Priority Matrix

| Feature | Impact | Effort | Priority | Dependencies |
|---|---|---|---|---|
| Accordion in Editor tab | High | Low | **P0** | None |
| Editor tab scroll fix | Medium | Low | **P0** | Accordion |
| Button hover/press polish | High | Medium | **P1** | Custom component |
| Map overlay toggle bar | High | Medium | **P1** | Block tinting C# |
| Territory overlay (block tint) | High | Low | **P1** | Overlay bar |
| Tab transition animations | Medium | Medium | **P2** | CanvasGroup |
| Treasury number animation | Low | Low | **P2** | Tween utility |
| Design system cleanup | Medium | Medium | **P2** | Audit all files |
| Land value overlay | Medium | Low | **P3** | Overlay bar |
| Police/Heat overlays | Medium | Low | **P3** | Overlay bar |
| Virtual scrolling (event log) | Low | High | **P3** | Package or custom |
| GPU territory borders | Low | High | **P4** | Shader work |
| Scroll-to-selection | Low | Low | **P4** | After accordion |
| Ghost bar for health | Low | Medium | **P4** | When health bars exist |

### P0 = Do Now (Quick wins, immediate UX improvement)
### P1 = Next Sprint (Core game feel + map functionality)
### P2 = Polish Pass (Visual refinement)
### P3 = When Needed (Scale concerns, additional overlays)
### P4 = Future (Advanced techniques, nice-to-have)

---

## 9. Reference Links

### Unity UI Best Practices
- [Unity UI Toolkit Best Practices (2025)](https://docs.unity3d.com/Manual/best-practice-guides/ui-toolkit-for-advanced-unity-developers/bpg-uiad-index.html)
- [Game UI Design: Principles & Best Practices (2026)](https://generalistprogrammer.com/tutorials/game-ui-design-best-practices)
- [Unity UI Extensions (uGUI)](https://unity-ui-extensions.github.io/ugui/)
- [Unity UI Extensions (UIToolkit)](https://unity-ui-extensions.github.io/uitoolkit/)

### UI Polish & Juice
- [UI Feedback Design — GameJuice](https://gamejuice.co.uk/articles/ui-feedback-design)
- [Color vs Movement in UI — Lost Continent Games](https://lostcontinentgames.com/color-vs-movement-in-ui/)
- [Animating Reactive UI in Unity 6](https://samuel-bouchet.fr/posts/2024-12-10-animating-reactive-ui/)
- [Recreating TLOU UI in Unity](https://www.adammadojemu.com/blog/recreating-ui-from-the-last-of-us-part-2-in-unity)

### Scrolling
- [Recyclable Scroll Rect](https://github.com/Migzro/Recyclable-Scroll-Rect)
- [InfinityScroll](https://github.com/Mr-sB/InfinityScroll)
- [VirtualList (Beam)](https://github.com/disruptorbeaminc/VirtualList)

### Accordion
- [Unity UI Extensions Accordion](https://unity-ui-extensions.github.io/ugui/controls/accordion/)
- [Unity Foldout (UI Toolkit)](https://docs.unity3d.com/Manual/UIE-uxml-element-Foldout.html)
- [ShunUI Accordion](https://www.experir.com/products/shunui-ugui/docs/accordion)

### Map Overlays & Territory
- [Gangsters 1998 Manual (PDF)](https://d2.xp.myabandonware.com/f/lzff/Gangsters-Organized-Crime_Manual_Win_EN.pdf)
- [Gangsters IGN Review](https://www.ign.com/articles/1998/12/19/gangsters)
- [Gangsters IGN Guide — Forming Teams](https://www.ign.com/wikis/gangsters-organized-crime/Forming_Teams)
- [Gangsters IGN Guide — Illegal Businesses](https://www.ign.com/wikis/gangsters-organized-crime/Illegal_Businesses)
- [Archon Engine — Grand Strategy Architecture](https://forgottenhistory.github.io/Archon-Engine/Docs/Engine/master-architecture-document.html)
- [Territory Border Shader (Jump-Flood)](https://discussions.unity.com/t/how-to-optimize-and-improve-this-jump-flood-and-distance-field-shader-grand-strategy-faction-borders-shader/1694427)
- [OpenFrontIO Territory Fragment Shader](https://github.com/openfrontio/OpenFrontIO/blob/20bc311c/src/client/render/gl/shaders/map-overlay/territory.frag.glsl)
- [Hex Strategy Map Render](https://nicolaschavez.com/projects/hex-map-render/)
- [World Map Strategy Kit 2 (Asset Store)](https://assetstore.unity.com/packages/tools/game-toolkits/world-map-strategy-kit-2-150938)
- [Terrain Grid System 2 (Asset Store)](https://assetstore.unity.com/packages/tools/terrain/terrain-grid-system-2-244921)

### Design Systems
- [drawcode Unity UI Design System](https://github.com/drawcode/unity-ui-document-design-system)
- [Unity Editor Design System — Tabs](https://unityeditordesignsystem.unity.com/components/tab)

---

*This document is a living reference. Update as techniques are implemented and new research is conducted.*
