using UnityEngine;
using UnityEngine.Serialization;

namespace FPLab.Gameplay.Player.Audio
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerMotor), typeof(PlayerAuxPower))]
    public sealed class PlayerSprintAudio : MonoBehaviour
    {
        [SerializeField] private PlayerMotor motor;
        [SerializeField, FormerlySerializedAs("stamina")] private PlayerAuxPower auxPower;
        [SerializeField] private AudioSource source;
        [SerializeField] private AudioClip sprintStartClip;
        [SerializeField] private AudioClip exhaustedClip;

        [Header("Volume")]
        [SerializeField, Range(0f, 1f)] private float sprintStartVolume = 0.9f;
        [SerializeField, Range(0f, 1f)] private float exhaustedVolume = 0.8f;

        private bool wasSprinting;
        private bool wasExhausted;

        private void LateUpdate()
        {
            bool exhausted = auxPower.IsSprintExhausted;
            bool sprinting = motor.IsSprinting && !exhausted;
            if (!motor.isActiveAndEnabled || Time.deltaTime <= 0f)
            {
                source.Stop();
                // Resuming the same sprint must not replay its start signal.
                wasSprinting = sprinting;
                wasExhausted = exhausted;
                return;
            }

            if (exhausted && !wasExhausted)
                Play(exhaustedClip, exhaustedVolume);
            else if (sprinting && !wasSprinting)
                Play(sprintStartClip, sprintStartVolume);

            wasSprinting = sprinting;
            wasExhausted = exhausted;
        }

        private void Play(AudioClip clip, float volume)
        {
            // Both HL2 events use one voice channel: replace, never stack.
            source.Stop();
            source.clip = clip;
            source.loop = false;
            source.volume = volume;
            source.Play();
        }

        private void OnDisable()
        {
            if (source != null) source.Stop();
        }
    }
}
