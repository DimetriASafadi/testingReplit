---
name: Persistent 2D city layouts
description: Why layout changes require identity-preserving migrations, including updates on the live testing link.
---

Treat a change to deterministic city geometry as a save migration, not only a rendering change. Preserve plot identity, buildings, job deadlines, money and earned neighborhood access. Accept only recognized prior layouts before transforming them; do not silently accept arbitrary coordinates or discard a save.

**Why:** The live testing server reloads during updates. An earlier layout can already be saved while geometry is still being revised; checking its coordinates against only the latest generator then blocks an otherwise valid save.

**How to apply:** Keep prior layout definitions available to validation/migration when changing the generator. Test both original saves and saves from earlier expanded layouts, including active work and edge plots. Random layouts must stay stable across reloads.
