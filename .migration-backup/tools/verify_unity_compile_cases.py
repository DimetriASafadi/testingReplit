#!/usr/bin/env python3
"""Controlled real-Unity cases in isolated projects; never edit the game to inject errors."""
import argparse
import json
from pathlib import Path
import shutil
import sys
import tempfile
from datetime import datetime, timezone
import uuid

from unity_compile import ROOT, compile_project, required_version


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--unity", required=True)
    parser.add_argument("--log-dir", type=Path)
    parser.add_argument("--timeout", type=float, default=1800)
    args = parser.parse_args()
    if args.timeout <= 0:
        parser.error("--timeout must be positive")
    logs = (args.log_dir or ROOT / "exports/unity-compile-cases" / (
        datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ") + "-" + uuid.uuid4().hex[:8])).resolve()
    version = required_version(ROOT / "testingReplic")
    if not Path(args.unity).expanduser().is_file():
        print(f"Unity unavailable: {args.unity}. Requires {version}. "
              "Real Unity pass/fail cases NOT RUN.", file=sys.stderr)
        return 1
    gate = ROOT / "testingReplic/Assets/NewGaza/Editor/NewGazaCompilationGate.cs"
    cases = {
        "pass": ("public", "public static int Read() => NewGaza.GameSession.Value;", None),
        "runtime-syntax-error": ("public", "public static int Read() => 1;", "CS"),
        "editor-internal-access": ("internal", "public static int Read() => NewGaza.GameSession.Value;", "CS0122"),
        "obsolete-unity-audio": ("public",
            "public static void Read(UnityEditor.AudioImporter audio) { audio.threeD = true; }", "CS0619"),
    }
    results = []
    with tempfile.TemporaryDirectory(prefix="new-gaza-unity-cases-") as temp:
        for name, (visibility, editor_code, diagnostic) in cases.items():
            project = Path(temp) / name
            runtime = project / "Assets/Runtime"
            editor = project / "Assets/Editor"
            runtime.mkdir(parents=True)
            editor.mkdir()
            (project / "ProjectSettings").mkdir()
            (project / "Packages").mkdir()
            (project / "ProjectSettings/ProjectVersion.txt").write_text(
                f"m_EditorVersion: {version}\n", encoding="utf-8")
            (project / "Packages/manifest.json").write_text(
                json.dumps({"dependencies": {"com.unity.modules.audio": "1.0.0",
                                             "com.unity.modules.jsonserialize": "1.0.0"}}),
                encoding="utf-8")
            source = f"namespace NewGaza {{ public class GameSession {{ {visibility} static int Value = 1; }} }}"
            if name == "runtime-syntax-error":
                source += "\npublic class Broken { this is not CSharp; }\n"
            (runtime / "GameSession.cs").write_text(source, encoding="utf-8")
            (editor / "NewGazaSmokeTest.cs").write_text(
                "namespace NewGaza.Editor { public static class NewGazaSmokeTest { " +
                editor_code + " } }", encoding="utf-8")
            shutil.copyfile(gate, editor / gate.name)
            report = compile_project(args.unity, project, logs / name, args.timeout)
            log = logs / name / "Editor.log"
            text = log.read_text(encoding="utf-8", errors="replace") if log.is_file() else ""
            matched = (report["success"] if diagnostic is None else
                       not report["success"] and diagnostic in text and
                       report["status"] not in ("timeout", "unavailable"))
            results.append({"case": name, "expectedDiagnostic": diagnostic,
                            "matched": matched, "compilationStatus": report["status"]})
            print(f"{name}: {'EXPECTED OUTCOME' if matched else 'UNEXPECTED OUTCOME'}")
    logs.mkdir(parents=True, exist_ok=True)
    (logs / "cases.json").write_text(json.dumps(results, indent=2) + "\n", encoding="utf-8")
    print(f"Retained real-Unity case logs: {logs}")
    print("These isolated cases do not replace compilation of testingReplic.")
    return 0 if all(result["matched"] for result in results) else 1


if __name__ == "__main__":
    sys.exit(main())