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
        Bomb,           // Can't collect for 3s
        SlowHourglass   // Slow Time impairs the player's catching (precision harder) for a few seconds
    }

    [RequireComponent(typeof(Collider))]
    [RequireComponent(typeof(HomingMover))]
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
        [SerializeField] private float handImpairSeconds = 4f; // Slow Time, precision catching window

        [Header("Feedback")]
        [SerializeField] private AudioSource sfx;
        [SerializeField] private ParticleSystem burst;
        [SerializeField] private AudioSource approachCue;
        [SerializeField] private AudioSource shieldBlockedCue;

        public bool IsHazard =>
            kind == SpecialKind.TimeDrainClock || kind == SpecialKind.Bomb || kind == SpecialKind.SlowHourglass;

        private CrystalCatchGame _game;
        private CrystalSpawner _pool;
        private HomingMover _mover;
        private bool _consumed;

        private void Awake()
        {
            _mover = GetComponent<HomingMover>();
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

        public void Launch(Transform target, Vector3 startDir, float? speed = null)
        {
            _consumed = false;
            _mover.Launch(target, startDir, speed);
            if (approachCue != null) approachCue.Play();   // Telegraph incoming
        }

        private void OnPassedPlayer()
        {
            if (_consumed) return;
            _mover.Stop();
            if (_pool != null) _pool.Return(this);
            else gameObject.SetActive(false);
        }

        public void Collect(CrystalCatchGame game)
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
                CatchFXPool.Instance.PlaySpecial(transform.position);
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

                // Hazards — the manager no-ops these while shielded.
                case SpecialKind.TimeDrainClock: g.ApplyHazardTime(-Mathf.Abs(timeDelta)); break;
                case SpecialKind.Bomb:           g.DisableCollection(bombSeconds); break;
                case SpecialKind.SlowHourglass:  g.ImpairHands(handImpairSeconds); break;
            }
        }
    }
}
