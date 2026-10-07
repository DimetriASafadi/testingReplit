using System;
using System.IO;
using NewGaza;
using UnityEngine;

internal static class Program
{
    private static int assertions;
    private static int Main()
    {
        try
        {
            string[] files = { "UrbanGround_Albedo", "CoastalSand_Albedo", "DryGround_Albedo" };
            Shader shader = Shader.Find("NewGaza/Ground");
            Resources.AssetOverrides["NewGazaGround"] = shader;
            foreach (string file in files)
                Resources.AssetOverrides["Ground/" + file] = new Texture2D(2,2,TextureFormat.RGBA32,false,false);
            var neutral = new Color(.6f,.58f,.54f);
            using (var geometry = new CityGeometry())
            {
                foreach (string file in files)
                {
                    Material material = geometry.GroundMaterial(file,file,neutral);
                    Check(material.shader == shader,"Ground retains its Resources shader.");
                    Check(material.GetTexture("_BaseMap") == Resources.Load<Texture2D>("Ground/"+file),
                        "Each terrain binds its own albedo.");
                    Check(material.GetTexture("_BaseMap").wrapMode == TextureWrapMode.Repeat,"Wrapping enforced.");
                    Check(material.enableInstancing,"Shared material supports instancing.");
                    Check(material.GetColor("_BaseColor").r == 1f,"Authored colors are not multiplied by old yellow paint.");
                    Check(Math.Abs(material.GetFloat("_WorldScale")-2.5f)<.00001f,"Eight real metres per tile.");
                    Check(Math.Abs(8f*50f/1000f*material.GetFloat("_WorldScale")-1f)<.00001f,
                        "World scale is independent of parcel size.");
                    Check(material.GetFloat("_CoarseBlend") == .18f,"Broad sample reduces repetition.");
                    Check(material.GetFloat("_MacroStrength") == .12f,"Macro variation remains subdued.");
                    Check(ReferenceEquals(material,geometry.GroundMaterial(file,file,neutral)),"No per-parcel material clones.");
                }
                Material changedScale = geometry.GroundMaterial("other projection",files[0],neutral,100f);
                Check(changedScale.GetFloat("_WorldScale") == 1.25f,"Projection scale changes real-world UV density.");
                foreach (float invalid in new[] { 0f,-1f,float.NaN,float.PositiveInfinity })
                {
                    bool rejected = false;
                    try { geometry.GroundMaterial("invalid",files[0],neutral,invalid); }
                    catch (ArgumentOutOfRangeException) { rejected = true; }
                    Check(rejected,"Reject invalid scale rather than infinite UVs.");
                }
                Resources.AssetOverrides["Ground/"+files[0]] = null;
                Material fallback = geometry.GroundMaterial("missing art",files[0],neutral);
                Check(fallback.shader.name == "Universal Render Pipeline/Lit","Missing art keeps existing ground shader.");
                Check(fallback.GetTexture("_BaseMap") != null,"Missing art keeps procedural surface texture.");
                Check(ReferenceEquals(fallback,geometry.GroundMaterial("missing art",files[0],neutral)),
                    "Missing-art fallback is cached, not recreated each placement.");
                Resources.AssetOverrides["NewGazaGround"] = null;
                Material shaderFallback = geometry.GroundMaterial("missing shader",files[1],neutral);
                Check(shaderFallback.shader.name == "Universal Render Pipeline/Lit","Missing custom shader remains safe.");
            }
            string source = FindSource();
            string shaderSource = File.ReadAllText(Path.Combine(source,"Resources/NewGazaGround.shader"));
            Check(shaderSource.Contains("input.positionWS.xz * _WorldScale"),"World mapping does not stretch object UVs.");
            Check(shaderSource.Contains("GetMainLight(shadowCoord)") &&
                shaderSource.Contains("TransformWorldToShadowCoord(input.positionWS)"),
                "Receives directional light / shadow.");
            Check(shaderSource.Contains("float4 shadowCoord = input.screenPos;"),
                "Screen-space shadow variant uses projected coordinates, not world shadow coordinates.");
            Check(shaderSource.Contains("DepthOnly"),"Opaque floor participates in depth rendering.");
            Check(shaderSource.Contains("UnityPerMaterial"),"SRP material constant buffer retained.");
            int level = 0;
            bool balanced = true;
            foreach (char c in shaderSource) { if (c=='{') level++; if (c=='}') level--; balanced &= level>=0; }
            Check(balanced,"Shader braces never close before opening.");
            Check(level==0,"Shader ends balanced.");
            string world = File.ReadAllText(Path.Combine(source,"World/CityWorld.cs"));
            foreach (string file in files) Check(world.Contains("\""+file+"\""),"All authored terrain types wired to city.");
            Check(world.Contains("stage == 3 ? sidewalk : urbanGround"),"Construction pads are not painted beach yellow.");
            Check(world.Contains("limestone = geometry.Material(\"neutral urban ground\""),
                "Walls, rubble and other borrowed limestone props keep their original material.");
            Console.WriteLine("PASS ground material contracts / "+assertions+" assertions. " +
                "Production CityGeometry compiled with a bounded Unity API fixture; Unity/shader execution NOT RUN.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { Resources.AssetOverrides.Clear(); }
    }
    private static string FindSource()
    {
        for (var directory=new DirectoryInfo(Directory.GetCurrentDirectory());directory!=null;directory=directory.Parent)
        {
            string path=Path.Combine(directory.FullName,"testingReplic/Assets/NewGaza");
            if (Directory.Exists(path)) return path;
            if (directory.Name=="testingReplic") return Path.Combine(directory.FullName,"Assets/NewGaza");
        }
        throw new DirectoryNotFoundException("Cannot find native source.");
    }
    private static void Check(bool condition,string reason)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(reason);
    }
}
