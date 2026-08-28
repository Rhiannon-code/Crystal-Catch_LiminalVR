using System.Collections.Generic;
using UnityEngine;
using Liminal.SDK.VR.Avatars;

namespace IntuitiveDesigns.CrystalCatch
{
    public class TrackObstacles : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private CartController cart;
        [SerializeField] private TrackPath track;
        [SerializeField] private CrystalCatchGame game;
        [SerializeField] private TrackObstacle duckPrefab;
        [SerializeField] private TrackObstacle leanLeftPrefab;
        [SerializeField] private TrackObstacle leanRightPrefab;
        [SerializeField] private PlayerHeightCalibration calibration;
        [SerializeField] private CaveAtmosphere atmosphere;

        [Header("Placement (data, metres)")]
        [SerializeField] private float firstObstacleAt = 140f;
        [SerializeField] private float minGap = 65f;
        [SerializeField] private float maxGap = 145f;
        [SerializeField] private int seed = 4242;
        [SerializeField] private float visibleAhead = 140f;
        [SerializeField] private float visibleBehind = 12f;

        [Header("Pool")]
        [SerializeField] private int poolPerKind = 3;

        [Header("Consequence")]
        [SerializeField] private float hitPenaltySeconds = 5f;
        [SerializeField] private Transform headOverride;
        [SerializeField] private bool logResults = true;

        private readonly List<float> _distances = new List<float>();
        private readonly List<TrackObstacle.Kind> _kinds = new List<TrackObstacle.Kind>();

        private TrackObstacle[][] _pools;
        private int[] _cursors;
        private System.Random _rng;
        private float _generatedTo;
        private int _firstLive;
        private int _lastHit = -1;
        private int _reportedTo;
        private Transform _head;

        private void Start()
        {
            if (cart == null || track == null)
            {
                Debug.LogWarning("[TrackObstacles] Missing cart or track, no obstacles will be placed.");
                enabled = false;
                return;
            }

            // An obstacle is long, so it has to be resident well before the fog would reveal it, or
            // its leading edge appears mid air as the cart arrives
            if (atmosphere != null)
            {
                float sectionLength = duckPrefab != null ? duckPrefab.SectionHalfLength * 2f : 23f;
                visibleAhead = atmosphere.DrawDistance + sectionLength;
                visibleBehind = atmosphere.DrawDistance + sectionLength;
            }

            var prefabs = new[] { duckPrefab, leanLeftPrefab, leanRightPrefab };
            _pools = new TrackObstacle[prefabs.Length][];
            _cursors = new int[prefabs.Length];

            for (int kind = 0; kind < prefabs.Length; kind++)
            {
                _pools[kind] = new TrackObstacle[poolPerKind];
                if (prefabs[kind] == null) continue;

                for (int i = 0; i < poolPerKind; i++)
                {
                    var instance = Instantiate(prefabs[kind], transform);
                    instance.name = prefabs[kind].name + "_" + i;
                    instance.gameObject.SetActive(false);
                    _pools[kind][i] = instance;
                }
            }

            _rng = new System.Random(seed);
            _generatedTo = firstObstacleAt;
        }

        private void LateUpdate()
        {
            if (_pools == null) return;

            float distance = cart.Distance;

            EnsureGeneratedTo(distance + visibleAhead);

            while (_firstLive < _distances.Count && _distances[_firstLive] < distance - visibleBehind)
                _firstLive++;

            for (int i = 0; i < _cursors.Length; i++) _cursors[i] = 0;

            for (int i = _firstLive; i < _distances.Count; i++)
            {
                if (_distances[i] > distance + visibleAhead) break;
                Show(i);
            }

            // Anything the cursors did not claim this frame is not in view
            for (int kind = 0; kind < _pools.Length; kind++)
                for (int i = _cursors[kind]; i < _pools[kind].Length; i++)
                    if (_pools[kind][i] != null && _pools[kind][i].gameObject.activeSelf)
                        _pools[kind][i].gameObject.SetActive(false);

            CheckHead(distance);
        }

        private void Show(int index)
        {
            int kind = (int)_kinds[index];
            if (_pools[kind] == null || _cursors[kind] >= _pools[kind].Length) return;

            var obstacle = _pools[kind][_cursors[kind]];
            if (obstacle == null) return;

            _cursors[kind]++;

            float d = _distances[index];
            Quaternion rotation = track.RotationAt(d, true);

            // Shifted along the TRACK's up, not the world's, so the offset stays vertical relative
            // to the player on a gradient
            float lift = calibration != null ? calibration.HeightOffset : 0f;

            obstacle.transform.position = track.PositionAt(d) + rotation * (Vector3.up * lift);
            obstacle.transform.rotation = rotation;

            if (!obstacle.gameObject.activeSelf) obstacle.gameObject.SetActive(true);
        }

        public TrackObstacle PrefabFor(TrackObstacle.Kind kind)
        {
            if (kind == TrackObstacle.Kind.DuckBeam) return duckPrefab;
            if (kind == TrackObstacle.Kind.LeanLeft) return leanLeftPrefab;
            return leanRightPrefab;
        }

        public void PreviewSequence(float toDistance,
                                    System.Collections.Generic.List<float> distances,
                                    System.Collections.Generic.List<TrackObstacle.Kind> kinds)
        {
            distances.Clear();
            kinds.Clear();

            var rng = new System.Random(seed);
            float at = firstObstacleAt;

            while (at < toDistance && distances.Count < 4000)
            {
                distances.Add(at);

                double roll = rng.NextDouble();
                kinds.Add(roll < 0.4 ? TrackObstacle.Kind.DuckBeam
                        : roll < 0.7 ? TrackObstacle.Kind.LeanLeft
                                     : TrackObstacle.Kind.LeanRight);

                at += Mathf.Lerp(minGap, maxGap, (float)rng.NextDouble());
            }
        }

        private void EnsureGeneratedTo(float required)
        {
            while (_generatedTo < required)
            {
                _distances.Add(_generatedTo);

                // Duck and lean roughly evenly, because they are different physical asks and
                // alternating them is what stops the player settling into one posture
                double roll = _rng.NextDouble();
                _kinds.Add(roll < 0.4 ? TrackObstacle.Kind.DuckBeam
                         : roll < 0.7 ? TrackObstacle.Kind.LeanLeft
                                      : TrackObstacle.Kind.LeanRight);

                _generatedTo += Mathf.Lerp(minGap, maxGap, (float)_rng.NextDouble());
            }
        }

        /// Only the obstacle the cart is actually passing is tested, and only once
        private void CheckHead(float distance)
        {
            if (game == null) return;

            for (int i = _firstLive; i < _distances.Count; i++)
            {
                float delta = _distances[i] - distance;
                if (delta > 2f) break;
                if (delta < -2f || i == _lastHit) continue;

                int kind = (int)_kinds[i];
                for (int p = 0; p < _pools[kind].Length; p++)
                {
                    var obstacle = _pools[kind][p];
                    if (obstacle == null || !obstacle.gameObject.activeSelf) continue;

                    // The pooled instance standing at this distance is the one to test
                    if ((obstacle.transform.position - track.PositionAt(_distances[i])).sqrMagnitude > 0.01f)
                        continue;

                    if (!ResolveHead()) return;
                    if (!obstacle.ContainsHead(_head.position)) continue;

                    _lastHit = i;
                    game.ApplyHazardTime(-Mathf.Abs(hitPenaltySeconds));
                    Report(i, true);
                    return;
                }
            }

            ReportPassed(distance);
        }

        /// Anything now behind the cart that was never flagged as a hit was cleared
        private void ReportPassed(float distance)
        {
            while (_reportedTo < _distances.Count && _distances[_reportedTo] < distance - 2f)
            {
                if (_reportedTo != _lastHit) Report(_reportedTo, false);
                _reportedTo++;
            }
        }

        private void Report(int index, bool hit)
        {
            // The bookkeeping runs whether or not the line is logged. Leaving it inside the log
            // would mean stripping logs from a build quietly changed which obstacles get reported
            if (hit && index >= _reportedTo) _reportedTo = index + 1;

            LogResult(index, hit);
        }

        /// Compiled out of a non development build, call site and all. Building the string for a
        /// log nothing reads still costs allocations on device
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        private void LogResult(int index, bool hit)
        {
            if (!logResults) return;

            Debug.Log("[TrackObstacles] " + _kinds[index] + " at " +
                      _distances[index].ToString("0") + " m: " + (hit ? "HIT" : "cleared"));
        }

        private bool ResolveHead()
        {
            if (_head != null) return true;

            if (headOverride != null) { _head = headOverride; return true; }

            // Don't use ?. on VRAvatar, it's a UnityEngine.Object, so null propagation bypasses
            // Unity's overloaded == and would sail past a destroyed avatar
            var avatar = VRAvatar.Active;
            if (avatar != null)
            {
                var limb = avatar.GetLimb(VRAvatarLimbType.Head);
                if (limb != null) { _head = limb.Transform; return true; }
            }

            var cam = Camera.main;
            if (cam == null) cam = Object.FindObjectOfType<Camera>();
            if (cam == null) return false;

            _head = cam.transform;
            return true;
        }
    }
}
