using UnityEngine;

namespace IntuitiveDesigns.CrystalCatch
{
    public enum CrystalColour { Blue, Green, Purple, Gold }

    [RequireComponent(typeof(Collider))]
    [RequireComponent(typeof(FallingMover))]
    public class Crystal : MonoBehaviour
    {
        [Header("Identity (data)")]
        [SerializeField] private CrystalColour colour = CrystalColour.Blue;
        [SerializeField] private int value = 1;              // Blue 1 / Green 3 / Purple 5 / Gold 10

        [Header("Feedback")]
        [SerializeField] private AudioSource chime;          // Pitch scales with value (brighter = rarer)
        [SerializeField] private ParticleSystem catchBurst;
        [SerializeField] private float chimeMinPitch = 0.9f; // Low value crystals
        [SerializeField] private float chimeMaxPitch = 1.6f; // High value crystals
        [SerializeField] private int chimeValueForMaxPitch = 10; // Gold

        public CrystalColour Colour => colour;
        public int Value => value;

        private CrystalSpawner _pool;
        private FallingMover _mover;
        private bool _consumed;

        private void Awake()
        {
            _mover = GetComponent<FallingMover>();
            _mover.Passed += OnPassedPlayer;   // Fell past the cart -> miss
        }

        private void OnDestroy()
        {
            if (_mover != null) _mover.Passed -= OnPassedPlayer;
        }

        public void Configure(CrystalSpawner pool) => _pool = pool;
        public void SetValue(int points) => value = points;
        public void Launch(float fallSpeed, float despawnY, CrystalCatchGame game)
        {
            _consumed = false;
            _mover.Launch(fallSpeed, despawnY, game);
        }

        public void Collect(CrystalCatchGame game)
        {
            Hit(game, Vector3.zero);
        }

        public void Hit(CrystalCatchGame game, Vector3 hitVelocity)
        {
            if (_consumed) return;
            _consumed = true;
            _mover.Stop();

            game.AddScore(value);

            if (CatchFXPool.Instance != null)
            {
                CatchFXPool.Instance.PlayCrystal(colour, transform.position, PitchForValue(), hitVelocity);
            }
            else
            {
                // Fallback for a scene with no FX pool
                if (chime != null) { chime.pitch = PitchForValue(); chime.Play(); }
                if (catchBurst != null) catchBurst.Play();
            }

            Despawn();
        }

        private void OnPassedPlayer()
        {
            if (_consumed) return;   // Already caught this frame
            Despawn();               // A clean miss, no penalty
        }

        // Higher value (rarer) crystals chime brighter, an audible value cue that reinforces the colour
        private float PitchForValue()
        {
            float t = Mathf.InverseLerp(1f, Mathf.Max(2, chimeValueForMaxPitch), value);
            return Mathf.Lerp(chimeMinPitch, chimeMaxPitch, t);
        }

        private void Despawn()
        {
            if (_pool != null) _pool.Return(this);
            else gameObject.SetActive(false);
        }
    }
}
