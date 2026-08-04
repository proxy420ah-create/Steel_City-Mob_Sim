# Unity uGUI Layout Gotchas — Lessons Learned

**Created**: August 2, 2026
**Status**: Active
**Project**: Steel City Mob Sim — Gang Organizer UI

---

## Overview

This document catalogs the critical Unity uGUI pitfalls discovered during the Gang Organizer UI development. Each gotcha includes the symptom, root cause, and fix. These lessons apply to any Unity project using `VerticalLayoutGroup`, `ScrollRect`, `Mask`, and tabbed UI patterns.

---

## 1. Mask Alpha = 0 Hides Everything (CRITICAL)

### Symptom
Text entries inside a `ScrollRect` → `Viewport` (with `Mask`) are invisible. The hierarchy is correct, entries exist, but nothing renders. Toggling the Viewport's `Image` component off in the Inspector makes the text appear.

### Root Cause
The `Mask` component uses the Viewport `Image`'s **alpha channel** to determine what's visible. Setting `Image.color = Color.clear` (alpha = 0) tells the mask that nothing should be visible — it masks out **all children**, including text.

### Fix
```csharp
// WRONG — masks out everything
viewportObj.AddComponent<Image>().color = Color.clear;

// CORRECT — mask shows all children, image not rendered
viewportObj.AddComponent<Image>().color = Color.white;
var mask = viewportObj.AddComponent<Mask>();
mask.showMaskGraphic = false;  // don't render the white image
```

### Key Takeaway
**`Mask` requires `Image.color` with alpha > 0.** Use `Color.white` + `showMaskGraphic = false` for invisible masking. This was the root cause of the event log appearing blank for the entire development session — it persisted from the original bottom-bar ScrollRect implementation through multiple refactors.

---

## 2. Layout System Ignores Inactive GameObjects

### Symptom
Text entries added to a `VerticalLayoutGroup` while its parent `GameObject` is `SetActive(false)` are never arranged. When the page is activated via `SetActive(true)`, the entries exist in the hierarchy but have zero-size `RectTransform`s and are invisible.

### Root Cause
Unity's layout system (`VerticalLayoutGroup`, `HorizontalLayoutGroup`, `ContentSizeFitter`) does not process inactive GameObjects. Children parented to an inactive container are not added to the layout queue. When the container is activated, Unity does not automatically trigger a layout pass for children that were added during the inactive period.

### Fix — Option A: Buffer + Rebuild (Preferred)
Store entries as data, rebuild on refresh:
```csharp
private readonly List<(string text, Color color)> logBuffer = new();

private void AddEntry(string text, Color color)
{
    logBuffer.Add((text, color));
    RefreshLog();  // ClearChildren + AddTextToParent for each entry
}

private void RefreshLog()
{
    if (content == null) return;
    ClearChildren(content);
    foreach (var (text, color) in logBuffer)
        AddTextToParent(content, text, color);
}
```
Call `RefreshLog()` from `RefreshAll()` so it runs on every UI update, including after `SetActive(true)`.

### Fix — Option B: Force Rebuild on Activation
```csharp
private void ShowTab(int index)
{
    // ... SetActive logic ...
    if (tabPages[index] != null)
        LayoutRebuilder.ForceRebuildLayoutImmediate(tabPages[index].GetComponent<RectTransform>());
}
```

### Key Takeaway
**Never add children to inactive GameObjects expecting them to lay out automatically.** Either rebuild from a buffer when the page becomes active, or force a layout rebuild in `ShowTab()`. The buffer approach is more reliable and matches the `ClearChildren + AddTextToParent` pattern used by all other tabs.

---

## 3. childControlHeight = false Ignores LayoutElement.preferredHeight

### Symptom
Text entries in a `VerticalLayoutGroup` have huge gaps between them (100px default `RectTransform` height) or content overflows far below the visible panel area. `LayoutElement.preferredHeight = 20` appears to be ignored.

### Root Cause
When `VerticalLayoutGroup.childControlHeight = false`, the VLG does not control the height of its children. This means `LayoutElement.preferredHeight` is not read by the layout system — children fall back to their `RectTransform`'s default size (100×100 for newly created GameObjects).

Additionally, `ContentSizeFitter` on the container cannot calculate the correct total preferred height because the VLG isn't reporting child preferred heights.

### Fix
```csharp
// WRONG — preferredHeight ignored, children default to 100px
vlg.childControlHeight = false;

// CORRECT — preferredHeight respected, ContentSizeFitter works
vlg.childControlHeight = true;
```

### Key Takeaway
**Always set `childControlHeight = true`** on `VerticalLayoutGroup` when using `LayoutElement.preferredHeight` to size entries. This is required for `ContentSizeFitter.PreferredSize` to calculate the correct total content height for scrolling.

---

## 4. ContentSizeFitter Without childControlHeight Breaks Scrolling

### Symptom
A `ScrollRect` with `ContentSizeFitter.PreferredSize` on the Content doesn't scroll. Content height is 0 or doesn't grow as entries are added.

### Root Cause
`ContentSizeFitter` reads preferred sizes from the `VerticalLayoutGroup`, which in turn reads `LayoutElement.preferredHeight` from each child. If `childControlHeight = false`, the VLG doesn't read child preferred heights, so `ContentSizeFitter` gets 0 and the Content never grows.

### Fix
Ensure both are set correctly:
```csharp
vlg.childControlHeight = true;
contentObj.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
```

---

## 5. SetActive(true) Doesn't Always Trigger Layout Rebuild

### Symptom
Switching to a tab via `SetActive(true)` shows the page but text entries are not positioned — they overlap or have zero size.

### Root Cause
`SetActive(true)` activates the GameObject but does not guarantee a layout pass. If children were added while the page was inactive (or if the layout system was dirty), the `VerticalLayoutGroup` may not arrange children until the next frame's layout pass.

### Fix
Force an immediate rebuild after activation:
```csharp
tabPages[index].SetActive(true);
LayoutRebuilder.ForceRebuildLayoutImmediate(tabPages[index].GetComponent<RectTransform>());
```

### Key Takeaway
**Always force a layout rebuild after `SetActive(true)`** in tab switching code. This is a safety net even when using the buffer + rebuild approach.

---

## 6. ScrollRect Content Anchors Must Be Top-Anchored

### Symptom
Entries in a ScrollRect appear at the bottom or center of the Content, or the Content grows downward from the middle.

### Root Cause
The Content's `RectTransform` anchors determine where it grows from. For a top-down list, Content must be anchored to the top.

### Fix
```csharp
contentRect.anchorMin = new Vector2(0, 1);  // bottom-left at top
contentRect.anchorMax = new Vector2(1, 1);  // top-right at top
contentRect.pivot = new Vector2(0.5f, 1);   // pivot at top-center
contentRect.sizeDelta = new Vector2(0, 0);   // let ContentSizeFitter control height
```

---

## Summary Checklist

| Gotcha | Fix |
|--------|-----|
| Mask hides everything | `Image.color = Color.white` + `showMaskGraphic = false` |
| Inactive GO layout ignored | Buffer + rebuild on refresh, or `LayoutRebuilder.ForceRebuildLayoutImmediate` |
| preferredHeight ignored | `childControlHeight = true` on VLG |
| ContentSizeFitter height = 0 | `childControlHeight = true` + `FitMode.PreferredSize` |
| SetActive doesn't layout | `LayoutRebuilder.ForceRebuildLayoutImmediate` after `SetActive(true)` |
| Content grows from wrong position | Top-anchored RectTransform (anchorMin/Max Y = 1, pivot Y = 1) |
