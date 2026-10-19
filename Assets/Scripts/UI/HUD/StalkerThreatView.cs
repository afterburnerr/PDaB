using FPLab.Gameplay.Enemies;
using FPLab.Gameplay.Session;
using UnityEngine;

namespace FPLab.UI.HUD
{
    // Presentation only: pursuit, safety and capture remain owned by gameplay.
    [DisallowMultipleComponent]
    public sealed class StalkerThreatView : MonoBehaviour
    {
        [SerializeField] private EscapeSession session;
        [SerializeField] private EntranceStalker stalker;
        [SerializeField] private Transform listener;
        [SerializeField] private AudioSource murmurs;
        [SerializeField] private AudioSource voice;
        [SerializeField] private AudioSource captureSound;
        [SerializeField] private AudioSource crackle;
        [SerializeField] private AudioClip[] staticBursts;
        [SerializeField] private AudioLowPassFilter murmurFilter;
        [SerializeField] private AudioLowPassFilter voiceFilter;
        [SerializeField] private AudioClip[] mutters;
        [SerializeField] private LayerMask obstructionMask = ~0;

        private float nextMutter;
        private float nextObstructionCheck;
        private float obstructionGain = 1f;
        private int lastMutter = -1;
        private int lastStatic = -1;
        private float nextStatic;
        private bool paused;
        private bool capturePlayed;

        private void OnEnable()
        {
            if (session != null) session.StateChanged += OnStateChanged;
            capturePlayed = false;
            nextMutter = Time.time + 1.5f;
            nextStatic = Time.time + Random.Range(6f, 12f);
            OnStateChanged();
        }

        private void OnStateChanged()
        {
            if (session == null) return;
            if (session.State == RunState.Paused)
            {
                if (murmurs != null) murmurs.Pause();
                if (voice != null) voice.Pause();
                if (crackle != null) crackle.Pause();
                paused = true;
            }
            else if (session.State == RunState.Playing)
            {
                capturePlayed = false;
                if (captureSound != null) captureSound.Stop();
                if (paused)
                {
                    if (murmurs != null) murmurs.UnPause();
                    if (voice != null) voice.UnPause();
                    if (crackle != null) crackle.UnPause();
                }
                paused = false;
            }
            else
            {
                StopWhispers();
                paused = false;
                if (session.State == RunState.Caught)
                {
                    if (!capturePlayed && captureSound != null && captureSound.clip != null)
                    {
                        capturePlayed = true;
                        // Caught freezes scaled time. Audio remains a 2D one-shot
                        // on the same state event as the hard-cut illustration.
                        captureSound.ignoreListenerPause = true;
                        captureSound.spatialBlend = 0f;
                        captureSound.loop = false;
                        captureSound.pitch = 1f;
                        captureSound.Stop();
                        captureSound.Play();
                    }
                }
                else if (session.State != RunState.Lost)
                {
                    if (captureSound != null) captureSound.Stop();
                    capturePlayed = false;
                }
            }
        }

        private void LateUpdate()
        {
            if (session == null || stalker == null) return;
            RunState state = session.State;
            bool playing = state == RunState.Playing;
            float threat = playing ? Mathf.Clamp01(stalker.Threat01) : 0f;
            if (!playing) return;
            if (threat <= 0.005f)
            {
                StopWhispers();
                return;
            }

            Vector3 mouth = stalker.transform.position + Vector3.up * 1.35f;
            if (murmurs != null) murmurs.transform.position = mouth;
            if (voice != null) voice.transform.position = mouth;
            if (crackle != null) crackle.transform.position = mouth;
            if (listener != null && Time.time >= nextObstructionCheck)
            {
                nextObstructionCheck = Time.time + 0.3f;
                Vector3 direction = mouth - listener.position;
                bool blocked = Physics.Raycast(listener.position, direction.normalized, out RaycastHit hit,
                    Mathf.Max(0f, direction.magnitude - 0.65f), obstructionMask, QueryTriggerInteraction.Ignore);
                obstructionGain = blocked ? 0.72f : 1f;
                float cutoff = blocked ? 950f : 3800f;
                if (murmurFilter != null) murmurFilter.cutoffFrequency = cutoff;
                if (voiceFilter != null) voiceFilter.cutoffFrequency = cutoff;
            }
            float safetyGain = stalker.PlayerIsSafe ? 0.65f : 1f;
            float volume = Mathf.Lerp(0.14f, 0.48f, Mathf.Sqrt(threat)) * obstructionGain * safetyGain;
            if (murmurs != null)
            {
                murmurs.volume = volume * 0.58f;
                if (!murmurs.isPlaying && murmurs.clip != null) murmurs.Play();
            }
            if (crackle != null)
            {
                crackle.volume = Mathf.Lerp(0.05f, 0.18f, threat) * obstructionGain;
                if (threat <= 0.02f) crackle.Stop();
                else if (!crackle.isPlaying && staticBursts != null && staticBursts.Length > 0 && Time.time >= nextStatic)
                {
                    int index = Random.Range(0, staticBursts.Length);
                    if (staticBursts.Length > 1 && index == lastStatic) index = (index + 1) % staticBursts.Length;
                    lastStatic = index;
                    crackle.clip = staticBursts[index];
                    crackle.pitch = Random.Range(0.94f, 1.06f);
                    crackle.Play();
                    nextStatic = Time.time + Mathf.Lerp(Random.Range(6f, 12f), Random.Range(2.5f, 6f), threat);
                }
            }
            if (voice == null) return;
            voice.volume = volume;
            if (!voice.isPlaying && mutters != null && mutters.Length > 0 && Time.time >= nextMutter)
            {
                int index = Random.Range(0, mutters.Length);
                if (mutters.Length > 1 && index == lastMutter) index = (index + 1) % mutters.Length;
                lastMutter = index;
                voice.clip = mutters[index];
                voice.pitch = Random.Range(0.83f, 0.94f);
                voice.Play();
                nextMutter = Time.time + voice.clip.length / voice.pitch + Random.Range(1.5f, 4f);
            }
        }

        private void StopWhispers()
        {
            if (murmurs != null) murmurs.Stop();
            if (voice != null) voice.Stop();
            if (crackle != null) crackle.Stop();
        }

        private void OnDisable()
        {
            if (session != null) session.StateChanged -= OnStateChanged;
            StopWhispers();
            if (captureSound != null) captureSound.Stop();
        }
    }
}
