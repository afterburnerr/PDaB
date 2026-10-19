using UnityEngine;

namespace FPLab.Gameplay.Player.Audio
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController), typeof(PlayerMotor))]
    public sealed class PlayerFootsteps : MonoBehaviour
    {
        [SerializeField] private PlayerMotor motor;
        [SerializeField] private CharacterController controller;
        [SerializeField] private AudioSource source;
        [SerializeField] private AudioClip[] leftSteps;
        [SerializeField] private AudioClip[] rightSteps;

        [Header("Distance between steps")]
        [SerializeField, Min(0.1f)] private float walkStepDistance = 2.4f;
        [SerializeField, Min(0.1f)] private float sprintStepDistance = 3f;
        [Header("Volume")]
        [SerializeField, Range(0f, 1f)] private float walkVolume = 0.45f;
        [SerializeField, Range(0f, 1f)] private float sprintVolume = 0.7f;

        private float stepProgress = 0.5f;
        private bool leftFoot = true;

        private void LateUpdate()
        {
            // Read the controller after the motor has moved, so blocked input
            // does not sound like walking. Menus and loading disable the motor.
            if (!motor.isActiveAndEnabled || !controller.enabled || Time.deltaTime <= 0f)
            {
                stepProgress = 0.5f;
                source.Stop();
                return;
            }

            Vector3 velocity = controller.velocity;
            velocity.y = 0f;
            if (velocity.sqrMagnitude < 0.01f || !HasGround())
            {
                stepProgress = 0.5f;
                return;
            }

            bool sprinting = motor.IsSprinting;
            float stride = sprinting ? sprintStepDistance : walkStepDistance;
            stepProgress += velocity.magnitude * Time.deltaTime / Mathf.Max(0.1f, stride);
            if (stepProgress < 1f) return;

            // Emit at most one step per frame after a hitch, instead of a burst.
            stepProgress %= 1f;
            AudioClip[] clips = leftFoot ? leftSteps : rightSteps;
            leftFoot = !leftFoot;
            if (clips != null && clips.Length > 0)
                source.PlayOneShot(clips[Random.Range(0, clips.Length)], sprinting ? sprintVolume : walkVolume);
        }

        private bool HasGround()
        {
            if (controller.isGrounded) return true;
            // Horizontal-only movement can leave isGrounded false even while
            // standing on the floor. Probe just beneath the capsule's feet.
            Bounds bounds = controller.bounds;
            Vector3 origin = new Vector3(bounds.center.x, bounds.min.y + 0.15f, bounds.center.z);
            return Physics.Raycast(origin, Vector3.down, 0.35f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }

        private void OnDisable()
        {
            stepProgress = 0.5f;
            if (source != null) source.Stop();
        }
    }
}
