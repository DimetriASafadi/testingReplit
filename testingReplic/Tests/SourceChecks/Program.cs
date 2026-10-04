#nullable enable
using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.IO;
using System.Globalization;
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
            CheckPointerProjection(roots);
            CheckZoomResponse(roots);
            CheckNativeWindowLayout(roots);
            Failures.AddRange(EditorCompilationChecks.Check(sourceRoot));
            CheckSceneBootstrap(sourceRoot, roots);
            CheckDistrictFog(sourceRoot, roots);
            CheckWorldRenderingContracts(roots);
            CheckImportedCityModelContracts(sourceRoot, roots);
            CheckSourcedCityContext(sourceRoot, roots);
            CheckHudIconContracts(roots);
            CheckAudioContracts(sourceRoot, roots);
            CheckEquipmentContracts(roots);
            CheckRoadContracts(roots);
            CheckArabicShaping();
            if (Failures.Count > 0)
            {
                Console.Error.WriteLine("Source checks FAILED (" + Failures.Count + "):");
                foreach (string failure in Failures) Console.Error.WriteLine("  - " + failure);
                return 1;
            }
            Console.WriteLine("Source checks passed: " + files.Length +
                " NewGaza .cs files parsed as C# 9 (player + editor/optional checks), integration signatures, GUID-independent scene bootstrap, sourced Gaza basemap/urban batching, five preserved imported city models plus 21 destroyed stage-zero resources, localized-fog/shader and geometry-UV/shadow/URP-light checks, procedural HUD icon coverage/ownership/navigation bindings, native audio wiring/assets/preferences, equipment articulation contracts, and Arabic shaping.");
            Console.WriteLine("Source-only check; Unity assemblies and shader were not compiled, and no Unity editor/player or GPU rendering was run.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("Source checks could not run: " + exception);
            return 1;
        }
    }

    private static void CheckRoadContracts(Dictionary<string, CompilationUnitSyntax> roots)
    {
        string Source(string path)
        {
            Require(roots.ContainsKey(path), "Missing native road integration source: " + path);
            return roots.TryGetValue(path, out var root) ? root.ToString() : "";
        }
        string camera = Source("Runtime/CityCamera.cs");
        string session = Source("Runtime/GameSession.cs");
        string world = Source("World/CityWorld.cs");
        string hud = Source("UI/CityHudRoad.cs");
        string economy = Source("Core/EconomyService.cs");
        string view = Source("World/CityRoadView.cs");
        Require(camera.Contains("TryPick") && camera.Contains("SelectRoad"),
            "Street taps must pick sourced street geometry and open road context without payment.");
        Require(world.Contains("RegisterRoadSegments(Roads.Definitions)") &&
            world.Contains("ConfigureRoads(Roads") && world.Contains("roadView?.Refresh"),
            "World must register authoritative segments, render saved levels and configure actual road transport.");
        Require(world.Contains("ConfigureTravelSurface") && world.Contains("RoadIdUnder(point)") &&
            world.Contains("GetLevel(session.State, roadId) == 2"),
            "Fleet dust surface classification must use the actual nearby road and saved level-two paving.");
        string network = Source("World/CityRoadNetwork.cs");
        string fleet = Source("World/CityFleet.cs");
        Require(network.Contains("FindRoute(Vector3 from, Vector3 to") &&
            network.Contains("FindEquipmentRoute") && network.Contains("FindRouteWithOffRoadAccess") &&
            network.Contains("RoadIdUnder(Vector3 position)"),
            "Strict road routing remains separate from fleet off-road access and paving lookup APIs.");
        Require(fleet.Contains("FindEquipmentRoute") && fleet.Contains("roadTripRoute.RoadIdAtDistance") &&
            fleet.Contains("RoadIdAtDistance(distance)"),
            "Trucks and tracked equipment must share road-first routing with live segment-speed provenance.");
        Require(session.Contains("Perform(e => e.ImproveRoad(id, targetLevel))") &&
            hud.Contains("Confirm(title") && hud.Contains("session.ImproveRoad(definition.id, target)"),
            "Road spending must use explicit confirmation and the save/error-aware session action path.");
        Require(economy.Contains("ImproveRoad") && economy.Contains("roadDefinitions"),
            "Road upgrades must be validated against registered definitions in the economy.");
        Require(view.Contains("renderedLevels") && view.Contains("BuildSelection") &&
            view.Contains("asphalt") && view.Contains("whiteLine") && view.Contains("sidewalk"),
            "Saved road levels need cached native surfaces, paving details and segment highlights.");
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
        string[] baselineAssetKeys =
        {
            "apartment", "ruined_building", "rubble_heap",
            "apartment_context", "ruined_building_context",
            "ruin_shujaiya", "ruin_tuffah", "ruin_sheikh_radwan", "ruin_daraj",
            "ruin_karama", "ruin_old_city", "ruin_nasr", "ruin_sabra",
            "ruin_zeitoun", "ruin_rimal", "ruin_tel_al_hawa", "ruin_sheikh_ijlin",
            "ruin_rashid", "ruin_mosque", "ruin_school", "ruin_clinic", "ruin_civic",
            "ruin_wall", "ruin_car", "ruin_crater", "ruin_debris"
        };
        string[] housingMasters =
        {
            "house_small_redtile", "house_cream_family", "house_modern_villa",
            "apartment_4floor_balcony", "apartment_6floor_balcony", "house_compound",
            "apartment_blueglass_midrise", "house_traditional_stonearches",
            "apartment_12floor_tower", "house_coastal_white_pool"
        };
        string[] housingSuffixes = { "_foundation", "_frame", "_finishing", "_final" };
        string[] housingModelKeys = housingMasters
            .SelectMany(master => housingSuffixes.Select(suffix => master + suffix)).ToArray();
        string[] modelKeys = baselineAssetKeys.Concat(housingModelKeys).ToArray();
        string[] primaryModelKeys = { "apartment", "ruined_building", "rubble_heap" };
        string[] baselineModelKeys =
        {
            "apartment", "ruined_building", "rubble_heap",
            "apartment_context", "ruined_building_context"
        };
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
        foreach (string key in housingModelKeys)
        {
            Require(File.Exists(Path.Combine(modelsDirectory, key + ".obj")),
                "Missing housing-phase OBJ source Resources/Models/" + key + ".obj.");
            Require(File.Exists(Path.Combine(modelsDirectory, key + "_albedo.png")),
                "Missing housing-phase albedo Resources/Models/" + key + "_albedo.png.");
            Require(File.Exists(Path.Combine(modelsDirectory, key + "_manifest.json")),
                "Missing housing-phase dimension manifest Resources/Models/" + key + "_manifest.json.");
        }
        CheckHousingStageManifests(sourceRoot, housingMasters, housingSuffixes, roots);
        string[] destroyedKeys = modelKeys.Where(key => key.StartsWith("ruin_", StringComparison.Ordinal))
            .ToArray();
        foreach (string key in destroyedKeys)
        {
            Require(File.Exists(Path.Combine(modelsDirectory, key + "_albedo.png")),
                "Missing destroyed-model atlas Resources/Models/" + key + "_albedo.png.");
            Require(File.Exists(Path.Combine(modelsDirectory, key + "_manifest.json")),
                "Missing destroyed-model manifest Resources/Models/" + key + "_manifest.json.");
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
            foreach (string key in baselineAssetKeys)
                Require(settings.Contains("ModelsPrefix+\"" + key + ".obj\"", StringComparison.Ordinal),
                    "The scoped preparation menu is missing the fixed OBJ path for " + key + ".");
            foreach (string key in primaryModelKeys)
                Require(settings.Contains("ModelsPrefix+\"" + key + "_albedo.png\"", StringComparison.Ordinal),
                    "The scoped preparation menu is missing the primary albedo path for " + key + ".");
            foreach (string key in destroyedKeys)
                Require(settings.Contains("\"" + key + "\"", StringComparison.Ordinal),
                    "The scoped preparation menu is missing destroyed OBJ/albedo import coverage for " + key + ".");
            Require(settings.Contains("CityHousingProfiles.ModelKeys", StringComparison.Ordinal) &&
                settings.Contains("PrepareModel(ModelsPrefix+key+\".obj\")", StringComparison.Ordinal) &&
                settings.Contains("PrepareTexture(ModelsPrefix+key+\"_albedo.png\")",
                    StringComparison.Ordinal),
                "The preparation menu must apply readable OBJ and albedo settings to all registered housing phase assets.");
        }

        var library = Type(roots, "World/CityModelLibrary.cs", "CityModelLibrary", "NewGaza");
        if (library != null)
        {
            string source = Compact(library.ToString());
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
            Require(new[] { "apartment", "ruined_building", "rubble_heap",
                    "apartment_context", "ruined_building_context" }
                    .All(key => loadedModels.Contains(key, StringComparer.Ordinal)),
                "CityModelLibrary must explicitly load all three baseline models and both context LOD models.");
            string constructorSource = Compact(constructor?.ToString() ?? "");
            Require(constructorSource.Contains("CityRuinProfiles.ModelKeys", StringComparison.Ordinal) &&
                constructorSource.Contains("CityHousingProfiles.ModelKeys", StringComparison.Ordinal) &&
                constructorSource.Contains("Load(key)", StringComparison.Ordinal),
                "CityModelLibrary must fail fast by loading all housing-phase, regional and damage-detail resources.");

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
            bool reusesCanonicalHousingAtlas = loadMethod?.DescendantNodes()
                .OfType<LiteralExpressionSyntax>()
                .Where(literal => literal.IsKind(SyntaxKind.StringLiteralExpression))
                .Select(literal => literal.Token.ValueText)
                .Contains("Models/house_small_redtile_final", StringComparer.Ordinal) == true &&
                source.Contains("housingStageKeys.Contains(key)", StringComparison.Ordinal);
            Require(loadsModelResources && loadsAliasedTexture && reusesPrimaryAlbedoAliases &&
                reusesCanonicalHousingAtlas,
                "Imported OBJ resources must preserve context aliases and explicitly share one canonical housing atlas for all registered phase keys.");

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
            replaceSource.Contains("CityRuinProfiles.StageZeroModel(districtId,plot.definition.id)",
                StringComparison.Ordinal) &&
            replaceSource.Contains("CityRuinProfiles.ForDistrict(districtId)", StringComparison.Ordinal) &&
            replaceSource.Contains("CityRuinProfiles.StageZeroDetail(districtId,index)",
                StringComparison.Ordinal) &&
            replaceSource.Contains("ruinProfile.maxHeightCityUnits", StringComparison.Ordinal) &&
            !replaceSource.Contains("Mathf.Min(footprint.x,footprint.z)*", StringComparison.Ordinal) &&
            replaceSource.Contains("plot.definition.kind==ProjectKind.Housing", StringComparison.Ordinal) &&
            replaceSource.Contains("AddHousingModel(batch,district,footprint", StringComparison.Ordinal) &&
            replaceSource.Contains("CityConstructionPhase.Complete", StringComparison.Ordinal),
            "Stage-zero plots must remain stable district-specific damaged resources, while housing uses district profiles and phase OBJ models.");
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
                cameraSource.Contains("Mathf.Clamp(targetZoom*Mathf.Pow(lastSpan/span,PinchZoomExponent),0.6f", StringComparison.Ordinal) &&
                cameraSource.Contains("Mathf.Clamp(targetZoom*Mathf.Exp(-scroll*WheelZoomSensitivity),0.6f",
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
                smoke.Contains("Models/house_small_redtile_final_albedo", StringComparison.Ordinal) &&
                smoke.Contains("world.ImportedModelCount==5+CityHousingProfiles.ModelKeys.Length+CityRuinProfiles.ModelKeys.Length",
                    StringComparison.Ordinal) &&
                smoke.Contains("CityRuinProfiles.ModelKeys", StringComparison.Ordinal) &&
                smoke.Contains("CheckDestroyedModelResources(destroyedAlbedos)", StringComparison.Ordinal) &&
                smoke.Contains("albedos[key]=albedo", StringComparison.Ordinal) &&
                smoke.Contains("uvs.Length==vertices.Length&&normals.Length==vertices.Length",
                    StringComparison.Ordinal) &&
                smoke.Contains("albedos[3]==albedos[0]&&albedos[4]==albedos[1]",
                    StringComparison.Ordinal) &&
                smoke.Contains("lit.shader.name==\"UniversalRenderPipeline/Lit\"",
                    StringComparison.Ordinal) &&
                smoke.Contains("mesh.isReadable", StringComparison.Ordinal) &&
                smoke.Contains("uvs.Length==vertices.Length", StringComparison.Ordinal) &&
                smoke.Contains("normals.Length==vertices.Length", StringComparison.Ordinal),
                "Unity smoke test must retain all five baseline model checks and inspect all 21 destroyed model/atlas resources, geometry, aliased context albedos and imported normals.");
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
        string districtBuild = Compact(world.Members.OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(method => method.Identifier.ValueText == "BuildDistricts")?.ToString() ?? "");
        Require(districtBuild.Contains("Point(route[route.Length-1])-routeMidpoint", StringComparison.Ordinal) &&
            !districtBuild.Contains("Point(routeMidpoint)", StringComparison.Ordinal),
            "Rashid badge offset must subtract the already-converted Vector3 route midpoint, not pass it to Point(GeoPoint).");
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
        float factoryApronScale = .2f;
        float sidewalkTopCityUnits = (.004f + .012f * .5f) * factoryApronScale;
        float asphaltTopCityUnits = (.012f + .006f * .5f) * factoryApronScale;
        Require(buildFactorySource.Contains(
                "batch.Box(sidewalk,newVector3(0f,.004f,0f),newVector3(7.6f,.012f,12f))",
                StringComparison.Ordinal) &&
            buildFactorySource.Contains(
                "batch.Box(asphalt,newVector3(-1.4f,.012f,0f),newVector3(2.4f,.006f,11f))",
                StringComparison.Ordinal) &&
            buildFactorySource.Contains("factorySite.transform.localScale=Vector3.one*.2f",
                StringComparison.Ordinal) &&
            sidewalkTopCityUnits <= .004f && asphaltTopCityUnits <= .004f,
            "Both factory-site native-grade apron slabs must remain at or below .004 city units after the authored .2 child scale.");
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
                "nextJobCenter=transform.InverseTransformPoint(worldJobCenter)", StringComparison.Ordinal) &&
            fleetSource.Contains("nextDepot=transform.InverseTransformPoint(worldDepot)",
                StringComparison.Ordinal) &&
            fleetSource.Contains(
                "newVector3(jobCenter.x,jobCenter.y+VehicleOffset(newVector3(0f,EquipmentMotion.TruckWorkRootHeightModel,0f)).y,jobCenter.z)",
                StringComparison.Ordinal);
        Require(scalesOnlyVehicleMeshesAndModelOffsets && destinationsStayInWorldSpace,
            "Fleet roots must use the .07 model scale, scale only model-authored offsets, and retain geographic work/depot/route destinations unscaled.");
        var groundedOffsets = fleet.Members.OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(method => method.Identifier.ValueText == "ConfigureGroundedVehicleOffsets");
        string groundedOffsetSource = groundedOffsets == null ? "" : Compact(groundedOffsets.ToString());
        Require(fleetSource.Contains("NativeWorkSurfaceAboveGroundCityUnits=.006f",
                StringComparison.Ordinal) &&
            fleetSource.Contains("DepotApronAboveGroundCityUnits=.003f", StringComparison.Ordinal) &&
            groundedOffsetSource.Contains("excavatorTracks.LowestShoeVertexYModel", StringComparison.Ordinal) &&
            groundedOffsetSource.Contains("bulldozerTracks.LowestShoeVertexYModel", StringComparison.Ordinal) &&
            groundedOffsetSource.Contains("FindLowestTruckWheelMeshYModel()", StringComparison.Ordinal) &&
            groundedOffsetSource.Contains("EquipmentMotion.ConfigureGroundedRootHeights", StringComparison.Ordinal),
            "Fleet ground roots must derive park/work/depot heights from actual authored shoe and tire mesh bottoms against native work/depot surfaces.");

        var createRoute = fleet.Members.OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(method => method.Identifier.ValueText == "CreateRoute");
        var depotApronSums = createRoute?.DescendantNodes()
            .OfType<BinaryExpressionSyntax>()
            .Where(sum => sum.IsKind(SyntaxKind.AddExpression) &&
                sum.Left.ToString() == "depot" &&
                sum.Right is InvocationExpressionSyntax offset &&
                offset.Expression.ToString() == "VehicleOffset")
            .ToArray() ?? Array.Empty<BinaryExpressionSyntax>();
        var depotApronVectors = depotApronSums.Select(sum =>
        {
            var offset = (InvocationExpressionSyntax)sum.Right;
            return offset.ArgumentList.Arguments.SingleOrDefault()?.Expression
                as ObjectCreationExpressionSyntax;
        }).Where(vector => vector?.ArgumentList?.Arguments.Count == 3 &&
            vector.ArgumentList.Arguments[0].Expression.ToString() == "-1.4f").ToArray();
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

    private static void CheckAudioContracts(string sourceRoot,
        Dictionary<string, CompilationUnitSyntax> roots)
    {
        var session = Type(roots, "Runtime/GameSession.cs", "GameSession", "NewGaza");
        var start = session?.Members.OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(method => method.Identifier.ValueText == "Start");
        Require(start != null, "GameSession: missing runtime startup for audio wiring.");
        if (start != null)
        {
            string[] requiredCalls =
            {
                "world.Initialize", "cityCamera.Initialize", "Audio.Initialize", "hud.Initialize"
            };
            string[] startupCalls = start.DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Select(call => call.Expression.ToString())
                .Where(name => requiredCalls.Contains(name, StringComparer.Ordinal))
                .ToArray();
            Require(startupCalls.SequenceEqual(requiredCalls, StringComparer.Ordinal),
                "GameSession must initialize the world, eased camera, camera-focused audio, then HUD in that order.");
            bool listenerIsOnExistingCamera = start.DescendantNodes()
                .OfType<ObjectCreationExpressionSyntax>()
                .Any(creation => Signature(creation.Type) == "GameObject" &&
                    creation.ArgumentList?.Arguments.Any(argument =>
                        argument.Expression is TypeOfExpressionSyntax type &&
                        Signature(type.Type) == "AudioListener") == true &&
                    creation.ArgumentList.Arguments.Any(argument =>
                        argument.Expression is TypeOfExpressionSyntax type &&
                        Signature(type.Type) == "Camera"));
            Require(listenerIsOnExistingCamera &&
                !start.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(call =>
                    call.Expression is MemberAccessExpressionSyntax add &&
                    add.Name.Identifier.ValueText == "AddComponent" &&
                    add.Expression.ToString() == "gameObject" &&
                    call.ArgumentList.Arguments.SingleOrDefault()?.Expression.ToString() == "AudioListener"),
                "Startup must keep its sole AudioListener on the rendering camera and must not create a second listener.");
            Require(start.DescendantNodes().OfType<AssignmentExpressionSyntax>().Any(assignment =>
                    assignment.Left.ToString() == "Audio" &&
                    assignment.Right is InvocationExpressionSyntax create &&
                    create.Expression is MemberAccessExpressionSyntax add &&
                    add.Name.Identifier.ValueText == "AddComponent" &&
                    add.Name is GenericNameSyntax generic &&
                    generic.TypeArgumentList.Arguments.SingleOrDefault()?.ToString() == "CityAudio"),
                "GameSession must own the runtime CityAudio component.");
        }

        var camera = Type(roots, "Runtime/CityCamera.cs", "CityCamera", "NewGaza");
        string cameraSource = camera == null ? "" : Compact(camera.ToString());
        Require(cameraSource.Contains("publicVector3AudioFocus{get{returnfocus;}}", StringComparison.Ordinal) &&
            cameraSource.Contains("publicfloatAudioZoom{get{returnzoom;}}", StringComparison.Ordinal) &&
            !cameraSource.Contains("AudioFocus{get{returntargetFocus;}}", StringComparison.Ordinal) &&
            !cameraSource.Contains("AudioZoom{get{returntargetZoom;}}", StringComparison.Ordinal),
            "Audio focus/zoom must expose the eased rendered camera state, not targetFocus/targetZoom.");

        var world = Type(roots, "World/CityWorld.cs", "CityWorld", "NewGaza");
        string worldSource = world == null ? "" : Compact(world.ToString());
        Require(worldSource.Contains("internalCityFleetFleet{get{returnfleet;}}", StringComparison.Ordinal) &&
            worldSource.Contains("fleet=newGameObject(\"Salvagefleet•articulatedmachines\").AddComponent<CityFleet>()",
                StringComparison.Ordinal) &&
            worldSource.Contains("fleet.Initialize(geometry,yellow,glass,dark,iron,teal,rubble)", StringComparison.Ordinal),
            "CityWorld must own and initialize its rendered CityFleet for the audio runtime.");

        var audio = Type(roots, "Runtime/CityAudio.cs", "CityAudio", "NewGaza");
        if (audio == null) return;
        var channelEnum = roots["Runtime/CityAudio.cs"].DescendantNodes()
            .OfType<EnumDeclarationSyntax>()
            .FirstOrDefault(declaration => declaration.Identifier.ValueText == "CityAudioChannel");
        Require(channelEnum != null &&
            channelEnum.Members.Select(member => member.Identifier.ValueText)
                .SequenceEqual(new[] { "Master", "Equipment", "Ambience", "Interface" },
                    StringComparer.Ordinal),
            "CityAudioChannel must keep its four stable master/equipment/ambience/interface values.");
        string[] expectedClips =
        {
            "excavator_engine", "truck_engine", "dozer_engine", "hydraulics", "tracks",
            "wind_high", "coastal_surf", "ui_click", "ui_confirm"
        };
        var initializeAudio = audio.Members.OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(method => method.Identifier.ValueText == "Initialize");
        string[] requiredLoads = initializeAudio?.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(call => call.Expression is IdentifierNameSyntax identifier &&
                identifier.Identifier.ValueText == "LoadRequired")
            .Select(call => call.ArgumentList.Arguments.SingleOrDefault()?.Expression
                .DescendantNodesAndSelf().OfType<LiteralExpressionSyntax>()
                .FirstOrDefault(literal => literal.IsKind(SyntaxKind.StringLiteralExpression))
                ?.Token.ValueText ?? "")
            .ToArray() ?? Array.Empty<string>();
        Require(requiredLoads.SequenceEqual(expectedClips, StringComparer.Ordinal),
            "CityAudio.Initialize must load each of the nine authored resource clips once, with no synthesized or silent fallback.");
        var loadRequired = audio.Members.OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(method => method.Identifier.ValueText == "LoadRequired");
        bool requiredResourceAndExplicitFailure = loadRequired != null &&
            loadRequired.DescendantNodes().OfType<InvocationExpressionSyntax>().Count(call =>
                call.Expression is MemberAccessExpressionSyntax load &&
                load.Expression.ToString() == "Resources" &&
                load.Name is GenericNameSyntax generic &&
                generic.Identifier.ValueText == "Load" &&
                generic.TypeArgumentList.Arguments.SingleOrDefault()?.ToString() == "AudioClip") == 1 &&
            loadRequired.DescendantNodes().OfType<ThrowStatementSyntax>().Any(statement =>
                statement.Expression?.ToString().Contains("InvalidOperationException", StringComparison.Ordinal) == true);
        Require(requiredResourceAndExplicitFailure,
            "A missing required audio asset must report a startup error instead of silently substituting another clip.");

        var update = audio.Members.OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(method => method.Identifier.ValueText == "Update");
        string updateSource = update == null ? "" : Compact(update.ToString());
        Require(updateSource.Contains("cityCamera.AudioFocus", StringComparison.Ordinal) &&
            updateSource.Contains("cityCamera.AudioZoom", StringComparison.Ordinal) &&
            updateSource.Contains("world.Fleet", StringComparison.Ordinal) &&
            updateSource.Contains("TryGetMachineAudioState", StringComparison.Ordinal),
            "CityAudio must mix against eased focus/zoom and the live CityWorld fleet audio state.");

        var createVoice = audio.Members.OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(method => method.Identifier.ValueText == "CreateVoice");
        var initializeVoiceCount = initializeAudio?.DescendantNodes()
            .OfType<AssignmentExpressionSyntax>().Any(assignment =>
                assignment.Left.ToString() == "voiceCount" &&
                assignment.Right.ToString() == "12") == true;
        string voiceSource = createVoice == null ? "" : Compact(createVoice.ToString());
        string initializeAudioSource = initializeAudio == null ? "" : Compact(initializeAudio.ToString());
        bool usesFlatManualSpatialRolloff = voiceSource.Contains("source.rolloffMode=AudioRolloffMode.Custom",
                StringComparison.Ordinal) &&
            voiceSource.Contains("source.SetCustomCurve(AudioSourceCurveType.CustomRolloff,flatRolloff)",
                StringComparison.Ordinal) &&
            initializeAudioSource.Contains(
                "newAnimationCurve(newKeyframe(0f,1f),newKeyframe(1f,1f))", StringComparison.Ordinal) &&
            voiceSource.Contains("source.maxDistance=CityAudioMix.MaximumSourceDistance", StringComparison.Ordinal);
        Require(initializeVoiceCount && usesFlatManualSpatialRolloff &&
            voiceSource.Contains("source.playOnAwake=false", StringComparison.Ordinal),
            "CityAudio must cap itself at twelve play-on-demand voices and use flat physical rolloff so its manual focus mix controls audibility.");
        bool boundedLoopPool = audio.Members.OfType<FieldDeclarationSyntax>().Any(field =>
                field.Declaration.Variables.Any(variable => variable.Identifier.ValueText == "loops" &&
                    variable.Initializer != null &&
                    Compact(variable.Initializer.Value.ToString()) == "newVoice[11]")) &&
            initializeAudio?.DescendantNodes().OfType<AssignmentExpressionSyntax>().Any(assignment =>
                assignment.Left.ToString() == "voiceCount" && assignment.Right.ToString() == "12") == true;
        Require(boundedLoopPool,
            "CityAudio's loop pool must remain bounded to the eleven looping voices plus one interface voice.");
        var updateLoop = audio.Members.OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(method => method.Identifier.ValueText == "UpdateLoop");
        string loopSource = updateLoop == null ? "" : Compact(updateLoop.ToString());
        var canPlayInterface = audio.Members.OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(method => method.Identifier.ValueText == "CanPlayInterface");
        string interfaceGate = canPlayInterface == null ? "" : Compact(canPlayInterface.ToString());
        var applyMute = audio.Members.OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(method => method.Identifier.ValueText == "ApplyMute");
        string muteSource = applyMute == null ? "" : Compact(applyMute.ToString());
        Require(loopSource.Contains("Volume(CityAudioChannel.Master)", StringComparison.Ordinal) &&
            loopSource.Contains("Volume(channel)", StringComparison.Ordinal) &&
            loopSource.Contains("voice.Source.volume=muted?0f:voice.Gain", StringComparison.Ordinal) &&
            interfaceGate.Contains("!muted", StringComparison.Ordinal) &&
            interfaceGate.Contains("Volume(CityAudioChannel.Master)>0f", StringComparison.Ordinal) &&
            interfaceGate.Contains("Volume(CityAudioChannel.Interface)>0f", StringComparison.Ordinal) &&
            muteSource.Contains("loops[i].Source.mute=muted", StringComparison.Ordinal) &&
            muteSource.Contains("interfaceVoice.Source.mute=muted", StringComparison.Ordinal),
            "Master/channel gains and mute must cover equipment, ambience and interface cues, including immediate silence of UI when muted.");

        var prefs = Type(roots, "Runtime/CityAudioPreferences.cs", "CityAudioPreferences", "NewGaza");
        string preferenceSource = prefs == null ? "" : Compact(prefs.ToString());
        Require(preferenceSource.Contains("Prefix=\"NewGaza.audio.v1.\"", StringComparison.Ordinal) &&
            preferenceSource.Contains("PlayerPrefs.SetFloat", StringComparison.Ordinal) &&
            preferenceSource.Contains("PlayerPrefs.SetInt", StringComparison.Ordinal) &&
            !preferenceSource.Contains("GameSaveStore", StringComparison.Ordinal) &&
            !preferenceSource.Contains("GameState", StringComparison.Ordinal),
            "Audio channel and mute preferences must use their isolated PlayerPrefs namespace, never mutate game saves/state.");

        var hudAudio = Type(roots, "UI/CityHudAudio.cs", "CityHud", "NewGaza");
        var hud = Type(roots, "UI/CityHud.cs", "CityHud", "NewGaza");
        Require(hud != null && hud.Modifiers.Any(SyntaxKind.PartialKeyword) &&
            hudAudio != null && hudAudio.Modifiers.Any(SyntaxKind.PartialKeyword),
            "CityHud audio settings must extend the original CityHud through a matching partial declaration.");
        var audioSettings = hudAudio?.Members.OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(method => method.Identifier.ValueText == "BuildAudioSettings");
        string[] settingsChannels = audioSettings?.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(call => call.Expression is IdentifierNameSyntax name &&
                name.Identifier.ValueText == "AudioVolumeRow")
            .Select(call => call.ArgumentList.Arguments.Count > 2
                ? call.ArgumentList.Arguments[2].Expression.ToString()
                : "")
            .ToArray() ?? Array.Empty<string>();
        Require(settingsChannels.SequenceEqual(new[]
                {
                    "CityAudioChannel.Master", "CityAudioChannel.Equipment",
                    "CityAudioChannel.Ambience", "CityAudioChannel.Interface"
                }, StringComparer.Ordinal),
            "The native audio settings page must expose all four master/equipment/ambience/interface channels.");
        string settingsSource = audioSettings == null ? "" : Compact(audioSettings.ToString());
        Require(settingsSource.Contains("audio.ToggleMute()", StringComparison.Ordinal) &&
            settingsSource.Contains("audio.IsMuted", StringComparison.Ordinal),
            "Audio settings must expose and reflect the global mute switch.");
        var actionButton = hud?.Members.OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(method => method.Identifier.ValueText == "ActionButton");
        var buttonListener = actionButton?.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(call => call.Expression is MemberAccessExpressionSyntax add &&
                add.Expression.ToString() == "button.onClick" &&
                add.Name.Identifier.ValueText == "AddListener")
            .ToArray() ?? Array.Empty<InvocationExpressionSyntax>();
        var listenerBody = buttonListener.SingleOrDefault()?.ArgumentList.Arguments
            .SingleOrDefault()?.Expression.DescendantNodesAndSelf().OfType<BlockSyntax>()
            .FirstOrDefault();
        string listenerSource = listenerBody == null ? "" : Compact(listenerBody.ToString());
        Require(buttonListener.Length == 1 &&
            listenerSource.Contains("session.Audio?.PlayClick()", StringComparison.Ordinal) &&
            listenerSource.Contains("action()", StringComparison.Ordinal) &&
            listenerSource.IndexOf("session.Audio?.PlayClick()", StringComparison.Ordinal) <
                listenerSource.IndexOf("action()", StringComparison.Ordinal),
            "Each enabled ActionButton must have one click listener that plays one click before its action; disabled Unity buttons remain silent.");
        var cardButton = hud?.Members.OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(method => method.Identifier.ValueText == "CardButton");
        var bindings = hud?.Members.OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(method => method.Identifier.ValueText == "RefreshBindings");
        Require(cardButton?.DescendantNodes().OfType<AssignmentExpressionSyntax>().Any(assignment =>
                    assignment.Left.ToString() == "button.interactable" &&
                    assignment.Right.ToString() == "enabled") == true &&
            bindings?.DescendantNodes().OfType<AssignmentExpressionSyntax>().Any(assignment =>
                    assignment.Left.ToString() == "bind.button.interactable" &&
                    assignment.Right.ToString() == "bind.enabled()") == true,
            "ActionButton listener delivery must remain guarded by Unity Button.interactable for disabled controls.");

        var perform = session?.Members.OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(method => method.Identifier.ValueText == "Perform");
        string performSource = perform == null ? "" : Compact(perform.ToString());
        var save = session?.Members.OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(method => method.Identifier.ValueText == "Save");
        string saveSource = save == null ? "" : Compact(save.ToString());
        Require(performSource.Contains("Audio?.PlayConfirmation()", StringComparison.Ordinal) &&
            performSource.Contains("Audio?.PlayFailure()", StringComparison.Ordinal) &&
            saveSource.Contains("SaveError=", StringComparison.Ordinal) &&
            saveSource.Contains("Audio?.PlayFailure()", StringComparison.Ordinal),
            "Successful native actions must confirm, failed actions and caught save errors must play the failure cue.");

        string importerPath = Path.Combine(sourceRoot, "Editor", "CityAudioImportSettings.cs");
        Require(File.Exists(importerPath), "Missing scoped mobile audio import settings.");
        if (File.Exists(importerPath))
        {
            string importer = Compact(File.ReadAllText(importerPath));
            CompilationUnitSyntax importerRoot = (CompilationUnitSyntax)
                CSharpSyntaxTree.ParseText(File.ReadAllText(importerPath),
                    new CSharpParseOptions(LanguageVersion.CSharp9), importerPath).GetRoot();
            bool referencesUnqualifiedSampleRateEnum = importerRoot.DescendantNodes()
                .OfType<IdentifierNameSyntax>().Any(identifier =>
                    identifier.Identifier.ValueText == "AudioSampleRateSetting" &&
                    !(identifier.Parent is MemberAccessExpressionSyntax member &&
                      member.Name == identifier));
            bool importsUnityEngineForSampleRateEnum = importerRoot.Usings.Any(usingDirective =>
                usingDirective.Name?.ToString() == "UnityEngine") ||
                importerRoot.Usings.Any(usingDirective =>
                    usingDirective.Alias?.Name.Identifier.ValueText == "AudioSampleRateSetting" &&
                    usingDirective.Name?.ToString() == "UnityEngine.AudioSampleRateSetting");
            Require(!referencesUnqualifiedSampleRateEnum || importsUnityEngineForSampleRateEnum,
                "The unqualified AudioSampleRateSetting enum must resolve through an explicit UnityEngine import or type alias.");
            Require(importer.Contains("AudioFolder=\"Assets/NewGaza/Resources/Audio/\"", StringComparison.Ordinal) &&
                importer.Contains("assetPath.StartsWith(AudioFolder,System.StringComparison.OrdinalIgnoreCase)", StringComparison.Ordinal) &&
                importer.Contains("importer.forceToMono=true", StringComparison.Ordinal) &&
                importer.Contains("preloadAudioData=true", StringComparison.Ordinal) &&
                !importer.Contains("importer.preloadAudioData", StringComparison.Ordinal) &&
                importer.Contains("importer.loadInBackground=false", StringComparison.Ordinal) &&
                importer.Contains("AudioSampleRateSetting.OverrideSampleRate", StringComparison.Ordinal) &&
                importer.Contains("sampleRateOverride=24000", StringComparison.Ordinal) &&
                importer.Contains("AudioCompressionFormat.Vorbis", StringComparison.Ordinal) &&
                importer.Contains("quality=0.6f", StringComparison.Ordinal) &&
                importer.Contains("importer.SetOverrideSampleSettings(\"Android\",settings)", StringComparison.Ordinal) &&
                importer.Contains("importer.SetOverrideSampleSettings(\"iPhone\",settings)", StringComparison.Ordinal),
                "Audio importer overrides must be scoped to authored city sounds and target forced-mono 24 kHz Vorbis on Android/iPhone.");
        }

        CheckAudioResources(sourceRoot, expectedClips);
    }

    private static void CheckAudioResources(string sourceRoot, string[] expectedClips)
    {
        string audioDirectory = Path.Combine(sourceRoot, "Resources", "Audio");
        string manifestPath = Path.Combine(audioDirectory, "AudioManifest.json");
        Require(File.Exists(manifestPath), "Missing authored Resources/Audio/AudioManifest.json.");
        if (!File.Exists(manifestPath)) return;

        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        JsonElement root = manifest.RootElement;
        Require(root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("schema", out JsonElement schema) &&
            schema.ValueKind == JsonValueKind.Number && schema.GetInt32() == 1,
            "AudioManifest.json must use supported schema version 1.");
        if (!root.TryGetProperty("clips", out JsonElement clips) ||
            clips.ValueKind != JsonValueKind.Array)
        {
            Require(false, "AudioManifest.json must list all authored audio clips.");
            return;
        }
        Require(clips.GetArrayLength() == expectedClips.Length,
            "AudioManifest.json must contain exactly the nine runtime-required clips.");
        string[] actualKeys = clips.EnumerateArray()
            .Where(clip => clip.ValueKind == JsonValueKind.Object &&
                clip.TryGetProperty("key", out JsonElement key) &&
                key.ValueKind == JsonValueKind.String)
            .Select(clip => clip.GetProperty("key").GetString() ?? "")
            .ToArray();
        Require(actualKeys.OrderBy(key => key, StringComparer.Ordinal)
                .SequenceEqual(expectedClips.OrderBy(key => key, StringComparer.Ordinal),
                    StringComparer.Ordinal),
            "AudioManifest.json must list the exact nine runtime resource keys.");

        long pcmMemoryBytes = 0;
        foreach (string key in expectedClips)
        {
            string path = Path.Combine(audioDirectory, key + ".wav");
            Require(File.Exists(path), "Missing required authored audio asset Resources/Audio/" + key + ".wav.");
            if (!File.Exists(path)) continue;
            byte[] bytes = File.ReadAllBytes(path);
            bool validWav = TryReadPcm16MonoWav(bytes, out int sampleRate,
                out int sampleCount, out double rms, out double peak, out int dataBytes);
            Require(validWav, key + ": expected a valid little-endian PCM16 mono WAV.");
            if (!validWav) continue;
            Require(sampleRate == 24000 && sampleCount > 0 && dataBytes > 0,
                key + ": audio must contain non-empty 24 kHz mono PCM samples.");
            Require(rms > 0.0001 && !double.IsNaN(rms) && !double.IsInfinity(rms) &&
                peak > 0.0001 && peak < 0.999,
                key + ": PCM samples must be finite, non-silent, and not clipped.");
            pcmMemoryBytes += dataBytes;

            JsonElement entry = default;
            foreach (JsonElement candidate in clips.EnumerateArray())
                if (candidate.ValueKind == JsonValueKind.Object &&
                    candidate.TryGetProperty("key", out JsonElement candidateKey) &&
                    candidateKey.ValueKind == JsonValueKind.String &&
                    candidateKey.GetString() == key)
                {
                    entry = candidate;
                    break;
                }
            bool hasEntry = entry.ValueKind == JsonValueKind.Object;
            Require(hasEntry, "Audio manifest has no metrics entry for " + key + ".");
            if (!hasEntry) continue;
            string actualSha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            string manifestSha = ManifestString(entry, "sha256");
            Require(manifestSha.Length == 64 && manifestSha == actualSha,
                key + ": manifest SHA-256 must be a valid digest matching the PCM WAV bytes.");
            Require(ManifestInt(entry, "sampleRate") == sampleRate &&
                ManifestInt(entry, "channels") == 1 &&
                ManifestInt(entry, "bytes") == bytes.Length,
                key + ": manifest sample rate, channel count, or file size disagrees with its WAV.");
            double duration = sampleCount / (double)sampleRate;
            Require(Math.Abs(ManifestDouble(entry, "durationSeconds") - duration) <= 0.0001,
                key + ": manifest duration must match the PCM frame count.");
            Require(Math.Abs(ManifestDouble(entry, "rms") - rms) <= 0.00002 &&
                Math.Abs(ManifestDouble(entry, "peak") - peak) <= 0.00002,
                key + ": manifest RMS/peak metrics must match the actual PCM samples.");
        }
        string[] wavNames = Directory.Exists(audioDirectory)
            ? Directory.GetFiles(audioDirectory, "*.wav", SearchOption.TopDirectoryOnly)
                .Select(path => Path.GetFileNameWithoutExtension(path))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray()
            : Array.Empty<string>();
        Require(wavNames.SequenceEqual(expectedClips.OrderBy(key => key, StringComparer.Ordinal),
                StringComparer.Ordinal),
            "Resources/Audio must contain only the nine explicitly manifested WAV clips.");
        Require(pcmMemoryBytes <= 5L * 1024 * 1024,
            "The complete uncompressed audio payload must remain within the 5 MiB mobile memory budget.");
    }

    private static bool TryReadPcm16MonoWav(byte[] bytes, out int sampleRate,
        out int sampleCount, out double rms, out double peak, out int dataBytes)
    {
        sampleRate = sampleCount = dataBytes = 0;
        rms = peak = 0d;
        if (bytes == null || bytes.Length < 44 ||
            System.Text.Encoding.ASCII.GetString(bytes, 0, 4) != "RIFF" ||
            System.Text.Encoding.ASCII.GetString(bytes, 8, 4) != "WAVE") return false;
        int format = 0, channels = 0, bits = 0, dataOffset = -1;
        int offset = 12;
        while (offset <= bytes.Length - 8)
        {
            string chunk = System.Text.Encoding.ASCII.GetString(bytes, offset, 4);
            uint rawSize = BitConverter.ToUInt32(bytes, offset + 4);
            if (rawSize > int.MaxValue) return false;
            int size = (int)rawSize;
            int chunkData = offset + 8;
            if (chunkData > bytes.Length - size) return false;
            if (chunk == "fmt " && size >= 16)
            {
                format = BitConverter.ToUInt16(bytes, chunkData);
                channels = BitConverter.ToUInt16(bytes, chunkData + 2);
                sampleRate = (int)BitConverter.ToUInt32(bytes, chunkData + 4);
                bits = BitConverter.ToUInt16(bytes, chunkData + 14);
            }
            else if (chunk == "data")
            {
                dataOffset = chunkData;
                dataBytes = size;
            }
            offset = chunkData + size + (size & 1);
        }
        if (format != 1 || channels != 1 || bits != 16 || sampleRate <= 0 ||
            dataOffset < 0 || dataBytes < 2 || dataBytes % 2 != 0) return false;
        sampleCount = dataBytes / 2;
        double squared = 0d;
        for (int i = 0; i < sampleCount; i++)
        {
            short value = BitConverter.ToInt16(bytes, dataOffset + i * 2);
            if (value == short.MinValue || value == short.MaxValue) return false;
            double normalized = value / 32768d;
            double absolute = Math.Abs(normalized);
            squared += normalized * normalized;
            if (absolute > peak) peak = absolute;
        }
        rms = Math.Sqrt(squared / sampleCount);
        return true;
    }

    private static string ManifestString(JsonElement entry, string property)
    {
        return entry.TryGetProperty(property, out JsonElement value) &&
            value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    }

    private static int ManifestInt(JsonElement entry, string property)
    {
        return entry.TryGetProperty(property, out JsonElement value) &&
            value.ValueKind == JsonValueKind.Number ? value.GetInt32() : -1;
    }

    private static double ManifestDouble(JsonElement entry, string property)
    {
        return entry.TryGetProperty(property, out JsonElement value) &&
            value.ValueKind == JsonValueKind.Number ? value.GetDouble() : double.NaN;
    }

    private static void CheckEquipmentContracts(Dictionary<string, CompilationUnitSyntax> roots)
    {
        var fleet = Type(roots, "World/CityFleet.cs", "CityFleet", "NewGaza");
        if (fleet == null) return;
        string fleetSource = Compact(fleet.ToString());
        var haulingSpeed = fleet.Members.OfType<FieldDeclarationSyntax>()
            .SelectMany(field => field.Declaration.Variables)
            .FirstOrDefault(variable => variable.Identifier.ValueText == "HaulingSpeed");
        var updateTruckTrip = fleet.Members.OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(method => method.Identifier.ValueText == "UpdateTruckTrip");
        string truckTripSource = updateTruckTrip == null ? "" : Compact(updateTruckTrip.ToString());
        Require(haulingSpeed?.Initializer?.Value.ToString() == ".22f" &&
            truckTripSource.Contains("tripDistance+=TruckSpeedAtTripDistance(false)*dt", StringComparison.Ordinal) &&
            truckTripSource.Contains("tripDistance+=TruckSpeedAtTripDistance(true)*dt", StringComparison.Ordinal) &&
            fleetSource.Contains("roadSpeedMultiplier(roadId)", StringComparison.Ordinal) &&
            fleetSource.Contains("privateconstfloatVehicleScale=.07f", StringComparison.Ordinal),
            "Truck base hauling speed must remain 0.22 unscaled city units/second, multiplied only by the current road's improvement level.");
        Require(truckTripSource.Contains("TruckTripState.TurningAtDepot", StringComparison.Ordinal) &&
            truckTripSource.Contains("TruckTripState.TurningInAtWork", StringComparison.Ordinal) &&
            truckTripSource.Contains("truck.localPosition=tripRoute[1]", StringComparison.Ordinal) &&
            truckTripSource.Contains("truck.localPosition=tripRoute[0]", StringComparison.Ordinal) &&
            truckTripSource.Contains("phase=0f", StringComparison.Ordinal),
            "Truck heading changes must occur while stopped at route endpoints, with the excavation cycle reset only at the work dock.");
        var initializeFleet = fleet.Members.OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(method => method.Identifier.ValueText == "Initialize");
        string transitionInitialization = initializeFleet == null ? "" :
            Compact(initializeFleet.DescendantNodes().OfType<AssignmentExpressionSyntax>()
                .FirstOrDefault(assignment => assignment.Left.ToString() == "transitionParts")?.Right.ToString() ?? "");
        Require(transitionInitialization.Contains("excavator,turret,boom,stick,bucket,bulldozer,blade,truckBed",
                StringComparison.Ordinal),
            "Geographic truck routes must not be smoothed over the excavator's 1.15-second pose blend.");

        var state = fleet.Members.OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(method => method.Identifier.ValueText == "TryGetMachineAudioState");
        Require(state != null && Signature(state.ReturnType) == "bool" &&
            state.ParameterList.Parameters.Select(parameter =>
                parameter.Type == null ? "" : Signature(parameter.Type))
                .SequenceEqual(new[] { "int", "Vector3", "float", "float", "float" },
                    StringComparer.Ordinal) &&
            state.ParameterList.Parameters.Skip(1).All(parameter =>
                parameter.Modifiers.Any(SyntaxKind.OutKeyword)),
            "CityFleet must expose the fixed indexed machine audio-state contract consumed by CityAudio.");
        if (state != null)
        {
            var mapping = state.DescendantNodes().OfType<SwitchStatementSyntax>()
                .FirstOrDefault(statement => statement.Expression.ToString() == "index");
            var mappedMachines = mapping?.Sections
                .Where(section => section.Labels.OfType<CaseSwitchLabelSyntax>().Any())
                .Select(section =>
            {
                string index = section.Labels.OfType<CaseSwitchLabelSyntax>()
                    .Select(label => label.Value.ToString()).FirstOrDefault() ?? "";
                string source = Compact(section.ToString());
                string machine = new[] { "excavator", "truck", "bulldozer" }
                    .FirstOrDefault(name => source.Contains("machine=" + name, StringComparison.Ordinal)) ?? "";
                return index + ":" + machine;
            }).ToArray() ?? Array.Empty<string>();
            Require(mappedMachines.SequenceEqual(new[] { "0:excavator", "1:truck", "2:bulldozer" },
                    StringComparer.Ordinal) &&
                state.DescendantNodes().OfType<ReturnStatementSyntax>().Any(statement =>
                    statement.Expression?.IsKind(SyntaxKind.FalseLiteralExpression) == true),
                "CityFleet audio indices must stay stable: 0 excavator, 1 truck, 2 dozer, with unsupported/inactive machines silent.");
        }

        var geometry = Type(roots, "World/EquipmentGeometry.cs", "EquipmentGeometry", "NewGaza");
        Require(geometry != null, "Missing production equipment geometry used by the runtime fleet.");
        if (geometry != null)
        {
            string[] detailedShapes =
            {
                "TrackBelt", "TireTread", "BucketShell", "CurvedBlade", "TaperedBeam"
            };
            foreach (string shape in detailedShapes)
                Require(geometry.Members.OfType<MethodDeclarationSyntax>().Any(method =>
                        method.Identifier.ValueText == shape &&
                        Compact(method.ToString()).Contains("owner.Own(mesh)", StringComparison.Ordinal)),
                    "Production equipment shape " + shape + " must be an owned, generated render mesh.");
        }

        var trackRig = Type(roots, "World/EquipmentTrackRig.cs", "EquipmentTrackRig", "NewGaza");
        Require(trackRig != null, "Missing production travel-driven crawler shoe/roller rig.");
        if (trackRig != null)
        {
            var trackUpdate = trackRig.Members.OfType<MethodDeclarationSyntax>()
                .FirstOrDefault(method => method.Identifier.ValueText == "Update");
            string trackUpdateSource = trackUpdate == null ? "" : Compact(trackUpdate.ToString());
            string completeTrackSource = Compact(trackRig.ToString());
            Require(trackUpdateSource.Contains("vehicle.localPosition", StringComparison.Ordinal) &&
                trackUpdateSource.Contains("Vector3.Dot", StringComparison.Ordinal) &&
                completeTrackSource.Contains("EquipmentGeometry.UpdateTrackShoeLoop", StringComparison.Ordinal) &&
                completeTrackSource.Contains("Quaternion.AngleAxis", StringComparison.Ordinal) &&
                completeTrackSource.Contains("track.rollers[roller].localRotation", StringComparison.Ordinal),
                "Visible crawler shoes and rollers must circulate from measured vehicle displacement and stay still without travel.");
        }

        var hydraulic = Type(roots, "World/EquipmentHydraulicLink.cs",
            "EquipmentHydraulicLink", "NewGaza");
        if (hydraulic != null)
        {
            var follow = hydraulic.Members.OfType<MethodDeclarationSyntax>()
                .FirstOrDefault(method => method.Identifier.ValueText == "Follow");
            string source = follow == null ? "" : Compact(follow.ToString());
            Require(source.Contains("Vector3direction=end-start", StringComparison.Ordinal) &&
                source.Contains("barrel.localPosition=start+direction.normalized*(barrelLength*.5f)", StringComparison.Ordinal) &&
                source.Contains("rod.localPosition=start+direction.normalized*(barrelLength+rodLength*.5f)",
                    StringComparison.Ordinal) &&
                source.Contains("basePin.localPosition=start", StringComparison.Ordinal) &&
                source.Contains("rodPin.localPosition=end", StringComparison.Ordinal) &&
                source.Contains("Mathf.Max(.04f,length*barrelFraction)", StringComparison.Ordinal) &&
                source.Contains("Mathf.Max(.025f,length-barrelLength)", StringComparison.Ordinal),
                "Hydraulic barrel, exposed rod and pinned eyes must follow real articulation anchors within bounded telescoping limits.");
        }

        var motion = Type(roots, "World/EquipmentMotion.cs", "EquipmentMotion", "NewGaza");
        if (motion != null)
        {
            var dig = motion.Members.OfType<MethodDeclarationSyntax>()
                .FirstOrDefault(method => method.Identifier.ValueText == "Dig");
            string digSource = dig == null ? "" : Compact(dig.ToString());
            var toothTip = motion.Members.OfType<MethodDeclarationSyntax>()
                .FirstOrDefault(method => method.Identifier.ValueText == "BucketToothTip");
            string toothTipSource = toothTip == null ? "" : Compact(toothTip.ToString());
            Require(digSource.Contains("Mathf.Repeat(seconds,DigCycleSeconds)", StringComparison.Ordinal) &&
                digSource.Contains("Mathf.SmoothStep(0f,1f,amount)", StringComparison.Ordinal) &&
                digSource.Contains("Vector3.Lerp(DigTargets[segment],DigTargets[segment+1]", StringComparison.Ordinal) &&
                digSource.Contains("SolveToothTarget(target,bucketAngle,boomPreference,stickPreference",
                    StringComparison.Ordinal) &&
                toothTipSource.Contains("newVector3(.17f,.99f,.36f)", StringComparison.Ordinal) &&
                toothTipSource.Contains("newVector3(0f,-1.15f,.35f)", StringComparison.Ordinal) &&
                toothTipSource.Contains("newVector3(0f,-.1964f,.52f)", StringComparison.Ordinal) &&
                toothTipSource.Contains("returnyaw*(newVector3(.17f,.99f,.36f)+boom*arm)",
                    StringComparison.Ordinal),
                "Dig poses must solve a deterministic eased trajectory against the production boom/stick/bucket pivot chain and tooth-tip coordinate.");
        }

        var smoke = Type(roots, "Editor/NewGazaSmokeTest.cs", "NewGazaSmokeTest", "NewGaza.Editor");
        if (smoke != null)
        {
            string smokeSource = Compact(smoke.ToString());
            var smokePoseSampler = smoke.Members.OfType<MethodDeclarationSyntax>()
                .FirstOrDefault(method => method.Identifier.ValueText == "CheckEquipmentContactAndTrackRuntime");
            string smokeSamplerSource = smokePoseSampler == null ? "" : Compact(smokePoseSampler.ToString());
            var cityWorld = Type(roots, "World/CityWorld.cs", "CityWorld", "NewGaza");
            string cityWorldSource = cityWorld == null ? "" : Compact(cityWorld.ToString());
            string[] requiredGroundSmokeTokens =
            {
                "CheckEquipmentContactAndTrackRuntime", "GetActualBucketToothTipWorldPoints",
                "MeasureActualBedInterior", "CheckFleetGroundSupportClearance",
                "\"PARK\"", "\"NORMALWORK\"", "\"IMPORTEDWORK\"",
                "CheckLowestTreadVertex", "trackshoes", "trucktires",
                "CheckTrackShoeMeshGeometry", "minimumArea", "signedVolume",
                "edgeCount==18", "normalTangentDot<.0001f"
            };
            string missingGroundSmokeTokens = string.Join(", ", requiredGroundSmokeTokens
                .Where(token => !smokeSource.Contains(token, StringComparison.Ordinal)));
            Require(cityWorldSource.Contains("CityGroundY=-.09f", StringComparison.Ordinal) &&
                smokeSource.Contains("TransformPoint(newVector3(0f,-.09f,0f))", StringComparison.Ordinal) &&
                smokeSource.Contains("nativeSurfaceRise=pose==\"PARK\"?.003f:.006f",
                    StringComparison.Ordinal),
                "Unity smoke ground samples must reference CityWorld's native -.09 grade and tolerate only the measured native apron surface height.");
            Require(string.IsNullOrEmpty(missingGroundSmokeTokens),
                "Unity smoke must exercise live tooth/bed clearance and actual tread/tire support in park, normal-work, and imported-work poses. Missing source tokens: " +
                missingGroundSmokeTokens);
            Require(
                smokeSource.Contains("CheckVisibleTrackTravel", StringComparison.Ordinal) &&
                smokeSource.Contains("CheckTruckRouteRuntime", StringComparison.Ordinal) &&
                smokeSource.Contains("maximumSpeed<=.35f", StringComparison.Ordinal) &&
                smokeSource.Contains("truck.rotation*Vector3.forward", StringComparison.Ordinal) &&
                smokeSource.Contains("world.Refresh()", StringComparison.Ordinal),
                "Unity smoke must exercise visible crawler travel, capped truck-route speed/heading, and restore session-owned presentation.");
            Require(smokeSamplerSource.Contains("Time.captureDeltaTime=sampleDelta", StringComparison.Ordinal) &&
                smokeSamplerSource.Contains("PrivateField<float>(fleet,\"phase\")", StringComparison.Ordinal) &&
                smokeSamplerSource.Contains("UpdateFleetOneFrame(fleet)", StringComparison.Ordinal) &&
                !smokeSamplerSource.Contains("SetPrivateField(fleet,\"phase\"", StringComparison.Ordinal),
                "Unity contact samples must advance the actual production phase via Time.captureDeltaTime and CityFleet.Update, never by assigning phase.");
        }
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

    private static void CheckHousingStageManifests(string sourceRoot, string[] masters,
        string[] suffixes, Dictionary<string, CompilationUnitSyntax> roots)
    {
        string repositoryRoot = Path.GetFullPath(Path.Combine(sourceRoot, "../../.."));
        string assetRoot = Path.Combine(repositoryRoot, "testingReplic", "Assets", "NewGaza");
        string resources = Path.Combine(assetRoot, "Resources", "Models");
        Dictionary<string, HousingProfileAudit> profiles = ReadHousingProfileAudits(roots);
        string manifestPath = Path.Combine(repositoryRoot, "exports", "housing-assets", "Manifest.json");
        Require(File.Exists(manifestPath), "Missing 10-archetype, 40-stage housing FBX manifest.");
        if (!File.Exists(manifestPath)) return;

        using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(manifestPath)))
        {
            JsonElement root = document.RootElement;
            Require(root.TryGetProperty("footprintInvariant", out JsonElement invariant) &&
                invariant.GetString()?.Contains("share the exact parcel width/depth bounds",
                    StringComparison.Ordinal) == true,
                "Housing generator manifest must guarantee one unchanged footprint across all four reference stages.");
            Require(root.TryGetProperty("archetypes", out JsonElement archetypes) &&
                archetypes.ValueKind == JsonValueKind.Array &&
                archetypes.GetArrayLength() == masters.Length,
                "Housing export manifest must contain exactly the ten authored master types.");
            if (!root.TryGetProperty("archetypes", out archetypes) ||
                archetypes.ValueKind != JsonValueKind.Array) return;

            var found = new HashSet<string>(StringComparer.Ordinal);
            string canonicalAtlasPath = Path.Combine(resources,
                "house_small_redtile_final_albedo.png");
            string canonicalAtlasHash = File.Exists(canonicalAtlasPath)
                ? Sha256File(canonicalAtlasPath) : "";
            Require(!string.IsNullOrEmpty(canonicalAtlasHash),
                "Missing canonical shared housing atlas used by runtime model materials.");
            string[] stageNames = { "foundation", "frame", "finishing", "final" };
            foreach (JsonElement archetype in archetypes.EnumerateArray())
            {
                if (!archetype.TryGetProperty("key", out JsonElement keyElement) ||
                    keyElement.ValueKind != JsonValueKind.String)
                {
                    Require(false, "Housing manifest contains an archetype with no stable master key.");
                    continue;
                }
                string master = keyElement.GetString() ?? "";
                Require(masters.Contains(master, StringComparer.Ordinal),
                    "Housing manifest contains an unexpected master key: " + master + ".");
                Require(found.Add(master), "Housing manifest repeats master key " + master + ".");
                if (!archetype.TryGetProperty("dimensionsMeters", out JsonElement finalDimensions) ||
                    !archetype.TryGetProperty("stages", out JsonElement stages))
                {
                    Require(false, "Housing manifest is missing dimensions or stage records for " + master + ".");
                    continue;
                }

                double expectedWidth = JsonNumber(finalDimensions, "width");
                double expectedDepth = JsonNumber(finalDimensions, "depth");
                double expectedHeightCap = JsonNumber(finalDimensions, "logicalHeightCap");
                double[] expectedStageHeights = new double[4];
                Require(expectedWidth > 0d && expectedDepth > 0d && expectedHeightCap > 0d,
                    "Housing manifest has non-positive logical dimensions for " + master + ".");

                for (int stageIndex = 0; stageIndex < stageNames.Length; stageIndex++)
                {
                    string stage = stageNames[stageIndex];
                    string stageKey = master + "_" + stage;
                    string objPath = Path.Combine(resources, stageKey + ".obj");
                    string albedoPath = Path.Combine(resources, stageKey + "_albedo.png");
                    string stageManifestPath = Path.Combine(resources, stageKey + "_manifest.json");
                    Require(File.Exists(objPath), "Missing native stage OBJ " + stageKey + ".");
                    Require(File.Exists(albedoPath), "Missing native stage albedo " + stageKey + ".");
                    Require(File.Exists(stageManifestPath), "Missing native stage dimensions manifest " + stageKey + ".");
                    if (!File.Exists(objPath) || !File.Exists(stageManifestPath)) continue;
                    if (!stages.TryGetProperty(stage, out JsonElement stageRecord) ||
                        !stageRecord.TryGetProperty("boundsMeters", out JsonElement fbxBounds))
                    {
                        Require(false, "Housing export manifest has no measured " + stage + " bounds for " + master + ".");
                        continue;
                    }

                    Require(Near(JsonNumber(fbxBounds, "width"), expectedWidth) &&
                        Near(JsonNumber(fbxBounds, "depth"), expectedDepth),
                        "Reference FBX stages must keep the common width/depth envelope: " + stageKey + ".");
                    if (File.Exists(albedoPath))
                        Require(Sha256File(albedoPath) == canonicalAtlasHash,
                            "Housing stage albedo must be byte-identical to the shared canonical atlas: " + stageKey + ".");
                    expectedStageHeights[stageIndex] = stage == "final" ? expectedHeightCap :
                        JsonNumber(fbxBounds, "height");
                    string fbxRelative = "Art/HousingFBX/" + stageKey + ".fbx";
                    Require(File.Exists(Path.Combine(assetRoot, fbxRelative)),
                        "Missing reference housing FBX " + stageKey + ".");
                    using (JsonDocument phaseDocument = JsonDocument.Parse(File.ReadAllText(stageManifestPath)))
                    {
                        JsonElement phaseRoot = phaseDocument.RootElement;
                        Require(phaseRoot.TryGetProperty("key", out JsonElement phaseKey) &&
                            phaseKey.GetString() == stageKey,
                            "Native OBJ manifest key mismatch for " + stageKey + ".");
                        Require(phaseRoot.TryGetProperty("dimensionsMeters", out JsonElement dimensions) &&
                            Near(JsonNumber(dimensions, "width"), expectedWidth) &&
                            Near(JsonNumber(dimensions, "depth"), expectedDepth) &&
                            Near(JsonNumber(dimensions, "logicalHeightCap"), expectedHeightCap),
                            "Native OBJ phase manifest must retain the common footprint and completed logical height cap: " +
                            stageKey + ".");
                        Require(Near(JsonNumber(dimensions, "actualStageHeight"),
                                expectedStageHeights[stageIndex]),
                            "Native OBJ actual stage height must match the stage FBX manifest: " + stageKey + ".");
                        Require(phaseRoot.TryGetProperty("metersPerCityUnit", out JsonElement units) &&
                            units.TryGetDouble(out double metersPerCityUnit) && Near(metersPerCityUnit, 20d),
                            "Native OBJ stage must declare the 20-metre city unit convention: " + stageKey + ".");
                        Require(phaseRoot.TryGetProperty("triangleCount", out JsonElement triangles) &&
                            triangles.TryGetInt32(out int triangleCount) &&
                            triangleCount > 0 && triangleCount <= 12000,
                            "Native OBJ stage must have a nonempty, budgeted mesh: " + stageKey + ".");
                    }
                    ObjAudit audit = AuditHousingObj(objPath);
                    Require(audit.triangles > 0 && audit.degenerateTriangles == 0,
                        "Native housing OBJ must contain valid, non-degenerate faces: " + stageKey + ".");
                    Require(Near(audit.width, expectedWidth) && Near(audit.depth, expectedDepth),
                        "Measured native OBJ width/depth must match the common authored footprint: " + stageKey + ".");
                }

                foreach (KeyValuePair<string, HousingProfileAudit> profile in profiles)
                {
                    if (profile.Value.master != master) continue;
                    Require(profile.Value.stageHeights.Length == expectedStageHeights.Length &&
                        profile.Value.stageHeights.Zip(expectedStageHeights,
                            (actual, expected) => Near(actual, expected)).All(equal => equal),
                        "Stable district housing profile must use manifest-measured phase heights: " +
                        profile.Key + ".");
                }
            }
            Require(found.Count == masters.Length,
                "Housing FBX manifest must include all ten stable archetype masters.");
            Require(profiles.Count == 12 && profiles.Values.Select(profile => profile.master)
                    .Distinct(StringComparer.Ordinal).Count() == masters.Length,
                "Housing profiles must cover all twelve stable inland IDs and use all ten master types.");
        }
    }

    private sealed class HousingProfileAudit
    {
        internal string master = "";
        internal double[] stageHeights = Array.Empty<double>();
    }

    private static Dictionary<string, HousingProfileAudit> ReadHousingProfileAudits(
        Dictionary<string, CompilationUnitSyntax> roots)
    {
        var result = new Dictionary<string, HousingProfileAudit>(StringComparer.Ordinal);
        var type = Type(roots, "World/CityHousingProfiles.cs", "CityHousingProfiles", "NewGaza");
        var field = type?.Members.OfType<FieldDeclarationSyntax>()
            .FirstOrDefault(candidate => candidate.Declaration.Variables
                .Any(variable => variable.Identifier.ValueText == "profiles"));
        var initializer = field?.Declaration.Variables
            .FirstOrDefault(variable => variable.Identifier.ValueText == "profiles")
            ?.Initializer?.Value as InitializerExpressionSyntax;
        if (initializer == null)
        {
            Require(false, "CityHousingProfiles must expose its stable district profile table.");
            return result;
        }

        foreach (ObjectCreationExpressionSyntax profile in initializer.Expressions
                     .OfType<ObjectCreationExpressionSyntax>())
        {
            SeparatedSyntaxList<ArgumentSyntax> args = profile.ArgumentList!.Arguments;
            if (args.Count != 7 || args[0].Expression is not LiteralExpressionSyntax districtLiteral ||
                args[1].Expression is not LiteralExpressionSyntax masterLiteral)
            {
                Require(false, "Housing profile entries must name a stable district, master and four measured stage heights.");
                continue;
            }
            string district = districtLiteral.Token.ValueText;
            var audit = new HousingProfileAudit
            {
                master = masterLiteral.Token.ValueText,
                stageHeights = new double[4]
            };
            bool valid = true;
            for (int i = 0; i < audit.stageHeights.Length; i++)
            {
                string text = args[i + 3].Expression.ToString().TrimEnd('f', 'F');
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture,
                        out audit.stageHeights[i]))
                    valid = false;
            }
            if (!valid)
                Require(false, "Housing profiles require literal phase dimensions in meters.");
            else if (!result.TryAdd(district, audit))
                Require(false, "Duplicate stable housing profile ID: " + district + ".");
        }
        return result;
    }

    private static double JsonNumber(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out JsonElement value) &&
            value.TryGetDouble(out double number) ? number : double.NaN;
    }

    private static bool Near(double a, double b)
    {
        return !double.IsNaN(a) && !double.IsNaN(b) && Math.Abs(a - b) <= .02d;
    }

    private static string Sha256File(string path)
    {
        using (SHA256 sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path)))
                .Replace("-", "").ToLowerInvariant();
    }

    private sealed class ObjAudit
    {
        internal double width;
        internal double depth;
        internal int triangles;
        internal int degenerateTriangles;
    }

    private static ObjAudit AuditHousingObj(string path)
    {
        var vertices = new List<(double x, double y, double z)>();
        var audit = new ObjAudit
        {
            width = 0d,
            depth = 0d
        };
        double minX = double.PositiveInfinity, maxX = double.NegativeInfinity;
        double minZ = double.PositiveInfinity, maxZ = double.NegativeInfinity;
        foreach (string line in File.ReadLines(path))
        {
            if (line.StartsWith("v ", StringComparison.Ordinal))
            {
                string[] fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length < 4 ||
                    !double.TryParse(fields[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double x) ||
                    !double.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double y) ||
                    !double.TryParse(fields[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double z) ||
                    double.IsNaN(x) || double.IsInfinity(x) ||
                    double.IsNaN(y) || double.IsInfinity(y) ||
                    double.IsNaN(z) || double.IsInfinity(z))
                {
                    audit.degenerateTriangles++;
                    continue;
                }
                vertices.Add((x, y, z));
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minZ = Math.Min(minZ, z);
                maxZ = Math.Max(maxZ, z);
            }
            else if (line.StartsWith("f ", StringComparison.Ordinal))
            {
                string[] fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length < 4) { audit.degenerateTriangles++; continue; }
                int[] indices = new int[fields.Length - 1];
                bool valid = true;
                for (int i = 1; i < fields.Length; i++)
                {
                    string vertexIndex = fields[i].Split('/')[0];
                    if (!int.TryParse(vertexIndex, NumberStyles.Integer, CultureInfo.InvariantCulture,
                            out int parsed))
                    {
                        valid = false;
                        break;
                    }
                    indices[i - 1] = parsed > 0 ? parsed - 1 : vertices.Count + parsed;
                    if (indices[i - 1] < 0 || indices[i - 1] >= vertices.Count)
                        valid = false;
                }
                if (!valid) { audit.degenerateTriangles++; continue; }
                for (int i = 1; i < indices.Length - 1; i++)
                {
                    audit.triangles++;
                    var a = vertices[indices[0]];
                    var b = vertices[indices[i]];
                    var c = vertices[indices[i + 1]];
                    double abx = b.x - a.x, aby = b.y - a.y, abz = b.z - a.z;
                    double acx = c.x - a.x, acy = c.y - a.y, acz = c.z - a.z;
                    double nx = aby * acz - abz * acy;
                    double ny = abz * acx - abx * acz;
                    double nz = abx * acy - aby * acx;
                    if (nx * nx + ny * ny + nz * nz < 1e-16)
                        audit.degenerateTriangles++;
                }
            }
        }
        audit.width = maxX - minX;
        audit.depth = maxZ - minZ;
        if (vertices.Count == 0) audit.degenerateTriangles++;
        return audit;
    }

    private static string Compact(string source) =>
        new string(source.Where(c => !char.IsWhiteSpace(c)).ToArray());

    private static void CheckZoomResponse(Dictionary<string, CompilationUnitSyntax> roots)
    {
        var camera = roots["Runtime/CityCamera.cs"];
        Func<string, double> constant = name =>
        {
            var variable = camera.DescendantNodes().OfType<VariableDeclaratorSyntax>()
                .Single(v => v.Identifier.ValueText == name);
            if (variable.Initializer?.Value is not LiteralExpressionSyntax literal)
                throw new InvalidOperationException("Zoom tuning must expose a numeric constant: " + name);
            return Convert.ToDouble(literal.Token.Value, System.Globalization.CultureInfo.InvariantCulture);
        };
        double wheel = constant("WheelZoomSensitivity"), pinch = constant("PinchZoomExponent"),
            response = constant("ZoomResponse");
        Require(wheel > .0015 && pinch > 1 && response > 8, "Wheel, pinch and camera convergence must all be faster.");
        double zoomIn = Math.Exp(-120 * wheel), zoomOut = Math.Exp(120 * wheel);
        Require(zoomIn > 0 && zoomIn < Math.Exp(-120 * .0015) &&
            Math.Abs(zoomIn * zoomOut - 1) < .00001, "Wheel zoom stays positive, faster and directionally reversible.");
        Require(Math.Pow(1 / 1.1, pinch) < 1 / 1.1 &&
            Math.Abs(Math.Pow(1 / 1.1, pinch) * Math.Pow(1.1, pinch) - 1) < .00001,
            "Pinch zoom is amplified symmetrically, not biased to one direction.");
        foreach (int fps in new[] { 30, 60, 120 })
        {
            double remaining = 1;
            for (int frame = 0; frame < fps; frame++) remaining *= Math.Exp(-response / fps);
            Require(Math.Abs(remaining - Math.Exp(-response)) < .00001,
                "Zoom response is frame-rate independent at " + fps + " FPS.");
        }
        string source = Compact(camera.ToFullString());
        Require(source.Contains("Mathf.Pow(lastSpan/span,PinchZoomExponent)") &&
            source.Contains("Mathf.Exp(-scroll*WheelZoomSensitivity)") &&
            source.Contains("Mathf.Lerp(zoom,targetZoom,zoomBlend)") &&
            source.Contains("floatblend=immediate?1:1-Mathf.Exp(-8*Time.unscaledDeltaTime)"),
            "Faster tuning must be wired to input/rendered zoom without accelerating camera panning.");
    }

    private static void CheckNativeWindowLayout(Dictionary<string, CompilationUnitSyntax> roots)
    {
        string hud = Compact(roots["UI/CityHud.cs"].ToFullString());
        string development = Compact(roots["UI/CityDevelopmentUI.cs"].ToFullString());
        string home = Compact(roots["UI/CityHudMainMenu.cs"].ToFullString());
        string layout = Compact(roots["UI/CityUiLayout.cs"].ToFullString());
        Require(hud.Contains("CityUiLayout.Apply(canvas,scaler,safe)") &&
            development.Contains("CityUiLayout.Apply(canvas,scaler,safe)") &&
            layout.Contains("CanvasScaler.ScaleMode.ConstantPixelSize") &&
            layout.Contains("canvas.scaleFactor=viewport.scale"),
            "All native canvases must share logical safe-area coordinates and scale.");
        Require(!development.Contains("lastSafe.width") && !development.Contains("lastSafe.height"),
            "Development panel geometry must not mix screen pixels with scaled canvas units.");
        foreach (string name in new[] { "Close guide", "Close notification", "Close modal" })
            Require(hud.Contains("\"" + name.Replace(" ", "") + "\""), name + " must have an explicit close control.");
        foreach (string name in new[] { "Close region", "Close work panel" })
            Require(development.Contains("\"" + name.Replace(" ", "") + "\""), name + " must have an explicit close control.");
        Require(hud.Contains("ActionButton(confirmationPanel,\"إغلاق\",CloseConfirmation") &&
            home.Contains("mainMenuClose=ActionButton(") && home.Contains("ContinueReconstruction"),
            "Confirmation and home screen must provide a visible close path.");
        Require(hud.Contains("BlockingWindowVisible||StoreVisible") &&
            hud.Contains("elseif(StoreVisible)session.Development.CloseStore()") &&
            hud.Contains("session.Development.CloseWorkPanel()") &&
            development.Contains("session.Hud.BeginStoreWindow()") &&
            development.Contains("session.Hud.EndStoreWindow()") &&
            development.Contains("boolblocked=session.Hud!=null&&session.Hud.BlockingWindowVisible"),
            "Native windows must coordinate mutual exclusion, Back and pointer-up camera blocking.");
        Require(home.Contains("if(modalObject!=null||confirmationObject!=null)return;") &&
            development.Contains("storeCanvas.overrideSorting=true;storeCanvas.sortingOrder=200;"),
            "Home chrome must not cover modal controls; store must have its own overlay layer.");
    }

    private static void CheckPointerProjection(Dictionary<string, CompilationUnitSyntax> roots)
    {
        var camera = roots["Runtime/CityCamera.cs"];
        var projections = camera.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(call => call.Expression is MemberAccessExpressionSyntax member &&
                member.Name.Identifier.ValueText == "ScreenPointToRay").ToArray();
        Require(projections.Length == 2, "Camera projection entry points changed; review pointer guards.");
        foreach (var call in projections)
        {
            var method = call.Ancestors().OfType<MethodDeclarationSyntax>().First();
            Require(method.Identifier.ValueText == "TryGroundPoint" || method.Identifier.ValueText == "Tap",
                "Every native screen ray must use a guarded projection entry point.");
            Require(method.Body != null, "Projection guard requires an explicit method body.");
            if (method.Body == null) continue;
            string body = Compact(method.Body.ToFullString());
            int guard = body.IndexOf("if(!CanProjectPointer(", StringComparison.Ordinal);
            int ray = body.IndexOf("ScreenPointToRay(", StringComparison.Ordinal);
            Require(guard >= 0 && guard < ray && body.Substring(guard, ray - guard).Contains("return"),
                "Invalid pointer must return before native camera projection.");
        }
        string development = Compact(roots["Runtime/CityDevelopment.cs"].ToFullString());
        Require(!development.Contains("ScreenPointToRay(") &&
            development.Contains("cameraControl.TryGroundPoint(mouse.position.ReadValue(),outvarground)"),
            "District hover must use the same guarded camera instead of an unchecked Camera.main ray.");
        string input = Compact(camera.ToFullString());
        Require(input.Contains("ScreenInputValidation.InViewport(") &&
            input.Contains("!Application.isFocused") &&
            input.Contains("if(!CanProjectPointer(touchPosition)){CancelGesture();return;}") &&
            input.Contains("if(!CanProjectPointer(position)){CancelGesture();return;}") &&
            input.Contains("ScreenInputValidation.Finite(scroll)") &&
            input.Contains("ScreenInputValidation.Finite(delta)"),
            "Focus, touch/mouse viewport and nonfinite scroll/rotation input guards must stay wired.");
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