using FPLab.Gameplay.Session;
using UnityEngine;

namespace FPLab.Gameplay.Audio
{
    [DisallowMultipleComponent]
    public sealed class MenuAmbience : MonoBehaviour
    {
        [SerializeField] private EscapeSession session;
        [SerializeField] private AudioSource source;
        private bool paused;

        private void OnEnable()
        {
            if (session == null || source == null) return;
            session.StateChanged += ApplyState;
            ApplyState();
        }

        private void ApplyState()
        {
            bool inMenu = session.State == RunState.Ready || session.State == RunState.Paused;
            if (inMenu)
            {
                if (paused) source.UnPause();
                else if (!source.isPlaying) source.Play();
                paused = false;
            }
            else if (source.isPlaying)
            {
                source.Pause();
                paused = true;
            }
        }

        private void OnDisable()
        {
            if (session != null) session.StateChanged -= ApplyState;
            if (source != null) source.Stop();
            paused = false;
        }
    }
}
