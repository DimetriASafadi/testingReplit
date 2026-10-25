# Android offline test build — 1.1

This is a debug-signed test APK with the same application ID, `com.newgaza.citygame`, and versionCode 2. It bundles the game rather than opening the development URL. Install over the previous build when Android accepts the signature; **do not uninstall to bypass a signature error without first exporting the old game's save**. Browser and Android saves are separate.

## Changes
- Phone framebuffer uses up to 2× pixel density, capped to a 2.5-million-pixel budget when the logical viewport is below that budget; very large displays never fall below 1×.
- Camera saves remain in logical zoom units. HUD dimensions, selection coordinates and audio attenuation account for the physical framebuffer.
- Procedural animation is sampled at 30 Hz; camera input/rendering remain independent, and gameplay deadlines still use real time.
- Obsolete construction and clearing textures are released periodically when no scene image references them. Original artwork is not downsampled by this cleanup.
- Removed the duplicate UI render following every timer tick. Hidden-page polling pauses; visibility changes reconcile timers.
- Local asset loading has bounded concurrency, timeouts and retries. Ad initialization is deferred until voluntarily requested, not part of offline startup.

## Verification on 2026-10-25
- TypeScript and 128 tests passed.
- Android SDK35/Java21 debug APK compiled successfully.
- Compared all 304 public assets byte-for-byte against the APK: no missing or altered files.
- Browser phone simulation, DPR3 with external requests blocked: 390×844 CSS / 780×1688 backing; visible district, plot selection, touch pan and pinch succeeded.
- Rotation produced 844×390 CSS / 1688×780 backing without page overflow.

## Limits
The browser automation process closed unexpectedly after rotation; it was not possible to finish the long-session, stage-eviction, landscape selection, reload and background tests. This does not establish an application crash, but it also does not establish stability on Android. The simulation used CanvasRenderer rather than a phone's WebGL GPU. No Android phone/emulator was available for a real airplane-mode cold launch or thermal/memory profiling.

The APK is ready for device testing, **not a claim of zero defects or verified performance on every phone**. Ads still require internet. The save remains local to the app; uninstalling or clearing app data erases it. Keep a save export before changing installations.

Suggested device pass: install without deleting data, airplane-mode cold launch, open a district, construct/clear/pave repeatedly, rotate and zoom, background/return, force-stop/relaunch, then play for 30–45 minutes while observing temperature, memory and frame pacing.
