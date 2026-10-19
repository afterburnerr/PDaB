using FPLab.Gameplay.Session;
using UnityEngine;

namespace FPLab.Gameplay.Player.Equipment
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerAuxPower), typeof(PlayerInputReader))]
    public sealed class PlayerFlashlight : MonoBehaviour
    {
        private const float SourceUnit = 0.0254f;
        [SerializeField] private PlayerInputReader input;
        [SerializeField] private PlayerAuxPower power;
        [SerializeField] private EscapeSession session;
        [SerializeField] private Transform view;
        [SerializeField] private Light beam;
        [SerializeField] private AudioSource source;
        [SerializeField] private AudioClip toggleClip;
        [SerializeField, Range(0f, 1f)] private float toggleVolume = 0.8f;
        [SerializeField, Range(0f, 1f)] private float minimumCharge = 0.048f;

        public bool IsOn { get; private set; }
        private readonly RaycastHit[] sweepHits = new RaycastHit[16];
        private float pullback;

        private void Awake()
        {
            IsOn = false;
            power.SetFlashlightActive(false);
            beam.enabled = false;
        }

        private void OnEnable() => session.StateChanged += RefreshPresentation;

        private void Update()
        {
            if (session.State != RunState.Playing || Time.deltaTime <= 0f) return;
            if (input.WasFlashlightTogglePressed)
            {
                if (IsOn) SetOn(false);
                else if (power.NormalizedPower > minimumCharge) SetOn(true);
            }
        }

        private void LateUpdate()
        {
            // The motor ticks the shared AUX resource before LateUpdate.
            if (IsOn && power.NormalizedPower <= minimumCharge) SetOn(false);
            if (!beam.enabled) return;

            float downwardLook = Vector3.Dot(view.forward, Vector3.up);
            float offset = (-20f + Mathf.Max(0f, -12f * downwardLook)) * SourceUnit;
            Vector3 origin = view.position + view.up * offset;
            Vector3 toOrigin = origin - view.position;
            if (Sweep(view.position, toOrigin.normalized, toOrigin.magnitude, out _))
                origin = view.position;
            Vector3 target = view.position + view.forward * (750f * SourceUnit);
            Vector3 direction = (target - origin).normalized;
            float desiredPullback = 0f;
            float closeRange = 128f * SourceUnit;
            if (Sweep(origin, direction, closeRange, out float frontDistance))
                desiredPullback = closeRange - frontDistance;
            float smoothing = 1f - Mathf.Pow(0.8f, Time.deltaTime * 60f);
            pullback = Mathf.Lerp(pullback, desiredPullback, smoothing);
            float distance = pullback;
            if (Sweep(origin, -direction, distance, out float backDistance))
                distance = Mathf.Max(0f, backDistance - 0.1f * SourceUnit);
            beam.transform.SetPositionAndRotation(origin - direction * distance,
                Quaternion.LookRotation(direction, view.up));
        }

        private bool Sweep(Vector3 origin, Vector3 direction, float distance, out float nearest)
        {
            nearest = distance;
            if (distance <= 0f) return false;
            int count = Physics.BoxCastNonAlloc(origin, Vector3.one * (4f * SourceUnit), direction,
                sweepHits, Quaternion.identity, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            bool blocked = false;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = sweepHits[i];
                if (hit.collider.transform.IsChildOf(transform)) continue;
                if (hit.distance <= nearest)
                {
                    nearest = hit.distance;
                    blocked = true;
                }
            }
            return blocked;
        }

        private void SetOn(bool on)
        {
            if (IsOn == on) return;
            IsOn = on;
            if (on) pullback = 0f;
            power.SetFlashlightActive(on);
            RefreshPresentation();
            if (session.State == RunState.Playing)
            {
                source.Stop();
                source.PlayOneShot(toggleClip, toggleVolume);
            }
        }

        private void RefreshPresentation()
        {
            bool playing = session.State == RunState.Playing;
            beam.enabled = playing && IsOn;
            if (!playing) source.Stop();
        }

        private void OnDisable()
        {
            session.StateChanged -= RefreshPresentation;
            IsOn = false;
            power.SetFlashlightActive(false);
            beam.enabled = false;
            source.Stop();
        }
    }
}
