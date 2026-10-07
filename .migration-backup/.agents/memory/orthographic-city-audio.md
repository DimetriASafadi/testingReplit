---
name: Orthographic city audio
description: Why city machinery audio uses virtual ground-focus distance and zoom rather than physical listener distance.
---

Keep the existing camera listener and apply ground-focus distance, explicit zoom attenuation and distant filtering to the machinery soundscape. Avoid adding a second listener or applying ordinary physical-distance rolloff on top of that mix.

**Why:** Orthographic image framing is independent of the camera's setback. The camera remains far from the ground even in a close inspection, so ordinary listener rolloff makes visibly nearby equipment quiet. Conversely, a distance envelope without explicit zoom attenuation leaves a machine under the focus equally loud in the high city overview. Map coordinates also represent 20 metres per unit; metre-sized falloff constants can unintentionally make machinery or surf audible across the entire city.

**How to apply:** Assert audible behavior, not only finite gain values: close equipment must dominate the overview, overview wind must dominate equipment, and inland focus must not receive shoreline surf. Stationary vehicle travel must not silence a moving hydraulic arm; track travel and hydraulic articulation are separate activities.