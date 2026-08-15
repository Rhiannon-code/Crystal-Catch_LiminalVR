using UnityEngine;

namespace IntuitiveDesigns.CrystalCatch
{
    public class TunnelBuilder : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private CartController cart;
        [SerializeField] private TrackPath track;
        [SerializeField] private GameObject ringPrefab;

        [Header("Layout (data)")]
        [SerializeField] private float ringSpacing = 4f;
        [SerializeField] private float visibleAhead = 60f;
        [SerializeField] private float visibleBehind = 12f;

        private Transform[] _rings;
        private float[] _ringDistance;
        private float _ringSpan;

        private void Start()
        {
            if (track == null || cart == null || ringPrefab == null)
            {
                Debug.LogWarning("[TunnelBuilder] Missing refs, tunnel will not be built.");
                enabled = false;
                return;
            }

            if (!track.IsGenerated) track.Generate();

            int count = Mathf.Max(2, Mathf.CeilToInt((visibleAhead + visibleBehind) / ringSpacing) + 1);
            _rings = new Transform[count];
            _ringDistance = new float[count];
            _ringSpan = count * ringSpacing;

            for (int i = 0; i < count; i++)
            {
                var go = Instantiate(ringPrefab, transform);
                go.name = "Ring_" + i;
                _rings[i] = go.transform;
                _ringDistance[i] = (i * ringSpacing) - visibleBehind;
                Place(i);
            }
        }

        private void LateUpdate()
        {
            if (_rings == null) return;

            float d = cart.Distance;

            for (int i = 0; i < _rings.Length; i++)
            {
                // while, not if, a frame hitch at speed can drop a ring more than one span behind,
                // and a single jump would tear a visible gap in the tunnel
                bool moved = false;
                while (_ringDistance[i] < d - visibleBehind)
                {
                    _ringDistance[i] += _ringSpan;
                    moved = true;
                }
                if (moved) Place(i);
            }
        }

        private void Place(int i)
        {
            float dist = _ringDistance[i];

            // Past the end of the track there is nothing to show, park it rather than smearing
            // rings on the clamped final point
            if (dist > track.Length)
            {
                _rings[i].gameObject.SetActive(false);
                return;
            }

            if (!_rings[i].gameObject.activeSelf) _rings[i].gameObject.SetActive(true);

            _rings[i].position = track.PositionAt(dist);
            _rings[i].rotation = track.RotationAt(dist, true);
        }
    }
}
