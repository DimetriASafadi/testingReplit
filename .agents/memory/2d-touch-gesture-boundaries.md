---
name: Mobile gesture boundaries
description: Keep camera gestures independent of automatically refreshed UI panels.
---

Do not reset camera gestures on ordinary UI rendering, countdown refreshes, or gameplay ticks. Reset on a genuine interaction with the UI or interruption of the gesture instead.

**Why:** The interface refreshes while the player is dragging or pinching. Treating those refreshes as gesture boundaries would repeatedly interrupt valid touches and create another mobile control bug.

**How to apply:** Preserve one-finger panning and two-finger zoom when changing menus. Gesture cleanup belongs to actual input and lifecycle transitions, not to panel updates.
