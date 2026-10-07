"""Simulated process protocol only: never evidence of real Unity/mobile compilation."""
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import unity_compile
import verify_unity_player_cases


FAKE_PLAYER = r'''
import json, pathlib, sys, time
mode = sys.argv[1]
args = sys.argv[2:]
def arg(key):
    return args[args.index(key) + 1]
target = arg("-newGazaPlayerTarget")
root = pathlib.Path(arg("-newGazaPlayerDirectory"))
root.mkdir()
output = root / ("Xcode" if target == "iOS" else "NewGaza.apk")
if target == "iOS":
    (output / "Unity-iPhone.xcodeproj").mkdir(parents=True)
    (output / "Unity-iPhone.xcodeproj/project.pbxproj").write_text("fixture")
else:
    output.write_bytes(b"SIMULATION-NOT-APK")
log = pathlib.Path(arg("-logFile"))
log.write_text("Fixture process; NOT Unity\n")
proof = {"success": True, "nonce": arg("-newGazaCompilationNonce"),
         "unityVersion": arg("-newGazaRequiredVersion"),
         "runtimeAssembly": "Assembly-CSharp", "editorAssembly": "Assembly-CSharp-Editor",
         "target": target, "scene": arg("-newGazaPlayerScene"),
         "buildPath": str(output), "buildResult": "Succeeded", "totalErrors": 0,
         "scope": "xcode-export" if target == "iOS" else "android-player"}
if mode == "timeout":
    time.sleep(10)
if mode == "wrong-target":
    proof["target"] = "StandaloneLinux64"
if mode == "wrong-scene":
    proof["scene"] = "Assets/Other.unity"
if mode == "wrong-scope":
    proof["scope"] = "editor-compilation"
if mode == "wrong-version":
    proof["unityVersion"] = "6000.0.1f1"
if mode == "stale":
    proof["nonce"] = "old"
if mode == "merged":
    proof["editorAssembly"] = "Assembly-CSharp"
if mode == "failed-build":
    proof["buildResult"] = "Failed"
if mode == "build-errors":
    proof["totalErrors"] = 1
if mode == "wrong-output":
    proof["buildPath"] = str(root.parent)
if mode == "bad-output-type":
    proof["buildPath"] = []
if mode == "no-artifact":
    if target == "Android":
        output.unlink()
    else:
        (output / "Unity-iPhone.xcodeproj/project.pbxproj").unlink()
if mode == "empty-artifact" and target == "Android":
    output.write_bytes(b"")
if mode == "compiler-error":
    log.write_text("error CS1029: NEW_GAZA_PLAYER_ONLY_FAILURE\n")
if mode == "native-error":
    proof["success"] = False
    proof["buildResult"] = "Failed"
    proof["message"] = "IL2CPP native compiler failed"
if mode == "missing-module":
    proof.update(success=False, status="prerequisite-missing",
                 message="Android Build Support module missing")
if mode == "startup-sdk":
    log.write_text("Android SDK not found\n")
    sys.exit(1)
if mode == "no-proof":
    sys.exit(0)
if mode == "no-log":
    log.unlink()
pathlib.Path(arg("-newGazaCompilationResult")).write_text(
    "not json" if mode == "bad-json" else "[]" if mode == "non-object-json" else json.dumps(proof))
sys.exit(1 if mode in ("native-error", "missing-module", "nonzero") else 0)
'''


class PlayerProtocolTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="player-protocol-")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.project = self.root / "project with spaces"
        for directory in ("Assets", "Packages", "ProjectSettings"):
            (self.project / directory).mkdir(parents=True)
        (self.project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 6000.3.18f1\n")
        scene = self.project / unity_compile.GAME_SCENE
        scene.parent.mkdir(parents=True)
        scene.write_text("protocol fixture, NOT Unity scene")
        self.logs = self.root / "logs with spaces"
        self.fake = self.root / "fake player.py"
        self.fake.write_text(FAKE_PLAYER)

    def simulate(self, mode="pass", target="Android", timeout=5):
        popen = subprocess.Popen
        commands = []

        def launch(command, **kwargs):
            commands.append(command)
            return popen([sys.executable, str(self.fake), mode, *command[1:]], **kwargs)

        with mock.patch.object(unity_compile.subprocess, "Popen", side_effect=launch), \
                mock.patch.object(unity_compile.platform, "system", return_value="Darwin"):
            report = unity_compile.compile_project(
                sys.executable, self.project, self.logs, timeout, target)
        command = commands[0]
        self.assertEqual(command[command.index("-buildTarget") + 1], target)
        self.assertIn(unity_compile.PLAYER_METHOD, command)
        self.assertNotIn(unity_compile.METHOD, command)
        self.assertEqual(json.loads((self.logs / "report.json").read_text()), report)
        return report

    def test_pass_android_and_ios(self):
        for target in unity_compile.TARGETS:
            with self.subTest(target=target):
                report = self.simulate(target=target)
                self.assertTrue(report["success"], report)
                self.assertEqual(report["scope"], "player-build")

    def test_fail_closed(self):
        for mode in ("wrong-target", "wrong-scene", "wrong-scope", "wrong-version", "stale",
                     "merged", "failed-build", "build-errors", "wrong-output", "no-artifact",
                     "empty-artifact", "compiler-error", "native-error", "no-proof", "no-log",
                     "bad-json", "non-object-json", "bad-output-type", "nonzero"):
            with self.subTest(mode=mode):
                self.assertFalse(self.simulate(mode)["success"])
        self.assertFalse(self.simulate("no-artifact", "iOS")["success"])

    def test_module_and_sdk_failure_reported(self):
        for mode in ("missing-module", "startup-sdk"):
            with self.subTest(mode=mode):
                report = self.simulate(mode)
                self.assertEqual(report["status"], "prerequisite-missing")
                self.assertIn("prerequisiteHelp", report)
                self.assertTrue((self.logs / "Editor.log").is_file())

    def test_timeout_and_unavailable(self):
        self.assertEqual(self.simulate("timeout", timeout=.2)["status"], "timeout")
        report = unity_compile.compile_project(
            self.root / "missing", self.project, self.logs, target="Android")
        self.assertEqual(report["status"], "unavailable")
        self.assertFalse((self.logs / "unity-result.json").exists())

    def test_output_preserved_on_log_dir_reuse(self):
        first = self.simulate()
        second = self.simulate()
        self.assertNotEqual(first["buildDirectory"], second["buildDirectory"])
        self.assertTrue(Path(first["unityResult"]["buildPath"]).is_file())

    def test_preflight_refuses_unsupported_host_target_scene_and_nested_output(self):
        with mock.patch.object(unity_compile.subprocess, "Popen") as launch, \
                mock.patch.object(unity_compile.platform, "system", return_value="Linux"):
            for target, scene, logs in (
                ("iOS", unity_compile.GAME_SCENE, self.logs),
                ("StandaloneLinux64", unity_compile.GAME_SCENE, self.logs),
                ("Android", "Assets/../outside.unity", self.logs),
                ("Android", "Assets/Missing.unity", self.logs),
                ("Android", unity_compile.GAME_SCENE, self.project / "Assets/Logs"),
            ):
                report = unity_compile.compile_project(sys.executable, self.project, logs,
                                                       target=target, scene=scene)
                self.assertFalse(report["success"])
            launch.assert_not_called()

    def test_cli_targets_explicit_and_packaging_requires_unity(self):
        script = unity_compile.ROOT / "tools"
        for command in (
            ["unity_compile.py", "--unity", sys.executable, "--target", "WebGL"],
            ["verify_unity_player_cases.py", "--unity", sys.executable],
            ["package_unity_source.py", "--player-target", "Android"],
        ):
            result = subprocess.run([sys.executable, str(script / command[0]), *command[1:]],
                                    capture_output=True, text=True)
            self.assertEqual(result.returncode, 2)

    def test_packaging_player_failure_does_not_touch_exports(self):
        # Execute just the packaging preflight, so a simulated Editor pass cannot make a ZIP.
        source = (unity_compile.ROOT / "tools/package_unity_source.py").read_text()
        preflight = source.split("root = pathlib.Path")[0]
        exports = unity_compile.ROOT / "exports"
        before = {p.name: (p.stat().st_size, p.stat().st_mtime_ns)
                  for p in exports.glob("NewGaza-Unity-Source*")}
        with mock.patch.object(sys, "argv", ["package", "--unity", "fake Unity",
                                             "--player-target", "Android"]), \
                mock.patch.object(subprocess, "run", side_effect=[
                    subprocess.CompletedProcess([], 0), subprocess.CompletedProcess([], 1)]) as run:
            with self.assertRaisesRegex(SystemExit, "Android player build did not pass"):
                exec(compile(preflight, "package-preflight", "exec"),
                     {"__file__": str(unity_compile.ROOT / "tools/package_unity_source.py")})
            self.assertIn("--target", run.call_args.args[0])
        after = {p.name: (p.stat().st_size, p.stat().st_mtime_ns)
                 for p in exports.glob("NewGaza-Unity-Source*")}
        self.assertEqual(before, after)

    def test_negative_probe_requires_editor_pass_and_exact_player_diagnostic(self):
        logs = self.root / "probe-logs"
        calls = []

        def compile_fixture(unity, project, log_dir, timeout, target=None):
            name = project.name
            text = (project / "Assets/Runtime/GameSession.cs").read_text()
            self.assertIn("#if UNITY_ANDROID && !UNITY_EDITOR", text)
            self.assertFalse(list(project.rglob("*.meta")))
            calls.append((name, target))
            if target:
                log_dir.mkdir(parents=True)
                diagnostic = ("error CS1029: NEW_GAZA_PLAYER_ONLY_FAILURE" if
                              name == "platform-only-error" else "error CS0246: UnityEditor")
                (log_dir / "Editor.log").write_text(diagnostic)
                return {"success": name == "pass", "status": "passed" if name == "pass" else "error"}
            return {"success": True, "status": "passed"}

        with mock.patch.object(verify_unity_player_cases, "compile_project", side_effect=compile_fixture):
            self.assertTrue(verify_unity_player_cases.verify_cases("fake", "Android", logs))
        self.assertEqual(len(calls), 6)
        with mock.patch.object(verify_unity_player_cases, "compile_project",
                               return_value={"success": False, "status": "unavailable"}):
            self.assertFalse(verify_unity_player_cases.verify_cases("missing", "Android", logs))
        self.assertTrue(all(row["playerStatus"] == "NOT RUN"
                            for row in json.loads((logs / "cases.json").read_text())))


if __name__ == "__main__":
    unittest.main()