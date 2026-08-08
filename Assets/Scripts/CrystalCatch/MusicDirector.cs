using UnityEngine;

namespace IntuitiveDesigns.CrystalCatch
{
    public class MusicDirector : MonoBehaviour
    {
        [SerializeField] private CrystalCatchGame game;
        [SerializeField] private AudioSource baseLayer;      // Always audible, builds with time
        [SerializeField] private AudioSource performanceLayer; // Swells with multiplier

        [Header("Time floor (over session)")]
        [SerializeField] private AnimationCurve baseVolume =
            new AnimationCurve(new Keyframe(0f, 0.35f), new Keyframe(1f, 1f));

        [Header("Performance layer (over multiplier)")]
        [SerializeField] private float multiplierForFullLayer = 4f; // Multiplier at which the overlay is full
        [SerializeField] private float layerLerp = 4f;              // Smoothing so it swells, not snaps

        private float _perfVolume;

        private void OnEnable()
        {
            if (game != null) game.StateChanged += OnStateChanged;
        }

        private void OnDisable()
        {
            if (game != null) game.StateChanged -= OnStateChanged;
        }

        private void OnStateChanged(CrystalCatchGame.State s)
        {
            if (s == CrystalCatchGame.State.Playing)
            {
                if (baseLayer != null && !baseLayer.isPlaying) baseLayer.Play();
                if (performanceLayer != null && !performanceLayer.isPlaying) performanceLayer.Play();
            }
            else if (s == CrystalCatchGame.State.Ended)
            {
                // Let the end flourish/fade handle the tail, the ScreenFader takes the visuals
            }
        }

        private void Update()
        {
            if (game == null) return;

            // Time floor
            if (baseLayer != null && game.Current == CrystalCatchGame.State.Playing)
                baseLayer.volume = baseVolume.Evaluate(game.ElapsedNormalized);

            // Performance layer, smoothed
            float target = Mathf.Clamp01((game.Multiplier - 1f) / Mathf.Max(0.001f, multiplierForFullLayer - 1f));
            _perfVolume = Mathf.Lerp(_perfVolume, target, layerLerp * Time.deltaTime);
            if (performanceLayer != null) performanceLayer.volume = _perfVolume;
        }
    }
}
