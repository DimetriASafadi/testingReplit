# Dense-district rendering safeguards

The regression case is a saved 144-parcel city, with mixed building types, four
orientations, completed roads and income indicators. A fresh district alone is
not sufficient to validate rendering or startup.

## Changed paths

- World creation keeps the loading panel active while rebuilding parcels in
  batches. It yields after approximately 8ms of parcel work or four parcels,
  whichever comes first. Individual terrain/road raster jobs are still
  synchronous; this is not a worker-thread renderer or an absolute latency cap.
- Loading progress is displayed. A failed render retains retry and save
  recovery paths; it does not erase a saved district.
- Yard textures upload only their nontransparent bounds plus a two-pixel
  sampling gutter. Phaser trim metadata retains original logical size, scale,
  depth and ground origin. Building source images are not downscaled.
- Unreferenced yards, shadows and construction/clearing stages are released.
  Live plot/site images protect their textures from eviction.
- Finished road coating rasterizes directly into <=1024px tiles. Each temporary
  shoulder buffer covers one tile plus a filter gutter, rather than a second
  whole-city canvas. Grain positions remain global across tile boundaries.
- Income indicators share one small raster texture and do not allocate endless
  tweens. Construction bars refresh at 5Hz only in view.
- Opacity masks are lazy, for actual machinery/building overlap checks.
- Decorative site animation has both a worker and parcel budget. Offscreen
  animated pools are destroyed; completed building art remains. Pools are
  recreated when building identity or orientation changes.
- Rendering targets 30 FPS, matching the existing simulation-animation cadence.
  Wall-clock deadlines, rewards, movement durations and saved camera units
  are unchanged.
- The dense fixture exposed incomplete legacy view families (for example,
  vegetables at a side angle). They previously threw while creating shadows,
  stopping the entire city load. Existing views of the same subject are now
  used explicitly with a warning; no generic subject is substituted, no art
  is mirrored, and no saved orientation is overwritten. This recovery does
  not constitute completion of the missing four-view artwork.

## Verification boundary

Pure tests cover scheduling, cancellation, pixel bounds and the existing game
rules. The isolated fixture in generated-artifacts/performance runs the actual
Phaser renderer without touching player storage; it exercises a dense district,
four full rotation cycles and a scene rebuild. It is deliberately not included
in native packages.

Desktop lifecycle checks cannot certify Android GPU memory, thermals, long-session
frame pacing, or performance on every handset. No claim of zero lag is made.
