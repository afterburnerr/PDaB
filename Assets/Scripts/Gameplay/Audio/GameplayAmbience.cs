using FPLab.Gameplay.Session;
using UnityEngine;

namespace FPLab.Gameplay.Audio
{
    [DisallowMultipleComponent]
    public sealed class GameplayAmbience : MonoBehaviour
    {
        [SerializeField] private EscapeSession session;
        [SerializeField] private AudioSource source;

        private bool paused;

        private void OnEnable()
        {
            if (session == null || source == null)
                return;

            session.StateChanged += ApplyState;
            ApplyState();
        }

        private void ApplyState()
        {
            if (session.State == RunState.Playing)
            {
                if (paused)
                    source.UnPause();
                else if (!source.isPlaying)
                    source.Play();

                paused = false;
            }
            else if (session.State == RunState.Paused)
            {
                if (source.isPlaying)
                {
                    source.Pause();
                    paused = true;
                }
            }
            else
            {
                source.Stop();
                paused = false;
            }
        }

        private void OnDisable()
        {
            if (session != null)
                session.StateChanged -= ApplyState;
            if (source != null)
                source.Stop();
            paused = false;
        }
    }
}
