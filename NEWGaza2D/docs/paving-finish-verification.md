# Final paving coat verification

The final-coat renderer now invalidates only changed, world-aligned road pieces and junction polygons. Diagonal polygon coverage is checked, not just rectangular bounds. Raster work uses one 256px tile per update, with separate planning steps. Existing textures are refreshed in place; unfinished mat remains until replacement is ready.

## Checks

- TypeScript passes.
- The full regression run passed 160 tests and exposed one incorrect new fixture expectation (a square crossed four tiles, not one). That fixture was corrected. All targeted dirty-tile/surface-cache tests then passed.
- After adding stable run splitting, all 12 targeted paving/geometry tests pass, including the new requirement that more than 80% of existing pieces remain unchanged when a crossing completes.
- Surface tests cover held mat without repeat uploads, cleanup, clock changes, and offscreen recovery.
- A disposable render-only Phaser harness restored a dense finished road grid, then completed one full street. The resulting grid was visually inspected. It updated eight affected tiles rather than all 31 existing tiles, in 11 bounded update steps including planning/completion.
- In the final headless render-only smoke run, the maximum CPU duration of a bounded update was 5.2ms. This is not a handset FPS measurement, a full-city gameplay benchmark, or a guarantee for all devices. The renderer backend was not recorded.
- The harness touched no saves or balances and its temporary preview route/bundle were removed before packaging.

## Device verification still needed

On the reported Android 11 / 4GB Helio phone, finish several streets in a densely built saved district, including concurrent jobs, repeated map switching, and background/resume. Measure full frame times, memory, and texture counts rather than relying on the desktop CPU result.

## Concurrent machine follow-up

The global handover condition could recreate an older completed street while its paver was still returning, when another job triggered a new coat. Mat ownership is now tracked by job; retired jobs cannot re-enter handover. Preparation and fresh asphalt also share one fair surface raster/update slot per animation update, separate from the already bounded finished-coat tile update. Deferred sections do not allocate blank textures or store outdated progress callbacks.

- TypeScript and 22 targeted road geometry/cache/handover tests pass.
- The production `RoadAnimation` was exercised with three machines in the render-only fixture `tests/render/concurrent-pavers.ts`: A completes while B/C continue, then B completes while A is returning and C continues.
- In that Canvas smoke run, completing A caused zero unrelated B/C surface refreshes; completing B caused zero new surface allocations for returning A and zero unrelated C refreshes. The shared preparation/fresh-mat upload counter peaked at one per update.
- Progress on unrelated jobs is effectively held constant in this fixture to separate invalidation from normal animation updates. This is not an Android/WebGL FPS benchmark.
- The temporary browser route and compiled fixture bundle were removed after verification; the source fixture remains outside production assets.
