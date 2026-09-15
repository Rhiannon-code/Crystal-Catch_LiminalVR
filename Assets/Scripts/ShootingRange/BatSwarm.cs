using UnityEngine;
using Liminal.SDK.VR.Avatars;

namespace IntuitiveDesigns.ShootingRange
{
    public class BatSwarm : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private RangeGame game;
        [SerializeField] private TrackDirector director;
        [SerializeField] private Bat batPrefab;

        [Header("Pool")]
        [SerializeField] private int poolSize = 10;

        [Header("Release (data, the first round with bats)")]
        [SerializeField] private int firstRound = 2;
        [SerializeField] private float releaseInterval = 4f;
        [SerializeField] private int maxInFlight = 2;
        [SerializeField] private float speed = 4f;
        [SerializeField] private float minLaunchDistance = 4f;

        [Header("Escalation per round (data)")]
        [SerializeField] private float intervalScalePerRound = 0.8f;
        [SerializeField] private int extraInFlightPerRound = 1;
        [SerializeField] private float speedScalePerRound = 1.08f;

        [Header("Death (data)")]
        [SerializeField] private int fragments = 3;
        [SerializeField] private float fragmentImpulse = 0.8f;

        [Header("Audio")]
        [SerializeField] private AudioClip[] launchClips;
        [SerializeField] private AudioClip[] deathClips;
        [SerializeField, Range(0f, 1f)] private float volume = 0.9f;

        private Bat[] _pool;
        private float _nextRelease;
        private float _interval;
        private float _speed;
        private int _cap;

        private void Awake()
        {
            if (batPrefab == null)
            {
                Debug.LogError("[BatSwarm] No bat prefab assigned. No bats will fly.");
                enabled = false;
                return;
            }

            _pool = new Bat[poolSize];
            for (int i = 0; i < poolSize; i++)
            {
                _pool[i] = Instantiate(batPrefab, transform);
                _pool[i].gameObject.SetActive(false);
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
            int steps = Mathf.Max(0, round - firstRound);

            _interval = releaseInterval * Mathf.Pow(intervalScalePerRound, steps);
            _speed = speed * Mathf.Pow(speedScalePerRound, steps);
            _cap = Mathf.Min(poolSize, maxInFlight + extraInFlightPerRound * steps);
            _nextRelease = Time.time + _interval;
        }

        private void OnRoundEnded(int round, int roundScore)
        {
            for (int i = 0; i < _pool.Length; i++) _pool[i].Stop();
        }

        private void Update()
        {
            if (game == null || game.Current != RangeGame.State.Playing || game.RoundNumber < firstRound) return;
            if (Time.time < _nextRelease) return;

            _nextRelease = Time.time + _interval;
            if (InFlight() < _cap) Release();
        }

        public void Return(Bat bat)
        {
            bat.Stop();
        }

        public void Popped(Bat bat, Vector3 direction)
        {
            Vector3 at = bat.transform.position;
            bat.Stop();

            if (ShatterPool.Instance != null) ShatterPool.Instance.Burst(at, fragments, direction, fragmentImpulse);
            PlayAt(deathClips, at);
        }

        private void Release()
        {
            var head = Head();
            var movers = director != null ? director.Movers : null;
            if (head == null || movers == null) return;

            float minSqr = minLaunchDistance * minLaunchDistance;
            int start = Random.Range(0, movers.Length);

            for (int i = 0; i < movers.Length; i++)
            {
                var mover = movers[(start + i) % movers.Length];
                if (!mover.Running || (mover.transform.position - head.position).sqrMagnitude < minSqr) continue;

                var bat = TakeIdle();
                if (bat == null) return;

                bat.Launch(this, mover.transform.position, head, _speed);
                PlayAt(launchClips, mover.transform.position);
                return;
            }
        }

        private static Transform Head()
        {
            var avatar = VRAvatar.Active;
            if (avatar == null || avatar.Head == null) return null;

            return avatar.Head.Transform;
        }

        private int InFlight()
        {
            int count = 0;
            for (int i = 0; i < _pool.Length; i++)
            {
                if (_pool[i].gameObject.activeSelf) count++;
            }

            return count;
        }

        private Bat TakeIdle()
        {
            for (int i = 0; i < _pool.Length; i++)
            {
                if (!_pool[i].gameObject.activeSelf) return _pool[i];
            }

            return null;
        }

        private void PlayAt(AudioClip[] clips, Vector3 at)
        {
            if (ImpactFX.Instance == null || clips == null || clips.Length == 0) return;
            ImpactFX.Instance.PlayClip(clips[Random.Range(0, clips.Length)], at, volume);
        }
    }
}
