using System;
using UnityEngine;

namespace IntuitiveDesigns.ShootingRange
{
    public enum PowerUpKind
    {
        FullAuto,
        Scattershot,
        SlowMotion,
    }

    public class PowerUps : MonoBehaviour
    {
        [Serializable]
        public class Tuning
        {
            public PowerUpKind kind;
            public float seconds = 8f;
            public int weight = 3;
            public Color colour = Color.white;

            // Off for slow motion, or a steady hit rate would hold the world slowed indefinitely
            public bool extendsOnCombo = true;
        }

        public static PowerUps Instance { get; private set; }
        public static readonly int KindCount = Enum.GetValues(typeof(PowerUpKind)).Length;

        [Header("Refs")]
        [SerializeField] private RangeGame game;
        [SerializeField] private ComboTracker combo;
        [SerializeField] private SlowMotion slowMotion;

        [Header("Durations, odds and colours (data)")]
        [SerializeField]
        private Tuning[] tuning =
        {
            new Tuning { kind = PowerUpKind.FullAuto,    seconds = 8f, weight = 3, colour = new Color(1f, 0.55f, 0.05f) },
            new Tuning { kind = PowerUpKind.Scattershot, seconds = 8f, weight = 3, colour = new Color(0.35f, 1f, 0.2f) },
            new Tuning { kind = PowerUpKind.SlowMotion,  seconds = 3f, weight = 1, colour = new Color(0.7f, 0.45f, 1f), extendsOnCombo = false },
        };

        [Header("Kept alive by the combo (data)")]
        [SerializeField] private float secondsPerHit = 0.5f;
        [SerializeField] private float maxLengthMultiple = 2f;

        [Header("Scattershot (data)")]
        [SerializeField] private int pellets = 5;
        [SerializeField] private float pelletSpreadDegrees = 6f;

        [Header("Slow motion (data)")]
        [SerializeField, Range(0.05f, 1f)] private float slowScale = 0.3f;

        public event Action<PowerUpKind, float> Granted;
        public event Action<PowerUpKind> Expired;

        public bool FullAuto { get { return IsActive(PowerUpKind.FullAuto); } }
        public bool BottomlessMagazine { get { return IsActive(PowerUpKind.FullAuto); } }
        public int Pellets { get { return IsActive(PowerUpKind.Scattershot) ? Mathf.Max(1, pellets) : 1; } }
        public float PelletSpread { get { return pelletSpreadDegrees; } }

        private readonly float[] _remaining = new float[KindCount];
        private readonly float[] _grantedAt = new float[KindCount];

        public static string Label(PowerUpKind kind)
        {
            switch (kind)
            {
                case PowerUpKind.FullAuto: return "FULL AUTO";
                case PowerUpKind.Scattershot: return "SCATTERSHOT";
                case PowerUpKind.SlowMotion: return "SLOW-MO";
                default: return kind.ToString().ToUpperInvariant();
            }
        }

        private void Awake()
        {
            Instance = this;
        }

        private void OnEnable()
        {
            if (combo != null)
            {
                combo.MilestoneReached += OnMilestone;
                combo.Changed += OnComboChanged;
            }

            if (game == null) return;

            game.RoundEnded += OnRoundEnded;
            game.StateChanged += OnStateChanged;
        }

        private void OnDisable()
        {
            // SlowMotion restores time in its own OnDisable
            for (int i = 0; i < KindCount; i++) _remaining[i] = 0f;

            if (combo != null)
            {
                combo.MilestoneReached -= OnMilestone;
                combo.Changed -= OnComboChanged;
            }

            if (game == null) return;

            game.RoundEnded -= OnRoundEnded;
            game.StateChanged -= OnStateChanged;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public bool IsActive(PowerUpKind kind)
        {
            return _remaining[(int)kind] > 0f;
        }

        public float Remaining(PowerUpKind kind)
        {
            return Mathf.Max(0f, _remaining[(int)kind]);
        }

        public Color Colour(PowerUpKind kind)
        {
            var entry = Find(kind);
            return entry != null ? entry.colour : Color.white;
        }

        /// The newest of the live power ups, which is the one the gun wears
        public bool TryLatest(out PowerUpKind latest)
        {
            latest = PowerUpKind.FullAuto;
            bool found = false;

            for (int i = 0; i < KindCount; i++)
            {
                if (_remaining[i] <= 0f || (found && _grantedAt[i] <= _grantedAt[(int)latest])) continue;

                latest = (PowerUpKind)i;
                found = true;
            }

            return found;
        }

        /// A repeat adds its full length on top of what is left, up to the cap
        public void Grant(PowerUpKind kind)
        {
            float seconds = BaseSeconds(kind);
            bool wasActive = IsActive(kind);

            AddTime((int)kind, seconds);
            _grantedAt[(int)kind] = Time.unscaledTime;

            if (!wasActive) Apply(kind, true);
            if (Granted != null) Granted(kind, seconds);
        }

        public void ClearAll()
        {
            for (int i = 0; i < KindCount; i++)
            {
                if (_remaining[i] <= 0f) continue;
                Expire((PowerUpKind)i);
            }
        }

        private void Update()
        {
            for (int i = 0; i < KindCount; i++)
            {
                if (_remaining[i] <= 0f) continue;

                _remaining[i] -= Time.unscaledDeltaTime;
                if (_remaining[i] <= 0f) Expire((PowerUpKind)i);
            }
        }

        private void Expire(PowerUpKind kind)
        {
            _remaining[(int)kind] = 0f;
            Apply(kind, false);
            if (Expired != null) Expired(kind);
        }

        private void OnMilestone(int chain)
        {
            if (game != null && game.Current != RangeGame.State.Playing) return;
            Grant(Roll());
        }

        private void OnComboChanged(float multiplier, int chain)
        {
            if (chain == 0) return;

            for (int i = 0; i < KindCount; i++)
            {
                var entry = Find((PowerUpKind)i);
                if (_remaining[i] > 0f && entry != null && entry.extendsOnCombo) AddTime(i, secondsPerHit);
            }
        }

        private void AddTime(int index, float seconds)
        {
            float cap = BaseSeconds((PowerUpKind)index) * maxLengthMultiple;
            _remaining[index] = Mathf.Min(Mathf.Max(0f, _remaining[index]) + seconds, cap);
        }

        private void OnRoundEnded(int round, int roundScore)
        {
            ClearAll();
        }

        private void OnStateChanged(RangeGame.State state)
        {
            if (state == RangeGame.State.Ended) ClearAll();
        }

        private void Apply(PowerUpKind kind, bool on)
        {
            if (kind != PowerUpKind.SlowMotion || slowMotion == null) return;

            if (on) slowMotion.Enter(slowScale);
            else slowMotion.Exit();
        }

        private PowerUpKind Roll()
        {
            int total = 0;
            for (int i = 0; i < tuning.Length; i++) total += Mathf.Max(0, tuning[i].weight);
            if (total <= 0) return PowerUpKind.FullAuto;

            int pick = UnityEngine.Random.Range(0, total);
            for (int i = 0; i < tuning.Length; i++)
            {
                pick -= Mathf.Max(0, tuning[i].weight);
                if (pick < 0) return tuning[i].kind;
            }

            return tuning[tuning.Length - 1].kind;
        }

        private float BaseSeconds(PowerUpKind kind)
        {
            var entry = Find(kind);
            return entry != null ? entry.seconds : 8f;
        }

        private Tuning Find(PowerUpKind kind)
        {
            for (int i = 0; i < tuning.Length; i++)
            {
                if (tuning[i] != null && tuning[i].kind == kind) return tuning[i];
            }

            return null;
        }
    }
}
