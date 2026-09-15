using System.Collections;
using UnityEngine;

namespace IntuitiveDesigns.ShootingRange
{
    public class MusicBed : MonoBehaviour
    {
        [SerializeField] private RangeGame game;
        [SerializeField] private AudioClip clip;
        [SerializeField, Range(0f, 1f)] private float volume = 0.7f;
        [SerializeField] private float fadeOutSeconds = 4f;

        private AudioSource _audio;

        private void Awake()
        {
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.clip = clip;
            _audio.loop = true;
            _audio.playOnAwake = false;
            _audio.spatialBlend = 0f;
            _audio.volume = volume;
        }

        private void OnEnable()
        {
            if (game != null) game.StateChanged += OnStateChanged;
        }

        private void OnDisable()
        {
            if (game != null) game.StateChanged -= OnStateChanged;
        }

        private void Start()
        {
            if (clip != null) _audio.Play();
        }

        private void OnStateChanged(RangeGame.State state)
        {
            if (state == RangeGame.State.Ended) StartCoroutine(FadeOut());
        }

        private IEnumerator FadeOut()
        {
            float start = _audio.volume;

            for (float t = 0f; t < fadeOutSeconds; t += Time.unscaledDeltaTime)
            {
                _audio.volume = Mathf.Lerp(start, 0f, t / fadeOutSeconds);
                yield return null;
            }

            _audio.Stop();
        }
    }
}
