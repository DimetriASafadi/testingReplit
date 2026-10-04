using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class ClearingClickChecks
{
    // Execute the real click/dispatch methods, rather than reimplementing their branches.
    internal static void Run(Dictionary<string, CompilationUnitSyntax> roots)
    {
        string Methods(string path, params string[] names) => string.Join("\n",
            roots[path].DescendantNodes().OfType<MethodDeclarationSyntax>()
                .Where(method => names.Contains(method.Identifier.ValueText))
                .Select(method => method.ToFullString()));
        string bridge = Harness.Replace("CONTROLLER_METHODS", Methods("Runtime/CityDevelopment.cs",
            "StartClear", "Parcel", "ClearFeedback"))
            .Replace("UI_METHODS", Methods("UI/CityDevelopmentUI.cs", "DoAction", "ShowWorkFeedback"));
        var trees = roots.Where(pair => pair.Key.StartsWith("Core/", StringComparison.Ordinal))
            .Select(pair => pair.Value.SyntaxTree).Concat(new[] {
                CSharpSyntaxTree.ParseText(bridge, new CSharpParseOptions(LanguageVersion.CSharp9)) });
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
            .Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("ClearingClickFixture", trees, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var bytes = new MemoryStream();
        var emitted = compilation.Emit(bytes);
        if (!emitted.Success)
            throw new InvalidOperationException("Clearing click fixture compile: " +
                string.Join("\n", emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        var assembly = Assembly.Load(bytes.ToArray());
        try { assembly.GetType("NewGaza.ClickScenarios").GetMethod("Run").Invoke(null, null); }
        catch (TargetInvocationException exception) { throw exception.InnerException ?? exception; }
    }

    private const string Harness = @"
using System;
using NewGaza.Core;
namespace NewGaza {
public static class Time { public static float unscaledTime; }
public struct Vector3 { public float x,y,z; public Vector3(float a,float b,float c) { x=a;y=b;z=c; } }
public sealed class CityDevelopmentParcel {
 public string id,sourceBuildingId; public int district,plot; public Vector3 position; public float width,depth;
}
public sealed class FixtureRoute { public bool IsReachable=true; public float Length=1; }
public sealed class FixtureRoads {
 public FixtureRoute FindEquipmentRoute(Vector3 from,Vector3 to,Func<string,float> speed) => new FixtureRoute();
}
public sealed class FixtureFleet { public int dispatches; public void BeginDepotDispatch() { dispatches++; } }
public sealed class FixtureWorld {
 public FixtureRoads Roads=new FixtureRoads(); public FixtureFleet Fleet=new FixtureFleet();
 public Vector3 CentralDepotPosition=new Vector3(0,0,0);
}
public sealed class FixtureSession {
 public bool Ready=true; public EconomyService Economy; public long Now; public string message;
 public int saves; public GameState State => Economy.State;
 public void Notify(string text) { message=text; }
 public void Perform(Func<EconomyService,ActionResult> action) {
  if(!Ready) return; Economy.Tick(Now); var result=action(Economy);
  if(result.success) saves++; Notify(result.message);
 }
}
public sealed class CityDevelopment {
 public FixtureSession session; public CityDevelopmentService Rules; public FixtureWorld world=new FixtureWorld();
 public CityDevelopmentUI ui; public string selectedSite,selectedBuilding;
 public CityDevelopmentParcel[] parcels=Array.Empty<CityDevelopmentParcel>();
 public string SelectedSite=>selectedSite; public string SelectedBuilding=>selectedBuilding;
 public bool Placing=>false; public void Confirm() { throw new Exception(""unexpected placement""); }
 public void BuildOnSelectedLand() { builtOnCleanLand=true; } public bool builtOnCleanLand;
 public void Collect() { throw new Exception(""unexpected collection""); }
 public void ClaimRegion() { throw new Exception(""unexpected claim""); }
 private static Vector3 DepotPoint(PlacedBuildingState building)=>new Vector3(building.x,0,building.z);
 CONTROLLER_METHODS
}
public sealed class CityDevelopmentUI {
 public CityDevelopment development; public string feedbackSite,workFeedback; public int refreshes; private float workFeedbackUntil;
 public void Refresh() { refreshes++; } public void Click() { DoAction(); }
 UI_METHODS
}
public static class ClickScenarios {
 private static int checks;
 private static void Check(bool ok,string message) {
  checks++; if(!ok) throw new Exception(""Clearing click: ""+message);
 }
 private static CityDevelopment Setup() {
  const long now=1800000000;
  var state=GameCatalog.CreateNew(now); state.development.requiresPlacedFactory=false;
  state.coins=10000000; var economy=new EconomyService(state);
  var controller=new CityDevelopment {session=new FixtureSession {Economy=economy,Now=now},
   Rules=new CityDevelopmentService(economy)};
  controller.Rules.RegisterBackgroundSites(new[] {
   new RubbleSiteState {id=""background:click"",background=true,sourceBuildingId=""click"",district=0,
    projectId=""housing"",x=50,z=60,width=1,depth=1,height=.2f,buildingPrice=2500},
   new RubbleSiteState {id=""background:other"",background=true,sourceBuildingId=""other"",district=0,
    projectId=""housing"",x=52,z=63,width=1,depth=1,height=.2f,buildingPrice=2500}});
  controller.selectedSite=""background:click"";
  controller.ui=new CityDevelopmentUI {development=controller}; return controller;
 }
 public static void Run() {
  var c=Setup(); c.ui.Click();
  Check(c.ui.workFeedback.Contains(""مصنع"") && c.world.Fleet.dispatches==0,""missing factory explains failure in work panel"");
  c.session.State.factoryLevel=1; c.ui.Click();
  Check(c.ui.workFeedback.Contains(""المعدات"") && c.world.Fleet.dispatches==0,""missing equipment explains failure"");
  foreach(var kind in new[]{""excavator"",""bulldozer"",""truck""})
   Check(c.session.Economy.BuyEquipment(kind,c.session.Now).success,""buy actual equipment"");
  c.ui.Click();
  Check(c.world.Fleet.dispatches==1 && c.session.saves==1,""real UI click starts and saves one dispatch"");
  Check(c.Rules.Data.activeRubbleId==""background:click"" && c.session.State.jobStage==JobStage.Clearing &&
   !c.Rules.Data.crewArrived,""selected background site becomes a traveling active job"");
  Check(c.ui.workFeedback.Contains(""انطلقت"") && c.ui.feedbackSite==c.selectedSite,""success visible in clicked site panel"");
  long finish=c.session.State.jobFinishUtc, coins=c.session.State.coins;
  c.ui.Click();
  Check(c.world.Fleet.dispatches==1 && c.session.State.jobFinishUtc==finish,""same-site repeat never resets dispatch or timer"");
  c.selectedSite=""background:other""; c.ui.Click();
  Check(c.selectedSite==""background:click"" && c.Rules.Data.activeRubbleId==c.selectedSite &&
   c.world.Fleet.dispatches==1 && c.session.State.coins==coins,""busy click shows saved active job without replacing it"");
  c=Setup(); c.selectedSite=""missing""; c.ui.Click();
  Check(c.ui.workFeedback.Contains(""اختر"") && c.world.Fleet.dispatches==0,""unknown site is not silent or null dereference"");
  c=Setup(); c.session.Ready=false; c.ui.Click();
  Check(c.ui.workFeedback.Contains(""تحميل"") && c.world.Fleet.dispatches==0,""not-ready click explicitly explains wait"");
  c=Setup(); c.Rules.Site(c.selectedSite).cleared=true; c.ui.Click();
  Check(c.builtOnCleanLand && c.world.Fleet.dispatches==0,""clean-land button still opens construction, not salvage"");
  c=Setup(); c.session.State.development.requiresPlacedFactory=true;
  Check(c.Rules.Build(""recycling"",0,80,80,0,c.session.Now,(a,b,d,e,f)=>null).success,""player places recycling factory"");
  var factory=c.Rules.Data.buildings[0]; c.session.Now=factory.finishUtc; c.session.Economy.Tick(c.session.Now);
  foreach(var kind in new[]{""excavator"",""bulldozer"",""truck""})
   Check(c.session.Economy.BuyEquipment(kind,c.session.Now).success,""equip player-placed depot"");
  c.ui.Click();
  Check(c.world.Fleet.dispatches==1 && c.Rules.Data.dispatchDepotId==factory.id,""actual click dispatches from player factory, not unavailable central depot"");
  Console.WriteLine(""PASS production clearing click/dispatch methods: ""+checks+"" assertions (bounded Unity/road/UI fixture)."");
 }
}}";
}