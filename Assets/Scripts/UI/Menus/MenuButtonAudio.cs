using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FPLab.UI.Menus
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Button))]
    public sealed class MenuButtonAudio : MonoBehaviour, IPointerEnterHandler, IPointerDownHandler, IPointerClickHandler, ISubmitHandler
    {
        [SerializeField] private AudioClip hoverClip;
        [SerializeField] private AudioClip pressClip;
        [SerializeField] private AudioClip clickClip;
        [SerializeField, Range(0f, 1f)] private float hoverVolume = 0.4f;
        [SerializeField, Range(0f, 1f)] private float clickVolume = 0.6f;

        private static AudioSource source;
        private Button button;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSource()
        {
            source = null;
        }

        private void Awake()
        {
            button = GetComponent<Button>();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (button.IsInteractable())
                Play(hoverClip, hoverVolume);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left && button.IsInteractable())
                Play(clickClip, clickVolume);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left && button.IsInteractable())
                Play(pressClip, clickVolume);
        }

        public void OnSubmit(BaseEventData eventData)
        {
            if (button.IsInteractable())
                Play(clickClip, clickVolume);
        }

        private static void Play(AudioClip clip, float volume)
        {
            if (clip == null)
                return;

            if (source == null)
            {
                var audioObject = new GameObject("Menu Button Audio");
                DontDestroyOnLoad(audioObject);
                source = audioObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;
                source.ignoreListenerPause = true;
            }

            // The menu can hide or reload its scene before this handler runs.
            source.PlayOneShot(clip, volume);
        }
    }
}
