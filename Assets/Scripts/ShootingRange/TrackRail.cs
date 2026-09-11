using System.Collections.Generic;
using UnityEngine;

namespace IntuitiveDesigns.ShootingRange
{

    public enum TrackGroup
    {
        FloorAcross,
        FloorAlong,
        RoofAcross,
        RoofAlong,
        Riser,
    }

    public class TrackRail : MonoBehaviour
    {
        [Header("Identity")]
        [SerializeField] private TrackGroup group = TrackGroup.FloorAcross;

        [Header("Shape (data, metres)")]
        [SerializeField] private float length = 8f;

        [Header("Traffic (data)")]
        [SerializeField] private float minGap = 0.9f;

        public TrackGroup Group { get { return group; } }
        public float Length { get { return length; } }
        public Vector3 Origin { get { return transform.position; } }
        public Vector3 Direction { get { return transform.forward; } }

        private struct Pass
        {
            public float Depart;
            public float Speed;
            public float Clear;
        }

        private readonly List<Pass> _passes = new List<Pass>();

        public Vector3 PointAt(float distance)
        {
            return Origin + Direction * Mathf.Clamp(distance, 0f, length);
        }

        /// Checked and committed separately so a dispatch that fails at a junction does not leave a
        /// phantom booking on the rail. TrackGrid checks everything, then commits everything
        public bool CanAccept(float departTime, float speed)
        {
            Prune(departTime);

            var candidate = Build(departTime, speed);
            for (int i = 0; i < _passes.Count; i++)
            {
                if (!Separated(_passes[i], candidate)) return false;
            }

            return true;
        }

        public void Accept(float departTime, float speed)
        {
            _passes.Add(Build(departTime, speed));
        }

        public void ClearTraffic()
        {
            _passes.Clear();
        }

        private Pass Build(float departTime, float speed)
        {
            float safe = Mathf.Max(0.01f, speed);
            return new Pass { Depart = departTime, Speed = safe, Clear = departTime + length / safe };
        }

        private void Prune(float now)
        {
            for (int i = _passes.Count - 1; i >= 0; i--)
            {
                if (_passes[i].Clear < now) _passes.RemoveAt(i);
            }
        }

        private bool Separated(Pass a, Pass b)
        {
            float start = Mathf.Max(a.Depart, b.Depart);
            float end = Mathf.Min(a.Clear, b.Clear);
            if (end <= start) return true;

            float gapAtStart = Offset(a, start) - Offset(b, start);
            float gapAtEnd = Offset(a, end) - Offset(b, end);

            // The sign flipped, so somewhere in between the gap was zero: one overtook the other
            if (gapAtStart * gapAtEnd < 0f) return false;

            return Mathf.Min(Mathf.Abs(gapAtStart), Mathf.Abs(gapAtEnd)) >= minGap;
        }

        private static float Offset(Pass pass, float time)
        {
            return (time - pass.Depart) * pass.Speed;
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.8f);
            Vector3 a = Origin;
            Vector3 b = Origin + Direction * length;
            Gizmos.DrawLine(a, b);
            Gizmos.DrawWireSphere(a, 0.08f);
            Gizmos.DrawWireCube(b, Vector3.one * 0.1f);
        }
#endif
    }
}
