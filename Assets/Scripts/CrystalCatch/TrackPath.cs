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
            Twisty          // Frequent corners, modest elevation. The busiest we will ship
        }

        public enum Manoeuvre { Straight, Turn, Grade }

        [Header("Generation (data)")]
        [SerializeField] private TrackStyle style = TrackStyle.LongSweeping;
        [SerializeField] private int seed = 12345;
        [SerializeField] private float pointSpacing = 2f;
        [SerializeField] private float length = 5000f;

        [Header("Comfort limits: comfort rules, not feel knobs")]
        [SerializeField] private float maxYawDegreesPerSecond = 15f;
        [SerializeField] private float topSpeed = 8f;
        [SerializeField] private float maxGradient = 0.14f;
        [SerializeField] private float maxPitchDegreesPerSecond = 4f;

        [Header("Comfort spacing (metres)")]
        [SerializeField] private float minStraightRun = 30f;
        [SerializeField] private float maxStraightRun = 90f;
        [SerializeField] private float minTurnRun = 30f;
        [SerializeField] private float maxTurnRun = 70f;
        [SerializeField] private float minGradeRun = 120f;
        [SerializeField] private float maxGradeRun = 200f;
        [SerializeField] private float verticalRange = 45f;
        [SerializeField] private float straightLeadIn = 30f;
        [SerializeField] private float maxHeadingDrift = 60f;

        [Header("Comfort ceiling")]
        [SerializeField] private bool allowBeyondComfortCeiling = false;
        [SerializeField] private float ceilingYawDegreesPerSecond = 20f;
        [SerializeField] private float ceilingGradient = 0.16f;
        [SerializeField] private float ceilingPitchDegreesPerSecond = 6f;
        [SerializeField, HideInInspector] private List<Vector3> _points = new List<Vector3>();

        public float TopSpeed { get { return topSpeed; } }

        public float PointSpacing { get { return pointSpacing; } }
        public int PointCount { get { return _points.Count; } }
        public float MaxYawDegreesPerSecond { get { return maxYawDegreesPerSecond; } }
        public float MaxGradient { get { return maxGradient; } }
        public float MaxPitchDegreesPerSecond { get { return maxPitchDegreesPerSecond; } }

        public float Length { get { return Mathf.Max(0f, (_points.Count - 1) * pointSpacing); } }
        public bool IsGenerated { get { return _points.Count > 1; } }

        /// Local space, relative to this transform. Used by the editor's handles
        public Vector3 GetLocalPoint(int index) { return _points[index]; }

        public void SetLocalPoint(int index, Vector3 value)
        {
            if (index < 0 || index >= _points.Count) return;
            _points[index] = value;

            // A hand edit invalidates the generator's idea of where it left off
            _generatorReady = false;
        }

        private void Awake()
        {
            // A track baked in the editor is used exactly as it was baked. Only an empty one is
            // generated at runtime
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
                    maxYawDegreesPerSecond = 8f;  maxGradient = 0.06f; maxPitchDegreesPerSecond = 2f;
                    minTurnRun = 40f; maxTurnRun = 90f; minStraightRun = 45f; maxStraightRun = 120f;
                    break;
                case TrackStyle.LongSweeping:
                    maxYawDegreesPerSecond = 15f; maxGradient = 0.14f; maxPitchDegreesPerSecond = 4f;
                    minTurnRun = 30f; maxTurnRun = 70f; minStraightRun = 30f; maxStraightRun = 90f;
                    break;
                case TrackStyle.Twisty:
                    maxYawDegreesPerSecond = 20f; maxGradient = 0.16f; maxPitchDegreesPerSecond = 6f;
                    minTurnRun = 20f; maxTurnRun = 45f; minStraightRun = 20f; maxStraightRun = 45f;
                    break;
                // Custom, leave the inspector values untouched
            }

            ClampToComfortCeiling();
        }

        private void ClampToComfortCeiling()
        {
            if (allowBeyondComfortCeiling) return;

            maxYawDegreesPerSecond = Mathf.Min(maxYawDegreesPerSecond, ceilingYawDegreesPerSecond);
            maxGradient = Mathf.Min(maxGradient, ceilingGradient);
            maxPitchDegreesPerSecond = Mathf.Min(maxPitchDegreesPerSecond, ceilingPitchDegreesPerSecond);

            // A grade run shorter than this drags the vertical oscillation up into the nauseating band
            minGradeRun = Mathf.Max(minGradeRun, 80f);
            maxGradeRun = Mathf.Max(maxGradeRun, minGradeRun);
            maxStraightRun = Mathf.Max(maxStraightRun, minStraightRun);
            maxTurnRun = Mathf.Max(maxTurnRun, minTurnRun);
        }

        // Generator state, kept as fields so the track can be EXTENDED rather than only rebuilt
        private System.Random _rng;
        private Vector3 _genPos;
        private float _genYaw;
        private float _genYawRate;
        private float _genGradient;
        private float _genTargetYawRate;
        private float _genTargetGradient;
        private Manoeuvre _manoeuvre = Manoeuvre.Straight;
        private float _manoeuvreRemaining;
        private bool _generatorReady;

        /// Builds the track from scratch. Uses `seed`, or the clock when seed is 0
        [ContextMenu("Generate")]
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

            _manoeuvre = Manoeuvre.Straight;
            _manoeuvreRemaining = Mathf.Max(straightLeadIn, minStraightRun);
            _generatorReady = true;

            _points.Add(_genPos);
            AppendPoints(Mathf.Max(2, Mathf.CeilToInt(length / pointSpacing)));
        }

        [ContextMenu("Clear")]
        public void Clear()
        {
            _points.Clear();
            _generatorReady = false;
        }

        /// Grows the track so it covers at least <paramref name="requiredLength"/> metres
        public void EnsureLength(float requiredLength)
        {
            if (!IsGenerated) { Generate(); return; }
            if (Length >= requiredLength) return;

            if (!_generatorReady) RestoreGeneratorState();

            int needed = Mathf.CeilToInt((requiredLength - Length) / pointSpacing);
            AppendPoints(Mathf.Max(1, needed));
        }

        private void RestoreGeneratorState()
        {
            ApplyStyle();

            if (_rng == null) _rng = new System.Random(seed != 0 ? seed : System.Environment.TickCount);

            _genPos = _points[_points.Count - 1];

            Vector3 dir = _points.Count >= 2
                ? (_points[_points.Count - 1] - _points[_points.Count - 2])
                : Vector3.forward;

            Vector3 flat = new Vector3(dir.x, 0f, dir.z);
            if (flat.sqrMagnitude < 1e-6f) flat = Vector3.forward;

            _genYaw = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
            _genGradient = dir.y / flat.magnitude;
            _genYawRate = 0f;
            _genTargetYawRate = 0f;
            _genTargetGradient = _genGradient;
            _manoeuvre = Manoeuvre.Straight;
            _manoeuvreRemaining = minStraightRun;
            _generatorReady = true;
        }

        private void AppendPoints(int steps)
        {
            float maxYawPerMetre = topSpeed > 0.01f ? maxYawDegreesPerSecond / topSpeed : 0f;
            float maxGradientPerMetre = topSpeed > 0.01f
                ? (maxPitchDegreesPerSecond * Mathf.Deg2Rad) / topSpeed
                : 0f;
            float yawRateEase = maxYawPerMetre * 0.08f;

            for (int i = 0; i < steps; i++)
            {
                _manoeuvreRemaining -= pointSpacing;
                if (_manoeuvreRemaining <= 0f) PickNextManoeuvre();

                _genYawRate = Mathf.MoveTowards(_genYawRate, _genTargetYawRate, yawRateEase * pointSpacing);
                _genGradient = Mathf.MoveTowards(_genGradient, _genTargetGradient, maxGradientPerMetre * pointSpacing);

                _genYaw = Mathf.Clamp(_genYaw + _genYawRate * pointSpacing,
                                      -maxHeadingDrift, maxHeadingDrift);

                if (Mathf.Abs(_genYaw) >= maxHeadingDrift - 0.001f) _genTargetYawRate = 0f;

                Vector3 flat = new Vector3(Mathf.Sin(_genYaw * Mathf.Deg2Rad), 0f,
                                           Mathf.Cos(_genYaw * Mathf.Deg2Rad));
                Vector3 step = (flat + Vector3.up * _genGradient).normalized * pointSpacing;

                _genPos += step;
                _points.Add(_genPos);
            }
        }

        private void PickNextManoeuvre()
        {
            if (_manoeuvre != Manoeuvre.Straight)
            {
                // Always rest after doing something
                _manoeuvre = Manoeuvre.Straight;
                _manoeuvreRemaining = RandomRange(minStraightRun, maxStraightRun);
                _genTargetYawRate = 0f;
                _genTargetGradient = 0f;
                return;
            }

            float maxYawPerMetre = topSpeed > 0.01f ? maxYawDegreesPerSecond / topSpeed : 0f;
            bool grade = NextFloat() < 0.3f;

            if (grade)
            {
                _manoeuvre = Manoeuvre.Grade;
                _manoeuvreRemaining = RandomRange(minGradeRun, maxGradeRun);
                _genTargetYawRate = 0f;

                float magnitude = Mathf.Lerp(maxGradient * 0.4f, maxGradient, NextFloat());
                _genTargetGradient = ChooseGradeDirection(_manoeuvreRemaining * magnitude) * magnitude;
            }
            else
            {
                _manoeuvre = Manoeuvre.Turn;
                _manoeuvreRemaining = RandomRange(minTurnRun, maxTurnRun);
                _genTargetGradient = 0f;

                float magnitude = Mathf.Lerp(maxYawPerMetre * 0.4f, maxYawPerMetre, NextFloat());
                _genTargetYawRate = ChooseTurnDirection(magnitude) * magnitude;
            }
        }

        private float ChooseTurnDirection(float magnitudePerMetre)
        {
            float projected = magnitudePerMetre * _manoeuvreRemaining;
            float direction = NextFloat() < 0.5f ? -1f : 1f;

            if (_genYaw + direction * projected > maxHeadingDrift) direction = -1f;
            else if (_genYaw + direction * projected < -maxHeadingDrift) direction = 1f;

            return direction;
        }

        private float ChooseGradeDirection(float projectedClimb)
        {
            if (_genPos.y + projectedClimb > verticalRange) return -1f;
            if (_genPos.y - projectedClimb < -verticalRange) return 1f;
            return NextFloat() < 0.5f ? -1f : 1f;
        }

        private float RandomRange(float min, float max) { return Mathf.Lerp(min, max, NextFloat()); }

        private float NextFloat()
        {
            if (_rng == null) _rng = new System.Random(seed != 0 ? seed : System.Environment.TickCount);
            return (float)_rng.NextDouble();
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

        public float YawRateAt(int index)
        {
            if (index < 1 || index >= _points.Count - 1) return 0f;

            Vector3 a = _points[index] - _points[index - 1];
            Vector3 b = _points[index + 1] - _points[index];
            a.y = 0f; b.y = 0f;
            if (a.sqrMagnitude < 1e-6f || b.sqrMagnitude < 1e-6f) return 0f;

            float degrees = Vector3.Angle(a.normalized, b.normalized);
            return pointSpacing > 0.01f ? degrees / pointSpacing * topSpeed : 0f;
        }

        public float GradientAt(int index)
        {
            if (index < 0 || index >= _points.Count - 1) return 0f;

            Vector3 d = _points[index + 1] - _points[index];
            Vector3 flat = new Vector3(d.x, 0f, d.z);
            return flat.sqrMagnitude < 1e-6f ? 0f : d.y / flat.magnitude;
        }

        /// 0 = comfortably inside the limits, 1 = exactly at them, above 1 = over
        public float ComfortLoadAt(int index)
        {
            float yaw = maxYawDegreesPerSecond > 0.01f ? YawRateAt(index) / maxYawDegreesPerSecond : 0f;
            float grade = maxGradient > 0.001f ? Mathf.Abs(GradientAt(index)) / maxGradient : 0f;
            return Mathf.Max(yaw, grade);
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (_points.Count < 2) return;

            Vector3 origin = transform.position;

            for (int i = 0; i < _points.Count - 1; i++)
            {
                float load = ComfortLoadAt(i);
                Gizmos.color = load > 1f
                    ? Color.red
                    : Color.Lerp(new Color(0.2f, 0.85f, 1f), new Color(1f, 0.75f, 0.1f), load);

                Gizmos.DrawLine(origin + _points[i], origin + _points[i + 1]);
            }

            // A tick every 100 m, so distances along the track can actually be read off the scene
            int stride = Mathf.Max(1, Mathf.RoundToInt(100f / Mathf.Max(0.01f, pointSpacing)));
            Gizmos.color = new Color(1f, 1f, 1f, 0.5f);
            for (int i = 0; i < _points.Count; i += stride)
                Gizmos.DrawWireCube(origin + _points[i], Vector3.one * 0.6f);
        }
#endif
    }
}
