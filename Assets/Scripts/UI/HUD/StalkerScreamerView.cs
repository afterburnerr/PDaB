using FPLab.Gameplay.Session;
using UnityEngine;
using UnityEngine.UI;

namespace FPLab.UI.HUD
{
    // A single still illustration cuts in on capture; no camera movement or animation.
    [DisallowMultipleComponent]
    public sealed class StalkerScreamerView : MonoBehaviour
    {
        [SerializeField] private EscapeSession session;
        [SerializeField] private Image image;

        private void OnEnable()
        {
            if (session != null) session.StateChanged += Refresh;
            Refresh();
        }

        private void Refresh()
        {
            if (image == null) return;
            bool caught = session != null && session.State == RunState.Caught;
            bool lost = session != null && session.State == RunState.Lost;
            image.enabled = caught || lost;
            image.raycastTarget = false;
            image.color = caught ? Color.white : new Color(0.38f, 0.34f, 0.34f, 1f);
        }

        private void OnDisable()
        {
            if (session != null) session.StateChanged -= Refresh;
            if (image != null) image.enabled = false;
        }
    }
}
