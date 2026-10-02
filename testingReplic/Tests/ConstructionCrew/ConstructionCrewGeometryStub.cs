using System;
using UnityEngine;

namespace NewGaza
{
    /// <summary>Small disposable source-geometry fixture; production actor still builds the tested mesh.</summary>
    internal sealed class CityGeometry : IDisposable
    {
        internal readonly Mesh Box;

        internal CityGeometry()
        {
            Vector3[] corners =
            {
                new Vector3(-.5f,-.5f,-.5f), new Vector3(.5f,-.5f,-.5f),
                new Vector3(.5f,.5f,-.5f), new Vector3(-.5f,.5f,-.5f),
                new Vector3(-.5f,-.5f,.5f), new Vector3(.5f,-.5f,.5f),
                new Vector3(.5f,.5f,.5f), new Vector3(-.5f,.5f,.5f)
            };
            int[] faces = { 0,3,2,1, 5,6,7,4, 4,7,3,0, 1,2,6,5, 3,7,6,2, 4,0,1,5 };
            var vertices = new Vector3[24];
            var triangles = new int[36];
            for (int face = 0; face < 6; face++)
            {
                int vertex = face * 4;
                for (int corner = 0; corner < 4; corner++)
                    vertices[vertex + corner] = corners[faces[vertex + corner]];
                int index = face * 6;
                triangles[index] = vertex;
                triangles[index + 1] = vertex + 1;
                triangles[index + 2] = vertex + 2;
                triangles[index + 3] = vertex;
                triangles[index + 4] = vertex + 2;
                triangles[index + 5] = vertex + 3;
            }
            Box = new Mesh { name = "Shared CityGeometry box source", subMeshCount = 1 };
            Box.vertices = vertices;
            Box.SetTriangles(triangles, 0, false);
            Box.RecalculateNormals();
        }

        public void Dispose() { UnityEngine.Object.Destroy(Box); }
    }
}