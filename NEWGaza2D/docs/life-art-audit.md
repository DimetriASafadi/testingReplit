# Life / reconstruction visual audit

Generated masters (1024px RGBA, transparent background confirmed: corner alpha 0) are in the git-ignored `art-src/new/`. Optimized runtime WebPs are in `public/art/life/` (4-20 KB each). The contact-sheet fixture (each sprite drawn over `ground-earth.jpg` at its in-game anchor and scale, with a red cross at the anchor) is saved to `art-src/new/life-fixture-contact.jpg`. The temporary fixture page has been removed. The fixture checks alpha and anchors only. It is not proof of in-city integration; idle/build/complete were not stepped through in a live city save.

| Planned visual | Problem found | Fix | Remaining |
|---|---|---|---|
| Flag pole project (idle) | An unknown project fell back to `ind`/`work`, so a factory appeared | `siteKey` returns null for unknown projects; `syncSites` only draws its 8 mapped projects and skips `flag`; `?? 'work'` removed | none |
| Flag (building) | Procedural pole only; earlier pass sank the plaza with a crop | Dedicated generated foundation site (formwork, rebar, paving stock), two crew walkers, progress bar; no pole or cloth | none |
| Flag (complete) | Procedural pole | Generated stone plaza with pole (anchor = pole foot 0.557, finial 0.095); existing animated Palestinian flag hoisted to the finial | checked only in the static fixture |
| Streetlamps (power project) | Crude canvas lamp | Generated steel LED lamp, anchor at base (0.88), scale 0.34 | thin at overview zoom (realistic) |
| Coastal boats | Crude canvas boat | Generated Gaza fishing boat, gentle bob kept | none |
| Low-rise fringe | Flat rectangles | 7 generated flat-roof houses, seeded spacing and mirroring | none |
| Rashid road palms | Crude procedural stubs | Generated single roadside date palm in stone planter (`palm.webp`, anchor 0.86, scale about 0.3, soft contact shadow), every 3.1 units on the promenade; included in `preloadLifeArt` | none |
| Greenhouse / agricultural fringe | Pale sheets already replaced by the user's blended fields | untouched | none |
| Orchard walls fringe | Thin procedural lines | kept (they read as low walls) | optional |
| Wood factory four faces (`wood_*`) | Inspected front, left, right and back: consistent pitched-roof stone workshop with lumber stacks | kept | none |
| Household store four faces (`hshop_*`) | Inspected front, left, right and back: consistent two-storey awning shop with rooftop tanks | kept | none |
| Tents (`exp:tent0-3`) | Inspected: painted, matching | kept | none |
| Paver / bulldozer machinery | Paver sprites painted and matching. `bulldozer-preview.webp` is a flatter cartoon icon (UI preview only); in-city bulldozer frames not changed | kept | the bulldozer UI preview style is flatter than the city art; optional repaint |
| Workers / residents | Existing drawn walker frames, consistent with the fleet | kept; reused as flag crew | none |
| Projects' construction (other 8) | Generic `ind_c`/`apt_c`/`shop_c` stages | kept (faithful existing art) | none |
