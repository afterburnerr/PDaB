using FPLab.Gameplay.Maze;
using UnityEngine;

namespace FPLab.Gameplay.Session
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider), typeof(Rigidbody))]
    public sealed class MazeExitTrigger : MonoBehaviour
    {
        [SerializeField] private MazeBuilder maze;
        [SerializeField] private CharacterController playerController;

        public void Initialize(MazeBuilder owner, CharacterController controller)
        {
            maze = owner;
            playerController = controller;
            GetComponent<BoxCollider>().isTrigger = true;
            Rigidbody body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (playerController != null && other == playerController)
                maze.NotifyPlayerExited(playerController.transform);
        }
    }
}
