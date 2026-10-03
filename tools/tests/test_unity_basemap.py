"""Simulated runner protocol only. These are NOT real Unity/JsonUtility evidence."""
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import unity_compile


FAKE_EDITOR = r'''
import json, pathlib, sys, time
mode = sys.argv[1]
args = sys.argv[2:]
def arg(name):
    return args[args.index(name) + 1]
checks = [{"name": name, "success": True, "message": "SIMULATION"} for name in __CASES__]
proof = {"nonce": arg("-newGazaCompilationNonce"), "success": True,
         "unityVersion": arg("-newGazaRequiredVersion"),
         "runtimeAssembly": "Assembly-CSharp", "editorAssembly": "Assembly-CSharp-Editor",
         "scope": "basemap-integration", "status": "passed", "performedInEditMode": True,
         "resourceAssetPath": "Assets/NewGaza/Resources/GazaBasemap.json", "checks": checks}
log = pathlib.Path(arg("-logFile"))
log.write_text("SIMULATION NOT UNITY\nNEW_GAZA_BASEMAP_PASS: simulated protocol\n")
if mode == "timeout":
    time.sleep(10)
if mode == "no-log":
    log.unlink()
if mode == "no-marker":
    log.write_text("SIMULATION NOT UNITY\n")
if mode == "no-result":
    sys.exit(0)
if mode == "wrong-version":
    proof["unityVersion"] = "6000.0.0f1"
if mode == "stale":
    proof["nonce"] = "old-run"
if mode == "merged-assemblies":
    proof["editorAssembly"] = "Assembly-CSharp"
if mode == "wrong-scope":
    proof["scope"] = "editor-compilation"
if mode == "wrong-asset":
    proof["resourceAssetPath"] = "Assets/Other/Resources/GazaBasemap.json"
if mode == "play-mode":
    proof["performedInEditMode"] = False
if mode == "no-checks":
    proof["checks"] = []
if mode == "missing-check":
    proof["checks"] = checks[:-1]
if mode == "duplicate-check":
    checks[-1] = checks[0]
if mode == "unknown-check":
    checks[-1]["name"] = "other"
if mode == "invalid-name":
    checks[-1]["name"] = 123
if mode == "invalid-checks":
    proof["checks"] = "not a list"
if mode == "false-check":
    checks[-1]["success"] = False
if mode == "false-success":
    proof["success"] = False
if mode == "failed-status":
    proof["status"] = "failed"
if mode == "compiler-error":
    log.write_text("error CS0122: inaccessible\nNEW_GAZA_BASEMAP_PASS: simulation\n")
if mode == "nonzero":
    checks[-1]["success"] = False
    proof.update(success=False, status="failed", message="Specific fixture failure")
pathlib.Path(arg("-newGazaCompilationResult")).write_text(
    "bad JSON" if mode == "bad-json" else "[]" if mode == "non-object" else json.dumps(proof))
sys.exit(1 if mode == "nonzero" else 0)
'''.replace("__CASES__", repr(unity_compile.BASEMAP_CASES))


class BasemapRunnerProtocolTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="basemap-runner-protocol-")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.project = self.root / "project with spaces"
        for name in ("Assets", "Packages", "ProjectSettings"):
            (self.project / name).mkdir(parents=True)
        (self.project / "ProjectSettings/ProjectVersion.txt").write_text(
            "m_EditorVersion: 6000.3.18f1\n")
        self.logs = self.root / "retained evidence"
        self.fake = self.root / "fake editor.py"
        self.fake.write_text(FAKE_EDITOR)

    def simulate(self, mode, timeout=5):
        popen = subprocess.Popen
        commands = []

        def launch(command, **kwargs):
            commands.append(command)
            return popen([sys.executable, str(self.fake), mode, *command[1:]], **kwargs)

        with mock.patch.object(unity_compile.subprocess, "Popen", side_effect=launch):
            report = unity_compile.compile_project(
                sys.executable, self.project, self.logs, timeout, basemap=True)
        self.assertEqual(report, json.loads((self.logs / "report.json").read_text()))
        command = commands[0]
        self.assertEqual(command[command.index("-executeMethod") + 1], unity_compile.BASEMAP_METHOD)
        self.assertNotIn(unity_compile.PLAYER_METHOD, command)
        self.assertNotIn("-buildTarget", command)
        self.assertIn("-batchmode", command)
        self.assertIn("-nographics", command)
        self.assertIn("-quit", command)
        return report

    def test_complete_protocol(self):
        report = self.simulate("pass")
        self.assertTrue(report["success"])
        self.assertEqual(report["scope"], "basemap-integration")
        self.assertEqual(len(report["unityResult"]["checks"]), len(unity_compile.BASEMAP_CASES))

    def test_fail_closed(self):
        for mode in ("no-log", "no-marker", "no-result", "wrong-version", "stale",
                     "merged-assemblies", "wrong-scope", "wrong-asset", "play-mode",
                     "no-checks", "missing-check", "duplicate-check", "unknown-check",
                     "invalid-name", "invalid-checks", "false-check", "false-success",
                     "failed-status", "compiler-error", "nonzero", "bad-json", "non-object"):
            with self.subTest(mode=mode):
                self.assertFalse(self.simulate(mode)["success"])

    def test_failure_evidence_is_retained(self):
        report = self.simulate("nonzero")
        self.assertEqual(report["unityResult"]["checks"][-1]["success"], False)
        self.assertIn("Specific fixture failure", report["message"])
        self.assertTrue((self.logs / "Editor.log").is_file())
        self.assertTrue((self.logs / "launcher.log").is_file())
        self.assertTrue((self.logs / "unity-result.json").is_file())

    def test_timeout(self):
        report = self.simulate("timeout", timeout=.2)
        self.assertEqual(report["status"], "timeout")
        self.assertTrue((self.logs / "Editor.log").is_file())

    def test_unavailable_clears_previous_success(self):
        self.assertTrue(self.simulate("pass")["success"])
        report = unity_compile.compile_project(
            self.root / "missing Unity", self.project, self.logs, basemap=True)
        self.assertFalse(report["success"])
        self.assertEqual(report["status"], "unavailable")
        self.assertIn("NOT RUN", report["message"])
        self.assertFalse((self.logs / "unity-result.json").exists())

    def test_output_cannot_be_imported_into_project(self):
        with mock.patch.object(unity_compile.subprocess, "Popen") as launch:
            report = unity_compile.compile_project(
                sys.executable, self.project, self.project / "Assets/Logs", basemap=True)
            launch.assert_not_called()
        self.assertFalse(report["success"])

    def test_no_player_build_combination(self):
        with mock.patch.object(unity_compile.subprocess, "Popen") as launch:
            report = unity_compile.compile_project(
                sys.executable, self.project, self.logs, target="Android", basemap=True)
            launch.assert_not_called()
        self.assertFalse(report["success"])

    def test_cli_reports_not_run(self):
        result = subprocess.run(
            [sys.executable, str(unity_compile.ROOT / "tools/unity_compile.py"),
             "--unity", str(self.root / "missing Unity"), "--basemap",
             "--log-dir", str(self.logs)], text=True, capture_output=True)
        self.assertEqual(result.returncode, 1)
        self.assertIn("NOT RUN", result.stdout)
        self.assertIn("basemap integration", result.stdout)

    def test_case_manifest_matches_gate(self):
        # Source consistency only, not engine evidence.
        source = (unity_compile.ROOT /
                  "testingReplic/Assets/NewGaza/Editor/NewGazaBasemapGate.cs").read_text()
        import re
        names = re.findall(r'(?:Check|Accept|Reject)\(checks,\s*"([^"]+)"', source)
        self.assertEqual(sorted(names), sorted(unity_compile.BASEMAP_CASES))
        for forbidden in ("GameSession.", "CityWorld.", "PlayerPrefs.", "EnterPlaymode(",
                          "OpenScene(", "GameSaveStore", "EditorPrefs."):
            self.assertNotIn(forbidden, source)


if __name__ == "__main__":
    print("Simulated protocol/source checks only; real Unity validation NOT performed.")
    unittest.main()