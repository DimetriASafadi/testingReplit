using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

/// <summary>
/// Compiles the reported editor/runtime contracts in separate assemblies.
/// Uses minimal Unity 6 API contracts, not actual Unity assemblies or a player.
/// </summary>
internal static class EditorCompilationChecks
{
    internal static IEnumerable<string> Check(string sourceRoot)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
            .Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path)).ToArray();
        CompilationUnitSyntax Read(string name) => CSharpSyntaxTree.ParseText(
            File.ReadAllText(Path.Combine(sourceRoot, name))).GetCompilationUnitRoot();
        FieldDeclarationSyntax Field(CompilationUnitSyntax root, string name) =>
            root.DescendantNodes().OfType<FieldDeclarationSyntax>()
                .Single(field => field.Declaration.Variables.Any(variable =>
                    variable.Identifier.ValueText == name));

        var motion = Read("World/EquipmentMotion.cs");
        var fleet = Read("World/CityFleet.cs");
        var refresh = fleet.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "Refresh")
            .WithBody(SyntaxFactory.Block()).WithExpressionBody(null)
            .WithSemicolonToken(default);
        var finiteMethods = Read("Editor/NewGazaSmokeTest.cs").DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Where(method => method.Identifier.ValueText == "IsFinite");
        var playerGate = Read("Editor/NewGazaPlayerBuildGate.cs");
        var resultCounters = playerGate.DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Single(type => type.Identifier.ValueText == "Result")
            .WithMembers(SyntaxFactory.List<MemberDeclarationSyntax>(
                playerGate.DescendantNodes().OfType<FieldDeclarationSyntax>()
                    .Where(field => field.Declaration.Variables.Any(variable =>
                        variable.Identifier.ValueText == "totalErrors" ||
                        variable.Identifier.ValueText == "totalWarnings"))));
        var counterAssignments = playerGate.DescendantNodes().OfType<AssignmentExpressionSyntax>()
            .Where(assignment => assignment.Left.ToString() == "result.totalErrors" ||
                assignment.Left.ToString() == "result.totalWarnings");
        string playerReportContract = @"
using System;
using UnityEditor.Build.Reporting;
namespace UnityEditor.Build.Reporting
{
    public struct BuildSummary { public int totalErrors, totalWarnings; }
    public sealed class BuildReport { public BuildSummary summary; }
}
internal static class PlayerReportContractProbe
{
    " + resultCounters + @"
    private static void Validate(BuildReport build)
    {
        var result = new Result();
        " + string.Join("\n", counterAssignments.Select(assignment => assignment + ";")) + @"
    }
}
";

        // Retain actual access modifiers, signatures and constants. Other runtime
        // behavior is covered separately, so no fabricated Unity world is needed.
        string motionModifiers = motion.DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Single(type => type.Identifier.ValueText == "EquipmentMotion").Modifiers.ToString();
        string runtimeContract = @"
using UnityEngine;
using NewGaza.Core;
namespace UnityEngine
{
    public struct Vector3 { public float x, y, z; }
    public enum AudioClipLoadType { DecompressOnLoad }
    public class TextAsset { public string text; }
    public static class Resources { public static T Load<T>(string path) where T : class => null; }
    public static class JsonUtility
    {
        public static T FromJson<T>(string text) => default;
        public static string ToJson(object value, bool prettyPrint) => """";
    }
    public static class Application { public static string unityVersion => ""contract-only""; }
    public static class Debug
    {
        public static void Log(object message) {}
        public static void LogException(System.Exception error) {}
    }
}
namespace NewGaza.Core { public sealed class GameState {} }
namespace NewGaza
{
    " + motionModifiers + @" class EquipmentMotion
    { " + Field(motion, "DigCycleSeconds") + @" }
    public sealed partial class CityFleet
    { " + Field(fleet, "HaulingSpeed") + "\n" + refresh + @" }
}
";
        var runtime = CSharpCompilation.Create("Assembly-CSharp",
            new[] {
                CSharpSyntaxTree.ParseText(runtimeContract),
                CSharpSyntaxTree.ParseText(File.ReadAllText(
                    Path.Combine(sourceRoot, "World/CityBasemap.cs"))),
                CSharpSyntaxTree.ParseText(File.ReadAllText(
                    Path.Combine(sourceRoot, "Runtime/AssemblyInfo.cs")))
            }, references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var runtimeImage = new MemoryStream();
        var runtimeResult = runtime.Emit(runtimeImage);
        if (!runtimeResult.Success)
            return runtimeResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => "Runtime/editor contract assembly: " + d).ToArray();

        string editorContract = @"
using System;
using UnityEngine;
using NewGaza;
using NewGaza.Core;
namespace UnityEditor
{
    public static class EditorApplication
    {
        public static bool isPlayingOrWillChangePlaymode, isCompiling;
        public static void Exit(int code) {}
    }
    public static class EditorUtility { public static bool scriptCompilationFailed; }
    public static class AssetDatabase
    {
        public static string GetAssetPath(UnityEngine.TextAsset asset) => """";
    }
    public enum AudioSampleRateSetting { OverrideSampleRate }
    public enum AudioCompressionFormat { Vorbis }
    public struct AudioImporterSampleSettings
    {
        public bool preloadAudioData;
        public AudioClipLoadType loadType;
        public AudioSampleRateSetting sampleRateSetting;
        public uint sampleRateOverride;
        public AudioCompressionFormat compressionFormat;
        public float quality;
    }
    public class AudioImporter
    {
        public bool forceToMono, loadInBackground;
        [Obsolete(""Preload moved to AudioImporterSampleSettings"", true)]
        public bool preloadAudioData;
        public AudioImporterSampleSettings defaultSampleSettings;
        public void SetOverrideSampleSettings(string platform, AudioImporterSampleSettings settings) {}
    }
    public class AssetPostprocessor
    {
        protected string assetPath;
        protected object assetImporter;
    }
}
internal static class EditorContractProbe
{
    private static void Validate(CityFleet fleet)
    {
        float cycle = EquipmentMotion.DigCycleSeconds;
        float speed = CityFleet.HaulingSpeed;
        Vector3 position = default;
        fleet.Refresh(new GameState(), position, position);
        IsFinite(position);
        IsFinite(0f);
        IsFinite(float.NaN);
        IsFinite(float.PositiveInfinity);
    }
    " + string.Join("\n", finiteMethods) + @"
}
";
        var editor = CSharpCompilation.Create("Assembly-CSharp-Editor",
            new[] {
                CSharpSyntaxTree.ParseText(editorContract),
                CSharpSyntaxTree.ParseText(playerReportContract),
                CSharpSyntaxTree.ParseText(File.ReadAllText(
                    Path.Combine(sourceRoot, "Editor/NewGazaBasemapGate.cs"))),
                CSharpSyntaxTree.ParseText(File.ReadAllText(
                    Path.Combine(sourceRoot, "Editor/CityAudioImportSettings.cs")))
            }, references.Cast<MetadataReference>().Append(
                MetadataReference.CreateFromImage(runtimeImage.ToArray())),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var editorImage = new MemoryStream();
        return editor.Emit(editorImage).Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => "Targeted Unity 6 editor contract: " + d).ToArray();
    }
}