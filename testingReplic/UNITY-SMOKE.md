# Real Play Mode smoke runner / فحص التشغيل الفعلي

This is **separate from compilation**. Requires Python 3.10+ and the exact Editor
in `ProjectSettings/ProjectVersion.txt` (currently **Unity 6000.3.18f1**) on an
approved, licensed machine with working graphics. Use the Editor executable,
not Unity Hub. No Unity installation or licensed CI setup is performed.

## Save effects — read before approving

Close this project in Unity first. In the Editor, use **New Gaza → Open save
folder** to locate `Application.persistentDataPath/new-gaza.json`. Back up the
whole save folder outside the project, including `.bak`, before running; prefer
a disposable OS account with no valuable game save. Do not commit player saves.

The checks do not deliberately grant currency, delete saves or purchase anything.
Equipment probes use cloned state and restore the displayed fleet. However,
**normal GameSession startup loads/migrates saves, ticks elapsed economy/job
progress and writes the save**; periodic saving and focus/quit saving also apply.
This can update balances, timestamps, jobs and backup files, even on a failed or
timed-out run. A new save is created if none exists. This is NOT a read-only test.
No automatic backup/restore is promised. Restore your external backup only with
Unity closed if you want to undo test-side save changes.

## Run explicitly

From the repository root (Windows: use `python` instead of `python3`):

```sh
python3 tools/unity_smoke.py --unity "/path/to/Unity" --approve-save-effects
```

Example Editor locations:

- Windows: `C:\Program Files\Unity\Hub\Editor\6000.3.18f1\Editor\Unity.exe`
- macOS: `/Applications/Unity/Hub/Editor/6000.3.18f1/Unity.app/Contents/MacOS/Unity`
- Linux: `/opt/Unity/Editor/Unity` with a working graphical session/GPU driver.

`--approve-save-effects` explicitly approves this machine/run and the save effects
above. Without it the runner refuses to launch. `--project` selects another full
project copy; copying only the project does **not** isolate persistentDataPath
saves. `--timeout 1800` bounds the whole Editor process, including first import,
compilation, domain reload and Play Mode. Package registry/cache access is needed.

The command uses `-batchmode -executeMethod NewGaza.Editor.NewGazaSmokeTest.RunBatch`
with graphics enabled: **no `-quit`, no `-nographics`**. RunBatch checks the running
version and rejects a Null graphics device, opens the city scene and enters Play
Mode asynchronously. The smoke test, not executeMethod returning, exits the Editor.
The existing menu **New Gaza → Run play mode smoke test** remains available for
interactive use; it has the same save effects but does not produce runner proof.

## Results and limits

Each attempt gets a unique folder under `exports/unity-smoke/`, or under the
parent supplied with `--log-dir`. Previous evidence is never overwritten/reused.
Retain/share `report.json`, `Editor.log`, `launcher.log` and `unity-result.json`
(when the Editor reaches the smoke checks). Paths in reports may identify local
folders; review before sharing. Logs can contain player-specific errors.

Exit **0** means the real Editor exited successfully and returned fresh matching
version/nonce, Play Mode and non-Null graphics evidence plus the smoke PASS log.
Exit **1** means failure, timeout, unavailable Editor/graphics or unverified result.
Compiler errors, startup/runtime errors during the checks, smoke failures, missing
proof and stale proof cannot pass. On timeout the runner kills the Editor process
tree and retains partial logs; a hard kill may interrupt saving, so keep backups.
Missing Editor/graphics is **unavailable / NOT RUN**, never fixture success.

Checks cover scene/session startup, active camera, HUD/EventSystem presence,
selection components, imported model/material/UV/normal/budget contracts, authored
audio resources/voices, live equipment geometry/motion/contact and fog/access/save
contracts. They do **not** prove correct-looking rendered frames, audible output,
touch/UI interaction flows, mobile performance or store/player readiness. Inspect
the city visually, interact with the UI and listen on target hardware separately.
`tools/unity_compile.py` remains a compilation-only gate; packaging does not
silently run this save-changing Play Mode check.

## Workspace verification

Unity Editor/working graphics are unavailable in the Replit workspace; real city
Play Mode has **NOT RUN here**. Protocol tests exercise a simulated process only:

```sh
python3 -m unittest discover -s tools/tests -p 'test_unity_smoke.py' -v
```

بالعربية: هذا فحص تشغيل مستقل يحتاج محرّر Unity المطابق ورسومات فعلية على جهاز
معتمد. خذ نسخة احتياطية من الحفظ أولًا؛ بدء اللعبة قد يحدّث الاقتصاد والتقدم
ويكتب الحفظ حتى عند فشل الاختبار. الخيار `--approve-save-effects` موافقة صريحة
على هذه الآثار. عدم توفر المحرّر أو الرسومات يُسجّل كتعذّر للفحص لا نجاح.
اختبارات المشغّل المحاكية لا تثبت تشغيل المدينة أو صحة الصورة والصوت.