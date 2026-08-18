using System.Collections.Generic;
using UnityEngine;

namespace IntuitiveDesigns.CrystalCatch
{
    public class TrackPath : MonoBehaviour
    {
        public enum TrackStyle
        {
            Custom,         // Use whatever is set in the inspector
            Gentle,         // Long lazy sweeps, barely any climb, the comfort baseline
            LongSweeping,   // Big wide corners, gentle rolling elevation
            Twisty,         // Frequent corners, modest elevation
            Rollercoaster   // Aggressive on both axes. Expect nausea complaints
        }

        [Header("Generation (data)")]
        [SerializeField] private TrackStyle style = TrackStyle.LongSweeping;
        [SerializeField] private int seed = 0;
        [SerializeField] private float pointSpacing = 2f;
        [SerializeField] private float length = 5000f;

        [Header("Comfort limits — these are comfort rules, not feel knobs")]
        [SerializeField] private float maxYawDegreesPerSecond = 30f;
        [SerializeField] private float topSpeed = 8f;
        [SerializeField] private float maxGradient = 0.22f;
        [SerializeField] private float steeringChangeRate = 0.06f;
        [SerializeField] private float verticalRange = 45f;
        [SerializeField] private float straightLeadIn = 30f;

        private readonly List<Vector3> _points = new List<Vector3>();

        /// The speed the curves were laid out for. Above this the generated yaw exceeds the
        /// 30 deg/sec comfort limit, so CartController clamps itself to it rather than trusting
        /// whoever tunes the round scaling to remember
        public float TopSpeed { get { return topSpeed; } }

        public float Length { get { return Mathf.Max(0f, (_points.Count - 1) * pointSpacing); } }
        public bool IsGenerated { get { return _points.Count > 1; } }

        private void Awake()
        {
            if (!IsGenerated) Generate();
        }

        /// Applies the named style's parameters. Called automatically by Generate so the dropdown
        /// is authoritative
        [ContextMenu("Apply Style")]
        public void ApplyStyle()
        {
            switch (style)
            {
                case TrackStyle.Gentle:
                    maxYawDegreesPerSecond = 10f; maxGradient = 0.06f; steeringChangeRate = 0.015f;
                    break;
                case TrackStyle.LongSweeping:
                    maxYawDegreesPerSecond = 18f; maxGradient = 0.14f; steeringChangeRate = 0.02f;
                    break;
                case TrackStyle.Twisty:
                    maxYawDegreesPerSecond = 30f; maxGradient = 0.16f; steeringChangeRate = 0.09f;
                    break;
                case TrackStyle.Rollercoaster:
                    maxYawDegreesPerSecond = 40f; maxGradient = 0.30f; steeringChangeRate = 0.10f;
                    break;
                // Custom, leave the inspector values untouched
            }
        }

        // Generator state, kept as fields so the track can be EXTENDED rather than only rebuilt
        private System.Random _rng;
        private Vector3 _genPos;
        private float _genYaw;
        private float _genYawRate;
        private float _genGradient;
        private float _genTargetYawRate;
        private float _genTargetGradient;

        /// Builds the track from scratch. Uses `seed`, or the clock when seed is 0
        public void Generate()
        {
            ApplyStyle();
            _points.Clear();

            _rng = new System.Random(seed != 0 ? seed : System.Environment.TickCount);
            _genPos = Vector3.zero;
            _genYaw = 0f;
            _genYawRate = 0f;
            _genGradient = 0f;
            _genTargetYawRate = 0f;
            _genTargetGradient = 0f;

            _points.Add(_genPos);
            AppendPoints(Mathf.Max(2, Mathf.CeilToInt(length / pointSpacing)));
        }

        /// Grows the track so it covers at least <paramref name="requiredLength"/> metres
        public void EnsureLength(float requiredLength)
        {
            if (!IsGenerated) Generate();
            if (Length >= requiredLength) return;

            int needed = Mathf.CeilToInt((requiredLength - Length) / pointSpacing);
            AppendPoints(Mathf.Max(1, needed));
        }

        private void AppendPoints(int steps)
        {
            // deg/sec at top speed -> deg/metre. This conversion is what keeps the comfort promise
            // honest across the whole speed ramp
            float maxYawPerMetre = topSpeed > 0.01f ? maxYawDegreesPerSecond / topSpeed : 0f;
            float ease = Mathf.Clamp01(pointSpacing * 0.15f);

            for (int i = 0; i < steps; i++)
            {
                float travelled = (_points.Count - 1) * pointSpacing;

                if (travelled < straightLeadIn)
                {
                    _genTargetYawRate = 0f;
                    _genTargetGradient = 0f;
                }
                else if (NextFloat(_rng) < steeringChangeRate * pointSpacing)
                {
                    _genTargetYawRate = Mathf.Lerp(-maxYawPerMetre, maxYawPerMetre, NextFloat(_rng));
                    _genTargetGradient = Mathf.Lerp(-maxGradient, maxGradient, NextFloat(_rng));
                }

                // Ease toward the targets so curvature itself changes gradually. A step change in
                // curvature reads as a kink and is felt as a jolt even within the limits
                _genYawRate = Mathf.Lerp(_genYawRate, _genTargetYawRate, ease);
                _genGradient = Mathf.Lerp(_genGradient, _genTargetGradient, ease);

                if (_genPos.y < -verticalRange) _genGradient = Mathf.Abs(_genGradient);
                if (_genPos.y > verticalRange) _genGradient = -Mathf.Abs(_genGradient);

                _genYaw += _genYawRate * pointSpacing;

                Vector3 flat = new Vector3(Mathf.Sin(_genYaw * Mathf.Deg2Rad), 0f,
                                           Mathf.Cos(_genYaw * Mathf.Deg2Rad));
                Vector3 step = (flat + Vector3.up * _genGradient).normalized * pointSpacing;

                _genPos += step;
                _points.Add(_genPos);
            }
        }

        private static float NextFloat(System.Random rng)
        {
            return (float)rng.NextDouble();
        }

        public Vector3 PositionAt(float distance)
        {
            if (_points.Count == 0) return transform.position;
            if (_points.Count == 1) return transform.position + _points[0];

            float d = Mathf.Clamp(distance, 0f, Length);
            float f = d / pointSpacing;
            int i = Mathf.Clamp(Mathf.FloorToInt(f), 0, _points.Count - 2);
            float t = f - i;

            return transform.position + Vector3.Lerp(_points[i], _points[i + 1], t);
        }

        public Vector3 ForwardAt(float distance)
        {
            if (_points.Count < 2) return Vector3.forward;

            float d = Mathf.Clamp(distance, 0f, Length);
            int i = Mathf.Clamp(Mathf.FloorToInt(d / pointSpacing), 0, _points.Count - 2);

            Vector3 dir = _points[i + 1] - _points[i];
            return dir.sqrMagnitude < 1e-6f ? Vector3.forward : dir.normalized;
        }

        public Vector3 RightAt(float distance)
        {
            Vector3 fwd = ForwardAt(distance);
            Vector3 flat = new Vector3(fwd.x, 0f, fwd.z);
            if (flat.sqrMagnitude < 1e-6f) flat = Vector3.forward;
            return Vector3.Cross(Vector3.up, flat.normalized);
        }

        public Quaternion RotationAt(float distance, bool includePitch)
        {
            Vector3 fwd = ForwardAt(distance);
            if (!includePitch)
            {
                fwd.y = 0f;
                if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
            }
            return Quaternion.LookRotation(fwd.normalized, Vector3.up);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (_points.Count < 2) return;
            Gizmos.color = Color.cyan;
            for (int i = 0; i < _points.Count - 1; i++)
                Gizmos.DrawLine(transform.position + _points[i], transform.position + _points[i + 1]);
        }
#endif
    }
}
