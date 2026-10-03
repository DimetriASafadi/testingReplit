#!/usr/bin/env python3
"""Real Unity compilation, never a source-fixture fallback. Python standard library only."""
import argparse
from datetime import datetime, timezone
import json
import os
import platform
from pathlib import Path
import re
import signal
import subprocess
import sys
import uuid

ROOT = Path(__file__).resolve().parents[1]
METHOD = "NewGaza.Editor.NewGazaCompilationGate.Run"
PLAYER_METHOD = "NewGaza.Editor.NewGazaPlayerBuildGate.Run"
TARGETS = ("Android", "iOS")
GAME_SCENE = "Assets/NewGaza/Scenes/NewGaza.unity"
COMPILER_ERROR = re.compile(
    r"\berror\s+CS\d+\b|Scripts have compiler errors|"
    r"script compilation failed|Compilation failed|Aborting batchmode due to failure",
    re.IGNORECASE,
)
PREREQUISITE_ERROR = re.compile(
    r"(module|build support).*(not installed|missing)|"
    r"(SDK|NDK|JDK|keystore|signing|license).*(missing|not found|not installed|invalid|failed)|"
    r"no valid.*license|build target.*not supported",
    re.IGNORECASE,
)


def required_version(project):
    text = (project / "ProjectSettings/ProjectVersion.txt").read_text(encoding="utf-8")
    match = re.search(r"^m_EditorVersion:\s*(\S+)\s*$", text, re.MULTILINE)
    if not match:
        raise ValueError("ProjectVersion.txt has no m_EditorVersion")
    return match.group(1)


def compile_project(unity, project, log_dir, timeout=1800, target=None, scene=GAME_SCENE):
    """Return a durable report, including unavailable/error outcomes; never reuse proof."""
    project = Path(project).resolve()
    log_dir = Path(log_dir).resolve()
    log_dir.mkdir(parents=True, exist_ok=True)
    nonce = uuid.uuid4().hex
    editor_log = log_dir / "Editor.log"
    launcher_log = log_dir / "launcher.log"
    result_path = log_dir / "unity-result.json"
    report_path = log_dir / "report.json"
    # Remove stale evidence even when the editor cannot be launched.
    for path in (editor_log, launcher_log, result_path, report_path):
        path.unlink(missing_ok=True)
    report = {"success": False, "status": "error", "project": str(project),
              "editorLog": str(editor_log), "launcherLog": str(launcher_log),
              "scope": "player-build" if target else "editor-compilation",
              "startedUtc": datetime.now(timezone.utc).isoformat()}
    try:
        if target is not None and target not in TARGETS:
            raise ValueError(f"Unapproved target {target!r}; choose Android or iOS explicitly")
        if target:
            report.update(target=target, scene=scene)
            # Never replace a prior player export, even when --log-dir is reused.
            build_directory = log_dir / ("player-" + nonce)
            report["buildDirectory"] = str(build_directory)
        version = required_version(project)
        report["requiredVersion"] = version
        executable = Path(unity).expanduser().resolve()
        if not executable.is_file():
            report["status"] = "unavailable"
            raise ValueError(f"Unity unavailable at {executable}. Install/use Unity {version} "
                             "on your own machine; no Unity compilation was performed.")
        if not (project / "Assets").is_dir() or not (project / "Packages").is_dir():
            raise ValueError("Not a complete Unity project (Assets/Packages missing)")
        if target == "iOS" and platform.system() != "Darwin":
            report["status"] = "prerequisite-missing"
            raise ValueError("iOS export requires macOS with the matching Unity iOS Build Support "
                             "module. Xcode compilation/signing is a separate check, not performed here.")
        if target and (not scene.startswith("Assets/") or
                       not scene.endswith(".unity") or ".." in Path(scene).parts or
                       not (project / scene).is_file()):
            raise ValueError(f"Player scene missing or invalid: {scene}; run approved project setup "
                             "manually if needed. The gate never creates the game scene.")
        if target and log_dir.is_relative_to(project):
            raise ValueError("Player logs/build output must be outside the Unity project")
        command = [str(executable), "-batchmode", "-nographics", "-quit",
                   "-projectPath", str(project), "-logFile", str(editor_log),
                   "-executeMethod", PLAYER_METHOD if target else METHOD,
                   "-newGazaRequiredVersion", version,
                   "-newGazaCompilationResult", str(result_path),
                   "-newGazaCompilationNonce", nonce]
        if target:
            command += ["-buildTarget", target, "-newGazaPlayerTarget", target,
                        "-newGazaPlayerDirectory", str(build_directory), "-newGazaPlayerScene", scene]
        report["command"] = command
        with launcher_log.open("wb") as output:
            process = subprocess.Popen(command, stdout=output, stderr=subprocess.STDOUT,
                                       start_new_session=(os.name != "nt"))
            try:
                code = process.wait(timeout=timeout)
            except subprocess.TimeoutExpired:
                if os.name == "nt":
                    subprocess.run(["taskkill", "/PID", str(process.pid), "/T", "/F"],
                                   stdout=output, stderr=subprocess.STDOUT, check=False)
                    if process.poll() is None:
                        process.kill()
                else:
                    os.killpg(process.pid, signal.SIGKILL)
                process.wait()
                report["status"] = "timeout"
                raise ValueError(f"Unity exceeded {timeout}s; see retained logs")
        report["exitCode"] = code
        lines = (editor_log.read_text(encoding="utf-8", errors="replace").splitlines()
                 if editor_log.is_file() else [])
        errors = [line for line in lines if COMPILER_ERROR.search(line)]
        report["compilerErrors"] = errors
        if target:
            report["prerequisiteDiagnostics"] = [line for line in lines if PREREQUISITE_ERROR.search(line)]
            if report["prerequisiteDiagnostics"]:
                report["status"] = "prerequisite-missing"
            report["prerequisiteHelp"] = (
                "Use the exact Unity version with the requested Build Support module. "
                "Android also needs configured SDK/NDK/JDK and the selected scripting backend "
                "(IL2CPP toolchain if enabled); custom signing needs your existing keystore setup. "
                "iOS needs macOS/iOS Build Support; this gate exports Xcode only, not a signed IPA. "
                "See Editor.log and launcher.log for license, module, SDK, signing or native compiler failures.")
            if result_path.is_file():
                report["unityResult"] = json.loads(result_path.read_text(encoding="utf-8-sig"))
                if not isinstance(report["unityResult"], dict):
                    raise ValueError("Unity result must be a JSON object")
                if report["unityResult"].get("status") == "prerequisite-missing":
                    report["status"] = "prerequisite-missing"
        if code != 0:
            detail = report.get("unityResult", {}).get("message", "")
            if not detail and report.get("prerequisiteDiagnostics"):
                detail = report["prerequisiteDiagnostics"][0]
            raise ValueError(f"Unity exited with code {code}; {detail or 'see retained logs'}")
        if not editor_log.is_file() or not editor_log.stat().st_size:
            raise ValueError("Unity produced no compiler log")
        if errors:
            raise ValueError("Unity compiler errors found, even though its exit code was zero")
        if not result_path.is_file():
            raise ValueError("Unity did not execute the post-compilation gate; compilation is unverified")
        proof = json.loads(result_path.read_text(encoding="utf-8-sig"))
        if not isinstance(proof, dict):
            raise ValueError("Unity result must be a JSON object")
        if not (proof.get("success") is True and proof.get("nonce") == nonce and
                proof.get("unityVersion") == version and
                proof.get("runtimeAssembly") == "Assembly-CSharp" and
                proof.get("editorAssembly") == "Assembly-CSharp-Editor"):
            raise ValueError("Unity result has wrong version, stale proof or invalid assembly boundary")
        if target:
            if not isinstance(proof.get("buildPath"), str):
                raise ValueError("Unity player result is missing a valid buildPath")
            build_path = Path(proof.get("buildPath", "")).resolve()
            if not (proof.get("target") == target and proof.get("scene") == scene and
                    proof.get("buildResult") == "Succeeded" and
                    proof.get("totalErrors") == 0 and
                    proof.get("scope") == ("xcode-export" if target == "iOS" else "android-player") and
                    build_path.is_relative_to(build_directory) and
                    ((target == "Android" and build_path.is_file() and build_path.stat().st_size > 0) or
                     (target == "iOS" and (build_path / "Unity-iPhone.xcodeproj/project.pbxproj").is_file()))):
                raise ValueError("Player build proof/output missing, mismatched or unsuccessful")
        report.update(success=True, status="passed", unityResult=proof)
    except (OSError, ValueError) as error:
        report["message"] = str(error)
    report_path.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--unity", required=True, help="Explicit path to the Unity Editor executable")
    parser.add_argument("--project", type=Path, default=ROOT / "testingReplic")
    parser.add_argument("--log-dir", type=Path, help="Retained logs (default: unique exports/unity-compile run)")
    parser.add_argument("--timeout", type=float, default=1800, help="Seconds, default 1800")
    parser.add_argument("--target", choices=TARGETS,
                        help="Explicit opt-in: Android native APK/AAB build or iOS Xcode export")
    args = parser.parse_args()
    if args.timeout <= 0:
        parser.error("--timeout must be positive")
    logs = args.log_dir or ROOT / "exports/unity-compile" / (
        datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ") + "-" + uuid.uuid4().hex[:8])
    report = compile_project(args.unity, args.project, logs, args.timeout, args.target)
    print(f"Unity compilation: {report['status']}. {report.get('message', '')}")
    if report.get("prerequisiteHelp") and not report["success"]:
        print(report["prerequisiteHelp"])
    if report["success"] and args.target:
        print(report["unityResult"].get("message", "Player build passed"))
    print(f"Retained report and logs: {logs.resolve()}")
    return 0 if report["success"] else 1


if __name__ == "__main__":
    sys.exit(main())