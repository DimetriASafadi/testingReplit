using System.Collections.Generic;
using UnityEngine;

namespace UnityEngine
{
    // Unity APIs used by the production road view but deliberately kept out of the shared
    // fixture while its fleet adapter is being extended by the parallel equipment task.
    public static class RoadViewTestExtensions
    {
        public static void SetUVs(this Mesh mesh, int channel, List<Vector2> values)
        {
            if (channel != 0) throw new System.ArgumentOutOfRangeException(nameof(channel));
            mesh.uv = values.ToArray();
        }

        public static void SetTriangles(this Mesh mesh, List<int> values, int submesh, bool calculateBounds)
        {
            mesh.SetTriangles(values, submesh);
            if (calculateBounds) mesh.RecalculateBounds();
        }
    }

    public static class Application
    {
        public static bool isPlaying { get; set; }
    }
}