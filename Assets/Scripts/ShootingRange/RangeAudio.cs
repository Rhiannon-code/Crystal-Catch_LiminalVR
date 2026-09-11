using UnityEngine;

namespace IntuitiveDesigns.ShootingRange
{
    public class RangeAudio : MonoBehaviour
    {
        [SerializeField] private RangeGame game;
        [SerializeField] private AudioClip countdownTickClip;
        [SerializeField] private AudioClip roundStartClip;
        [SerializeField] private AudioClip roundEndClip;
        [SerializeField, Range(0f, 1f)] private float volume = 0.75f;

        private AudioSource _audio;

        private void Awake()
        {
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 0f;
        }

        private void OnEnable()
        {
            if (game == null) return;

            game.CountdownTick += OnTick;
            game.RoundStarted += OnRoundStarted;
            game.RoundEnded += OnRoundEnded;
        }

        private void OnDisable()
        {
            if (game == null) return;

            game.CountdownTick -= OnTick;
            game.RoundStarted -= OnRoundStarted;
            game.RoundEnded -= OnRoundEnded;
        }

        private void OnTick(int n) { Play(countdownTickClip); }
        private void OnRoundStarted(int round) { Play(roundStartClip); }
        private void OnRoundEnded(int round, int roundScore) { Play(roundEndClip); }

        private void Play(AudioClip clip)
        {
            if (clip != null && _audio != null) _audio.PlayOneShot(clip, volume);
        }
    }
}
