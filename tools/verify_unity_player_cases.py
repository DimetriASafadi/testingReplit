#!/usr/bin/env python3
"""Opt-in real Unity player probes; fixtures and protocol tests are not game-build evidence."""
import argparse
from datetime import datetime, timezone
import json
from pathlib import Path
import re
import shutil
import sys
import tempfile
import uuid

from unity_compile import ROOT, TARGETS, GAME_SCENE, compile_project, required_version


def verify_cases(unity, target, logs, timeout=3600):
    """Each negative must first pass Editor compilation, then fail with its player diagnostic."""
    logs = Path(logs).resolve()
    logs.mkdir(parents=True, exist_ok=True)
    summary = logs / "cases.json"
    summary.unlink(missing_ok=True)
    version = required_version(ROOT / "testingReplic")
    cases = {
        "pass": ("", None),
        "platform-only-error": ('#error NEW_GAZA_PLAYER_ONLY_FAILURE', "CS1029"),
        "player-editor-api-leak": (
            "public class PlayerEditorLeak { public UnityEditor.EditorWindow window; }", "CS0246|CS0234"),
    }
    results = []
    with tempfile.TemporaryDirectory(prefix="new-gaza-player-cases-") as temp:
        for name, (probe, diagnostic) in cases.items():
            project = Path(temp) / name
            runtime = project / "Assets/Runtime"
            editor = project / "Assets/Editor"
            runtime.mkdir(parents=True)
            editor.mkdir()
            (project / "ProjectSettings").mkdir()
            (project / "Packages").mkdir()
            (project / "ProjectSettings/ProjectVersion.txt").write_text(
                f"m_EditorVersion: {version}\n", encoding="utf-8")
            (project / "Packages/manifest.json").write_text(json.dumps({
                "dependencies": {"com.unity.modules.jsonserialize": "1.0.0"}
            }), encoding="utf-8")
            source = ("namespace NewGaza { public class GameSession {} }\n"
                      f"#if UNITY_{target.upper()} && !UNITY_EDITOR\n{probe}\n#endif\n")
            (runtime / "GameSession.cs").write_text(source, encoding="utf-8")
            (editor / "NewGazaSmokeTest.cs").write_text(
                "namespace NewGaza.Editor { public static class NewGazaSmokeTest {} }",
                encoding="utf-8")
            for gate in ("NewGazaCompilationGate.cs", "NewGazaPlayerBuildGate.cs"):
                shutil.copyfile(ROOT / "testingReplic/Assets/NewGaza/Editor" / gate, editor / gate)
            scene = project / GAME_SCENE
            scene.parent.mkdir(parents=True)
            # This scene contains only an empty GameObject; no game runtime or setup is invoked.
            shutil.copyfile(ROOT / "testingReplic" / GAME_SCENE, scene)
            baseline = compile_project(unity, project, logs / name / "editor", timeout)
            player = None
            if baseline["success"]:
                player = compile_project(unity, project, logs / name / target, timeout, target)
            log = logs / name / target / "Editor.log"
            text = log.read_text(encoding="utf-8", errors="replace") if log.is_file() else ""
            matched = bool(baseline["success"] and player and (
                player["success"] if diagnostic is None else
                not player["success"] and
                player["status"] not in ("timeout", "unavailable", "prerequisite-missing") and
                re.search(r"\berror\s+(?:" + diagnostic + r")\b", text) and
                ("NEW_GAZA_PLAYER_ONLY_FAILURE" if name == "platform-only-error"
                 else "UnityEditor") in text))
            results.append({"case": name, "target": target, "expectedDiagnostic": diagnostic,
                            "editorStatus": baseline["status"],
                            "playerStatus": player["status"] if player else "NOT RUN",
                            "matched": matched})
            print(f"{target}/{name}: {'EXPECTED OUTCOME' if matched else 'UNEXPECTED / NOT VERIFIED'}")
    summary.write_text(json.dumps(results, indent=2) + "\n", encoding="utf-8")
    return all(result["matched"] for result in results)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--unity", required=True)
    parser.add_argument("--target", choices=TARGETS, required=True,
                        help="Explicit approval to build this platform in isolated projects")
    parser.add_argument("--log-dir", type=Path)
    parser.add_argument("--timeout", type=float, default=3600)
    args = parser.parse_args()
    if args.timeout <= 0:
        parser.error("--timeout must be positive")
    logs = args.log_dir or ROOT / "exports/unity-player-cases" / (
        datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ") + "-" + uuid.uuid4().hex[:8])
    success = verify_cases(args.unity, args.target, logs, args.timeout)
    print(f"Retained real-Unity case logs: {logs.resolve()}")
    print("Isolated probes do not verify the game, device behavior, or Xcode compilation/signing.")
    if not success:
        print("Real player cases NOT VERIFIED; inspect missing prerequisites or unexpected diagnostics.",
              file=sys.stderr)
    return 0 if success else 1


if __name__ == "__main__":
    sys.exit(main())