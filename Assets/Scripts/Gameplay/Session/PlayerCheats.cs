using FPLab.Gameplay.Enemies;
using FPLab.Gameplay.Maze;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FPLab.Gameplay.Session
{
    [DisallowMultipleComponent]
    public sealed class PlayerCheats : MonoBehaviour
    {
        [SerializeField] private EscapeSession session;
        [SerializeField] private MazeBuilder maze;
        [SerializeField] private EntranceStalker stalker;
        [SerializeField] private Transform player;
        private string recent = "";
        private float lastLetterAt;

        private void Update()
        {
            if (session == null || session.State != RunState.Playing)
            {
                recent = "";
                return;
            }
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;
            // Physical A-Z keys also work with a Russian keyboard layout.
            foreach (var key in keyboard.allKeys)
            {
                int code = (int)key.keyCode;
                if (code < (int)Key.A || code > (int)Key.Z || !key.wasPressedThisFrame) continue;
                if (Time.unscaledTime - lastLetterAt > 3f) recent = "";
                lastLetterAt = Time.unscaledTime;
                recent += (char)('a' + code - (int)Key.A);
                if (recent.Length > 4) recent = recent.Substring(recent.Length - 4);
                if (recent.EndsWith("easy", System.StringComparison.Ordinal))
                {
                    stalker?.DisableForRun();
                    recent = "";
                }
                else if (recent.EndsWith("tp", System.StringComparison.Ordinal))
                {
                    TeleportToExit();
                    recent = "";
                }
            }
        }

        private void TeleportToExit()
        {
            if (maze == null || maze.Data == null || player == null) return;
            CharacterController controller = player.GetComponent<CharacterController>();
            bool wasEnabled = controller != null && controller.enabled;
            float feetOffset = controller == null ? 1f
                : (Mathf.Max(controller.height * 0.5f, controller.radius) - controller.center.y) * player.lossyScale.y;
            Vector3 direction = maze.Data.ExitWall == MazeWall.North ? Vector3.forward
                : maze.Data.ExitWall == MazeWall.South ? Vector3.back
                : maze.Data.ExitWall == MazeWall.East ? Vector3.right : Vector3.left;
            Vector3 outward = maze.transform.TransformDirection(direction);
            if (wasEnabled) controller.enabled = false;
            player.position = maze.ExitPosition - outward * maze.CellSize + Vector3.up * feetOffset;
            player.rotation = Quaternion.LookRotation(outward, Vector3.up);
            if (wasEnabled) controller.enabled = true;
            Physics.SyncTransforms();
        }
    }
}
