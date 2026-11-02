# Campaign update verification

## Completed checks

- Full domain regression suite: 155 passing, zero failing.
- TypeScript check and whitespace/diff validation pass.
- After adding ad-batch profile conversion, all nine rewarded-ad tests pass again, including five shared grants, cancellation, reload, duplicate grants, recovery after failed storage, and 30-second/5-hour profile conversion.
- Managed web workflow builds successfully and serves the preview.
- Isolated browser verification used Phaser 3.90 WebGL. A valid saved Zeitoun with 130/144 built plots, all six added building families, and 500,000 coins reached gameplay.
- Three map/return cycles retained progress and balance. Twelve active map pins, no removed districts, blue gem store, and hidden wheel odds were confirmed. No browser console errors surfaced in that run.
- The screenshot of the dense restored district was visually inspected.

## Limits, not success claims

- No physical Android 11 / 4 GB Helio device was available. No measured before/after FPS, native memory, GPU texture budget or long-duration stability comparison was obtained.
- The managed preview serves a watched production bundle; development scene hooks are absent. The browser pass did not finish active paving stage transitions, celebration portrait/landscape interactions, or celebration close/reload behavior.
- One-time reward logic and save migration are unit-tested; this does not replace the unfinished interactive checks.
- No APK was built or delivered in this task.

## Follow-up fixture support

`tests/campaign-browser-fixtures.ts` exports two disposable validated saves: a dense city with a valid paving job and a fully complete district eligible for its first reward. Compile the fixture with the same CommonJS TypeScript options as the test runner, then call `campaignBrowserFixtures()` at test time. It never writes user storage.

Seed only a disposable browser profile. The completion fixture starts at 500,000 coins / zero gems with project rewards already acknowledged; expected first district reward is 100,000 coins / 10 gems. Reopening must not pay again. Do not deploy fixture controls into the app.
