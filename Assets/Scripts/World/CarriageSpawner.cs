// Populates the world with carriages around the rider: spawns them some distance
// ahead of the camera on nearby lanes, and despawns them once they fall behind.
//
// With no prefab assigned it builds a placeholder: a capsule, lying longways
// (longer than the rider), tinted a random green/blue/red. Drop a real carriage
// prefab in the slot later to replace it.

using System.Collections.Generic;
using UnityEngine;

namespace NightRider.World
{
    public class CarriageSpawner : MonoBehaviour
    {
        [Header("Refs")]
        public LaneFollower rider;
        [Tooltip("Viewpoint used for spawn-ahead / despawn-behind. Defaults to main camera.")]
        public Transform view;
        [Tooltip("Optional carriage prefab (needs a Carriage component). If empty, a coloured capsule is built.")]
        public GameObject carriagePrefab;

        [Header("Population")]
        [Min(0)] public int maxCarriages = 8;
        [Min(1f), Tooltip("How far in front of the camera to spawn.")]
        public float spawnAhead = 60f;
        [Min(0f), Tooltip("Distance behind the camera at which to cull a carriage.")]
        public float despawnBehind = 25f;
        [Min(0.05f), Tooltip("Seconds between spawn attempts.")]
        public float spawnInterval = 0.5f;

        [Header("Carriage settings")]
        [Range(0f, 1f), Tooltip("Carriage cruise speed as a fraction of the rider's.")]
        public float speedFraction = 0.5f;
        [Min(0f)] public float acceleration = 8f;
        public float heightOffset = 0.4f;
        [Tooltip("Placeholder visual scale (longer than the rider along travel).")]
        public Vector3 visualScale = new(0.7f, 0.7f, 1.8f);

        readonly List<Carriage> _spawned = new();
        float _timer;

        void OnEnable()
        {
            if (view == null && Camera.main != null) view = Camera.main.transform;
        }

        void Update()
        {
            if (rider == null || view == null) return;

            Despawn();

            _timer += Time.deltaTime;
            if (_timer >= spawnInterval && _spawned.Count < maxCarriages)
            {
                _timer = 0f;
                TrySpawn();
            }
        }

        void Despawn()
        {
            Vector3 camPos = view.position, camFwd = view.forward;
            for (int i = _spawned.Count - 1; i >= 0; i--)
            {
                var c = _spawned[i];
                if (c == null) { _spawned.RemoveAt(i); continue; }
                if (Vector3.Dot(c.transform.position - camPos, camFwd) < -despawnBehind)
                {
                    _spawned.RemoveAt(i);
                    Destroy(c.gameObject);
                }
            }
        }

        void TrySpawn()
        {
            var lanes = FindObjectsByType<Lane>();
            if (lanes.Length == 0) return;

            var lane = lanes[Random.Range(0, lanes.Length)];
            if (lane == null || !lane.IsValid) return;
            if (!FindSpawnT(lane, out float t)) return;
            if (Carriage.Occupied(lane, t, visualScale.z * 2f)) return;   // don't stack on traffic

            Spawn(lane, t);
        }

        // A point on the lane that's in front of the camera at roughly spawnAhead.
        bool FindSpawnT(Lane lane, out float t)
        {
            t = 0f;
            Vector3 camPos = view.position, camFwd = view.forward;
            float len = lane.Length;
            int n = Mathf.Clamp(Mathf.CeilToInt(len / 4f), 16, 256);

            float best = float.MaxValue;
            bool found = false;
            for (int i = 0; i < n; i++)
            {
                float tt = i / (float)n;
                lane.EvaluateWorld(tt, out var p, out _, out _);
                float forward = Vector3.Dot(p - camPos, camFwd);
                if (forward <= 0f) continue;                  // must be ahead of the camera
                float err = Mathf.Abs(forward - spawnAhead);
                if (err < best) { best = err; t = tt; found = true; }
            }
            return found && best < spawnAhead;                // within a sensible band
        }

        void Spawn(Lane lane, float t)
        {
            GameObject go;
            if (carriagePrefab != null)
            {
                go = Instantiate(carriagePrefab);
            }
            else
            {
                go = new GameObject("Carriage");
                var visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                Destroy(visual.GetComponent<Collider>());
                visual.transform.SetParent(go.transform, false);
                visual.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);  // lie longways
                visual.transform.localScale = visualScale;
                visual.GetComponent<MeshRenderer>().material.color = RandomColor();
            }

            var car = go.GetComponent<Carriage>();
            if (car == null) car = go.AddComponent<Carriage>();
            car.lane = lane;
            car.t = t;
            car.idealSpeed = (rider != null ? rider.speed : 12f) * speedFraction;
            car.acceleration = acceleration;
            car.heightOffset = heightOffset;

            _spawned.Add(car);
        }

        static Color RandomColor()
        {
            switch (Random.Range(0, 3))
            {
                case 0:  return new Color(0.20f, 0.70f, 0.25f); // green
                case 1:  return new Color(0.20f, 0.45f, 0.85f); // blue
                default: return new Color(0.85f, 0.20f, 0.20f); // red
            }
        }
    }
}
