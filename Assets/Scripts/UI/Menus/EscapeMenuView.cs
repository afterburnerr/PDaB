using FPLab.Gameplay.Session;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FPLab.UI.Menus
{
    // Presents a session; generation, victory and the clock belong to gameplay.
    [DisallowMultipleComponent]
    public sealed class EscapeMenuView : MonoBehaviour
    {
        private static readonly Color MenuInk = new Color(0.84f, 0.85f, 0.83f);
        [SerializeField] private EscapeSession session;
        [SerializeField] private GameObject menuRoot;
        [SerializeField] private GameObject loadingRoot;
        [SerializeField] private GameObject runHud;
        [SerializeField] private GameObject menuArtwork;
        [SerializeField] private GameObject deathShade;
        [SerializeField] private Text menuTitle;
        // Retained so existing scenes can hide the previous slogan during migration.
        [SerializeField] private Text description;
        [SerializeField] private Font introductionFont;
        [SerializeField] private Text result;
        [SerializeField] private Text primaryLabel;
        [SerializeField] private Button primaryButton;
        [SerializeField] private Button restartButton;
        [SerializeField] private Button menuButton;
        [SerializeField] private Button quitButton;

        private void OnEnable()
        {
            session.StateChanged += Refresh;
            primaryButton.onClick.AddListener(PrimaryAction);
            restartButton.onClick.AddListener(session.RestartRun);
            menuButton.onClick.AddListener(session.ReturnToMenu);
            quitButton.onClick.AddListener(Quit);
        }

        private void Start() => Refresh();

        private void Refresh()
        {
            RunState state = session.State;
            bool playing = state == RunState.Playing;
            bool loading = state == RunState.Loading;
            bool caught = state == RunState.Caught;
            bool lost = state == RunState.Lost;
            menuRoot.SetActive(!playing && !caught);
            if (menuArtwork != null) menuArtwork.SetActive(!caught && !lost);
            if (deathShade != null) deathShade.SetActive(lost);
            loadingRoot.SetActive(loading);
            primaryButton.transform.parent.gameObject.SetActive(!loading);
            runHud.SetActive(playing);
            restartButton.gameObject.SetActive(state == RunState.Paused);
            menuButton.gameObject.SetActive(state == RunState.Paused || state == RunState.Won || lost);
            result.gameObject.SetActive(state == RunState.Won || state == RunState.Paused || lost);
            if (description != null) description.gameObject.SetActive(false);

            if (menuTitle != null)
            {
                menuTitle.gameObject.SetActive(!playing && !loading && !caught && !lost);
                menuTitle.text = "PDaB";
                ConfigureText(menuTitle, 128, 170f, 245f, 850f, 170f);
            }
            ConfigureText(result, lost ? 32 : 22, 174f, lost ? 405f : 482f, 850f, lost ? 105f : 34f);
            if (lost) result.color = new Color(0.67f, 0.12f, 0.10f);
            ConfigureButtons(state);
            if (state == RunState.Ready)
                primaryLabel.text = "НОВАЯ ИГРА";
            else if (state == RunState.Paused)
            {
                primaryLabel.text = "ПРОДОЛЖИТЬ";
                result.text = "ВРЕМЯ   " + FormatTime(session.ElapsedSeconds);
            }
            else if (state == RunState.Won)
            {
                primaryLabel.text = "НАЧАТЬ ЗАНОВО";
                result.text = "ВЫХОД НАЙДЕН   " + FormatTime(session.ElapsedSeconds);
            }
            else if (lost)
            {
                primaryLabel.text = "НАЧАТЬ ЗАНОВО";
                result.text = "ТЕБЯ НАШЛИ\nВРЕМЯ   " + FormatTime(session.ElapsedSeconds);
            }
            AlignTitleWithButtons();

            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(null);
        }

        private void AlignTitleWithButtons()
        {
            if (menuTitle == null || introductionFont == null || string.IsNullOrEmpty(primaryLabel.text)) return;
            introductionFont.RequestCharactersInTexture("P", menuTitle.fontSize, FontStyle.Normal);
            introductionFont.RequestCharactersInTexture(primaryLabel.text.Substring(0, 1), primaryLabel.fontSize, FontStyle.Normal);
            if (!introductionFont.GetCharacterInfo('P', out CharacterInfo titleGlyph, menuTitle.fontSize, FontStyle.Normal)
                || !introductionFont.GetCharacterInfo(primaryLabel.text[0], out CharacterInfo buttonGlyph, primaryLabel.fontSize, FontStyle.Normal))
                return;
            Vector2 position = menuTitle.rectTransform.anchoredPosition;
            position.x += buttonGlyph.minX - titleGlyph.minX;
            menuTitle.rectTransform.anchoredPosition = position;
        }

        private void PrimaryAction()
        {
            switch (session.State)
            {
                case RunState.Ready: session.BeginRun(); break;
                case RunState.Paused: session.Resume(); break;
                case RunState.Won:
                case RunState.Lost: session.RestartRun(); break;
            }
        }

        private void ConfigureText(Text label, int fontSize, float x, float y, float width, float height)
        {
            label.font = introductionFont;
            label.fontStyle = FontStyle.Normal;
            label.fontSize = fontSize;
            label.color = MenuInk;
            label.alignment = TextAnchor.MiddleLeft;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;
            RectTransform rect = label.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
            foreach (Shadow shadow in label.GetComponents<Shadow>()) shadow.enabled = false;
        }

        private void ConfigureButtons(RunState state)
        {
            int buttonCount = state == RunState.Paused ? 4 : state == RunState.Won || state == RunState.Lost ? 3 : 2;
            var container = (RectTransform)primaryButton.transform.parent;
            container.anchorMin = container.anchorMax = new Vector2(0f, 1f);
            container.pivot = new Vector2(0f, 1f);
            container.anchoredPosition = new Vector2(170f, -530f);
            container.sizeDelta = new Vector2(450f, buttonCount * 46f);
            var layout = container.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlHeight = layout.childControlWidth = false;
            layout.childForceExpandHeight = layout.childForceExpandWidth = false;
            layout.spacing = 0f;
            restartButton.GetComponentInChildren<Text>(true).text = "НАЧАТЬ ЗАНОВО";
            menuButton.GetComponentInChildren<Text>(true).text = "ГЛАВНОЕ МЕНЮ";
            quitButton.GetComponentInChildren<Text>(true).text = "ВЫХОД";
            foreach (Transform child in container)
            {
                ((RectTransform)child).sizeDelta = new Vector2(450f, 46f);
                Text label = child.GetComponentInChildren<Text>(true);
                ConfigureText(label, 24, 0f, 0f, 450f, 46f);
                // The clear image retains a comfortable pointer target without a visible panel.
                Image hit = child.GetComponent<Image>();
                if (hit != null) hit.color = Color.clear;
                label.color = Color.white;
                Button button = child.GetComponent<Button>();
                button.targetGraphic = label;
                button.transition = Selectable.Transition.ColorTint;
                ColorBlock colors = button.colors;
                colors.normalColor = MenuInk;
                colors.highlightedColor = new Color(0.95f, 0.79f, 0.38f);
                colors.selectedColor = colors.normalColor;
                colors.pressedColor = new Color(0.71f, 0.57f, 0.27f);
                colors.disabledColor = new Color(0.38f, 0.39f, 0.37f);
                colors.colorMultiplier = 1f;
                colors.fadeDuration = 0.12f;
                button.colors = colors;
            }
            LayoutRebuilder.MarkLayoutForRebuild(container);
        }

        public static string FormatTime(float seconds)
        {
            int total = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return $"{total / 60:00}:{total % 60:00}";
        }

        private static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void OnDisable()
        {
            session.StateChanged -= Refresh;
            primaryButton.onClick.RemoveListener(PrimaryAction);
            restartButton.onClick.RemoveListener(session.RestartRun);
            menuButton.onClick.RemoveListener(session.ReturnToMenu);
            quitButton.onClick.RemoveListener(Quit);
        }
    }
}
