using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class ButtonContentChecks
{
    internal static void Run(Dictionary<string, CompilationUnitSyntax> roots)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("ButtonContentFixture", new[] {
            roots["Core/ButtonContentMetrics.cs"].SyntaxTree,
            roots["UI/CityButtonContent.cs"].SyntaxTree,
            CSharpSyntaxTree.ParseText(Harness, new CSharpParseOptions(LanguageVersion.CSharp9))
        }, references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);
        if (!emitted.Success) throw new Exception("Button layout fixture: " +
            string.Join("\n", emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        var assembly = Assembly.Load(stream.ToArray());
        try { assembly.GetType("Scenarios").GetMethod("Run").Invoke(null, null); }
        catch (TargetInvocationException error) { throw error.InnerException ?? error; }
    }
    private const string Harness = @"
using System;
using NewGaza.UI;
namespace UnityEngine {
public struct Vector2 {
 public float x,y; public Vector2(float x,float y){this.x=x;this.y=y;}
 public static Vector2 zero=>new Vector2(0,0); public static Vector2 one=>new Vector2(1,1);
}
public struct Rect {public float width,height;}
public class GameObject {public bool activeSelf=true;public void SetActive(bool value){activeSelf=value;}}
public class RectTransform {
 public Rect rect;public Vector2 anchorMin,anchorMax,offsetMin,offsetMax,pivot,anchoredPosition,sizeDelta;
 public GameObject gameObject=new GameObject();
}
public enum TextAnchor {MiddleCenter,MiddleRight}
public enum HorizontalWrapMode {Wrap}
public enum VerticalWrapMode {Truncate}
public static class Mathf {public static int Max(int a,int b)=>Math.Max(a,b);}
}
namespace UnityEngine.UI {public class Text {}}
namespace NewGaza.UI {
public class ArabicLabel {
 public UnityEngine.TextAnchor alignment=UnityEngine.TextAnchor.MiddleRight;
 public UnityEngine.HorizontalWrapMode horizontalOverflow;
 public UnityEngine.VerticalWrapMode verticalOverflow;
 public bool resizeTextForBestFit;public int resizeTextMinSize,resizeTextMaxSize,fontSize=18;
 public float lineSpacing;public UnityEngine.RectTransform rectTransform=new UnityEngine.RectTransform();
}
}
public static class Scenarios {
 static int count;
 static void Check(bool value,string message){count++;if(!value)throw new Exception(message);}
 public static void Run(){
  foreach(float width in new[]{72f,96f,180f,320f})
  foreach(float height in new[]{30f,44f,72f,120f}){
   var root=new UnityEngine.RectTransform{rect=new UnityEngine.Rect{width=width,height=height}};
   var caption=new ArabicLabel();var icon=new UnityEngine.RectTransform();
   CityButtonContent.Apply(root,new[]{caption},icon);
   var rect=caption.rectTransform;
   Check(caption.alignment==UnityEngine.TextAnchor.MiddleCenter,""actual native helper centers caption"");
   Check(rect.offsetMin.x==-rect.offsetMax.x && rect.offsetMin.x>0 &&
    rect.offsetMin.y>0 && rect.offsetMax.y<0,""actual native helper applies symmetric horizontal and vertical padding"");
   Check(rect.offsetMin.x-rect.offsetMax.x<width && rect.offsetMin.y-rect.offsetMax.y<height,
    ""caption content never collapses for short, nav or wide controls"");
   Check(caption.resizeTextForBestFit && caption.resizeTextMinSize>=12 &&
    caption.resizeTextMaxSize>=caption.resizeTextMinSize,""caption fit is bounded and readable"");
   if(height==30)Check(!icon.gameObject.activeSelf,""30px button never stacks icon over its only text line"");
  }
  var multi=new[]{new ArabicLabel{fontSize=24},new ArabicLabel{fontSize=16}};
  multi[0].rectTransform.offsetMin=new UnityEngine.Vector2(20,40);
  CityButtonContent.Apply(new UnityEngine.RectTransform{rect=new UnityEngine.Rect{width=180,height=120}},multi,null);
  Check(multi[0].alignment==UnityEngine.TextAnchor.MiddleCenter &&
   multi[1].alignment==UnityEngine.TextAnchor.MiddleCenter &&
   multi[0].rectTransform.offsetMin.y==40,""composite home card keeps its geometry but centers all button copy"");
  Console.WriteLine(""PASS production button content layout: ""+count+"" assertions (bounded Unity layout fixture)."");
 }
}";
}