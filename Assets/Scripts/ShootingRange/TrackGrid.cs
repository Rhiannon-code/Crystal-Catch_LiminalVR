using System.Collections.Generic;
using UnityEngine;

namespace IntuitiveDesigns.ShootingRange
{
    public class TrackGrid : MonoBehaviour
    {
        [Header("Rails (empty = every TrackRail below this object)")]
        [SerializeField] private TrackRail[] rails;

        [Header("Junctions (data, metres)")]
        [SerializeField] private float crossTolerance = 0.4f;
        [SerializeField] private float junctionClearance = 0.7f;
        [SerializeField] private bool logJunctionsOnAwake = true;

        public TrackRail[] Rails { get { return rails; } }
        public int JunctionCount { get { return _junctions.Count; } }

        private struct Window
        {
            public float Enter;
            public float Exit;
        }

        private class Junction
        {
            public TrackRail A;
            public TrackRail B;
            public float DistanceOnA;
            public float DistanceOnB;
            public readonly List<Window> Windows = new List<Window>();
        }

        private readonly List<Junction> _junctions = new List<Junction>();
        private readonly Dictionary<TrackRail, List<Junction>> _byRail =
            new Dictionary<TrackRail, List<Junction>>();

        private void Awake()
        {
            if (rails == null || rails.Length == 0) rails = GetComponentsInChildren<TrackRail>();

            FindJunctions();

            if (logJunctionsOnAwake)
                Debug.Log("[TrackGrid] " + rails.Length + " rails, " + _junctions.Count + " junctions.");
        }

        /// Books the rail and every junction on it, or books nothing at all
        public bool TryDispatch(TrackRail rail, float speed, float moverLength)
        {
            if (rail == null || speed <= 0f) return false;

            float now = Time.time;
            if (!rail.CanAccept(now, speed)) return false;

            List<Junction> onRoute;
            if (_byRail.TryGetValue(rail, out onRoute))
            {
                float half = (moverLength + junctionClearance) * 0.5f;

                for (int i = 0; i < onRoute.Count; i++)
                {
                    if (Conflicts(onRoute[i], rail, now, speed, half)) return false;
                }

                for (int i = 0; i < onRoute.Count; i++)
                {
                    onRoute[i].Windows.Add(WindowFor(onRoute[i], rail, now, speed, half));
                }
            }

            rail.Accept(now, speed);
            return true;
        }

        public void ClearTraffic()
        {
            for (int i = 0; i < _junctions.Count; i++) _junctions[i].Windows.Clear();
            if (rails == null) return;

            for (int i = 0; i < rails.Length; i++)
            {
                if (rails[i] != null) rails[i].ClearTraffic();
            }
        }

        private Window WindowFor(Junction junction, TrackRail rail, float now, float speed, float half)
        {
            float distance = junction.A == rail ? junction.DistanceOnA : junction.DistanceOnB;
            return new Window
            {
                Enter = now + (distance - half) / speed,
                Exit = now + (distance + half) / speed,
            };
        }

        private bool Conflicts(Junction junction, TrackRail rail, float now, float speed, float half)
        {
            var candidate = WindowFor(junction, rail, now, speed, half);

            for (int i = junction.Windows.Count - 1; i >= 0; i--)
            {
                if (junction.Windows[i].Exit < now) { junction.Windows.RemoveAt(i); continue; }

                var existing = junction.Windows[i];
                if (candidate.Enter < existing.Exit && existing.Enter < candidate.Exit) return true;
            }

            return false;
        }

        private void FindJunctions()
        {
            _junctions.Clear();
            _byRail.Clear();

            for (int i = 0; i < rails.Length; i++)
            {
                for (int k = i + 1; k < rails.Length; k++)
                {
                    var a = rails[i];
                    var b = rails[k];
                    if (a == null || b == null) continue;

                    float onA, onB;
                    if (!Crosses(a, b, out onA, out onB)) continue;

                    var junction = new Junction { A = a, B = b, DistanceOnA = onA, DistanceOnB = onB };
                    _junctions.Add(junction);
                    Index(a, junction);
                    Index(b, junction);
                }
            }
        }

        private void Index(TrackRail rail, Junction junction)
        {
            List<Junction> list;
            if (!_byRail.TryGetValue(rail, out list))
            {
                list = new List<Junction>();
                _byRail.Add(rail, list);
            }

            list.Add(junction);
        }

        /// Closest approach between two segments. Directions are unit length, so the usual dot
        /// products collapse to this
        private bool Crosses(TrackRail a, TrackRail b, out float onA, out float onB)
        {
            onA = 0f;
            onB = 0f;

            Vector3 da = a.Direction;
            Vector3 db = b.Direction;
            Vector3 r = a.Origin - b.Origin;

            float dot = Vector3.Dot(da, db);
            float denominator = 1f - dot * dot;

            // Parallel rails have no single crossing point, and two of them sharing a line is a
            // layout mistake rather than a junction
            if (Mathf.Abs(denominator) < 1e-5f) return false;

            float c = Vector3.Dot(da, r);
            float f = Vector3.Dot(db, r);

            float s = (dot * f - c) / denominator;
            float t = (f - dot * c) / denominator;

            if (s < 0f || s > a.Length || t < 0f || t > b.Length) return false;
            if (Vector3.Distance(a.PointAt(s), b.PointAt(t)) > crossTolerance) return false;

            onA = s;
            onB = t;
            return true;
        }
    }
}
