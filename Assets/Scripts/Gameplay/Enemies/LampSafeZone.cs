using System.Collections.Generic;
using UnityEngine;

namespace FPLab.Gameplay.Enemies
{
    // Only fixed, powered ceiling lights are registered by the maze decorator.
    [DisallowMultipleComponent]
    public sealed class LampSafeZone : MonoBehaviour
    {
        private static readonly List<LampSafeZone> active = new List<LampSafeZone>();
        private static readonly RaycastHit[] hits = new RaycastHit[32];
        [SerializeField] private Light source;
        [SerializeField, Min(0.1f)] private float radius = 1.25f;

        public static IReadOnlyList<LampSafeZone> Active => active;
        public Vector3 FloorCenter => source == null ? transform.position
            : new Vector3(source.transform.position.x,
                source.transform.parent != null ? source.transform.parent.position.y : 0f,
                source.transform.position.z);
        public float Radius => radius;
        public bool IsPowered => isActiveAndEnabled && source != null && source.isActiveAndEnabled
            && source.type == LightType.Spot && source.intensity > 0.05f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRegistry() => active.Clear();

        public void Configure(Light poweredSource, float safeRadius = 1.25f)
        {
            source = poweredSource;
            radius = Mathf.Max(0.1f, safeRadius);
        }

        private void OnEnable()
        {
            if (!active.Contains(this)) active.Add(this);
        }

        private void OnDisable() => active.Remove(this);
        private void OnDestroy() => active.Remove(this);

        public static LampSafeZone FindAt(Vector3 feet, Transform actorRoot = null)
        {
            foreach (LampSafeZone zone in active)
                if (zone != null && zone.Contains(feet, actorRoot)) return zone;
            return null;
        }

        public bool Contains(Vector3 feet, Transform actorRoot = null, float padding = 0f)
        {
            if (!IsPowered) return false;
            Vector3 flat = feet - source.transform.position;
            flat.y = 0f;
            float testRadius = radius + Mathf.Max(0f, padding);
            if (flat.sqrMagnitude > testRadius * testRadius) return false;

            // Evaluate real cone/range at torso height; the explicit floor radius
            // keeps a readable safe area beneath a working lamp.
            Vector3 torso = feet + Vector3.up * 0.9f;
            Vector3 delta = torso - source.transform.position;
            if (delta.sqrMagnitude > source.range * source.range || delta.sqrMagnitude < 0.001f)
                return false;
            if (Vector3.Dot(source.transform.forward, delta.normalized)
                < Mathf.Cos(source.spotAngle * 0.5f * Mathf.Deg2Rad)) return false;

            // Start beneath the glass so the fixture cannot shadow its own test.
            Vector3 origin = source.transform.position + source.transform.forward * 0.24f;
            Vector3 ray = torso - origin;
            int count = Physics.RaycastNonAlloc(origin, ray.normalized, hits, ray.magnitude,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            // Fail closed if the buffer fills; never grant safety through a wall.
            if (count == hits.Length) return false;
            for (int i = 0; i < count; i++)
            {
                Transform hit = hits[i].transform;
                if (actorRoot != null && (hit == actorRoot || hit.IsChildOf(actorRoot))) continue;
                if (hit == source.transform || hit.IsChildOf(source.transform)) continue;
                return false;
            }
            return true;
        }
    }
}
