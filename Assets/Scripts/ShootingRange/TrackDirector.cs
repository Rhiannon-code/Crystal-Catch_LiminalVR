using System;
using System.Collections.Generic;
using UnityEngine;

namespace IntuitiveDesigns.ShootingRange
{
    public class TrackDirector : MonoBehaviour
    {
        [Serializable]
        public class PatternUnlock
        {
            public TrackPatternKind pattern;
            public int fromRound = 1;
        }

        [Header("Refs")]
        [SerializeField] private TrackGrid grid;
        [SerializeField] private RangeGame game;
        [SerializeField] private TrackMover moverPrefab;

        [Header("Pool")]
        [SerializeField] private int poolSize = 32;

        [Header("Wave size (data)")]
        [SerializeField] private int waveSizeFirstRound = 30;
        [SerializeField] private int waveSizeGrowthPerRound = 20;

        [Header("Pace (data, round 1)")]
        [SerializeField] private float waveInterval = 2.2f;
        [SerializeField] private float minSpeed = 1.3f;
        [SerializeField] private float maxSpeed = 2.6f;
        [SerializeField] private int concurrentTargets = 4;

        [Header("Escalation per round (data)")]
        [SerializeField] private float waveScalePerRound = 0.82f;
        [SerializeField] private float speedScalePerRound = 1.12f;
        [SerializeField] private int extraConcurrentPerRound = 2;
        [SerializeField] private float minWaveInterval = 0.6f;

        [Header("Refusals (data)")]
        [SerializeField] private float retrySeconds = 0.75f;

        [Header("Patterns, and the round each is let loose")]
        [SerializeField]
        private PatternUnlock[] patterns =
        {
            new PatternUnlock { pattern = TrackPatternKind.Single,      fromRound = 1 },
            new PatternUnlock { pattern = TrackPatternKind.Gauntlet,    fromRound = 1 },
            new PatternUnlock { pattern = TrackPatternKind.Sweep,       fromRound = 2 },
            new PatternUnlock { pattern = TrackPatternKind.RoofRush,    fromRound = 3 },
            new PatternUnlock { pattern = TrackPatternKind.RiserVolley, fromRound = 4 },
            new PatternUnlock { pattern = TrackPatternKind.Crossfire,   fromRound = 4 },
            new PatternUnlock { pattern = TrackPatternKind.Cascade,     fromRound = 5 },
        };

        public event Action<TrackPatternKind> PatternStarted;
        public event Action<int> TargetsLeftChanged;

        public int ConcurrencyCap { get { return _concurrency; } }
        public TrackMover[] Movers { get { return _pool; } }
        public int WaveSize { get { return _waveSize; } }
        public int Killed { get { return _killed; } }
        public int TargetsLeft { get { return Mathf.Max(0, _waveSize - _resolved); } }
        public bool WaveCleared { get { return _waveSize > 0 && _resolved >= _waveSize; } }

        private struct Pending
        {
            public TrackRail Rail;
            public float Due;
            public float Speed;
        }

        private readonly List<TrackPattern.Launch> _launches = new List<TrackPattern.Launch>();
        private readonly List<Pending> _pending = new List<Pending>();
        private readonly List<TrackPatternKind> _unlocked = new List<TrackPatternKind>();

        private TrackMover[] _pool;
        private float _nextWave;
        private float _waveInterval;
        private float _speedScale = 1f;
        private int _concurrency;
        private int _waveSize;
        private int _launched;
        private int _resolved;
        private int _killed;

        private void Awake()
        {
            if (moverPrefab == null)
            {
                Debug.LogError("[TrackDirector] No mover prefab assigned. Nothing will run the tracks.");
                enabled = false;
                return;
            }

            _waveInterval = waveInterval;
            _concurrency = concurrentTargets;

            _pool = new TrackMover[poolSize];
            for (int i = 0; i < poolSize; i++)
            {
                _pool[i] = Instantiate(moverPrefab, transform);
                _pool[i].gameObject.SetActive(false);
                _pool[i].Resolved += OnResolved;
            }
        }

        private void OnEnable()
        {
            if (game == null) return;

            game.RoundStarted += OnRoundStarted;
            game.RoundEnded += OnRoundEnded;
        }

        private void OnDisable()
        {
            if (game == null) return;

            game.RoundStarted -= OnRoundStarted;
            game.RoundEnded -= OnRoundEnded;
        }

        private void OnRoundStarted(int round)
        {
            int steps = Mathf.Max(0, round - 1);

            _waveSize = Mathf.Max(1, waveSizeFirstRound + waveSizeGrowthPerRound * steps);
            _launched = 0;
            _resolved = 0;
            _killed = 0;

            _waveInterval = Mathf.Max(minWaveInterval, waveInterval * Mathf.Pow(waveScalePerRound, steps));
            _speedScale = Mathf.Pow(speedScalePerRound, steps);
            _concurrency = Mathf.Min(poolSize, concurrentTargets + extraConcurrentPerRound * steps);

            RebuildUnlocked(round);

            _pending.Clear();
            _nextWave = Time.time;
            RaiseTargetsLeft();

            Debug.Log("[TrackDirector] Round " + round + ": " + _waveSize + " targets, wave every " +
                      _waveInterval.ToString("0.00") + " s, speed x" + _speedScale.ToString("0.00") +
                      ", up to " + _concurrency + " at once, " + _unlocked.Count + " patterns in play.");
        }

        private void OnRoundEnded(int round, int roundScore)
        {
            _pending.Clear();
            if (grid != null) grid.ClearTraffic();

            for (int i = 0; i < _pool.Length; i++) _pool[i].Stop();
        }

        private void RebuildUnlocked(int round)
        {
            _unlocked.Clear();
            if (patterns == null) return;

            for (int i = 0; i < patterns.Length; i++)
            {
                if (patterns[i] != null && round >= patterns[i].fromRound)
                    _unlocked.Add(patterns[i].pattern);
            }

            if (_unlocked.Count == 0) _unlocked.Add(TrackPatternKind.Single);
        }

        private void Update()
        {
            if (grid == null || game == null || game.Current != RangeGame.State.Playing) return;

            ReleaseDue();

            if (Time.time < _nextWave || _launched + _pending.Count >= _waveSize) return;

            _nextWave = Time.time + _waveInterval;
            StartWave();
        }

        private void StartWave()
        {
            if (_unlocked.Count == 0) RebuildUnlocked(1);

            var kind = _unlocked[UnityEngine.Random.Range(0, _unlocked.Count)];
            TrackPattern.Build(kind, grid.Rails, _launches);

            // The last pattern of a wave is cut short rather than overfilling it
            int count = Mathf.Min(_launches.Count, _waveSize - _launched - _pending.Count);
            if (count <= 0) return;

            float baseSpeed = UnityEngine.Random.Range(minSpeed, maxSpeed) * _speedScale;

            float now = Time.time;
            for (int i = 0; i < count; i++)
            {
                var launch = _launches[i];
                _pending.Add(new Pending
                {
                    Rail = launch.Rail,
                    Due = now + launch.Delay,
                    Speed = baseSpeed * launch.SpeedMultiplier,
                });
            }

            if (PatternStarted != null) PatternStarted(kind);
        }

        private void ReleaseDue()
        {
            float now = Time.time;

            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                var due = _pending[i];
                if (due.Due > now) continue;

                if (now > due.Due + retrySeconds) { _pending.RemoveAt(i); continue; }

                if (ActiveCount() >= _concurrency) continue;

                var mover = TakeIdle();
                if (mover == null) continue;
                if (!grid.TryDispatch(due.Rail, due.Speed, mover.MoverLength)) continue;

                _pending.RemoveAt(i);
                _launched++;
                mover.Launch(due.Rail, due.Speed);
            }
        }

        private void OnResolved(TrackMover mover, bool shot)
        {
            _resolved++;
            if (shot) _killed++;
            RaiseTargetsLeft();
        }

        private void RaiseTargetsLeft()
        {
            if (TargetsLeftChanged != null) TargetsLeftChanged(TargetsLeft);
        }

        private int ActiveCount()
        {
            int count = 0;
            for (int i = 0; i < _pool.Length; i++)
            {
                if (_pool[i].gameObject.activeSelf) count++;
            }

            return count;
        }

        private TrackMover TakeIdle()
        {
            for (int i = 0; i < _pool.Length; i++)
            {
                if (!_pool[i].gameObject.activeSelf) return _pool[i];
            }

            return null;
        }
    }
}
