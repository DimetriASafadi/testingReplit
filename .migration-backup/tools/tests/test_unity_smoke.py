"""Simulated launcher protocol only: NOT evidence of Unity Play Mode or rendering."""
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import unity_smoke

FAKE_EDITOR = r'''
import json, pathlib, sys, time
mode = sys.argv[1]
args = sys.argv[2:]
def arg(key):
    return args[args.index(key) + 1]
log = pathlib.Path(arg("-logFile"))
proof = {"success": True, "nonce": arg("-newGazaSmokeNonce"),
         "unityVersion": arg("-newGazaSmokeVersion"), "playMode": True,
         "graphicsDeviceType": "Vulkan", "graphicsDeviceName": "Simulated GPU"}
log.write_text("New Gaza smoke test PASS: simulated\n")
if mode == "timeout":
    time.sleep(10)
if mode == "missing-proof":
    sys.exit(0)
if mode == "stale":
    proof["nonce"] = "old-run"
if mode == "version":
    proof["unityVersion"] = "wrong"
if mode == "no-play":
    proof["playMode"] = False
if mode == "null-graphics":
    proof["graphicsDeviceType"] = "Null"
if mode == "missing-graphics":
    del proof["graphicsDeviceType"]
if mode == "unavailable":
    proof.update(success=False, unavailable=True, graphicsDeviceType="Null")
if mode == "failure":
    proof["success"] = False
if mode == "fail-log":
    log.write_text("New Gaza smoke test FAIL: failed\n")
if mode == "compiler-error":
    log.write_text("error CS0122: inaccessible\n")
if mode == "missing-pass":
    log.write_text("Editor starting\n")
if mode == "no-log":
    log.unlink()
if mode == "non-object":
    proof = []
pathlib.Path(arg("-newGazaSmokeResult")).write_text(
    "bad JSON" if mode == "bad-json" else json.dumps(proof))
if mode in ("nonzero", "unavailable"):
    sys.exit(7)
'''


class SmokeProtocolTests(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory(prefix="smoke-protocol-")
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)
        self.project = self.root / "project with spaces"
        for folder in ("Assets", "Packages", "ProjectSettings"):
            (self.project / folder).mkdir(parents=True)
        (self.project / "ProjectSettings/ProjectVersion.txt").write_text(
            "m_EditorVersion: 6000.3.18f1\n")
        self.logs = self.root / "logs with spaces"
        self.fake = self.root / "fake editor.py"
        self.fake.write_text(FAKE_EDITOR)

    def simulate(self, mode, timeout=5):
        popen = subprocess.Popen
        commands = []

        def launch(command, **kwargs):
            commands.append(command)
            return popen([sys.executable, str(self.fake), mode, *command[1:]], **kwargs)

        with mock.patch.object(unity_smoke.subprocess, "Popen", side_effect=launch):
            report = unity_smoke.run_smoke(sys.executable, self.project, self.logs,
                                           timeout, approve_save_effects=True)
        command = commands[0]
        self.assertIn("-batchmode", command)
        self.assertNotIn("-quit", command)
        self.assertNotIn("-nographics", command)
        self.assertIn(unity_smoke.METHOD, command)
        self.assertEqual(command[command.index("-projectPath") + 1], str(self.project))
        self.assertEqual(json.loads(Path(report["reportPath"]).read_text()), report)
        self.assertTrue(Path(report["launcherLog"]).is_file())
        return report

    def test_pass_protocol(self):
        self.assertTrue(self.simulate("pass")["success"])

    def test_fail_closed(self):
        for mode in ("missing-proof", "stale", "version", "no-play", "null-graphics",
                     "missing-graphics", "failure", "fail-log", "compiler-error",
                     "missing-pass", "no-log", "bad-json", "non-object", "nonzero"):
            with self.subTest(mode=mode):
                self.assertFalse(self.simulate(mode)["success"])

    def test_graphics_unavailable_is_not_a_pass(self):
        self.assertEqual(self.simulate("unavailable")["status"], "unavailable")

    def test_timeout_retains_evidence(self):
        report = self.simulate("timeout", .2)
        self.assertEqual(report["status"], "timeout")
        self.assertTrue(Path(report["editorLog"]).is_file())
        self.assertFalse(report["success"])

    def test_prior_evidence_is_retained_not_reused(self):
        passed = self.simulate("pass")
        unavailable = unity_smoke.run_smoke(self.root / "missing", self.project, self.logs,
                                            approve_save_effects=True)
        self.assertEqual(unavailable["status"], "unavailable")
        self.assertNotEqual(passed["reportPath"], unavailable["reportPath"])
        self.assertTrue(Path(passed["editorLog"]).is_file())
        self.assertNotIn("unityResult", unavailable)

    def test_requires_explicit_save_approval(self):
        with mock.patch.object(unity_smoke.subprocess, "Popen") as launch:
            report = unity_smoke.run_smoke(sys.executable, self.project, self.logs)
        launch.assert_not_called()
        self.assertFalse(report["success"])
        self.assertIn("--approve-save-effects", report["message"])

    def test_invalid_timeout_never_launches(self):
        for value in (0, -1, float("nan"), float("inf")):
            with mock.patch.object(unity_smoke.subprocess, "Popen") as launch:
                report = unity_smoke.run_smoke(sys.executable, self.project, self.logs,
                                               value, True)
            launch.assert_not_called()
            self.assertFalse(report["success"])

    def test_cli_unavailable_nonzero(self):
        result = subprocess.run(
            [sys.executable, str(unity_smoke.ROOT / "tools/unity_smoke.py"),
             "--unity", str(self.root / "missing"), "--project", str(self.project),
             "--log-dir", str(self.logs), "--approve-save-effects"],
            capture_output=True, text=True)
        self.assertEqual(result.returncode, 1)
        self.assertIn("unavailable", result.stdout)
        self.assertIn("NOT RUN", result.stdout)


if __name__ == "__main__":
    print("Simulated launcher protocol only; Unity Play Mode is NOT tested.")
    unittest.main()