#!/usr/bin/env python3
"""Explicit real-Editor Play Mode smoke run. No headless/fixture fallback."""
import argparse
from datetime import datetime, timezone
import json
import math
import os
from pathlib import Path
import signal
import subprocess
import sys
import uuid

from unity_compile import COMPILER_ERROR, ROOT, required_version

METHOD = "NewGaza.Editor.NewGazaSmokeTest.RunBatch"


def run_smoke(unity, project, log_dir, timeout=1800, approve_save_effects=False):
    """Use a new evidence directory every time, including unavailable runs."""
    logs = Path(log_dir).resolve() / (
        datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ") + "-" + uuid.uuid4().hex)
    logs.mkdir(parents=True)
    editor_log = logs / "Editor.log"
    launcher_log = logs / "launcher.log"
    result_path = logs / "unity-result.json"
    nonce = uuid.uuid4().hex
    report = {"success": False, "status": "error", "project": str(Path(project).resolve()),
              "editorLog": str(editor_log), "launcherLog": str(launcher_log),
              "reportPath": str(logs / "report.json"),
              "startedUtc": datetime.now(timezone.utc).isoformat(),
              "saveEffectsApproved": approve_save_effects}
    try:
        if not math.isfinite(timeout) or timeout <= 0:
            raise ValueError("Timeout must be finite and positive")
        version = required_version(Path(report["project"]))
        report["requiredVersion"] = version
        if not approve_save_effects:
            raise ValueError("Smoke run not approved: startup ticks the economy and writes local saves. "
                             "Back up saves and pass --approve-save-effects on an approved machine.")
        executable = Path(unity).expanduser().resolve()
        if not executable.is_file():
            report["status"] = "unavailable"
            raise ValueError(f"Unity unavailable at {executable}; Play Mode smoke test NOT RUN.")
        project = Path(report["project"])
        if not (project / "Assets").is_dir() or not (project / "Packages").is_dir():
            raise ValueError("Not a complete Unity project (Assets/Packages missing)")
        # RunBatch exits after asynchronous Play Mode checks, not at executeMethod return.
        command = [str(executable), "-batchmode", "-projectPath", str(project),
                   "-logFile", str(editor_log), "-executeMethod", METHOD,
                   "-newGazaSmokeVersion", version, "-newGazaSmokeNonce", nonce,
                   "-newGazaSmokeResult", str(result_path)]
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
                report["exitCode"] = process.returncode
                raise ValueError(f"Unity exceeded {timeout}s; smoke test did not complete. Logs retained.")
        report["exitCode"] = code
        if result_path.is_file():
            proof = json.loads(result_path.read_text(encoding="utf-8-sig"))
            if not isinstance(proof, dict):
                raise ValueError("Unity smoke result must be a JSON object")
            report["unityResult"] = proof
        if code != 0:
            if report.get("unityResult", {}).get("unavailable") is True:
                report["status"] = "unavailable"
            raise ValueError(f"Unity exited with code {code}; see retained logs/result")
        if not editor_log.is_file() or not editor_log.stat().st_size:
            raise ValueError("No Unity Editor log; smoke test unverified")
        text = editor_log.read_text(encoding="utf-8", errors="replace")
        if COMPILER_ERROR.search(text) or "New Gaza smoke test FAIL:" in text:
            raise ValueError("Unity log contains compilation/smoke failure")
        proof = report.get("unityResult", {})
        if (proof.get("success") is not True or proof.get("nonce") != nonce or
                proof.get("unityVersion") != version or proof.get("playMode") is not True or
                proof.get("graphicsDeviceType") in (None, "", "Null") or
                not proof.get("graphicsDeviceName")):
            raise ValueError("Missing, stale or invalid graphics-capable Play Mode result")
        if "New Gaza smoke test PASS:" not in text:
            raise ValueError("Missing smoke completion log")
        report.update(success=True, status="passed")
    except (OSError, ValueError) as error:
        report["message"] = str(error)
    (logs / "report.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--unity", required=True, help="Explicit Unity Editor executable, not Hub")
    parser.add_argument("--project", type=Path, default=ROOT / "testingReplic")
    parser.add_argument("--log-dir", type=Path, default=ROOT / "exports/unity-smoke",
                        help="Parent for unique retained run directories")
    parser.add_argument("--timeout", type=float, default=1800)
    parser.add_argument("--approve-save-effects", action="store_true",
                        help="Approve running on this machine and updating its local game saves")
    args = parser.parse_args()
    report = run_smoke(args.unity, args.project, args.log_dir, args.timeout,
                       args.approve_save_effects)
    print(f"Unity Play Mode smoke: {report['status']}. {report.get('message', '')}")
    print(f"Retained report: {report['reportPath']}")
    return 0 if report["success"] else 1


if __name__ == "__main__":
    sys.exit(main())