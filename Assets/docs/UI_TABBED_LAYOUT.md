# Gang Organizer UI — Tabbed Layout Architecture

**Created**: August 2, 2026
**Status**: Active
**Files**: `GameUIAutoBuilder.cs`, `GameUIController.cs`, `CityMap3D.cs`

---

## Overview

The Gang Organizer UI uses a **tabbed notebook** layout: a top bar, a left-side info panel with tab bar + content area, a 3D isometric city map on the right, and a slim bottom bar with the Run Week button. All UI is built programmatically by `GameUIAutoBuilder` (Editor menu: **Steel City → Build Game UI**) and controlled at runtime by `GameUIController`.

## Screen Layout (normalized anchors)

```
┌─────────────────────────────────────────────────┐  y=1.0
│  TOP BAR (Week | Phase | Treasury)              │  y=0.93
├──────────────────────────┬──────────────────────┤
│  INFO PANEL (left 60%)   │  3D CITY MAP (40%)   │
│  ┌────────────────────┐  │                      │
│  │ TAB BAR (7 tabs)   │  │  Isometric camera    │
│  ├────────────────────┤  │  viewport rect:      │
│  │ CONTENT AREA       │  │  x: 0.6→1.0          │
│  │ (one page visible  │  │  y: 0.09→0.93        │
│  │  at a time)        │  │                      │
│  └────────────────────┘  │                      │
├──────────────────────────┴──────────────────────┤  y=0.09
│  BOTTOM BAR (Run Week button, centered)         │  y=0.0
└─────────────────────────────────────────────────┘
```

## Tab Bar — 7 Tabs

| Index | Tab Label | Page Name | Content Source |
|-------|-----------|-----------|----------------|
| 0 | Hoods | HoodsPage | `RefreshHoods()` |
| 1 | Block | BlockInfoPage | `RefreshBlockInfo()` |
| 2 | Orders | OrdersPage | Order buttons + `RefreshBlockInfo()` |
| 3 | Finance | FinancePage | `RefreshFinances()` |
| 4 | Police | PolicePage | `RefreshPolice()` |
| 5 | Invest | InvestigationPage | `RefreshInvestigations()` |
| 6 | Log | EventLogPage | `RefreshEventLog()` |

### Tab Switching

`GameUIController.ShowTab(int index)`:
1. Sets `activeTabIndex`
2. Iterates `tabPages[]` — `SetActive(i == index)` so only one page is visible
3. Iterates `tabButtons[]` — sets active tab color (gold) vs inactive (dark blue)
4. Calls `LayoutRebuilder.ForceRebuildLayoutImmediate` on the activated page

### Auto-Switch to Log Tab

After `OnRunWeek()` completes, `ShowTab(6)` is called to auto-switch to the Event Log tab so the player immediately sees the week's results.

## Page Creation Methods (GameUIAutoBuilder)

### `CreatePage(name, parent, out content)` — Standard Pages

Used by tabs 0-5. Structure:
```
Page (stretch anchors, VLG)
  └── text entries added at runtime via AddTextToParent()
```

- `VerticalLayoutGroup`: `childControlWidth=true`, `childControlHeight=true`, `childForceExpandHeight=false`
- No ScrollRect, no ContentSizeFitter
- Entries maintain their `LayoutElement.preferredHeight` (20px each)
- Content fits within the fixed page height — entries compress if too many

### `CreateScrollablePage(name, parent, out content)` — Event Log Page

Used by tab 6 (Log). Structure:
```
Page (stretch anchors)
  └── ScrollRect
       └── Viewport (Image + Mask, showMaskGraphic=false)
            └── Content (VLG + ContentSizeFitter.PreferredSize)
                 └── text entries added at runtime via AddTextToParent()
```

- Same VLG settings as `CreatePage`
- `ContentSizeFitter` with `PreferredSize` lets Content grow to fit all entries
- `ScrollRect` enables scrolling when Content exceeds the viewport
- **CRITICAL**: Viewport `Image.color` must be `Color.white` (NOT `Color.clear`) — see [UI Layout Gotchas](UI_LAYOUT_GOTCHAS.md)
- `Mask.showMaskGraphic = false` so the white image isn't rendered

## Event Log Architecture

### Data Flow

1. `AddEventLogEntry(text, color)` — appends `(string, Color)` tuple to `eventLogBuffer` list (max 100 entries)
2. Calls `RefreshEventLog()`
3. `RefreshEventLog()`:
   - `ClearChildren(eventLogContent)` — destroys all existing text GameObjects
   - Loops through `eventLogBuffer` — calls `AddTextToParent()` for each entry
   - Auto-scrolls to bottom (`scrollRect.verticalNormalizedPosition = 0f`)
4. Also called from `RefreshAll()` so the log stays in sync with all other tabs

### Why Buffer + Rebuild (not live GameObjects)

Previous approach created live `TextMeshProUGUI` GameObjects directly in `eventLogContent`. This failed because:
- Entries were often added while the page was inactive (`SetActive(false)`)
- Unity's layout system doesn't process inactive GameObjects
- The `VerticalLayoutGroup` never arranged children added during inactive state
- Even `LayoutRebuilder.ForceRebuildLayoutImmediate` didn't reliably fix this

The buffer + rebuild approach matches exactly how `RefreshInvestigations()`, `RefreshPolice()`, etc. work — `ClearChildren` + `AddTextToParent` on every refresh. This is the proven pattern for all other tabs.

## Pre-Flight Check

`RunPreflightCheck()` runs in `Start()` after `RefreshAll()`. Validates all 28 critical UI references:
- 3D Map: `cityMap`
- Top bar: `weekText`, `phaseText`, `treasuryText`
- Content containers: `hoodList`, `blockInfoContent`, `financeContent`, `policeContent`, `investigationContent`, `eventLogContent`
- Tab pages (7): all page GameObjects
- Tab buttons (7): all tab Button components
- Order buttons (5): extort, collect, patrol, intimidate, lieLow
- Bottom bar: `runWeekButton`

Output:
- **All pass** → Console log + green "PASSED" entry in Event Log
- **Any fail** → Console error with full list + red error entries in Event Log naming each missing ref

## Camera Viewport Alignment

`CityMap3D` uses serialized `viewportYMin` and `viewportYMax` fields to set the camera's `Rect`:
- `viewportYMin = 0.09` (matches InfoPanel bottom anchor)
- `viewportYMax = 0.93` (matches InfoPanel top anchor)
- `viewportWidth = 0.4` (right 40% of screen)
- `mapOnRightSide = true`

This prevents render bleed-through behind the top/bottom bars. The auto-builder sets these via `SerializedObject` when creating the UI.

## Color Palette

| Name | RGB | Usage |
|------|-----|-------|
| BgPanel | (0.137, 0.137, 0.220) | Top bar, InfoPanel, BottomBar backgrounds |
| BgCard | (0.094, 0.094, 0.157) | ContentArea background |
| Gold | (0.886, 0.690, 0.290) | Tab active color, week headers |
| Green | (0.29, 0.86, 0.46) | Success, Run Week button, economy events |
| Red | (0.92, 0.29, 0.29) | Arrests, failures, execution phase |
| Yellow | (0.98, 0.82, 0.29) | Warnings, planning phase |
| tabInactiveColor | (0.094, 0.094, 0.157) | Inactive tab buttons |

## File Responsibilities

| File | Role |
|------|------|
| `GameUIAutoBuilder.cs` (Editor) | One-click UI builder: creates Canvas hierarchy, wires all references via SerializedObject |
| `GameUIController.cs` (Runtime) | Game state management, tab switching, all Refresh methods, event log, pre-flight check |
| `CityMap3D.cs` (Runtime) | 3D isometric map rendering, camera viewport, block click detection |
