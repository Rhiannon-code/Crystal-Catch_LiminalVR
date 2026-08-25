using UnityEngine;

namespace IntuitiveDesigns.CrystalCatch
{
    public enum SpecialKind
    {
        // Power ups
        TimeOrb,        // +15s
        Shield,         // Hazard immunity (30s)
        ScoreGem,       // 2x score for 10s
        CrashCrystals,  // Spawns a burst of random crystals

        // Hazards
        TimeDrainClock, // -15s
        Bomb,           // Swings pass through for 3s (bat ghosts so it reads as inflicted, not broken)
        SlowHourglass,  // Slow Time, slows the FALL, leaving items overhead and out of reach

        // Power ups added for the bat design
        ReachBoost,     // Longer bat
        ArcBoost        // Wider swing arc
    }

    [RequireComponent(typeof(Collider))]
    [RequireComponent(typeof(FallingMover))]
    public class SpecialItem : MonoBehaviour
    {
        [Header("Identity (data)")]
        [SerializeField] private SpecialKind kind = SpecialKind.TimeOrb;

        [Header("Tunable magnitudes")]
        [SerializeField] private float timeDelta = 15f;      // TimeOrb (+15)/TimeDrainClock (-15)
        [SerializeField] private float shieldSeconds = 30f;  // Shield duration
        [SerializeField] private float bombSeconds = 3f;     // Bomb, no collection for 3s
        [SerializeField] private float scoreMultiplier = 2f; // ScoreGem
        [SerializeField] private float multiplierSeconds = 10f;
        [SerializeField] private int burstCount = 5;         // Crash Crystals
        [SerializeField] private float slowFallSeconds = 4f;  // Slow Time window
        [SerializeField] private float slowFallScale = 0.45f; // How much the fall slows (below 1)
        [SerializeField] private float reachMultiplier = 1.6f;
        [SerializeField] private float reachSeconds = 10f;
        [SerializeField] private float arcMultiplier = 2.0f;
        [SerializeField] private float arcSeconds = 10f;

        [Header("Feedback")]
        [SerializeField] private AudioSource sfx;
        [SerializeField] private ParticleSystem burst;
        [SerializeField] private AudioSource approachCue;
        [SerializeField] private AudioSource shieldBlockedCue;

        public bool IsHazard =>
            kind == SpecialKind.TimeDrainClock || kind == SpecialKind.Bomb || kind == SpecialKind.SlowHourglass;

        private CrystalCatchGame _game;
        private CrystalSpawner _pool;
        private FallingMover _mover;
        private bool _consumed;

        private void Awake()
        {
            _mover = GetComponent<FallingMover>();
            _mover.Passed += OnPassedPlayer;
        }

        private void OnDestroy()
        {
            if (_mover != null) _mover.Passed -= OnPassedPlayer;
        }

        public void Configure(CrystalCatchGame game, CrystalSpawner pool)
        {
            _game = game;
            _pool = pool;
            _consumed = false;
        }

        public void Launch(float fallSpeed, float despawnY, CrystalCatchGame game)
        {
            _consumed = false;
            _mover.Launch(fallSpeed, despawnY, game);
            if (approachCue != null) approachCue.Play();   // Telegraph incoming
        }

        private void OnPassedPlayer()
        {
            if (_consumed) return;
            _mover.Stop();
            if (_pool != null) _pool.Return(this);
            else gameObject.SetActive(false);
        }

        /// Legacy touch collection entry point, kept so the old HandCollector still compiles
        public void Collect(CrystalCatchGame game)
        {
            Hit(game, Vector3.zero);
        }

        /// Called by BatSwinger. A shielded swing at a hazard safely shatters it
        public void Hit(CrystalCatchGame game, Vector3 hitVelocity)
        {
            if (_consumed) return;
            _consumed = true;
            _mover.Stop();

            // Shield fizzles hazards on contact (the game manager's hazard methods also no op while shielded)
            bool blocked = IsHazard && game.IsShielded;
            Apply(game);

            // Detached pooled FX, Return() below deactivates this GameObject in the same frame, so a
            // child ParticleSystem/AudioSource would be killed before it played
            if (!blocked && CatchFXPool.Instance != null)
            {
                CatchFXPool.Instance.PlaySpecial(transform.position, hitVelocity);
            }
            else
            {
                if (blocked && shieldBlockedCue != null) shieldBlockedCue.Play();
                else if (sfx != null) sfx.Play();
                if (!blocked && burst != null) burst.Play();
            }

            if (_pool != null) _pool.Return(this);
            else gameObject.SetActive(false);
        }

        private void Apply(CrystalCatchGame g)
        {
            switch (kind)
            {
                case SpecialKind.TimeOrb:        g.AddTime(+Mathf.Abs(timeDelta)); break;
                case SpecialKind.Shield:         g.SetShield(shieldSeconds); break;
                case SpecialKind.ScoreGem:       g.SetScoreMultiplier(scoreMultiplier, multiplierSeconds); break;
                case SpecialKind.CrashCrystals:  g.SpawnBurst(burstCount); break;
                case SpecialKind.ReachBoost:     g.SetReachBoost(reachMultiplier, reachSeconds); break;
                case SpecialKind.ArcBoost:       g.SetArcBoost(arcMultiplier, arcSeconds); break;

                // Hazards, the manager no-ops these while shielded
                case SpecialKind.TimeDrainClock: g.ApplyHazardTime(-Mathf.Abs(timeDelta)); break;
                case SpecialKind.Bomb:           g.DisableCollection(bombSeconds); break;
                case SpecialKind.SlowHourglass:  g.SlowFalling(slowFallScale, slowFallSeconds); break;
            }
        }
    }
}
