using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class HudActivityVisibilityChecks
{
    // Compile the real visibility statement against the actual partial-class
    // field declarations. This catches unresolved identifiers, unlike parsing.
    // It is a focused contract test, not compilation against Unity assemblies.
    public static IEnumerable<string> Run(Dictionary<string, CompilationUnitSyntax> roots)
    {
        var hud = roots["UI/CityHud.cs"];
        var refresh = hud.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(m => m.Identifier.ValueText == "RefreshHud");
        var statement = refresh.DescendantNodes().OfType<ExpressionStatementSyntax>()
            .Single(s => s.Expression is InvocationExpressionSyntax call &&
                call.Expression.ToString() == "activity.gameObject.SetActive");
        var requiredFields = new[] { "activityCollapsed", "liveConstructionCount", "liveReadyCount" };
        var declarations = roots.Where(r => r.Key.StartsWith("UI/CityHud", StringComparison.Ordinal))
            .SelectMany(r => r.Value.DescendantNodes().OfType<FieldDeclarationSyntax>())
            .SelectMany(f => f.Declaration.Variables
                .Where(v => requiredFields.Contains(v.Identifier.ValueText))
                .Select(v => "private " + f.Declaration.Type + " " + v.Identifier.ValueText + ";"));
        var jobStage = roots["Core/GameModels.cs"].DescendantNodes().OfType<EnumDeclarationSyntax>()
            .Single(e => e.Identifier.ValueText == "JobStage");
        string source = jobStage.ToFullString() + @"
class State { public JobStage jobStage; }
class GameObject {
    public bool Active;
    public void SetActive(bool value) { Active = value; }
}
class Activity { public GameObject gameObject = new GameObject(); }
public class HudProbe {
" + string.Join("\n", declarations) + @"
    private Activity activity = new Activity();
    public bool Evaluate(bool collapsed, int stage, int construction, int ready) {
        activityCollapsed = collapsed;
        liveConstructionCount = construction;
        liveReadyCount = ready;
        var s = new State { jobStage = (JobStage)stage };
" + statement.ToFullString() + @"
        return activity.gameObject.Active;
    }
}";
        var compilation = CSharpCompilation.Create("HudVisibilityContract",
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp9)) },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var output = new MemoryStream();
        var emitted = compilation.Emit(output);
        var errors = emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => "HUD activity visibility: " + d).ToList();
        if (!emitted.Success) return errors;
        var assembly = Assembly.Load(output.ToArray());
        var probe = assembly.GetType("HudProbe");
        var instance = Activator.CreateInstance(probe!);
        var evaluate = probe!.GetMethod("Evaluate")!;
        var stageType = assembly.GetType("JobStage")!;
        int idle = Convert.ToInt32(Enum.Parse(stageType, "Idle"));
        int working = Convert.ToInt32(Enum.Parse(stageType, "Clearing"));
        foreach (bool collapsed in new[] { false, true })
        foreach (int stage in new[] { idle, working })
        foreach (int construction in new[] { 0, 1 })
        foreach (int ready in new[] { 0, 1 })
        {
            bool expected = !collapsed && (stage != idle || construction > 0 || ready > 0);
            bool actual = (bool)evaluate.Invoke(instance, new object[] { collapsed, stage, construction, ready })!;
            if (actual != expected)
                errors.Add($"HUD visibility mismatch: collapsed={collapsed}, stage={stage}, construction={construction}, ready={ready}.");
        }
        return errors;
    }
}
