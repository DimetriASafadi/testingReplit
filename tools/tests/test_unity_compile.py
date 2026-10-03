"""Runner protocol tests using a simulated process, NOT Unity compilation evidence."""
import contextlib
import io
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
args = sys.argv[2:]
mode = sys.argv[1]
def arg(key):
    return args[args.index(key) + 1]
log = pathlib.Path(arg("-logFile"))
version = arg("-newGazaRequiredVersion")
proof = {"success": True, "nonce": arg("-newGazaCompilationNonce"),
         "unityVersion": version, "runtimeAssembly": "Assembly-CSharp",
         "editorAssembly": "Assembly-CSharp-Editor"}
if mode == "timeout":
    log.write_text("Unity starting\n")
    time.sleep(10)
if mode != "no-log":
    log.write_text("Compilation complete\n", encoding="utf-8")
if mode == "nonzero":
    sys.exit(7)
if mode == "missing-proof":
    sys.exit(0)
if mode == "wrong-version":
    proof["unityVersion"] = "6000.0.0f1"
if mode == "stale-proof":
    proof["nonce"] = "old-run"
if mode == "merged-assemblies":
    proof["editorAssembly"] = "Assembly-CSharp"
if mode == "false-proof":
    proof["success"] = False
if mode == "compiler-error":
    log.write_text("Assets/Editor/Bad.cs(2,3): error CS0122: inaccessible\n")
if mode == "obsolete-api":
    log.write_text("Assets/Editor/Bad.cs(2,3): error CS0619: obsolete\n")
if mode == "batch-error":
    log.write_text("Aborting batchmode due to failure:\n")
path = pathlib.Path(arg("-newGazaCompilationResult"))
path.write_text("not json" if mode == "bad-json" else json.dumps(proof))
'''


class RunnerProtocolTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="unity-runner-protocol-")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.project = self.root / "project with spaces"
        (self.project / "Assets").mkdir(parents=True)
        (self.project / "Packages").mkdir()
        (self.project / "ProjectSettings").mkdir()
        (self.project / "ProjectSettings/ProjectVersion.txt").write_text(
            "m_EditorVersion: 6000.3.18f1\n", encoding="utf-8")
        self.logs = self.root / "logs with spaces"
        self.fake = self.root / "fake editor.py"
        self.fake.write_text(FAKE_EDITOR, encoding="utf-8")

    def run_simulation(self, mode, timeout=5):
        # Use a real subprocess to exercise paths, files, exit codes and timeout.
        # The simulated editor is explicitly NOT a real Unity executable.
        popen = subprocess.Popen
        commands = []

        def launch(command, **kwargs):
            commands.append(command)
            return popen([sys.executable, str(self.fake), mode, *command[1:]], **kwargs)

        with mock.patch.object(unity_compile.subprocess, "Popen", side_effect=launch):
            report = unity_compile.compile_project(
                sys.executable, self.project, self.logs, timeout)
        self.assertEqual(commands[0][commands[0].index("-projectPath") + 1], str(self.project))
        self.assertIn("-batchmode", commands[0])
        self.assertIn("-nographics", commands[0])
        self.assertIn("-quit", commands[0])
        self.assertIn(unity_compile.METHOD, commands[0])
        self.assertEqual(json.loads((self.logs / "report.json").read_text()), report)
        self.assertTrue((self.logs / "launcher.log").exists())
        return report

    def test_valid_protocol_passes(self):
        self.assertTrue(self.run_simulation("pass")["success"])

    def test_fail_closed_protocol_cases(self):
        for mode in ("nonzero", "missing-proof", "wrong-version", "stale-proof",
                     "merged-assemblies", "false-proof", "compiler-error",
                     "obsolete-api", "batch-error", "no-log", "bad-json"):
            with self.subTest(mode=mode):
                report = self.run_simulation(mode)
                self.assertFalse(report["success"])
                self.assertIn("message", report)

    def test_timeout_retains_logs(self):
        report = self.run_simulation("timeout", timeout=.2)
        self.assertEqual(report["status"], "timeout")
        self.assertTrue((self.logs / "Editor.log").exists())

    def test_unavailable_cannot_reuse_old_success(self):
        self.assertTrue(self.run_simulation("pass")["success"])
        report = unity_compile.compile_project(
            self.root / "missing Unity", self.project, self.logs)
        self.assertEqual(report["status"], "unavailable")
        self.assertFalse((self.logs / "unity-result.json").exists())
        self.assertIn("no Unity compilation was performed", report["message"])

    def test_project_version_is_authoritative(self):
        (self.project / "ProjectSettings/ProjectVersion.txt").write_text(
            "m_EditorVersion: 6000.9.1f1\n", encoding="utf-8")
        report = self.run_simulation("pass")
        self.assertEqual(report["requiredVersion"], "6000.9.1f1")

    def test_invalid_project_fails(self):
        (self.project / "ProjectSettings/ProjectVersion.txt").write_text("")
        report = unity_compile.compile_project(sys.executable, self.project, self.logs)
        self.assertFalse(report["success"])
        self.assertIn("no m_EditorVersion", report["message"])

    def test_cli_unavailable_is_nonzero(self):
        result = subprocess.run([sys.executable, str(unity_compile.ROOT / "tools/unity_compile.py"),
                                 "--unity", str(self.root / "missing"),
                                 "--log-dir", str(self.logs)], capture_output=True, text=True)
        self.assertEqual(result.returncode, 1)
        self.assertIn("unavailable", result.stdout)

    def test_case_suite_unavailable_is_not_a_pass(self):
        result = subprocess.run(
            [sys.executable, str(unity_compile.ROOT / "tools/verify_unity_compile_cases.py"),
             "--unity", str(self.root / "missing")], capture_output=True, text=True)
        self.assertEqual(result.returncode, 1)
        self.assertIn("NOT RUN", result.stderr)

    def test_packaging_refused_before_exports_are_changed(self):
        exports = unity_compile.ROOT / "exports"
        before = {p.name: (p.stat().st_size, p.stat().st_mtime_ns)
                  for p in exports.glob("NewGaza-Unity-Source*")}
        result = subprocess.run(
            [sys.executable, str(unity_compile.ROOT / "tools/package_unity_source.py"),
             "--unity", str(self.root / "missing")], capture_output=True, text=True)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("Packaging refused", result.stderr)
        after = {p.name: (p.stat().st_size, p.stat().st_mtime_ns)
                 for p in exports.glob("NewGaza-Unity-Source*")}
        self.assertEqual(before, after)


if __name__ == "__main__":
    print("Simulated runner protocol only; these tests do NOT compile Unity.")
    unittest.main()