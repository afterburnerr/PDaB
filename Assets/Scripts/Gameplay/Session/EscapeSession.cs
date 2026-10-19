using System;
using System.Collections;
using FPLab.Gameplay.Maze;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace FPLab.Gameplay.Session
{
    public enum RunState
    {
        Ready,
        Playing,
        Paused,
        Won,
        Loading,
        Caught,
        Lost
    }

    [DisallowMultipleComponent]
    public sealed class EscapeSession : MonoBehaviour
    {
        [SerializeField] private MazeBuilder maze;
        [SerializeField] private PlayerMotor playerMotor;
        [SerializeField] private PlayerLook playerLook;
        [SerializeField, Min(3f)] private float minimumLoadingSeconds = 3f;
        [SerializeField, Min(0.5f)] private float captureDuration = 2.4f;

        private static bool beginAfterReload;

        public RunState State { get; private set; } = RunState.Ready;
        public float ElapsedSeconds { get; private set; }
        public event Action StateChanged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetPendingRun()
        {
            beginAfterReload = false;
        }

        private void Awake()
        {
            Time.timeScale = 1f;
            if (maze != null)
                maze.PlayerExited += OnPlayerExited;
        }

        private void Start()
        {
            bool begin = beginAfterReload;
            beginAfterReload = false;
            SetState(RunState.Ready);
            if (begin)
                BeginRun();
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                if (State == RunState.Playing)
                    Pause();
                else if (State == RunState.Paused)
                    Resume();
            }

            if (State == RunState.Playing)
                ElapsedSeconds += Time.deltaTime;
        }

        public void BeginRun()
        {
            if (State != RunState.Ready)
                return;

            ElapsedSeconds = 0f;
            SetState(RunState.Loading);
            StartCoroutine(GenerateRun());
        }

        private IEnumerator GenerateRun()
        {
            double startedAt = Time.realtimeSinceStartupAsDouble;
            float minimumDuration = Mathf.Max(3f, minimumLoadingSeconds);
            // Let the loading interface render before synchronous generation.
            yield return null;
            yield return null;

            Exception generationError = null;
            try
            {
                if (maze == null)
                    throw new InvalidOperationException("Assign the maze before starting a run.");
                maze.Generate();
            }
            catch (Exception exception)
            {
                generationError = exception;
            }

            if (generationError != null)
            {
                Debug.LogException(generationError, this);
                SetState(RunState.Ready);
                yield break;
            }

            while (Time.realtimeSinceStartupAsDouble - startedAt < minimumDuration)
                yield return null;

            SetState(RunState.Playing);
        }

        public void Pause()
        {
            if (State == RunState.Playing)
                SetState(RunState.Paused);
        }

        public void Resume()
        {
            if (State == RunState.Paused)
                SetState(RunState.Playing);
        }

        public void CatchPlayer()
        {
            if (State != RunState.Playing) return;
            SetState(RunState.Caught);
            StartCoroutine(FinishCapture());
        }

        private IEnumerator FinishCapture()
        {
            yield return new WaitForSecondsRealtime(captureDuration);
            if (State == RunState.Caught) SetState(RunState.Lost);
        }

        // Reloading resets the generated maze, player stamina and camera together.
        public void RestartRun()
        {
            ReloadScene(true);
        }

        public void ReturnToMenu()
        {
            ReloadScene(false);
        }

        private static void ReloadScene(bool begin)
        {
            beginAfterReload = begin;
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        private void OnPlayerExited(Transform exitedPlayer)
        {
            if (State == RunState.Playing)
                SetState(RunState.Won);
        }

        private void SetState(RunState state)
        {
            State = state;
            bool playing = state == RunState.Playing;
            if (playerMotor != null)
                playerMotor.enabled = playing;
            if (playerLook != null)
                playerLook.enabled = playing;
            Time.timeScale = playing ? 1f : 0f;
            bool lockCursor = playing || state == RunState.Caught;
            Cursor.lockState = lockCursor ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !lockCursor;
            StateChanged?.Invoke();
        }

        private void OnDestroy()
        {
            if (maze != null)
                maze.PlayerExited -= OnPlayerExited;
            Time.timeScale = 1f;
        }
    }
}
