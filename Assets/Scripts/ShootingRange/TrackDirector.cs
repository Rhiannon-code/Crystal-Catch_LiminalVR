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

        [Header("Pace (data, round 1)")]
        [SerializeField] private float waveInterval = 2.2f;
        [SerializeField] private float minSpeed = 1.3f;
        [SerializeField] private float maxSpeed = 2.6f;
        [SerializeField] private int concurrentTargets = 4;

        [Header("Escalation per round (data)")]
        [SerializeField] private float waveScalePerRound = 0.82f;
        [SerializeField] private float speedScalePerRound = 1.12f;
        [SerializeField] private int extraTargetsPerRound = 2;
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

        public int ConcurrencyCap { get { return _concurrency; } }

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
            }
        }

        private void OnEnable()
        {
            if (game != null) game.RoundStarted += OnRoundStarted;
        }

        private void OnDisable()
        {
            if (game != null) game.RoundStarted -= OnRoundStarted;
        }

        private void OnRoundStarted(int round)
        {
            int steps = Mathf.Max(0, round - 1);

            _waveInterval = Mathf.Max(minWaveInterval, waveInterval * Mathf.Pow(waveScalePerRound, steps));
            _speedScale = Mathf.Pow(speedScalePerRound, steps);
            _concurrency = Mathf.Min(poolSize, concurrentTargets + extraTargetsPerRound * steps);

            RebuildUnlocked(round);

            _pending.Clear();
            _nextWave = Time.time;

            if (grid != null) grid.ClearTraffic();
            RetireAll();

            Debug.Log("[TrackDirector] Round " + round + ": wave every " + _waveInterval.ToString("0.00") +
                      " s, speed x" + _speedScale.ToString("0.00") + ", up to " + _concurrency +
                      " targets, " + _unlocked.Count + " patterns in play.");
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

            if (Time.time < _nextWave) return;

            _nextWave = Time.time + _waveInterval;
            StartWave();
        }

        private void StartWave()
        {
            if (_unlocked.Count == 0) RebuildUnlocked(1);

            var kind = _unlocked[UnityEngine.Random.Range(0, _unlocked.Count)];
            TrackPattern.Build(kind, grid.Rails, _launches);
            if (_launches.Count == 0) return;

            float baseSpeed = UnityEngine.Random.Range(minSpeed, maxSpeed) * _speedScale;

            float now = Time.time;
            for (int i = 0; i < _launches.Count; i++)
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
                mover.Launch(due.Rail, due.Speed);
            }
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

        private void RetireAll()
        {
            for (int i = 0; i < _pool.Length; i++) _pool[i].gameObject.SetActive(false);
        }
    }
}
