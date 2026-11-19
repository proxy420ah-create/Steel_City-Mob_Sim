# Construct — Chat Field Holds Focus While Hidden, Eats All Game Keys

**Date**: 2026-11-18
**Status**: 🟢 FIXED
**Severity**: 🟡 MODERATE (dead keyboard until click/blur)

## Symptoms

- Latent dead-keyboard state: any key routed through the window `keydown`
  handler goes silent while the chat `<input>` holds DOM focus — including
  Enter in place mode, WASD, rotation keys.
- Found while triaging the Enter-drop bug (see
  CONSTRUCT_ENTER_DROP_STACK_OVERFLOW.md — the actual culprit there was a
  stack overflow in placeDrop, not this field).

## Root Cause

Two compounding bugs in `docs/construct/19_chat.js`:

1. **`display:none` does not release DOM focus in Chrome.** `chatPin` hid the
   chat `<input>` whenever the band wasn't on screen but never blurred it.
   `document.activeElement` stayed `chatIn`, and its `stopPropagation()`
   keydown swallowed every key before the game handler saw it.
2. **Interval gating left the field stuck.** The `chatPin` `setInterval` was
   gated to `padPage === 'chat'` for perf — so navigating off the chat tab
   skipped the hide branch entirely, leaving the field `display:block` and
   potentially focused at its last screen position forever.

## Fix

- `chatHide()` helper hides the field AND `blur()`s it when it holds
  `activeElement` (`19_chat.js`).
- The interval still only runs `chatPin()` on the chat tab, but now runs the
  cheap `chatHide()` cleanup whenever the field is visible/focused off-tab.

## Verification

- `node --check docs/construct/19_chat.js` — clean.
