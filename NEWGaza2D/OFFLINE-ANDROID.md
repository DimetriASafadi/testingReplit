# Android offline test build — 1.5

This is a debug-signed test APK with the same application ID, `com.newgaza.citygame`, and versionCode 6. It bundles the game rather than opening the development URL. Install over the previous build when Android accepts the signature; **do not uninstall to bypass a signature error without first exporting the old game's save**. Browser and Android saves are separate.

## Current package
- Dense saved districts rebuild their parcels in yielding batches with loading progress. Transparent yard padding is removed from stored textures without changing the ground anchor or reducing the source building resolution.
- Unused yard/shadow/stage textures are released. Income indicators share a texture without infinite tweens; animation has a bounded parcel budget even when the city is full.
- Finished roads rasterize directly into mobile-sized tiles; temporary shoulder canvases are tile-sized, not city-sized. Construction bars refresh at 5Hz only in view. Rendering targets 30 FPS.
- TypeScript and 141 regression tests passed. These checks are not an Android handset benchmark; see docs/render-performance.md for scope and remaining device-verification limits.
- Incomplete legacy view families no longer stop a district from reopening: the same building uses an available view, with an explicit warning, without changing its saved orientation. This is recovery behavior, not newly authored missing-angle art.
- Paving and road preparation ground details are rasterized into short-section sprites, updated only when progress changes (at most five times per second). Vehicle animation is still independent at 30 Hz; gameplay timers are unchanged.
- Completed paving uses GPU tiles no larger than 1024px, with a union shoulder blur per tile instead of blurring every shoulder polygon. Offscreen paver/dozer effects are skipped and unused ground textures are destroyed.
- Regression coverage includes repeated-cache, viewport re-entry, clock adjustment and 100 reset-cycle tests.
- Includes the latest flag construction site and completed plaza, streetlamp, fishing boat, low-rise house and roadside palm artwork. Assets load with the initial game loader, not a remote development URL.
- Includes the earlier startup recovery and agricultural-ground fixes from 1.2, as well as the performance improvements below.
- This is a new package for phone testing, not a claim that every device or every gameplay state has been tested.

## Package verification — 1.5
- Android build succeeded: versionName 1.5-offline, versionCode 6, package com.newgaza.citygame.
- APK signature verified; signing certificate matches 1.4.
- All 310 public files and 314 built web files match the APK byte-for-byte.
- No remote game server URL and no isolated dense-city fixture are included.
- No physical Android handset/emulator benchmark was performed.

## Earlier package verification — 1.4
- Android build succeeded; APK signature verified and signing certificate matches 1.3, so it can update that test build without uninstalling it.
- All 310 public files and 314 built web files match the APK byte-for-byte; no remote game URL and no performance fixture are included.
- An isolated browser renderer fixture showed three streets with pavers and road preparation, and exercised finished paving plus repeated resets. It ran with Canvas, not a phone's WebGL GPU. This is not an Android FPS or long-session stability test.
- No Android phone/emulator was available for device measurements.

## Earlier package verification — 1.3
- Android build succeeded; package version is 1.3-offline / versionCode 4.
- APK signature verified and the signing certificate matches 1.2.
- All 310 public files and all 314 built web files match the APK byte-for-byte, including the six new artwork files.
- No remote `server.url` is configured. No new Android device/emulator test was performed for this package.

## Changes
- Phone framebuffer uses up to 2× pixel density, capped to a 2.5-million-pixel budget when the logical viewport is below that budget; very large displays never fall below 1×.
- Camera saves remain in logical zoom units. HUD dimensions, selection coordinates and audio attenuation account for the physical framebuffer.
- Procedural animation and rendering target 30 Hz; input handling remains separate, and gameplay deadlines still use real time.
- Obsolete construction and clearing textures are released periodically when no scene image references them. Original artwork is not downsampled by this cleanup.
- Removed the duplicate UI render following every timer tick. Hidden-page polling pauses; visibility changes reconcile timers.
- Local asset loading has bounded concurrency, timeouts and retries. Ad initialization is deferred until voluntarily requested, not part of offline startup.

## Earlier verification on 2026-10-25 (1.1)
- TypeScript and 128 tests passed.
- Android SDK35/Java21 debug APK compiled successfully.
- Compared all 304 public assets byte-for-byte against the APK: no missing or altered files.
- Browser phone simulation, DPR3 with external requests blocked: 390×844 CSS / 780×1688 backing; visible district, plot selection, touch pan and pinch succeeded.
- Rotation produced 844×390 CSS / 1688×780 backing without page overflow.

## Limits
During the earlier 1.1 verification, the browser automation process closed unexpectedly after rotation; that pass did not complete long-session, stage-eviction, landscape selection, reload and background tests. This did not establish an application crash or Android stability. Desktop renderer checks cannot substitute for a physical phone's WebGL GPU. No Android phone/emulator was available for a real airplane-mode cold launch or thermal/memory profiling.

The APK is ready for device testing, **not a claim of zero defects or verified performance on every phone**. Ads still require internet. The save remains local to the app; uninstalling or clearing app data erases it. Keep a save export before changing installations.

Suggested device pass: install without deleting data, airplane-mode cold launch, open a district, construct/clear/pave repeatedly, rotate and zoom, background/return, force-stop/relaunch, then play for 30–45 minutes while observing temperature, memory and frame pacing.
