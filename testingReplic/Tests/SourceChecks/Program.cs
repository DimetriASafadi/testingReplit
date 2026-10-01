#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NewGaza;

internal static class Program
{
    private static readonly List<string> Failures = new List<string>();

    private static int Main(string[] args)
    {
        try
        {
            string sourceRoot = FindSources(args);
            var files = Directory.GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories);
            Array.Sort(files, StringComparer.Ordinal);
            Require(files.Length > 0, "No NewGaza C# files found in " + sourceRoot);
            var roots = new Dictionary<string, CompilationUnitSyntax>(StringComparer.Ordinal);
            foreach (string file in files)
            {
                string relative = Path.GetRelativePath(sourceRoot, file).Replace('\\', '/');
                string source = File.ReadAllText(file);
                // Check both player and editor conditional code, including any files under Editor.
                foreach (bool editor in new[] { false, true })
                {
                    var options = new CSharpParseOptions(LanguageVersion.CSharp9,
                        preprocessorSymbols: editor
                            ? new[] { "UNITY_EDITOR", "NEWGAZA_ARABIC_STATIC_CHECK" }
                            : Array.Empty<string>());
                    var tree = CSharpSyntaxTree.ParseText(source, options, file);
                    foreach (Diagnostic diagnostic in tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error))
                        Failures.Add((editor ? "editor" : "player") + " " + relative + ": " + diagnostic);
                    if (!editor) roots.Add(relative, (CompilationUnitSyntax)tree.GetRoot());
                }
            }
            CheckContracts(roots);
            CheckArabicShaping();
            if (Failures.Count > 0)
            {
                Console.Error.WriteLine("Source checks FAILED (" + Failures.Count + "):");
                foreach (string failure in Failures) Console.Error.WriteLine("  - " + failure);
                return 1;
            }
            Console.WriteLine("Source checks passed: " + files.Length +
                " NewGaza .cs files parsed as C# 9 (player + editor/optional checks), integration signatures and Arabic shaping.");
            Console.WriteLine("Source-only check; Unity assemblies were not compiled and no Unity editor/player was run.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("Source checks could not run: " + exception);
            return 1;
        }
    }

    private static string FindSources(string[] args)
    {
        if (args.Length > 1) throw new ArgumentException("Usage: dotnet run --project Tests/SourceChecks [path-to-Assets/NewGaza]");
        if (args.Length == 1)
        {
            string given = Path.GetFullPath(args[0]);
            if (!Directory.Exists(given)) throw new DirectoryNotFoundException(given);
            return given;
        }
        foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (DirectoryInfo? dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
            {
                string candidate = Path.Combine(dir.FullName, "Assets", "NewGaza");
                if (Directory.Exists(candidate)) return candidate;
                candidate = Path.Combine(dir.FullName, "testingReplic", "Assets", "NewGaza");
                if (Directory.Exists(candidate)) return candidate;
            }
        }
        throw new DirectoryNotFoundException("Could not find Assets/NewGaza; pass its path as the first argument.");
    }

    private static void CheckContracts(Dictionary<string, CompilationUnitSyntax> roots)
    {
        var session = Type(roots, "Runtime/GameSession.cs", "GameSession", "NewGaza");
        Base(session, "MonoBehaviour");
        Method(session, "Perform", "void", "Func<EconomyService,ActionResult>");
        Method(session, "ChooseDistrict", "void", "int");
        Method(session, "SelectPlot", "void", "int");
        Method(session, "SetPlayerName", "void", "string");
        Method(session, "Notify", "void", "string");
        Method(session, "Save", "void");
        Property(session, "Instance", "GameSession");
        Property(session, "State", "GameState");
        Property(session, "Economy", "EconomyService");
        Property(session, "Ready", "bool");
        Property(session, "SaveError", "string");
        Property(session, "Now", "long");
        Event(session, "Changed", "Action");
        Event(session, "Notification", "Action<string>");
        Event(session, "PlotSelected", "Action<int>");

        var world = Type(roots, "World/CityWorld.cs", "CityWorld", "NewGaza");
        Base(world, "MonoBehaviour");
        Method(world, "Initialize", "void", "GameSession");
        Method(world, "Refresh", "void");
        Method(world, "DistrictPosition", "Vector3", "int");
        Method(world, "FocusDistrict", "void", "int");
        Method(world, "SetSelectedPlot", "void", "int");
        Method(world, "PlayFinale", "void");

        var camera = Type(roots, "Runtime/CityCamera.cs", "CityCamera", "NewGaza");
        Base(camera, "MonoBehaviour");
        Method(camera, "Initialize", "void", "GameSession", "CityWorld");
        Method(camera, "Focus", "void", "Vector3");
        Method(camera, "FrameCity", "void");
        Method(camera, "PlayFinale", "void");
        Property(camera, "ModalOpen", "bool");

        var hud = Type(roots, "UI/CityHud.cs", "CityHud", "NewGaza");
        Base(hud, "MonoBehaviour");
        Method(hud, "Initialize", "void", "GameSession", "CityWorld", "CityCamera");
        var label = Type(roots, "UI/ArabicLabel.cs", "ArabicLabel", "NewGaza.UI");
        Base(label, "Text");
        Method(label, "SetText", "void", "string");
        Method(Type(roots, "UI/ArabicText.cs", "ArabicText", "NewGaza"),
            "Shape", "string", "string");
        Method(Type(roots, "Runtime/CityCapture.cs", "CityCapture", "NewGaza"),
            "Capture", "void", "GameSession", "CityCamera");
        var selectable = Type(roots, "World/CitySelectable.cs", "CitySelectable", "NewGaza");
        Base(selectable, "MonoBehaviour");
        Field(selectable, "districtIndex", "int");
        Field(selectable, "plotIndex", "int");
        Method(Type(roots, "Core/EconomyService.cs", "EconomyService", "NewGaza.Core"),
            "FindProject", "ProjectState", "int", "string");
    }

    private static TypeDeclarationSyntax? Type(
        Dictionary<string, CompilationUnitSyntax> roots, string path, string name, string expectedNamespace)
    {
        if (!roots.TryGetValue(path, out CompilationUnitSyntax? root))
        {
            Failures.Add("Missing integration source: " + path);
            return null;
        }
        var match = root.DescendantNodes().OfType<TypeDeclarationSyntax>()
            .FirstOrDefault(t => t.Identifier.ValueText == name &&
                t.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Any(n => n.Name.ToString() == expectedNamespace));
        Require(match != null, path + ": missing type " + expectedNamespace + "." + name);
        return match;
    }

    private static bool Public(MemberDeclarationSyntax member) => member.Modifiers.Any(SyntaxKind.PublicKeyword);
    private static string Signature(TypeSyntax syntax) =>
        new string(syntax.ToString().Where(c => !char.IsWhiteSpace(c)).ToArray());

    private static void Base(TypeDeclarationSyntax? owner, string baseName)
    {
        if (owner == null) return;
        Require(owner.BaseList?.Types.Any(t => Signature(t.Type) == baseName) == true,
            owner.Identifier.ValueText + ": expected base " + baseName);
    }

    private static void Method(TypeDeclarationSyntax? owner, string name, string returns, params string[] parameters)
    {
        if (owner == null) return;
        Require(owner.Members.OfType<MethodDeclarationSyntax>().Any(m =>
                Public(m) && m.Identifier.ValueText == name && Signature(m.ReturnType) == returns &&
                m.ParameterList.Parameters.Count == parameters.Length &&
                m.ParameterList.Parameters.Select(p => p.Type == null ? "" : Signature(p.Type))
                    .SequenceEqual(parameters)),
            owner.Identifier.ValueText + ": missing public " + returns + " " + name +
            "(" + string.Join(", ", parameters) + ")");
    }

    private static void Property(TypeDeclarationSyntax? owner, string name, string type)
    {
        if (owner == null) return;
        Require(owner.Members.OfType<PropertyDeclarationSyntax>().Any(p =>
                Public(p) && p.Identifier.ValueText == name && Signature(p.Type) == type),
            owner.Identifier.ValueText + ": missing public property " + type + " " + name);
    }

    private static void Event(TypeDeclarationSyntax? owner, string name, string type)
    {
        if (owner == null) return;
        Require(owner.Members.OfType<EventFieldDeclarationSyntax>().Any(e =>
                Public(e) && Signature(e.Declaration.Type) == type &&
                e.Declaration.Variables.Any(v => v.Identifier.ValueText == name)),
            owner.Identifier.ValueText + ": missing public event " + type + " " + name);
    }

    private static void Field(TypeDeclarationSyntax? owner, string name, string type)
    {
        if (owner == null) return;
        Require(owner.Members.OfType<FieldDeclarationSyntax>().Any(f =>
                Public(f) && Signature(f.Declaration.Type) == type &&
                f.Declaration.Variables.Any(v => v.Identifier.ValueText == name)),
            owner.Identifier.ValueText + ": missing public field " + type + " " + name);
    }

    private static void CheckArabicShaping()
    {
        foreach (string error in ArabicText.Validate()) Failures.Add("ArabicText.Validate: " + error);
        Equal("joined Gaza label", ArabicText.Shape("غزة"), "\uFE93\uFEB0\uFECF");
        Equal("joining / ligature", ArabicText.Shape("لا"), "\uFEFB");
        Equal("diacritic attached", ArabicText.Shape("بَ"), "\uFE8F\u064E");
        Equal("multiple paragraphs", ArabicText.Shape("ب\nأ\r\nب"), "\uFE8F\n\uFE83\n\uFE8F");
        string budget = ArabicText.Shape("ميزانية البداية: 50,000 عملة");
        Require(budget.Contains("50,000", StringComparison.Ordinal), "Grouped budget digits changed order: " + budget);
        string timer = ArabicText.Shape("الوقت 02:35");
        Require(timer.Contains("02:35", StringComparison.Ordinal), "Timer digits changed order: " + timer);
        string latin = ArabicText.Shape("نسخة Unity 6");
        Require(latin.Contains("Unity 6", StringComparison.Ordinal), "Latin run changed order: " + latin);
        string eastern = ArabicText.Shape("هدية ١٢٣");
        Require(eastern.Contains("١٢٣", StringComparison.Ordinal), "Arabic-Indic digits changed order: " + eastern);
        foreach (string label in new[] { "غزة الجديدة", "مشاريع الحي", "إزالة الركام", "المواد والتدوير" })
        {
            string shaped = ArabicText.Shape(label);
            Require(shaped != label && shaped.Any(c => c >= '\uFB50' && c <= '\uFEFF'),
                "Representative Arabic label not presentation-shaped: " + label);
            Require(!shaped.Contains('\u202E'), "Arabic label contains bidi override: " + label);
        }
    }

    private static void Equal(string name, string actual, string expected) =>
        Require(actual == expected, name + ": expected [" + expected + "], got [" + actual + "]");

    private static void Require(bool condition, string error)
    {
        if (!condition) Failures.Add(error);
    }
}