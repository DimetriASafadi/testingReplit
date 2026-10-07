using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

internal static class PlacementInputCompilationChecks
{
    internal static IEnumerable<string> Check(string sourceRoot)
    {
        var failures = new List<string>();
        var source = File.ReadAllText(Path.Combine(sourceRoot, "UI/CityHoldRotateButton.cs"));
        var contracts = @"
namespace UnityEngine {
 public class MonoBehaviour { public T GetComponent<T>() where T:new() => new T(); }
 public static class Time { public static float unscaledDeltaTime = .02f; }
}
namespace UnityEngine.UI {
 public class Button { public static bool Enabled=true; public bool IsInteractable() => Enabled; }
}
namespace UnityEngine.EventSystems {
 public class PointerEventData { public enum InputButton { Left, Right, Middle }
  public int pointerId; public InputButton button; }
 public interface IPointerDownHandler { void OnPointerDown(PointerEventData data); }
 public interface IPointerUpHandler { void OnPointerUp(PointerEventData data); }
 public interface IPointerExitHandler { void OnPointerExit(PointerEventData data); }
}
namespace NewGaza {
 public class CityDevelopment {
  public bool Placing {get;set;} = true;
  public float Angle; public int Finishes;
  public void RotateBy(float value) { Angle += value; }
  public void FinishRotation() { Finishes++; }
 }
}";
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("NativeHeldRotationProbe",
            new[] { CSharpSyntaxTree.ParseText(source), CSharpSyntaxTree.ParseText(contracts) }, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var binary = new MemoryStream();
        var result = compilation.Emit(binary);
        if (!result.Success)
        {
            failures.AddRange(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString()));
            return failures;
        }
        var assembly = Assembly.Load(binary.ToArray());
        Type componentType = assembly.GetType("NewGaza.CityHoldRotateButton")!;
        Type controllerType = assembly.GetType("NewGaza.CityDevelopment")!;
        Type pointerType = assembly.GetType("UnityEngine.EventSystems.PointerEventData")!;
        object component = Activator.CreateInstance(componentType)!;
        object controller = Activator.CreateInstance(controllerType)!;
        componentType.GetProperty("Development")!.SetValue(component, controller);
        object pointer = Activator.CreateInstance(pointerType)!;
        pointerType.GetField("pointerId")!.SetValue(pointer, 17);
        void Call(string name, params object[] arguments) =>
            componentType.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(component, arguments);
        float Angle() => (float)controllerType.GetField("Angle")!.GetValue(controller)!;
        void Verify(bool condition, string message) { if (!condition) failures.Add("Held rotation: " + message); }
        Call("Update"); Verify(Angle() == 0, "must not rotate before a press");
        Call("OnPointerDown", pointer);
        for (int i = 0; i < 50; i++) Call("Update");
        Verify(Math.Abs(Angle() - 45) < .001, "holding for one second must rotate smoothly by 45 degrees");
        object other = Activator.CreateInstance(pointerType)!;
        pointerType.GetField("pointerId")!.SetValue(other, 18);
        Call("OnPointerUp", other); Call("Update");
        Verify(Angle() > 45, "another finger's release must not stop the captured press");
        Call("OnPointerUp", pointer);
        float stopped = Angle();
        Call("Update"); Verify(Angle() == stopped, "rotation must stop on release");
        Call("OnPointerDown", pointer); Call("OnPointerExit", pointer);
        Call("Update"); Verify(Angle() == stopped, "leaving the button cancels rotation");
        Call("OnPointerDown", pointer); Call("OnApplicationFocus", false);
        Call("Update"); Verify(Angle() == stopped, "loss of focus cancels rotation");
        Call("OnPointerDown", pointer); Call("OnDisable");
        Call("Update"); Verify(Angle() == stopped, "closing the UI cancels rotation");
        controllerType.GetProperty("Placing")!.SetValue(controller, false);
        Call("OnPointerDown", pointer); Call("Update");
        Verify(Angle() == stopped, "cannot rotate outside placement");
        return failures;
    }
}