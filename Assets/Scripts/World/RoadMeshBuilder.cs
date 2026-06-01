// Builds a flat road ribbon along a spline.
//
// Unity's Spline Extrude makes a round tube; we want a flat strip with UVs that
// run along the road's length (so the texture can scroll in M3). This walks the
// spline, places a left/right vertex pair at each sample, and stitches a strip.
//
// Usage: add to the same GameObject as a Spline Container. A MeshFilter +
// MeshRenderer are added automatically — assign a material to the renderer.
// Rebuilds live in the editor as you drag knots; builds once at play start.

using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;

namespace NightRider.World
{
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class RoadMeshBuilder : MonoBehaviour
    {
        [Tooltip("Source spline. Defaults to a Spline Container on this object.")]
        public SplineContainer spline;

        [Min(0.01f), Tooltip("Road width in world units.")]
        public float width = 4f;

        [Min(2), Tooltip("Segments along the spline. Higher = smoother curves.")]
        public int samples = 200;

        [Min(0.0001f), Tooltip("Texture repeats per world unit along the road's length.")]
        public float tilesPerUnit = 0.1f;

        [Tooltip("Tick if the road renders upside-down (only visible from below).")]
        public bool flipFaces;

        Mesh _mesh;
        Vector3[] _verts;
        Vector2[] _uvs;
        Vector4[] _tangents;
        int[] _tris;
        int _builtSamples = -1;

        void OnEnable()
        {
#if UNITY_EDITOR
            Spline.Changed += OnSplineChanged;
#endif
            Build();
        }

        void OnDisable()
        {
#if UNITY_EDITOR
            Spline.Changed -= OnSplineChanged;
#endif
        }

#if UNITY_EDITOR
        bool _dirty;

        // Inspector tweaks (width, samples, ...) — rebuild next editor tick.
        void OnValidate() => _dirty = true;

        // A knot on our spline moved — rebuild next editor tick.
        void OnSplineChanged(Spline s, int knot, SplineModification mod)
        {
            if (spline != null && s == spline.Spline) _dirty = true;
        }

        // Editor only: rebuild on demand, never per frame.
        void Update()
        {
            if (!Application.isPlaying && _dirty) { _dirty = false; Build(); }
        }
#endif

        [ContextMenu("Rebuild")]
        public void Build()
        {
            if (spline == null) spline = GetComponent<SplineContainer>();
            if (spline == null || spline.Spline == null || spline.Spline.Count < 2) return;

            int count = Mathf.Max(2, samples);
            int vertCount = (count + 1) * 2;

            if (_verts == null || _builtSamples != count)
            {
                _verts     = new Vector3[vertCount];
                _uvs       = new Vector2[vertCount];
                _tangents  = new Vector4[vertCount];
                _tris      = new int[count * 6];
                _builtSamples = count;
            }

            float halfW = width * 0.5f;
            float lengthAccum = 0f;
            Vector3 prevWorld = default;

            for (int i = 0; i <= count; i++)
            {
                float t = i / (float)count;
                spline.Evaluate(t, out float3 pos, out float3 tan, out float3 up);

                Vector3 world   = (Vector3)pos;
                Vector3 forward = ((Vector3)tan).normalized;
                // World up (flat world); a mirrored/reversed spline's own up flips.
                Vector3 right   = Vector3.Cross(Vector3.up, forward).normalized;

                Vector3 local        = transform.InverseTransformPoint(world);
                Vector3 localRight   = transform.InverseTransformDirection(right);
                Vector3 localForward = transform.InverseTransformDirection(forward);

                _verts[i * 2]     = local - localRight * halfW;
                _verts[i * 2 + 1] = local + localRight * halfW;

                // Road length-direction, for the shader's view-relative scroll.
                var fwd4 = new Vector4(localForward.x, localForward.y, localForward.z, 1f);
                _tangents[i * 2]     = fwd4;
                _tangents[i * 2 + 1] = fwd4;

                if (i > 0) lengthAccum += Vector3.Distance(world, prevWorld);
                prevWorld = world;

                float v = lengthAccum * tilesPerUnit;
                _uvs[i * 2]     = new Vector2(0f, v);
                _uvs[i * 2 + 1] = new Vector2(1f, v);
            }

            for (int i = 0; i < count; i++)
            {
                int a = i * 2, b = i * 2 + 1, c = (i + 1) * 2, d = (i + 1) * 2 + 1;
                int ti = i * 6;
                if (!flipFaces)
                {
                    _tris[ti] = a; _tris[ti + 1] = c; _tris[ti + 2] = b;
                    _tris[ti + 3] = b; _tris[ti + 4] = c; _tris[ti + 5] = d;
                }
                else
                {
                    _tris[ti] = a; _tris[ti + 1] = b; _tris[ti + 2] = c;
                    _tris[ti + 3] = b; _tris[ti + 4] = d; _tris[ti + 5] = c;
                }
            }

            if (_mesh == null)
            {
                _mesh = new Mesh { name = "RoadMesh" };
                _mesh.MarkDynamic();
            }
            _mesh.Clear();
            _mesh.vertices  = _verts;
            _mesh.uv        = _uvs;
            _mesh.triangles = _tris;
            _mesh.tangents  = _tangents;
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();

            GetComponent<MeshFilter>().sharedMesh = _mesh;
        }
    }
}
