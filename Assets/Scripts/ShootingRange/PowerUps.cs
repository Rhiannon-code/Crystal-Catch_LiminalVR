using System;
using UnityEngine;
using Liminal.SDK.VR.Avatars;

namespace IntuitiveDesigns.ShootingRange
{
    public enum PowerUpKind
    {
        FullAuto,
        Scattershot,
        DualWield,
        SlowMotion,
    }

    /// Which power-ups are live and for how long. Awarded on combo milestones and stacked freely
    /// Timers run in real seconds, or slow motion would stretch every other power up to 3x its length
    public class PowerUps : MonoBehaviour
    {
        [Serializable]
        public class Tuning
        {
            public PowerUpKind kind;
            public float seconds = 8f;
            public int weight = 3;
        }

        public static PowerUps Instance { get; private set; }

        [Header("Refs")]
        [SerializeField] private RangeGame game;
        [SerializeField] private ComboTracker combo;
        [SerializeField] private Pistol mainPistol;
        [SerializeField] private Pistol offHandPistol;
        [SerializeField] private SlowMotion slowMotion;

        [Header("Durations and odds (data)")]
        [SerializeField]
        private Tuning[] tuning =
        {
            new Tuning { kind = PowerUpKind.FullAuto,    seconds = 8f,  weight = 3 },
            new Tuning { kind = PowerUpKind.Scattershot, seconds = 8f,  weight = 3 },
            new Tuning { kind = PowerUpKind.DualWield,   seconds = 10f, weight = 3 },
            new Tuning { kind = PowerUpKind.SlowMotion,  seconds = 3f,  weight = 1 },
        };

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

        private static readonly int KindCount = Enum.GetValues(typeof(PowerUpKind)).Length;
        private readonly float[] _remaining = new float[KindCount];

        public static string Label(PowerUpKind kind)
        {
            switch (kind)
            {
                case PowerUpKind.FullAuto: return "FULL AUTO";
                case PowerUpKind.Scattershot: return "SCATTERSHOT";
                case PowerUpKind.DualWield: return "DUAL WIELD";
                case PowerUpKind.SlowMotion: return "SLOW-MO";
                default: return kind.ToString().ToUpperInvariant();
            }
        }

        private void Awake()
        {
            Instance = this;
            if (offHandPistol != null) offHandPistol.gameObject.SetActive(false);
        }

        private void OnEnable()
        {
            if (combo != null) combo.MilestoneReached += OnMilestone;
            if (game == null) return;

            game.RoundEnded += OnRoundEnded;
            game.StateChanged += OnStateChanged;
        }

        private void OnDisable()
        {
            // SlowMotion restores time in its own OnDisable, so teardown only has to drop the timers
            for (int i = 0; i < KindCount; i++) _remaining[i] = 0f;

            if (combo != null) combo.MilestoneReached -= OnMilestone;
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

        /// A repeat refreshes to full length rather than banking another stretch on top
        public void Grant(PowerUpKind kind)
        {
            var entry = Find(kind);
            float seconds = entry != null ? entry.seconds : 8f;
            bool wasActive = IsActive(kind);

            _remaining[(int)kind] = seconds;

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
            if (kind == PowerUpKind.DualWield)
            {
                SetOffHand(on);
                return;
            }

            if (kind != PowerUpKind.SlowMotion || slowMotion == null) return;

            if (on) slowMotion.Enter(slowScale);
            else slowMotion.Exit();
        }

        private void SetOffHand(bool on)
        {
            if (offHandPistol == null) return;

            if (on && mainPistol != null)
            {
                offHandPistol.AssignHand(mainPistol.Hand == VRAvatarLimbType.LeftHand
                                         ? VRAvatarLimbType.RightHand
                                         : VRAvatarLimbType.LeftHand);
            }

            offHandPistol.gameObject.SetActive(on);
            if (on) offHandPistol.SetHeld(true);
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
