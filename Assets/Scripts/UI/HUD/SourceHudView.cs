using FPLab.Gameplay.Session;
using FPLab.Gameplay.Player.Equipment;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace FPLab.UI.HUD
{
    [DisallowMultipleComponent]
    public sealed class SourceHudView : MonoBehaviour
    {
        private static readonly Color NormalColor = new Color32(255, 220, 0, 220);
        private static readonly Color LowColor = new Color32(255, 0, 0, 220);
        private const float ColorTransitionSeconds = 0.4f;

        [SerializeField] private EscapeSession session;
        [SerializeField, FormerlySerializedAs("stamina")] private PlayerAuxPower auxPower;
        [SerializeField] private PlayerMotor motor;
        [SerializeField] private PlayerFlashlight flashlight;
        [SerializeField] private RectTransform auxPanel;
        [SerializeField] private Image[] segments;
        [SerializeField] private Text auxCaption;
        [SerializeField] private Text sprintCaption;
        [SerializeField] private Text flashlightCaption;
        [SerializeField] private Text minutes;
        [SerializeField] private Text seconds;

        private Color transitionFrom;
        private Color currentColor;
        private float transitionTime;
        private bool lowPower;
        private int displayedSecond = -1;

        private void OnEnable()
        {
            lowPower = auxPower.NormalizedPower < 0.25f;
            currentColor = lowPower ? LowColor : NormalColor;
            transitionFrom = currentColor;
            transitionTime = ColorTransitionSeconds;
            displayedSecond = -1;
            Refresh();
        }

        private void LateUpdate() => Refresh();

        private void Refresh()
        {
            float power = Mathf.Clamp01(auxPower.NormalizedPower);
            bool low = power < 0.25f;
            if (low != lowPower)
            {
                lowPower = low;
                transitionFrom = currentColor;
                transitionTime = 0f;
            }
            transitionTime += Time.deltaTime;
            currentColor = Color.Lerp(transitionFrom, lowPower ? LowColor : NormalColor,
                Mathf.Clamp01(transitionTime / ColorTransitionSeconds));

            int filled = Mathf.RoundToInt(power * segments.Length);
            for (int i = 0; i < segments.Length; i++)
            {
                Color color = currentColor;
                if (i >= filled) color.a = 70f / 255f;
                segments[i].color = color;
            }
            auxCaption.color = currentColor;
            sprintCaption.color = currentColor;
            bool sprinting = motor.IsSprinting && !auxPower.IsSprintExhausted;
            bool lighting = flashlight != null && flashlight.IsOn;
            sprintCaption.gameObject.SetActive(sprinting);
            flashlightCaption.gameObject.SetActive(lighting);
            flashlightCaption.color = currentColor;
            flashlightCaption.rectTransform.anchoredPosition = new Vector2(18f, -(sprinting ? 34f : 22f) * 2.25f);
            float panelHeight = (sprinting && lighting ? 48f : sprinting || lighting ? 36f : 26f) * 2.25f;
            auxPanel.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, panelHeight);

            int elapsed = Mathf.FloorToInt(session.ElapsedSeconds);
            if (elapsed == displayedSecond) return;
            displayedSecond = elapsed;
            minutes.text = (elapsed / 60).ToString("00");
            seconds.text = (elapsed % 60).ToString("00");
        }
    }
}
