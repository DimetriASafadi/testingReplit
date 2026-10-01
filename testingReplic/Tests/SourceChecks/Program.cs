#nullable enable
using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
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
            CheckSceneBootstrap(sourceRoot, roots);
            CheckDistrictFog(sourceRoot, roots);
            CheckWorldRenderingContracts(roots);
            CheckImportedCityModelContracts(sourceRoot, roots);
            CheckSourcedCityContext(sourceRoot, roots);
            CheckHudIconContracts(roots);
            CheckArabicShaping();
            if (Failures.Count > 0)
            {
                Console.Error.WriteLine("Source checks FAILED (" + Failures.Count + "):");
                foreach (string failure in Failures) Console.Error.WriteLine("  - " + failure);
                return 1;
            }
            Console.WriteLine("Source checks passed: " + files.Length +
                " NewGaza .cs files parsed as C# 9 (player + editor/optional checks), integration signatures, GUID-independent scene bootstrap, sourced Gaza basemap/urban batching, five imported city models, localized-fog/shader and geometry-UV/shadow/URP-light checks, procedural HUD icon coverage/ownership/navigation bindings, and Arabic shaping.");
            Console.WriteLine("Source-only check; Unity assemblies and shader were not compiled, and no Unity editor/player or GPU rendering was run.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("Source checks could not run: " + exception);
            return 1;
        }
    }

    private static void CheckSceneBootstrap(string sourceRoot,
        Dictionary<string, CompilationUnitSyntax> roots)
    {
        string scene = File.ReadAllText(Path.Combine(sourceRoot, "Scenes", "NewGaza.unity"));
        Require(!scene.Contains("m_Script:", StringComparison.Ordinal),
            "The entry scene must not serialize per-machine MonoScript GUIDs.");
        var bootstrap = roots["Runtime/GameSession.cs"].DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m => m.Identifier.ValueText == "StartEntryScene");
        Require(bootstrap != null, "Missing GUID-independent runtime session bootstrap.");
        if (bootstrap == null) return;
        Require(bootstrap.Modifiers.Any(SyntaxKind.StaticKeyword) &&
            bootstrap.AttributeLists.SelectMany(a => a.Attributes).Any(a =>
                a.Name.ToString() == "RuntimeInitializeOnLoadMethod" &&
                a.ArgumentList?.ToString().Contains("RuntimeInitializeLoadType.AfterSceneLoad",
                    StringComparison.Ordinal) == true),
            "The session bootstrap must run after the entry scene loads.");
        Require(bootstrap.DescendantNodes().OfType<LiteralExpressionSyntax>().Any(e =>
                e.Token.ValueText == "Assets/NewGaza/Scenes/NewGaza.unity"),
            "Bootstrap must be restricted to the NewGaza entry scene.");
        Require(bootstrap.DescendantNodes().OfType<GenericNameSyntax>().Any(n =>
                n.Identifier.ValueText == "AddComponent" &&
                n.TypeArgumentList.Arguments.SingleOrDefault()?.ToString() == "GameSession"),
            "Bootstrap must attach GameSession by type, not by a serialized script GUID.");
        Require(bootstrap.DescendantNodes().OfType<GenericNameSyntax>().Any(n =>
                n.Identifier.ValueText == "FindFirstObjectByType" &&
                n.TypeArgumentList.Arguments.SingleOrDefault()?.ToString() == "GameSession"),
            "Bootstrap must guard against creating a duplicate session.");
    }

    private static void CheckDistrictFog(string sourceRoot,
        Dictionary<string, CompilationUnitSyntax> roots)
    {
        var city = Type(roots, "World/CityWorld.cs", "CityWorld", "NewGaza");
        Method(city, "IsDistrictFogged", "bool", "int");
        var fog = Type(roots, "World/DistrictFog.cs", "DistrictFog", "NewGaza");
        Require(fog?.Modifiers.Any(SyntaxKind.PublicKeyword) == true,
            "DistrictFog must be public.");
        Base(fog, "MonoBehaviour");
        Method(fog, "Show", "void", "bool");

        CheckFogStateBindings(city);
        CheckNoFogColliders(fog);
        CheckFogShader(sourceRoot, roots);
        CheckNoGlobalDistanceFog(sourceRoot, roots);
    }

    private static void CheckWorldRenderingContracts(
        Dictionary<string, CompilationUnitSyntax> roots)
    {
        var geometry = Type(roots, "World/CityGeometry.cs", "CityGeometry", "NewGaza");
        var createBox = geometry?.Members.OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m => m.Identifier.ValueText == "CreateBox");
        Require(createBox != null, "CityGeometry: missing shared box mesh construction.");
        if (createBox != null)
        {
            bool mapsPerFaceUvs = createBox.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Any(call => call.Expression is MemberAccessExpressionSyntax add &&
                    add.Expression.ToString() == "uv" &&
                    add.Name.Identifier.ValueText == "Add" &&
                    call.ArgumentList.Arguments.SingleOrDefault()?.Expression.ToString() == "faceUv[j]");
            bool assignsUvsToMesh = createBox.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Any(call => call.Expression is IdentifierNameSyntax make &&
                    make.Identifier.ValueText == "Make" &&
                    call.ArgumentList.Arguments.Count == 4 &&
                    call.ArgumentList.Arguments[3].Expression.ToString() == "uv.ToArray()");
            Require(mapsPerFaceUvs && assignsUvsToMesh,
                "CityGeometry.CreateBox must assign per-face UVs to the generated mesh.");
        }

        var world = Type(roots, "World/CityWorld.cs", "CityWorld", "NewGaza");
        var merge = world?.Members.OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m => m.Identifier.ValueText == "MergeDistrictPlots");
        Require(merge != null, "CityWorld: missing merged district presentation batches.");
        if (merge != null)
        {
            var presentationBuilds = merge.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Where(call => call.Expression is MemberAccessExpressionSyntax build &&
                    build.Name.Identifier.ValueText == "Build" &&
                    (build.Expression.ToString() == "damaged" ||
                     build.Expression.ToString() == "finished"))
                .ToArray();
            string[] batches = presentationBuilds
                .Select(call => ((MemberAccessExpressionSyntax)call.Expression).Expression.ToString())
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            Require(batches.SequenceEqual(new[] { "damaged", "finished" }) &&
                presentationBuilds.All(call =>
                    call.ArgumentList.Arguments.LastOrDefault()?.Expression.IsKind(
                        SyntaxKind.TrueLiteralExpression) == true),
                "Both damaged and finished district presentation batches must build with shadow casting enabled.");
        }

        var session = Type(roots, "Runtime/GameSession.cs", "GameSession", "NewGaza");
        var lighting = session?.Members.OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m => m.Identifier.ValueText == "ConfigureLighting");
        Require(lighting != null, "GameSession: missing daylight and URP lighting configuration.");
        if (lighting == null) return;
        bool usesExplicitSunUrpSettings = lighting.DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .Any(assignment => assignment.Left is MemberAccessExpressionSyntax property &&
                property.Name.Identifier.ValueText == "usePipelineSettings" &&
                property.Expression is InvocationExpressionSyntax getAdditionalData &&
                getAdditionalData.Expression is MemberAccessExpressionSyntax getMethod &&
                getMethod.Expression.ToString() == "sun" &&
                getMethod.Name.Identifier.ValueText == "GetUniversalAdditionalLightData" &&
                assignment.Right.IsKind(SyntaxKind.FalseLiteralExpression));
        Require(usesExplicitSunUrpSettings,
            "The directional sun must opt out of URP pipeline light settings to retain its configured bias.");
    }

    private static void CheckImportedCityModelContracts(string sourceRoot,
        Dictionary<string, CompilationUnitSyntax> roots)
    {
        string modelsDirectory = Path.Combine(sourceRoot, "Resources", "Models");
        string[] modelKeys =
        {
            "apartment", "ruined_building", "rubble_heap",
            "apartment_context", "ruined_building_context"
        };
        string[] primaryModelKeys = { "apartment", "ruined_building", "rubble_heap" };
        foreach (string key in modelKeys)
        {
            Require(File.Exists(Path.Combine(modelsDirectory, key + ".obj")),
                "Missing converted imported model source Resources/Models/" + key + ".obj.");
        }
        foreach (string key in primaryModelKeys)
        {
            Require(File.Exists(Path.Combine(modelsDirectory, key + "_albedo.png")),
                "Missing imported model albedo Resources/Models/" + key + "_albedo.png.");
        }

        string importSettingsPath = Path.Combine(sourceRoot, "Editor", "CityModelImportSettings.cs");
        Require(File.Exists(importSettingsPath), "Missing focused city model import settings postprocessor.");
        if (File.Exists(importSettingsPath))
        {
            string settings = Compact(File.ReadAllText(importSettingsPath));
            Require(settings.Contains("ModelsPrefix=\"Assets/NewGaza/Resources/Models/\"",
                    StringComparison.Ordinal) &&
                settings.Contains("path.StartsWith(ModelsPrefix,StringComparison.Ordinal)&&path.EndsWith(\".obj\",StringComparison.OrdinalIgnoreCase)",
                    StringComparison.Ordinal),
                "The model postprocessor must be restricted to OBJ assets below Resources/Models.");
            Require(settings.Contains("importer.isReadable=true", StringComparison.Ordinal) &&
                settings.Contains("importer.importNormals=ModelImporterNormals.Import", StringComparison.Ordinal) &&
                settings.Contains("importer.materialImportMode=ModelImporterMaterialImportMode.None",
                    StringComparison.Ordinal),
                "City OBJ import must retain readable meshes and imported normals without generated materials.");
            Require(settings.Contains("path.EndsWith(\"_albedo.png\",StringComparison.OrdinalIgnoreCase)",
                    StringComparison.Ordinal) &&
                settings.Contains("TextureImporterType.Default", StringComparison.Ordinal) &&
                settings.Contains("importer.sRGBTexture=true", StringComparison.Ordinal) &&
                settings.Contains("importer.maxTextureSize=1024", StringComparison.Ordinal) &&
                settings.Contains("importer.mipmapEnabled=true", StringComparison.Ordinal) &&
                settings.Contains("TextureImporterCompression.Compressed", StringComparison.Ordinal) &&
                settings.Contains("TextureWrapMode.Repeat", StringComparison.Ordinal),
                "City albedos must use the intended sRGB, mipmapped, compressed, repeating mobile import settings.");
            Require(settings.Contains("[MenuItem(\"NewGaza/Prepareimportedcitymodels\"",
                    StringComparison.Ordinal) &&
                settings.Contains("importer.SaveAndReimport()", StringComparison.Ordinal) &&
                !settings.Contains("SetPlatformTextureSettings", StringComparison.Ordinal),
                "The preparation menu must reimport only changed known assets and preserve platform defaults.");
            foreach (string key in modelKeys)
                Require(settings.Contains("ModelsPrefix+\"" + key + ".obj\"", StringComparison.Ordinal),
                    "The scoped preparation menu is missing the fixed OBJ path for " + key + ".");
            foreach (string key in primaryModelKeys)
                Require(settings.Contains("ModelsPrefix+\"" + key + "_albedo.png\"", StringComparison.Ordinal),
                    "The scoped preparation menu is missing the primary albedo path for " + key + ".");
        }

        var library = Type(roots, "World/CityModelLibrary.cs", "CityModelLibrary", "NewGaza");
        if (library != null)
        {
            var addTo = library.Members.OfType<MethodDeclarationSyntax>()
                .FirstOrDefault(method => method.Identifier.ValueText == "AddTo");
            ParameterSyntax? footprintParameter = addTo?.ParameterList.Parameters.LastOrDefault();
            Require(footprintParameter != null &&
                Signature(footprintParameter.Type!) == "bool" &&
                footprintParameter.Identifier.ValueText == "footprintIsLocal" &&
                footprintParameter.Default?.Value.IsKind(SyntaxKind.FalseLiteralExpression) == true,
                "CityModelLibrary must keep geographic hero placements unchanged and expose an optional local-OBB footprint flag.");

            var constructor = library.Members.OfType<ConstructorDeclarationSyntax>()
                .FirstOrDefault(ctor => ctor.ParameterList.Parameters.Count == 0);
            string[] loadedModels = constructor?.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Where(call => call.Expression is IdentifierNameSyntax load &&
                    load.Identifier.ValueText == "Load")
                .Select(call => call.ArgumentList.Arguments.SingleOrDefault()?.Expression
                    .DescendantNodesAndSelf().OfType<LiteralExpressionSyntax>()
                    .FirstOrDefault(literal => literal.IsKind(SyntaxKind.StringLiteralExpression))
                    ?.Token.ValueText ?? "")
                .ToArray() ?? Array.Empty<string>();
            Require(modelKeys.All(key => loadedModels.Contains(key, StringComparer.Ordinal)),
                "CityModelLibrary must explicitly load all three native models and both context LOD models.");

            var loadMethod = library.Members.OfType<MethodDeclarationSyntax>()
                .FirstOrDefault(method => method.Identifier.ValueText == "Load");
            var loadCalls = loadMethod?.DescendantNodes().OfType<InvocationExpressionSyntax>().ToArray()
                ?? Array.Empty<InvocationExpressionSyntax>();
            bool loadsModelResources = loadCalls.Any(call =>
                call.Expression is MemberAccessExpressionSyntax load &&
                load.Expression.ToString() == "Resources" &&
                load.Name is GenericNameSyntax generic &&
                generic.Identifier.ValueText == "Load" &&
                generic.TypeArgumentList.Arguments.SingleOrDefault()?.ToString() == "GameObject");
            bool loadsAliasedTexture = loadCalls.Any(call =>
                call.Expression is MemberAccessExpressionSyntax load &&
                load.Expression.ToString() == "Resources" &&
                load.Name is GenericNameSyntax generic &&
                generic.Identifier.ValueText == "Load" &&
                generic.TypeArgumentList.Arguments.SingleOrDefault()?.ToString() == "Texture2D" &&
                call.ArgumentList.Arguments.SingleOrDefault()?.Expression.ToString()
                    .Contains("_albedo", StringComparison.Ordinal) == true);
            bool reusesPrimaryAlbedoAliases = loadMethod?.DescendantNodes()
                .OfType<LiteralExpressionSyntax>()
                .Where(literal => literal.IsKind(SyntaxKind.StringLiteralExpression))
                .Select(literal => literal.Token.ValueText)
                .Contains("Models/apartment", StringComparer.Ordinal) == true &&
                loadMethod.DescendantNodes().OfType<LiteralExpressionSyntax>()
                    .Where(literal => literal.IsKind(SyntaxKind.StringLiteralExpression))
                    .Select(literal => literal.Token.ValueText)
                    .Contains("Models/ruined_building", StringComparer.Ordinal);
            Require(loadsModelResources && loadsAliasedTexture && reusesPrimaryAlbedoAliases,
                "Imported OBJ resources must load through Resources aliases, with context LODs reusing the two primary PNG albedos.");

            string source = Compact(library.ToString());
            Require(source.Contains("Resources.Load<Material>(\"NewGazaLit\")", StringComparison.Ordinal) &&
                source.Contains("newMaterial(template)", StringComparison.Ordinal) &&
                source.Contains("material.SetTexture(\"_BaseMap\",albedo)", StringComparison.Ordinal),
                "Imported models must clone the retained URP Lit material and bind their own albedo.");
            Require(source.Contains("models.TryGetValue(key,outImportedModelcached)", StringComparison.Ordinal) &&
                source.Contains("models.Add(key,imported)", StringComparison.Ordinal),
                "Imported model source meshes/materials must be cached rather than regenerated per placement.");
            Require(source.Contains("batch.Add(model.meshes[i],model.material,placement*model.childTransforms[i])",
                    StringComparison.Ordinal),
                "CityModelLibrary must pass imported source meshes and transforms to CityMeshBatch.");
            Require(!source.Contains("Ruin(", StringComparison.Ordinal) &&
                !source.Contains("proceduralfallback", StringComparison.Ordinal),
                "Missing imported assets must not silently fall back to procedural replacement models.");

            var dispose = library.Members.OfType<MethodDeclarationSyntax>()
                .FirstOrDefault(method => method.Identifier.ValueText == "Dispose");
            string disposeSource = dispose == null ? "" : Compact(dispose.ToString());
            Require(disposeSource.Contains("Object.Destroy(material)", StringComparison.Ordinal) &&
                !disposeSource.Contains("Object.Destroy(mesh)", StringComparison.Ordinal),
                "CityModelLibrary may destroy its cloned materials but must never destroy imported source meshes.");
        }

        var geometry = Type(roots, "World/CityGeometry.cs", "CityGeometry", "NewGaza");
        var release = geometry?.Members.OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(method => method.Identifier.ValueText == "Release");
        string releaseSource = release == null ? "" : Compact(release.ToString());
        Require(releaseSource.Contains("owned.Remove(mesh)", StringComparison.Ordinal) &&
            releaseSource.Contains("Object.Destroy(mesh)", StringComparison.Ordinal),
            "CityGeometry.Release must destroy only meshes registered as geometry-owned.");

        var meshBatch = Type(roots, "World/CityGeometry.cs", "CityMeshBatch", "NewGaza");
        var add = meshBatch?.Members.OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(method => method.Identifier.ValueText == "Add" &&
                method.ParameterList.Parameters.Count == 3);
        bool addsEverySubmesh = add?.Body?.DescendantNodes().OfType<ForStatementSyntax>()
            .Any(loop => loop.Declaration?.Variables.Count == 1 &&
                loop.Declaration.Variables[0].Identifier.ValueText == "submesh" &&
                loop.Condition is BinaryExpressionSyntax condition &&
                condition.IsKind(SyntaxKind.LessThanExpression) &&
                condition.Left.ToString() == "submesh" &&
                condition.Right.ToString() == "mesh.subMeshCount" &&
                loop.Incrementors.Any(increment => increment is PostfixUnaryExpressionSyntax postfix &&
                    postfix.IsKind(SyntaxKind.PostIncrementExpression) &&
                    postfix.Operand.ToString() == "submesh") &&
                loop.Statement.DescendantNodesAndSelf().OfType<ObjectCreationExpressionSyntax>()
                    .Any(creation => Signature(creation.Type) == "CombineInstance" &&
                        creation.Initializer?.Expressions.OfType<AssignmentExpressionSyntax>()
                            .Any(assignment => assignment.Left.ToString() == "mesh" &&
                                assignment.Right.ToString() == "mesh") == true &&
                        creation.Initializer.Expressions.OfType<AssignmentExpressionSyntax>()
                            .Any(assignment => assignment.Left.ToString() == "subMeshIndex" &&
                                assignment.Right.ToString() == "submesh") &&
                        creation.Initializer.Expressions.OfType<AssignmentExpressionSyntax>()
                            .Any(assignment => assignment.Left.ToString() == "transform" &&
                                assignment.Right.ToString() == "transform"))) == true;
        bool inputMeshIsNotOwned = add != null &&
            !add.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(call =>
                call.Expression is MemberAccessExpressionSyntax member &&
                member.Name.Identifier.ValueText == "Own");
        Require(addsEverySubmesh && inputMeshIsNotOwned,
            "CityMeshBatch.Add must preserve every imported OBJ submesh without taking ownership of its source mesh.");
        var build = meshBatch?.Members.OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(method => method.Identifier.ValueText == "Build");
        bool ownsCombinedOutput = build?.DescendantNodes().OfType<VariableDeclaratorSyntax>()
            .Any(variable => variable.Identifier.ValueText == "mesh" &&
                variable.Initializer?.Value.DescendantNodesAndSelf()
                    .OfType<InvocationExpressionSyntax>().Any(call =>
                        call.Expression is MemberAccessExpressionSyntax own &&
                        own.Expression.ToString() == "geometry" &&
                        own.Name.Identifier.ValueText == "Own" &&
                        call.ArgumentList.Arguments.SingleOrDefault()?.Expression is
                            ObjectCreationExpressionSyntax creation &&
                        Signature(creation.Type) == "Mesh") == true) == true;
        bool combinesOwnedOutput = build?.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Any(call => call.Expression is MemberAccessExpressionSyntax combine &&
                combine.Expression.ToString() == "mesh" &&
                combine.Name.Identifier.ValueText == "CombineMeshes" &&
                call.ArgumentList.Arguments.FirstOrDefault()?.Expression.ToString() ==
                    "batch.Value.ToArray()") == true;
        Require(ownsCombinedOutput && combinesOwnedOutput,
            "CityMeshBatch must own the combined output, not imported source meshes.");

        var world = Type(roots, "World/CityWorld.cs", "CityWorld", "NewGaza");
        var releaseVisual = world?.Members.OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(method => method.Identifier.ValueText == "ReleaseVisual");
        bool deactivatesVisual = releaseVisual?.DescendantNodes()
            .OfType<InvocationExpressionSyntax>().Any(call =>
                call.Expression is MemberAccessExpressionSyntax setActive &&
                setActive.Expression.ToString() == "visual" &&
                setActive.Name.Identifier.ValueText == "SetActive" &&
                call.ArgumentList.Arguments.SingleOrDefault()?.Expression.IsKind(
                    SyntaxKind.FalseLiteralExpression) == true) == true;
        bool releasesInactiveChildMeshes = releaseVisual?.DescendantNodes()
            .OfType<ForEachStatementSyntax>().Any(loop =>
                Signature(loop.Type) == "MeshFilter" && loop.Identifier.ValueText == "filter" &&
                loop.Expression is InvocationExpressionSyntax children &&
                children.Expression is MemberAccessExpressionSyntax getChildren &&
                getChildren.Expression.ToString() == "visual" &&
                getChildren.Name is GenericNameSyntax generic &&
                generic.Identifier.ValueText == "GetComponentsInChildren" &&
                generic.TypeArgumentList.Arguments.SingleOrDefault()?.ToString() == "MeshFilter" &&
                children.ArgumentList.Arguments.SingleOrDefault()?.Expression.IsKind(
                    SyntaxKind.TrueLiteralExpression) == true &&
                loop.Statement.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>()
                    .Any(call => call.Expression is MemberAccessExpressionSyntax release &&
                        release.Expression.ToString() == "geometry" &&
                        release.Name.Identifier.ValueText == "Release" &&
                        call.ArgumentList.Arguments.SingleOrDefault()?.Expression.ToString() ==
                            "filter.sharedMesh")) == true;
        bool destroysOnlyVisualObject = releaseVisual != null &&
            releaseVisual.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(call =>
                call.Expression is IdentifierNameSyntax destroy &&
                destroy.Identifier.ValueText == "Destroy" &&
                call.ArgumentList.Arguments.SingleOrDefault()?.Expression.ToString() == "visual") &&
            !releaseVisual.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(call =>
                call.Expression is IdentifierNameSyntax destroy &&
                destroy.Identifier.ValueText == "Destroy" &&
                call.ArgumentList.Arguments.SingleOrDefault()?.Expression.ToString() ==
                    "filter.sharedMesh");
        Require(deactivatesVisual && releasesInactiveChildMeshes && destroysOnlyVisualObject,
            "ReleaseVisual must include inactive children and release meshes only through geometry ownership, never destroy imported source meshes directly.");

        var replacePlot = world?.Members.OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(method => method.Identifier.ValueText == "ReplacePlot");
        string replaceSource = replacePlot == null ? "" : Compact(replacePlot.ToString());
        Require(replaceSource.Contains("if(stage==0)", StringComparison.Ordinal) &&
            replaceSource.Contains("modelLibrary.AddTo(batch,\"ruined_building\"", StringComparison.Ordinal) &&
            replaceSource.Contains("plot.definition.kind==ProjectKind.Housing", StringComparison.Ordinal) &&
            replaceSource.Contains("modelLibrary.AddTo(batch,\"apartment\"", StringComparison.Ordinal),
            "Damaged structures and completed housing must use their actual imported city models.");
        Require(!replaceSource.Contains("Ruin(batch", StringComparison.Ordinal),
            "The stage-zero imported building must not require the old procedural ruin mesh.");

        var districtViewingSize = world?.Members.OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(method => method.Identifier.ValueText == "DistrictViewingSize");
        string[] extentNames = { "radius", "extent", "bounds", "size", "footprint", "plot", "width", "height" };
        string[] viewingNames = districtViewingSize?.DescendantNodes()
            .OfType<IdentifierNameSyntax>().Select(name => name.Identifier.ValueText)
            .Concat(districtViewingSize.DescendantNodes().OfType<MemberAccessExpressionSyntax>()
                .Select(member => member.Name.Identifier.ValueText))
            .ToArray() ?? Array.Empty<string>();
        bool usesPresentationExtent = extentNames.Any(extent =>
            viewingNames.Any(name => name.IndexOf(extent, StringComparison.OrdinalIgnoreCase) >= 0));
        bool avoidsRootScaleZoom = districtViewingSize?.DescendantNodes()
            .OfType<MemberAccessExpressionSyntax>()
            .All(member => member.Name.Identifier.ValueText != "lossyScale") == true;
        Require(usesPresentationExtent && avoidsRootScaleZoom,
            "District viewing focus must frame a sourced district's actual urban presentation extent, not scale tiny isolated districts.");

        var camera = Type(roots, "Runtime/CityCamera.cs", "CityCamera", "NewGaza");
        string cameraSource = camera == null ? "" : Compact(camera.ToString());
        Require(cameraSource.Contains("targetZoom=world.DistrictViewingSize(session.State.selectedDistrict)",
                    StringComparison.Ordinal) &&
                cameraSource.Contains("Mathf.Clamp(targetZoom*lastSpan/span,0.6f", StringComparison.Ordinal) &&
                cameraSource.Contains("Mathf.Clamp(targetZoom*Mathf.Exp(-scroll*0.0015f),0.6f",
                    StringComparison.Ordinal),
            "District-scaled camera focus and the 0.6 minimum zoom must remain in the touch and mouse controls.");

        string smokePath = Path.Combine(sourceRoot, "Editor", "NewGazaSmokeTest.cs");
        Require(File.Exists(smokePath), "Missing New Gaza editor smoke test.");
        if (File.Exists(smokePath))
        {
            string smoke = Compact(File.ReadAllText(smokePath));
            Require(smoke.Contains("Resources.Load<GameObject>(\"Models/\"+key)", StringComparison.Ordinal) &&
                smoke.Contains("Resources.Load<Texture2D>(\"Models/\"+albedoKeys[i]+\"_albedo\")",
                    StringComparison.Ordinal) &&
                smoke.Contains("world.ImportedModelCount==5", StringComparison.Ordinal) &&
                smoke.Contains("albedos[3]==albedos[0]&&albedos[4]==albedos[1]",
                    StringComparison.Ordinal) &&
                smoke.Contains("lit.shader.name==\"UniversalRenderPipeline/Lit\"",
                    StringComparison.Ordinal) &&
                smoke.Contains("mesh.isReadable", StringComparison.Ordinal) &&
                smoke.Contains("uvs.Length==vertices.Length", StringComparison.Ordinal) &&
                smoke.Contains("normals.Length==vertices.Length", StringComparison.Ordinal),
                "Unity smoke test must inspect all five imported model resources, aliased context albedos, readable UVs and imported normals.");
            Require(smoke.Contains("Resources.Load<TextAsset>(\"GazaBasemap\")", StringComparison.Ordinal) &&
                smoke.Contains("JsonUtility.FromJson<CityBasemap>(source.text)", StringComparison.Ordinal) &&
                smoke.Contains("float.IsNaN(point.x)", StringComparison.Ordinal) &&
                smoke.Contains("float.IsInfinity(point.z)", StringComparison.Ordinal) &&
                smoke.Contains("maxX-minX>150f&&maxZ-minZ>150f", StringComparison.Ordinal),
                "Unity smoke test must parse GazaBasemap JSON and check finite feature coordinates across a wide city extent.");
            Require(smoke.Contains("int[]triangleBudgets={2500,4000,1400,350,450}", StringComparison.Ordinal) &&
                smoke.Contains("Mathf.CeilToInt(triangleBudgets[i]*1.01f)", StringComparison.Ordinal) &&
                smoke.Contains("material.GetTexture(\"_BaseMap\")", StringComparison.Ordinal) &&
                smoke.Contains("ShadowCastingMode.On", StringComparison.Ordinal),
                "Unity smoke test must check all five native-model triangle budgets, URP albedos and casting batch shadows.");
        }
    }

    private static void CheckSourcedCityContext(string sourceRoot,
        Dictionary<string, CompilationUnitSyntax> roots)
    {
        CheckBasemapSourceArchive(sourceRoot);
        string basemapPath = Path.Combine(sourceRoot, "Resources", "GazaBasemap.json");
        Require(File.Exists(basemapPath), "Missing authentic Gaza city basemap Resources/GazaBasemap.json.");
        if (File.Exists(basemapPath))
        {
            using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(basemapPath)))
            {
                JsonElement root = document.RootElement;
                bool hasSchema = root.TryGetProperty("schemaVersion", out JsonElement schema) &&
                    schema.ValueKind == JsonValueKind.Number && schema.GetInt32() == 1;
                bool hasMetadata = root.TryGetProperty("metadata", out JsonElement metadata);
                string attribution = hasMetadata &&
                    metadata.TryGetProperty("attribution", out JsonElement attributionElement)
                    ? attributionElement.GetString() ?? "" : "";
                string source = hasMetadata &&
                    metadata.TryGetProperty("source", out JsonElement sourceElement)
                    ? sourceElement.GetString() ?? "" : "";
                Require(hasSchema && attribution.Contains("OpenStreetMap", StringComparison.OrdinalIgnoreCase) &&
                    source.Contains("OpenStreetMap", StringComparison.OrdinalIgnoreCase),
                    "City context must be the versioned, attributed OpenStreetMap source rather than illustrative geometry.");

                bool hasRoads = root.TryGetProperty("roads", out JsonElement roads) &&
                    roads.ValueKind == JsonValueKind.Array && roads.GetArrayLength() >= 20;
                bool hasBuildings = root.TryGetProperty("buildings", out JsonElement buildings) &&
                    buildings.ValueKind == JsonValueKind.Array && buildings.GetArrayLength() >= 100;
                bool hasAreas = root.TryGetProperty("areas", out JsonElement areas) &&
                    areas.ValueKind == JsonValueKind.Array && areas.GetArrayLength() >= 5;
                double minX = double.PositiveInfinity, maxX = double.NegativeInfinity;
                double minZ = double.PositiveInfinity, maxZ = double.NegativeInfinity;
                bool finiteCoordinates = hasRoads && hasBuildings && hasAreas &&
                    AccumulateFeaturePoints(roads, "points", false, ref minX, ref maxX, ref minZ, ref maxZ) &&
                    AccumulateFeaturePoints(buildings, "outline", true, ref minX, ref maxX, ref minZ, ref maxZ) &&
                    AccumulateFeaturePoints(areas, "points", false, ref minX, ref maxX, ref minZ, ref maxZ);
                Require(hasRoads && hasBuildings && hasAreas && finiteCoordinates &&
                    maxX - minX > 150 && maxZ - minZ > 150,
                    "Gaza basemap must contain finite, nontrivial road, footprint and landuse coverage across an urban extent.");
            }
        }

        var basemap = Type(roots, "World/CityBasemap.cs", "CityBasemap", "NewGaza");
        if (basemap != null)
        {
            string basemapSource = Compact(basemap.ToString());
            Require(basemapSource.Contains("JsonUtility.FromJson<CityBasemap>", StringComparison.Ordinal) &&
                basemapSource.Contains("Resources.Load<TextAsset>(resourcePath)", StringComparison.Ordinal) &&
                basemapSource.Contains("map.Validate(sourceName)", StringComparison.Ordinal),
                "CityBasemap must load required JSON from Resources and validate its typed source data.");
        }

        var context = Type(roots, "World/CityUrbanContext.cs", "CityUrbanContext", "NewGaza");
        if (context == null) return;
        var contextSource = context.DescendantNodesAndSelf().ToArray();
        bool usesSourcedFeatureCollections = new[] { "roads", "buildings", "areas" }.All(name =>
            contextSource.OfType<IdentifierNameSyntax>().Any(identifier =>
                identifier.Identifier.ValueText == name));
        var contextChunk = context.Members.OfType<ClassDeclarationSyntax>()
            .FirstOrDefault(type => type.Identifier.ValueText == "ContextChunk");
        var chunkBuild = contextChunk?.Members.OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(method => method.Identifier.ValueText == "Build");
        bool hasBatchCollector = contextChunk != null &&
            contextChunk.Members.OfType<FieldDeclarationSyntax>()
            .Any(field => Signature(field.Declaration.Type) == "CityMeshBatch" &&
                field.Declaration.Variables.Any(variable => variable.Identifier.ValueText == "models")) == true &&
            contextChunk.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(call =>
                call.Expression is MemberAccessExpressionSyntax build &&
                build.Expression.ToString() == "models" &&
                build.Name.Identifier.ValueText == "Build" &&
                call.ArgumentList.Arguments.Count == 3);
        bool buildsOwnedChunkMeshes = chunkBuild != null && chunkBuild.DescendantNodes()
            .OfType<VariableDeclaratorSyntax>().Any(variable =>
                variable.Initializer?.Value is InvocationExpressionSyntax own &&
                own.Expression is MemberAccessExpressionSyntax ownCall &&
                ownCall.Expression.ToString() == "geometry" &&
                ownCall.Name.Identifier.ValueText == "Own" &&
                own.ArgumentList.Arguments.SingleOrDefault()?.Expression is ObjectCreationExpressionSyntax mesh &&
                Signature(mesh.Type) == "Mesh") == true;
        bool buildsChunkRenderers = chunkBuild != null &&
            chunkBuild.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Any(call => call.Expression is MemberAccessExpressionSyntax add &&
                add.Name is GenericNameSyntax generic &&
                generic.Identifier.ValueText == "AddComponent" &&
                generic.TypeArgumentList.Arguments.SingleOrDefault()?.ToString() == "MeshFilter") == true &&
            chunkBuild.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(call =>
                call.Expression is MemberAccessExpressionSyntax add &&
                add.Name is GenericNameSyntax generic &&
                generic.Identifier.ValueText == "AddComponent" &&
                generic.TypeArgumentList.Arguments.SingleOrDefault()?.ToString() == "MeshRenderer") == true;
        bool batchesAllChunks = context.Members.OfType<MethodDeclarationSyntax>()
            .Where(method => method.Identifier.ValueText == "Build")
            .SelectMany(method => method.DescendantNodes().OfType<InvocationExpressionSyntax>())
            .Any(call => call.Expression is MemberAccessExpressionSyntax build &&
                build.Expression.ToString() == "pair.Value" &&
                build.Name.Identifier.ValueText == "Build");
        bool usesLocalOsmFootprintDimensions = contextSource.OfType<InvocationExpressionSyntax>()
            .Any(call => call.Expression is MemberAccessExpressionSyntax add &&
                add.Name.Identifier.ValueText == "AddTo" &&
                call.ArgumentList.Arguments.Count == 7 &&
                call.ArgumentList.Arguments[3].Expression is ObjectCreationExpressionSyntax footprint &&
                footprint.ArgumentList is ArgumentListSyntax footprintArguments &&
                footprintArguments.Arguments.Any(argument =>
                    argument.Expression.ToString() == "building.size.x") == true &&
                footprintArguments.Arguments.Any(argument =>
                    argument.Expression.ToString() == "building.size.z") &&
                call.ArgumentList.Arguments[6].Expression.IsKind(SyntaxKind.TrueLiteralExpression));
        bool createsObjectsOnlyAtChunkGranularity = contextSource
            .OfType<ObjectCreationExpressionSyntax>()
            .Where(creation => Signature(creation.Type) == "GameObject")
            .All(creation => creation.Ancestors().OfType<ClassDeclarationSyntax>()
                    .Any(type => type.Identifier.ValueText == "ContextChunk") &&
                creation.Ancestors().OfType<MethodDeclarationSyntax>()
                    .Any(method => method.Identifier.ValueText == "Build"));
        var nestedContextTypes = context.Members.OfType<ClassDeclarationSyntax>().ToArray();
        bool noPerBuildingBehaviours = context.BaseList?.Types.Any(type =>
                Signature(type.Type) == "MonoBehaviour") != true &&
            nestedContextTypes.All(type =>
                type.BaseList?.Types.Any(baseType => Signature(baseType.Type) == "MonoBehaviour") != true) &&
            contextSource.OfType<MethodDeclarationSyntax>().All(method =>
                method.Identifier.ValueText != "Update");
        bool hasNoPerBuildingBehavioursOrColliders =
            noPerBuildingBehaviours &&
            !contextSource.OfType<InvocationExpressionSyntax>().Any(call =>
                call.Expression is MemberAccessExpressionSyntax addComponent &&
                addComponent.Name is GenericNameSyntax generic &&
                generic.Identifier.ValueText == "AddComponent" &&
                generic.TypeArgumentList.Arguments.SingleOrDefault()?.ToString()
                    .EndsWith("Collider", StringComparison.Ordinal) == true) &&
            !contextSource.OfType<TypeSyntax>().Any(type =>
                Signature(type).EndsWith("Collider", StringComparison.Ordinal));
        Require(usesSourcedFeatureCollections && hasBatchCollector &&
            buildsOwnedChunkMeshes && buildsChunkRenderers && batchesAllChunks &&
            createsObjectsOnlyAtChunkGranularity &&
            usesLocalOsmFootprintDimensions &&
            hasNoPerBuildingBehavioursOrColliders,
            "Sourced roads, landuse and footprints must be statically chunk-batched, with oriented parcel-local LOD placement and no per-building behaviours or colliders.");
        CheckExtrudedContextBuildings(context, contextChunk, chunkBuild);
        CheckSourcedUtilitiesAndDepot(context, roots);

        var world = Type(roots, "World/CityWorld.cs", "CityWorld", "NewGaza");
        if (world == null) return;
        var initialize = world.Members.OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(method => method.Identifier.ValueText == "Initialize");
        var initializeCalls = initialize?.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .ToArray() ?? Array.Empty<InvocationExpressionSyntax>();
        bool loadsTypedBasemap = initializeCalls.Any(call =>
            call.Expression is MemberAccessExpressionSyntax load &&
            load.Expression.ToString() == "CityBasemap" &&
            load.Name.Identifier.ValueText == "LoadFromResources");
        bool contextBuildsBeforeDistricts = initialize?.Body != null &&
            initialize.Body.Statements.Select((statement, index) => new { statement, index })
                .Where(item => item.statement.DescendantNodesAndSelf()
                    .OfType<InvocationExpressionSyntax>().Any(call =>
                        call.Expression is MemberAccessExpressionSyntax build &&
                        build.Expression.ToString() == "CityUrbanContext" &&
                        build.Name.Identifier.ValueText == "Build"))
                .Select(item => item.index).DefaultIfEmpty(-1).Min() is int contextIndex &&
            initialize.Body.Statements.Select((statement, index) => new { statement, index })
                .Where(item => item.statement.DescendantNodesAndSelf()
                    .OfType<InvocationExpressionSyntax>().Any(call =>
                        call.Expression is IdentifierNameSyntax buildDistricts &&
                        buildDistricts.Identifier.ValueText == "BuildDistricts"))
                .Select(item => item.index).DefaultIfEmpty(int.MaxValue).Min() > contextIndex;
        Require(loadsTypedBasemap && contextBuildsBeforeDistricts,
            "CityWorld must load the required Gaza basemap and build its sourced context before native districts.");

        var worldNames = world.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>()
            .Select(identifier => identifier.Identifier.ValueText).ToArray();
        Require(worldNames.Contains("CityBasemap", StringComparer.Ordinal) &&
            worldNames.Contains("CityUrbanContext", StringComparer.Ordinal),
            "CityWorld must present the real sourced Gaza basemap through the shared urban-context renderer.");
        bool doesNotShrinkDistrictRoots = !world.DescendantNodes()
            .OfType<AssignmentExpressionSyntax>().Any(assignment =>
                assignment.Left.ToString().EndsWith(".root.localScale", StringComparison.Ordinal) &&
                assignment.Right.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>()
                    .Any(call => call.Expression is MemberAccessExpressionSyntax clamp &&
                        clamp.Expression.ToString() == "Mathf" &&
                        clamp.Name.Identifier.ValueText == "Clamp"));
        Require(doesNotShrinkDistrictRoots,
            "District centers must retain geographic scale instead of shrinking isolated roots to fit.");
        bool doesNotMutateSessionState = !world.DescendantNodes().OfType<AssignmentExpressionSyntax>()
            .Any(assignment => assignment.Left.ToString()
                .StartsWith("session.State", StringComparison.Ordinal));
        Require(doesNotMutateSessionState,
            "CityWorld and basemap presentation must leave saved core progress state owned by GameSession.");

        var boundsProperties = world.Members.OfType<PropertyDeclarationSyntax>()
            .Where(property => new[] { "MapMinX", "MapMaxX", "MapMinZ", "MapMaxZ" }
                .Contains(property.Identifier.ValueText, StringComparer.Ordinal))
            .ToArray();
        bool mapBoundsIncludeSourcedExtents = boundsProperties.Length == 4 &&
            boundsProperties.All(property => property.DescendantNodes()
                .OfType<MemberAccessExpressionSyntax>().Any(member =>
                    member.Expression.ToString() == "basemap.actualBounds"));
        Require(mapBoundsIncludeSourcedExtents,
            "CityWorld's public map bounds must union the OSM extents with the geographic game route.");

        var palette = world.Members.OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(method => method.Identifier.ValueText == "MakePalette");
        bool retainsAnimatedSeaShader = palette?.DescendantNodes()
            .OfType<InvocationExpressionSyntax>().Any(call =>
                call.Expression is MemberAccessExpressionSyntax load &&
                load.Expression.ToString() == "Resources" &&
                load.Name is GenericNameSyntax generic &&
                generic.Identifier.ValueText == "Load" &&
                generic.TypeArgumentList.Arguments.SingleOrDefault()?.ToString() == "Shader" &&
                call.ArgumentList.Arguments.SingleOrDefault()?.Expression.ToString() == "\"NewGazaSea\"") == true &&
            palette.DescendantNodes().OfType<ObjectCreationExpressionSyntax>()
                .Any(creation => Signature(creation.Type) == "Material" &&
                    creation.ArgumentList?.Arguments.SingleOrDefault()?.Expression.ToString() == "seaShader");
        Require(retainsAnimatedSeaShader,
            "CityWorld must keep the retained custom URP animated-sea shader resource.");
        CheckSeaUvAndFleetScale(world, roots);

        var camera = Type(roots, "Runtime/CityCamera.cs", "CityCamera", "NewGaza");
        if (camera != null)
        {
            bool cameraFramesUnionBounds = new[] { "MapMinX", "MapMaxX", "MapMinZ", "MapMaxZ" }
                .All(bound => camera.DescendantNodes().OfType<MemberAccessExpressionSyntax>()
                    .Any(member => member.Expression.ToString() == "world" &&
                        member.Name.Identifier.ValueText == bound));
            Require(cameraFramesUnionBounds,
                "CityCamera overview framing and centre must include the basemap/game-route union bounds.");
        }
    }

    private static void CheckExtrudedContextBuildings(TypeDeclarationSyntax context,
        TypeDeclarationSyntax? contextChunk, MethodDeclarationSyntax? chunkBuild)
    {
        var volume = Type(new Dictionary<string, CompilationUnitSyntax>
        {
            { "World/CityUrbanContext.cs", (CompilationUnitSyntax)context.SyntaxTree.GetRoot() }
        }, "World/CityUrbanContext.cs", "CityUrbanBuildingVolume", "NewGaza");
        var volumeFields = volume?.Members.OfType<FieldDeclarationSyntax>().ToArray()
            ?? Array.Empty<FieldDeclarationSyntax>();
        bool plainVolumeDto = volume != null && volume.BaseList == null &&
            volume.Members.OfType<MethodDeclarationSyntax>().Any() == false &&
            volume.Members.OfType<PropertyDeclarationSyntax>().Any() == false &&
            new[]
            {
                new { Name = "sourceBuildingId", Type = "string" },
                new { Name = "height", Type = "float" },
                new { Name = "outline", Type = "CityBasemapPoint[]" }
            }.All(expected => volumeFields.Any(field =>
                field.Modifiers.Any(SyntaxKind.ReadOnlyKeyword) &&
                Signature(field.Declaration.Type) == expected.Type &&
                field.Declaration.Variables.Any(variable =>
                    variable.Identifier.ValueText == expected.Name)));
        var volumeConstructor = volume?.Members.OfType<ConstructorDeclarationSyntax>()
            .FirstOrDefault(constructor => constructor.ParameterList.Parameters.Count == 3);
        string volumeConstructorSource = volumeConstructor == null
            ? "" : Compact(volumeConstructor.ToString());
        Require(plainVolumeDto && volumeConstructorSource.Contains(
                "this.sourceBuildingId=sourceBuildingId", StringComparison.Ordinal) &&
            volumeConstructorSource.Contains("this.height=height", StringComparison.Ordinal) &&
            volumeConstructorSource.Contains("this.outline=outline", StringComparison.Ordinal),
            "Extruded context volumes must remain plain immutable DTOs retaining the original source polygon and height.");

        var extrude = context.Members.OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(method => method.Identifier.ValueText == "AddExtrudedBuilding");
        string extrudeSource = extrude == null ? "" : Compact(extrude.ToString());
        string[] expectedWallTriangles =
        {
            "material,bottomA,topA,topB",
            "material,bottomA,topB,bottomB",
            "material,bottomA,bottomB,topB",
            "material,bottomA,topB,topA"
        };
        string[] actualWallTriangles = extrude?.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(call => call.Expression is MemberAccessExpressionSyntax add &&
                add.Expression.ToString() == "chunk" &&
                add.Name.Identifier.ValueText == "AddTriangle")
            .Select(call => string.Join(",", call.ArgumentList.Arguments
                .Select(argument => argument.Expression.ToString())))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray() ?? Array.Empty<string>();
        bool buildsRoofAndExactWindingAwareWalls =
            extrudeSource.Contains("AddPolygon(newList<Vector2>(outline),GroundY+building.height,material,chunks,geometry,owner)",
                StringComparison.Ordinal) &&
            extrudeSource.Contains("List<float>cuts=SegmentChunkCuts(a.x,a.y,b.x,b.y)",
                StringComparison.Ordinal) &&
            extrudeSource.Contains("Vector2low=Vector2.LerpUnclamped(a,b,t0)", StringComparison.Ordinal) &&
            extrudeSource.Contains("Vector2high=Vector2.LerpUnclamped(a,b,t1)", StringComparison.Ordinal) &&
            actualWallTriangles.SequenceEqual(expectedWallTriangles.OrderBy(value => value,
                StringComparer.Ordinal), StringComparer.Ordinal) &&
            extrudeSource.Contains("owner.triangleCount+=2", StringComparison.Ordinal) &&
            extrudeSource.Contains("owner.extrudedVolumes.Add(newCityUrbanBuildingVolume(building.FeatureId,building.height,building.outline))",
                StringComparison.Ordinal);
        var clippedPolygons = context.Members.OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(method => method.Identifier.ValueText == "AddClippedPolygon");
        string clippedSource = clippedPolygons == null ? "" : Compact(clippedPolygons.ToString());
        bool ownsAndCountsGeneratedSurfaces = chunkBuild != null &&
            clippedSource.Contains("chunk.AddTopTriangle(material,clipped[0],clipped[i],clipped[i+1],y)",
                StringComparison.Ordinal) &&
            clippedSource.Contains("owner.triangleCount++", StringComparison.Ordinal) &&
            Compact(chunkBuild.ToString()).Contains("geometry.Own(newMesh", StringComparison.Ordinal) &&
            Compact(chunkBuild.ToString()).Contains("mesh.RecalculateNormals()", StringComparison.Ordinal) &&
            Compact(chunkBuild.ToString()).Contains("mesh.RecalculateBounds()", StringComparison.Ordinal) &&
            Compact(chunkBuild.ToString()).Contains("mesh.SetTriangles(indices[pair.Key],0,true)",
                StringComparison.Ordinal);
        Require(buildsRoofAndExactWindingAwareWalls && ownsAndCountsGeneratedSurfaces,
            "Context buildings must emit roof polygons and exact winding-aware walls from every source edge, count generated triangles on their owner, and build owned normaled meshes.");
    }

    private static void CheckSourcedUtilitiesAndDepot(TypeDeclarationSyntax context,
        Dictionary<string, CompilationUnitSyntax> roots)
    {
        var utilityPlacement = context.Members.OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(method => method.Identifier.ValueText == "PlaceUtilities");
        string utilitySource = utilityPlacement == null ? "" : Compact(utilityPlacement.ToString());
        bool reservesFullUtilityClearances =
            utilitySource.Contains("newCityUrbanUtilityKind(\"salvage\",.30f)", StringComparison.Ordinal) &&
            utilitySource.Contains("newCityUrbanUtilityKind(\"crane\",.30f)", StringComparison.Ordinal) &&
            utilitySource.Contains("newCityUrbanUtilityKind(\"badge\",.07f)", StringComparison.Ordinal);
        Require(reservesFullUtilityClearances,
            "Sourced utility clearances must reserve the full .30 salvage/crane and .07 badge world-space radii.");

        var depotApi = context.Members.OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(method => method.Identifier.ValueText == "GetDepotPosition");
        bool hasSourcedDepotApi = depotApi != null &&
            Signature(depotApi.ReturnType) == "Vector3" &&
            depotApi.ParameterList.Parameters.Count == 2 &&
            Signature(depotApi.ParameterList.Parameters[0].Type!) == "Vector3" &&
            depotApi.ParameterList.Parameters[0].Identifier.ValueText == "preferredPosition" &&
            Signature(depotApi.ParameterList.Parameters[1].Type!) == "float" &&
            depotApi.ParameterList.Parameters[1].Identifier.ValueText == "clearanceRadius" &&
            depotApi.ParameterList.Parameters[1].Default?.Value.ToString() == "1.5f" &&
            Compact(depotApi.ToString()).Contains(
                "FindDepotPosition(sourceMap,preferredPosition,clearanceRadius,roadIndex,footprintIndex,utilities)",
                StringComparison.Ordinal);
        var depotSearch = context.Members.OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(method => method.Identifier.ValueText == "FindDepotPosition");
        string depotSearchSource = depotSearch == null ? "" : Compact(depotSearch.ToString());
        bool testsAllSourcedClearances = new[]
            {
                "IsInsideWaterOrShore", "BuildingFootprintsClear", "RoadRibbonClear",
                "NearRoadEdge", "DepotOverlapsUtility"
            }.All(name => depotSearchSource.Contains(name + "(", StringComparison.Ordinal));
        var world = Type(roots, "World/CityWorld.cs", "CityWorld", "NewGaza");
        var factoryPosition = world?.Members.OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(method => method.Identifier.ValueText == "FactoryPosition");
        string factorySource = factoryPosition == null ? "" : Compact(factoryPosition.ToString());
        var buildFactory = world?.Members.OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(method => method.Identifier.ValueText == "BuildFactorySite");
        string buildFactorySource = buildFactory == null ? "" : Compact(buildFactory.ToString());
        Require(factorySource.Contains("center/=count", StringComparison.Ordinal),
            "Factory depot search must start at the average of the representative inland district centers.");
        Require(factorySource.Contains("urbanContext.GetDepotPosition(center,1.5f)",
                StringComparison.Ordinal),
            "Factory depot search must use the sourced city-context pad finder.");
        Require(factorySource.Contains("depot.x<=ShoreX(depot.z)+1.5f", StringComparison.Ordinal) &&
            factorySource.Contains("depot.y=CityGroundY", StringComparison.Ordinal),
            "Factory depot placement must stay inland of the shoreline and sit at city ground height.");
        Require(buildFactorySource.Contains(
                "factorySite=batch.Build(\"Recyclingdepot•dispatchapron\",cityRoot,depot)",
                StringComparison.Ordinal) &&
            buildFactorySource.Contains("factorySite.transform.localScale=Vector3.one*.2f",
                StringComparison.Ordinal),
            "Factory presentation scale must not scale its sourced world-position destination.");
        Require(hasSourcedDepotApi,
            "GetDepotPosition must expose the sourced depot API with its required 1.5-unit clearance.");
        Require(testsAllSourcedClearances,
            "Sourced depot search must reject water/shore, buildings, road ribbons and utility envelopes.");
    }

    private static void CheckSeaUvAndFleetScale(TypeDeclarationSyntax world,
        Dictionary<string, CompilationUnitSyntax> roots)
    {
        var addQuad = world.Members.OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(method => method.Identifier.ValueText == "AddQuad");
        bool mapsSeaDepthToShoreUv = addQuad?.DescendantNodes()
            .OfType<AssignmentExpressionSyntax>().Any(assignment =>
                assignment.Left.ToString() == "uv" &&
                assignment.Right is ConditionalExpressionSyntax mapping &&
                mapping.Condition.ToString() == "material == sea" &&
                Compact(mapping.WhenTrue.ToString()) ==
                    "new[]{Vector2.zero,Vector2.up,Vector2.one,Vector2.right}" &&
                Compact(mapping.WhenFalse.ToString()) ==
                    "new[]{newVector2(a.x,a.z),newVector2(b.x,b.z),newVector2(c.x,c.z),newVector2(d.x,d.z)}") == true;
        Require(mapsSeaDepthToShoreUv,
            "The sea mesh must map normalized west/deep-to-shore UVs for the animated sea shader while retaining world UVs on land.");

        var fleet = Type(roots, "World/CityFleet.cs", "CityFleet", "NewGaza");
        if (fleet == null) return;
        string fleetSource = Compact(fleet.ToString());
        bool scalesOnlyVehicleMeshesAndModelOffsets =
            fleetSource.Contains("privateconstfloatVehicleScale=.07f", StringComparison.Ordinal) &&
            fleetSource.Contains("root.localScale=Vector3.one*VehicleScale", StringComparison.Ordinal) &&
            fleetSource.Contains("returnmodelOffset*VehicleScale", StringComparison.Ordinal) &&
            fleetSource.Contains("vehicle.localPosition=geographicPosition+VehicleOffset(modelOffset)",
                StringComparison.Ordinal);
        bool destinationsStayInWorldSpace = fleetSource.Contains(
                "jobCenter=transform.InverseTransformPoint(worldJobCenter)", StringComparison.Ordinal) &&
            fleetSource.Contains("depot=transform.InverseTransformPoint(worldDepot)", StringComparison.Ordinal) &&
            fleetSource.Contains(
                "newVector3(jobCenter.x,jobCenter.y+VehicleOffset(newVector3(0f,.22f,0f)).y,jobCenter.z)",
                StringComparison.Ordinal);
        Require(scalesOnlyVehicleMeshesAndModelOffsets && destinationsStayInWorldSpace,
            "Fleet roots must use the .07 model scale, scale only model-authored offsets, and retain geographic work/depot/route destinations unscaled.");

        var createRoute = fleet.Members.OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(method => method.Identifier.ValueText == "CreateRoute");
        var depotApronAssignments = createRoute?.DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .Where(assignment => assignment.Left.ToString().StartsWith("route[", StringComparison.Ordinal) &&
                assignment.Right is BinaryExpressionSyntax sum &&
                sum.IsKind(SyntaxKind.AddExpression) &&
                sum.Left.ToString() == "depot" &&
                sum.Right is InvocationExpressionSyntax offset &&
                offset.Expression.ToString() == "VehicleOffset")
            .ToArray() ?? Array.Empty<AssignmentExpressionSyntax>();
        var depotApronVectors = depotApronAssignments.Select(assignment =>
        {
            var sum = (BinaryExpressionSyntax)assignment.Right;
            var offset = (InvocationExpressionSyntax)sum.Right;
            return offset.ArgumentList.Arguments.SingleOrDefault()?.Expression
                as ObjectCreationExpressionSyntax;
        }).ToArray();
        bool scalesEveryCompleteApronVector = depotApronVectors.Length == 3 &&
            depotApronVectors.All(vector => vector != null &&
                Signature(vector.Type) == "Vector3" &&
                vector.ArgumentList?.Arguments.Count == 3 &&
                vector.ArgumentList.Arguments[0].Expression.ToString() == "-1.4f" &&
                (vector.ArgumentList.Arguments[2].Expression.ToString() == "-4.5f" ||
                 vector.ArgumentList.Arguments[2].Expression.ToString() == "4.5f")) &&
            depotApronVectors.Count(vector => vector?.ArgumentList?.Arguments.Count == 3 &&
                vector.ArgumentList.Arguments[2].Expression.ToString() == "-4.5f") == 2 &&
            depotApronVectors.Count(vector => vector?.ArgumentList?.Arguments.Count == 3 &&
                vector.ArgumentList.Arguments[2].Expression.ToString() == "4.5f") == 1;
        Require(scalesEveryCompleteApronVector,
            "All three depot-relative CreateRoute apron endpoints must pass their complete ±4.5 m Vector3 through VehicleOffset.");
        Require(4.71f * .07f + .24f < 1.5f,
            "The scaled 4.71 m apron offset plus the .24 m vehicle envelope must fit inside the 1.5 m depot-pad clearance.");
    }

    private static void CheckBasemapSourceArchive(string sourceRoot)
    {
        DirectoryInfo? assetsDirectory = Directory.GetParent(sourceRoot);
        DirectoryInfo? projectDirectory = assetsDirectory?.Parent;
        if (projectDirectory == null) return;
        string mapDataDirectory = Path.Combine(projectDirectory.FullName, "MapData");
        string metadataPath = Path.Combine(mapDataDirectory, "GazaBasemap.metadata.json");
        if (!File.Exists(metadataPath)) return;

        using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(metadataPath)))
        {
            JsonElement metadata = document.RootElement;
            string sourceHash = "";
            foreach (string name in new[]
                {
                    "sourceRawSHA256", "sourceRawSha256", "sourceUncompressedSha256",
                    "rawSHA256", "rawSha256", "sourceSha256"
                })
            {
                if (metadata.TryGetProperty(name, out JsonElement hash) &&
                    hash.ValueKind == JsonValueKind.String)
                {
                    sourceHash = hash.GetString() ?? "";
                    if (sourceHash.Length > 0) break;
                }
            }
            if (sourceHash.Length == 0) return;

            string namedSource = metadata.TryGetProperty("sourceFile", out JsonElement sourceFile) &&
                sourceFile.ValueKind == JsonValueKind.String
                ? Path.GetFileName(sourceFile.GetString() ?? "") : "";
            string[] candidateNames =
            {
                "GazaBasemap-source.json.gz",
                "GazaBasemap-source.json",
                namedSource
            };
            string archivePath = candidateNames.Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => Path.Combine(mapDataDirectory, name))
                .FirstOrDefault(File.Exists) ?? "";
            // The raw archive may be excluded from a checkout; when present, verify either
            // its bytes or the decompressed source bytes against the recorded source SHA-256.
            if (archivePath.Length == 0) return;

            using (Stream file = File.OpenRead(archivePath))
            using (Stream content = archivePath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
                ? (Stream)new GZipStream(file, CompressionMode.Decompress, leaveOpen: false)
                : file)
            using (SHA256 sha = SHA256.Create())
            {
                string actualHash = BitConverter.ToString(sha.ComputeHash(content))
                    .Replace("-", "").ToLowerInvariant();
                Require(string.Equals(actualHash, sourceHash, StringComparison.OrdinalIgnoreCase),
                    "GazaBasemap source archive must match its metadata RawSHA-256 after gzip decompression.");
            }
        }
    }

    private static bool AccumulateFeaturePoints(JsonElement features, string pointProperty,
        bool includeCenter, ref double minX, ref double maxX, ref double minZ, ref double maxZ)
    {
        bool foundPoint = false;
        foreach (JsonElement feature in features.EnumerateArray())
        {
            if (includeCenter && feature.TryGetProperty("center", out JsonElement center))
            {
                if (!AccumulatePoint(center, ref minX, ref maxX, ref minZ, ref maxZ)) return false;
                foundPoint = true;
            }
            if (!feature.TryGetProperty(pointProperty, out JsonElement points) ||
                points.ValueKind != JsonValueKind.Array) return false;
            foreach (JsonElement point in points.EnumerateArray())
            {
                if (!AccumulatePoint(point, ref minX, ref maxX, ref minZ, ref maxZ)) return false;
                foundPoint = true;
            }
        }
        return foundPoint;
    }

    private static bool AccumulatePoint(JsonElement point, ref double minX, ref double maxX,
        ref double minZ, ref double maxZ)
    {
        if (point.ValueKind != JsonValueKind.Object ||
            !point.TryGetProperty("x", out JsonElement x) ||
            !point.TryGetProperty("z", out JsonElement z) ||
            x.ValueKind != JsonValueKind.Number || z.ValueKind != JsonValueKind.Number)
            return false;
        double px = x.GetDouble(), pz = z.GetDouble();
        if (double.IsNaN(px) || double.IsInfinity(px) || double.IsNaN(pz) || double.IsInfinity(pz))
            return false;
        minX = Math.Min(minX, px);
        maxX = Math.Max(maxX, px);
        minZ = Math.Min(minZ, pz);
        maxZ = Math.Max(maxZ, pz);
        return true;
    }

    private static void CheckHudIconContracts(Dictionary<string, CompilationUnitSyntax> roots)
    {
        var icons = Type(roots, "UI/CityHudIcons.cs", "CityHudIcons", "NewGaza.UI");
        if (icons == null) return;

        Require(icons.BaseList?.Types.Any(t => Signature(t.Type) == "IDisposable") == true,
            "CityHudIcons must implement IDisposable.");

        var iconEnum = icons.Members.OfType<EnumDeclarationSyntax>()
            .FirstOrDefault(e => e.Identifier.ValueText == "Icon");
        string[] expectedIcons =
        {
            "None", "Map", "Projects", "Fleet", "Investment", "Resources",
            "Gift", "Settings", "Close"
        };
        Require(iconEnum != null &&
            iconEnum.Members.Select(m => m.Identifier.ValueText)
                .SequenceEqual(expectedIcons, StringComparer.Ordinal),
            "CityHudIcons.Icon must contain None and the eight supported HUD icons.");
        Method(icons, "Get", "Sprite", "Icon");

        var dispose = icons.Members.OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m => m.Identifier.ValueText == "Dispose");
        Require(dispose != null && Public(dispose) &&
            Signature(dispose.ReturnType) == "void" &&
            dispose.ParameterList.Parameters.Count == 0,
            "CityHudIcons must expose public void Dispose().");
        if (dispose != null)
        {
            string[] destroyedArrays = dispose.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Where(call => call.Expression is MemberAccessExpressionSyntax destroy &&
                    destroy.Expression.ToString() == "UnityEngine.Object" &&
                    destroy.Name.Identifier.ValueText == "Destroy")
                .Select(call => call.ArgumentList.Arguments.SingleOrDefault()?.Expression)
                .OfType<ElementAccessExpressionSyntax>()
                .Select(element => element.Expression.ToString())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            Require(destroyedArrays.SequenceEqual(new[] { "sprites", "textures" },
                    StringComparer.Ordinal),
                "CityHudIcons.Dispose must destroy both cached sprites and their textures.");
        }

        var hud = Type(roots, "UI/CityHud.cs", "CityHud", "NewGaza");
        if (hud == null) return;
        var ownsIcons = hud.Members.OfType<FieldDeclarationSyntax>()
            .Any(field => Signature(field.Declaration.Type) == "CityHudIcons" &&
                field.Declaration.Variables.Any(variable => variable.Identifier.ValueText == "hudIcons"));
        Require(ownsIcons, "CityHud must own its CityHudIcons instance.");

        var initialize = hud.Members.OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m => m.Identifier.ValueText == "Initialize");
        bool createsOwnedIcons = initialize?.DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .Any(assignment => assignment.Left.ToString() == "hudIcons" &&
                assignment.Right is ObjectCreationExpressionSyntax creation &&
                Signature(creation.Type) == "CityHudIcons") == true;
        Require(createsOwnedIcons, "CityHud.Initialize must create its owned CityHudIcons instance.");

        var onDestroy = hud.Members.OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m => m.Identifier.ValueText == "OnDestroy");
        bool disposesOwnedIcons = onDestroy?.DescendantNodes()
            .OfType<ConditionalAccessExpressionSyntax>()
            .Any(access => access.Expression.ToString() == "hudIcons" &&
                access.WhenNotNull is InvocationExpressionSyntax invocation &&
                invocation.Expression is MemberBindingExpressionSyntax binding &&
                binding.Name.Identifier.ValueText == "Dispose" &&
                invocation.ArgumentList.Arguments.Count == 0) == true;
        Require(disposesOwnedIcons, "CityHud.OnDestroy must dispose its owned CityHudIcons instance.");

        var actionButton = hud.Members.OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m => m.Identifier.ValueText == "ActionButton");
        bool usesCachedIcon = actionButton?.DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .Any(assignment => assignment.Left.ToString() == "image.sprite" &&
                assignment.Right is InvocationExpressionSyntax get &&
                get.Expression is MemberAccessExpressionSyntax member &&
                member.Expression.ToString() == "hudIcons" &&
                member.Name.Identifier.ValueText == "Get" &&
                get.ArgumentList.Arguments.SingleOrDefault()?.Expression.ToString() == "icon") == true;
        bool iconDoesNotInterceptTouches = actionButton?.DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .Any(assignment => assignment.Left.ToString() == "image.raycastTarget" &&
                assignment.Right.IsKind(SyntaxKind.FalseLiteralExpression)) == true;
        Require(usesCachedIcon && iconDoesNotInterceptTouches,
            "CityHud.ActionButton must use the cached icon sprite and keep its decoration image non-interactive.");

        var buildHud = hud.Members.OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m => m.Identifier.ValueText == "BuildHud");
        string[] expectedNavigationIcons =
        {
            "CityHudIcons.Icon.Map", "CityHudIcons.Icon.Projects", "CityHudIcons.Icon.Fleet",
            "CityHudIcons.Icon.Investment", "CityHudIcons.Icon.Resources"
        };
        string[] navigationIcons = buildHud?.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(call => call.Expression is IdentifierNameSyntax identifier &&
                identifier.Identifier.ValueText == "AddNav")
            .Select(call => call.ArgumentList.Arguments.Count == 3
                ? call.ArgumentList.Arguments[2].Expression.ToString()
                : "")
            .ToArray() ?? Array.Empty<string>();
        Require(navigationIcons.SequenceEqual(expectedNavigationIcons, StringComparer.Ordinal),
            "CityHud.BuildHud must explicitly provide an icon for each of the five navigation items.");

        bool hasActionIcon(string methodName, string expectedIcon)
        {
            var method = hud.Members.OfType<MethodDeclarationSyntax>()
                .FirstOrDefault(m => m.Identifier.ValueText == methodName);
            return method?.DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Any(call => call.Expression is IdentifierNameSyntax identifier &&
                    identifier.Identifier.ValueText == "ActionButton" &&
                    call.ArgumentList.Arguments.Count >= 5 &&
                    call.ArgumentList.Arguments[4].Expression.ToString() == expectedIcon) == true;
        }
        Require(hasActionIcon("BuildHud", "CityHudIcons.Icon.Gift") &&
            hasActionIcon("BuildHud", "CityHudIcons.Icon.Settings") &&
            hasActionIcon("BuildModalShell", "CityHudIcons.Icon.Close"),
            "Gift, settings, and modal-close buttons must explicitly select their HUD icons.");
    }

    private static void CheckFogStateBindings(TypeDeclarationSyntax? city)
    {
        if (city == null) return;
        var build = city.Members.OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m => m.Identifier.ValueText == "BuildDistricts");
        Require(build != null, "CityWorld: missing district construction/fog initialization.");
        if (build == null) return;

        VariableDeclaratorSyntax? initialFog = build.DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .FirstOrDefault(v => v.Identifier.ValueText == "initiallyFogged");
        ExpressionSyntax? initialValue = initialFog?.Initializer?.Value;
        bool readsSavedLockState = initialValue is BinaryExpressionSyntax conjunction &&
            conjunction.IsKind(SyntaxKind.LogicalAndExpression) &&
            conjunction.Left.ToString().Contains("savedDistrict != null", StringComparison.Ordinal) &&
            conjunction.Right is PrefixUnaryExpressionSyntax negation &&
            negation.IsKind(SyntaxKind.LogicalNotExpression) &&
            negation.Operand is MemberAccessExpressionSyntax unlocked &&
            unlocked.Name.Identifier.ValueText == "unlocked";
        Require(readsSavedLockState,
            "Initial fog must reflect the saved district's locked state, not readiness or construction progress.");

        bool initializesFogWithInitialState = build.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Any(call => call.Expression is MemberAccessExpressionSyntax member &&
                member.Name.Identifier.ValueText == "Initialize" &&
                member.Expression.ToString() == "district.fog" &&
                call.ArgumentList.Arguments.Any(argument =>
                    argument.Expression.ToString() == "initiallyFogged"));
        Require(initializesFogWithInitialState,
            "DistrictFog.Initialize must receive the saved lock-state visibility.");

        bool storesInitialFogState = build.DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .Any(assignment => assignment.Left.ToString() == "district.fogged" &&
                assignment.Right.ToString() == "initiallyFogged");
        Require(storesInitialFogState,
            "CityWorld must bind its initial fog query state to the same saved lock state.");

        var refresh = city.Members.OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m => m.Identifier.ValueText == "Refresh");
        Require(refresh != null, "CityWorld: missing district refresh for fog visibility.");
        if (refresh == null) return;

        bool derivesVisibilityFromUnlockOnly = refresh.DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .Any(assignment => assignment.Left.ToString() == "view.fogged" &&
                assignment.Right is PrefixUnaryExpressionSyntax hidden &&
                hidden.IsKind(SyntaxKind.LogicalNotExpression) &&
                hidden.Operand is MemberAccessExpressionSyntax unlocked &&
                unlocked.Expression.ToString() == "district" &&
                unlocked.Name.Identifier.ValueText == "unlocked");
        Require(derivesVisibilityFromUnlockOnly,
            "Refreshed fog must track district.unlocked only; accessible unfinished buildings stay clear.");

        bool sendsVisibilityToFog = refresh.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Any(call => call.Expression is MemberAccessExpressionSyntax show &&
                show.Expression.ToString() == "view.fog" &&
                show.Name.Identifier.ValueText == "Show" &&
                call.ArgumentList.Arguments.SingleOrDefault()?.Expression.ToString() == "view.fogged");
        Require(sendsVisibilityToFog,
            "CityWorld.Refresh must send the district's lock-state visibility to DistrictFog.Show.");

        var initialize = city.Members.OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m => m.Identifier.ValueText == "Initialize");
        if (initialize == null)
        {
            Require(false, "CityWorld: missing initialization for initial fog refresh.");
            return;
        }
        bool refreshesOnSessionChanges = initialize.DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .Any(assignment => assignment.IsKind(SyntaxKind.AddAssignmentExpression) &&
                assignment.Left.ToString() == "session.Changed" &&
                assignment.Right.ToString() == "Refresh");
        Require(refreshesOnSessionChanges,
            "CityWorld must refresh fog visibility when session district state changes.");

        var directCalls = initialize.Body?.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(call => call.Expression is IdentifierNameSyntax identifier &&
                (identifier.Identifier.ValueText == "BuildDistricts" ||
                 identifier.Identifier.ValueText == "Refresh"))
            .ToArray() ?? Array.Empty<InvocationExpressionSyntax>();
        int buildOrder = Array.FindIndex(directCalls, call =>
            ((IdentifierNameSyntax)call.Expression).Identifier.ValueText == "BuildDistricts");
        int refreshOrder = Array.FindIndex(directCalls, call =>
            ((IdentifierNameSyntax)call.Expression).Identifier.ValueText == "Refresh");
        Require(buildOrder >= 0 && refreshOrder > buildOrder,
            "CityWorld.Initialize must refresh fog after district geometry and fog have been created.");
    }

    private static void CheckNoFogColliders(TypeDeclarationSyntax? fog)
    {
        if (fog == null) return;
        bool hasColliderType = fog.DescendantNodes().OfType<TypeSyntax>().Any(type =>
            Signature(type).EndsWith("Collider", StringComparison.Ordinal));
        Require(!hasColliderType, "DistrictFog must remain collider-free.");
    }

    private static void CheckFogShader(string sourceRoot,
        Dictionary<string, CompilationUnitSyntax> roots)
    {
        string path = Path.Combine(sourceRoot, "Resources", "NewGazaFog.shader");
        Require(File.Exists(path), "Missing Resources/NewGazaFog.shader fog resource.");
        if (!File.Exists(path)) return;

        string shader = Compact(File.ReadAllText(path));
        Require(shader.Contains("Shader\"NewGaza/SoftDistrictFog\"", StringComparison.Ordinal),
            "Fog resource must declare the expected NewGaza/Soft District Fog shader.");
        Require(shader.Contains("\"RenderPipeline\"=\"UniversalPipeline\"", StringComparison.Ordinal) &&
            shader.Contains("Pass{Name\"SoftDistrictFog\"Tags{\"LightMode\"=\"UniversalForward\"}",
                StringComparison.Ordinal) &&
            shader.Contains("\"LightMode\"=\"UniversalForward\"", StringComparison.Ordinal) &&
            shader.Contains("#include\"Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl\"",
                StringComparison.Ordinal),
            "Fog shader source must target URP and include its Core shader library.");
        Require(shader.Contains("BlendSrcAlphaOneMinusSrcAlpha", StringComparison.Ordinal) &&
            shader.Contains("ZTestLEqual", StringComparison.Ordinal) &&
            shader.Contains("ZWriteOff", StringComparison.Ordinal) &&
            shader.Contains("CullOff", StringComparison.Ordinal),
            "Fog shader source must use transparent alpha blending, scene depth testing, no depth writes, and two-sided rendering.");
        Require(shader.Contains("halfradialDistance=length(input.uv*2.0h-1.0h);", StringComparison.Ordinal) &&
            shader.Contains("halfradialMask=1.0h-smoothstep(_Softness,1.0h,radialDistance);",
                StringComparison.Ordinal) &&
            shader.Contains("halfalpha=saturate(input.color.a*_FogOpacity*radialMask);",
                StringComparison.Ordinal),
            "Fog shader source must apply a soft radial falloff and per-mesh opacity.");

        bool cityLoadsResource = roots.TryGetValue("World/CityWorld.cs", out CompilationUnitSyntax? cityRoot) &&
            cityRoot.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(call =>
                call.Expression is MemberAccessExpressionSyntax member &&
                member.Expression.ToString() == "Resources" &&
                member.Name is GenericNameSyntax generic &&
                generic.Identifier.ValueText == "Load" &&
                generic.TypeArgumentList.Arguments.SingleOrDefault()?.ToString() == "Shader" &&
                call.ArgumentList.Arguments.SingleOrDefault()?.Expression.ToString() == "\"NewGazaFog\"");
        Require(cityLoadsResource,
            "CityWorld must load the included Resources/NewGazaFog shader resource.");
    }

    private static void CheckNoGlobalDistanceFog(string sourceRoot,
        Dictionary<string, CompilationUnitSyntax> roots)
    {
        var globalFogAssignments = roots.Values
            .SelectMany(root => root.DescendantNodes().OfType<AssignmentExpressionSyntax>())
            .Where(assignment => assignment.Left is MemberAccessExpressionSyntax member &&
                member.Expression.ToString() == "RenderSettings" &&
                member.Name.Identifier.ValueText == "fog")
            .ToArray();
        Require(globalFogAssignments.Any(assignment =>
                assignment.Right.IsKind(SyntaxKind.FalseLiteralExpression)),
            "Runtime must explicitly disable global RenderSettings.fog.");
        Require(globalFogAssignments.All(assignment =>
                assignment.Right.IsKind(SyntaxKind.FalseLiteralExpression)),
            "Localized district fog must not enable global distance fog.");

        string scenePath = Path.Combine(sourceRoot, "Scenes", "NewGaza.unity");
        string scene = File.ReadAllText(scenePath);
        Require(scene.Contains("m_Fog: 0", StringComparison.Ordinal),
            "The entry scene must keep serialized global RenderSettings fog disabled.");
    }

    private static string Compact(string source) =>
        new string(source.Where(c => !char.IsWhiteSpace(c)).ToArray());

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