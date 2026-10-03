#!/usr/bin/env python3
"""Real Unity compilation, never a source-fixture fallback. Python standard library only."""
import argparse
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import re
import signal
import subprocess
import sys
import uuid

ROOT = Path(__file__).resolve().parents[1]
METHOD = "NewGaza.Editor.NewGazaCompilationGate.Run"
COMPILER_ERROR = re.compile(
    r"\berror\s+CS\d+\b|Scripts have compiler errors|"
    r"script compilation failed|Compilation failed|Aborting batchmode due to failure",
    re.IGNORECASE,
)


def required_version(project):
    text = (project / "ProjectSettings/ProjectVersion.txt").read_text(encoding="utf-8")
    match = re.search(r"^m_EditorVersion:\s*(\S+)\s*$", text, re.MULTILINE)
    if not match:
        raise ValueError("ProjectVersion.txt has no m_EditorVersion")
    return match.group(1)


def compile_project(unity, project, log_dir, timeout=1800):
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
              "startedUtc": datetime.now(timezone.utc).isoformat()}
    try:
        version = required_version(project)
        report["requiredVersion"] = version
        executable = Path(unity).expanduser().resolve()
        if not executable.is_file():
            report["status"] = "unavailable"
            raise ValueError(f"Unity unavailable at {executable}. Install/use Unity {version} "
                             "on your own machine; no Unity compilation was performed.")
        if not (project / "Assets").is_dir() or not (project / "Packages").is_dir():
            raise ValueError("Not a complete Unity project (Assets/Packages missing)")
        command = [str(executable), "-batchmode", "-nographics", "-quit",
                   "-projectPath", str(project), "-logFile", str(editor_log),
                   "-executeMethod", METHOD,
                   "-newGazaRequiredVersion", version,
                   "-newGazaCompilationResult", str(result_path),
                   "-newGazaCompilationNonce", nonce]
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
        if code != 0:
            raise ValueError(f"Unity exited with code {code}; see retained logs")
        if not editor_log.is_file() or not editor_log.stat().st_size:
            raise ValueError("Unity produced no compiler log")
        errors = [line for line in editor_log.read_text(encoding="utf-8", errors="replace")
                  .splitlines() if COMPILER_ERROR.search(line)]
        report["compilerErrors"] = errors
        if errors:
            raise ValueError("Unity compiler errors found, even though its exit code was zero")
        if not result_path.is_file():
            raise ValueError("Unity did not execute the post-compilation gate; compilation is unverified")
        proof = json.loads(result_path.read_text(encoding="utf-8-sig"))
        if not (proof.get("success") is True and proof.get("nonce") == nonce and
                proof.get("unityVersion") == version and
                proof.get("runtimeAssembly") == "Assembly-CSharp" and
                proof.get("editorAssembly") == "Assembly-CSharp-Editor"):
            raise ValueError("Unity result has wrong version, stale proof or invalid assembly boundary")
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
    args = parser.parse_args()
    if args.timeout <= 0:
        parser.error("--timeout must be positive")
    logs = args.log_dir or ROOT / "exports/unity-compile" / (
        datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ") + "-" + uuid.uuid4().hex[:8])
    report = compile_project(args.unity, args.project, logs, args.timeout)
    print(f"Unity compilation: {report['status']}. {report.get('message', '')}")
    print(f"Retained report and logs: {logs.resolve()}")
    return 0 if report["success"] else 1


if __name__ == "__main__":
    sys.exit(main())