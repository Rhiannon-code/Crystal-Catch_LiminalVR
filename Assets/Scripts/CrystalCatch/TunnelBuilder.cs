using UnityEngine;

namespace IntuitiveDesigns.CrystalCatch
{
    public class TunnelBuilder : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private CartController cart;
        [SerializeField] private TrackPath track;
        [SerializeField] private GameObject ringPrefab;
        [SerializeField] private GameObject railPrefab;
        [SerializeField] private GameObject framePrefab;
        [SerializeField] private CaveAtmosphere atmosphere;

        public const string AuthoringRootName = "TUNNEL (editor authoring)";

        [Header("Layout (data)")]
        [SerializeField] private float ringSpacing = 4f;
        [SerializeField] private float visibleAhead = 60f;
        [SerializeField] private float visibleBehind = 12f;

        [Header("Frames")]
        [SerializeField] private float frameSpacing = 16f;

        [Header("Rail")]
        [SerializeField] private float railSpacing = 2f;
        [SerializeField] private float railVisibleAhead = 50f;

        /// One pooled run of a repeating piece, a ring, a rail segment, a timber set
        private class Pool
        {
            public Transform[] Items;
            public float[] Distances;
            public float Span;
        }

        private Pool _rings;
        private Pool _rails;
        private Pool _frames;

        private void Start()
        {
            if (track == null || cart == null || ringPrefab == null)
            {
                Debug.LogWarning("[TunnelBuilder] Missing refs, tunnel will not be built.");
                enabled = false;
                return;
            }

            if (!track.IsGenerated) track.Generate();

            // Before the pools are built, because their array sizes come from these
            if (atmosphere != null)
            {
                visibleAhead = atmosphere.DrawDistance;
                visibleBehind = atmosphere.DrawDistance;
                railVisibleAhead = atmosphere.DrawDistance;
            }

            HideAuthoredTunnel();

            _rings = BuildPool(ringPrefab, "Ring", ringSpacing, visibleAhead);
            _rails = BuildPool(railPrefab, "Rail", railSpacing, railVisibleAhead);
            _frames = BuildPool(framePrefab, "Frame", frameSpacing, visibleAhead);
        }

        private Pool BuildPool(GameObject prefab, string label, float spacing, float ahead)
        {
            if (prefab == null || spacing <= 0.01f) return null;

            int count = Mathf.Max(2, Mathf.CeilToInt((ahead + visibleBehind) / spacing) + 1);

            var pool = new Pool();
            pool.Items = new Transform[count];
            pool.Distances = new float[count];
            pool.Span = count * spacing;

            for (int i = 0; i < count; i++)
            {
                var go = Instantiate(prefab, transform);
                go.name = label + "_" + i;
                pool.Items[i] = go.transform;
                pool.Distances[i] = (i * spacing) - visibleBehind;
                Place(pool, i);
            }

            return pool;
        }

        /// The baked tunnel is an authoring aid, not the shipping tunnel. It is tagged EditorOnly so
        /// a build strips it
        private void HideAuthoredTunnel()
        {
            var authored = GameObject.Find(AuthoringRootName);
            if (authored == null) return;

            authored.SetActive(false);
            Debug.Log("[TunnelBuilder] Editor authored tunnel hidden for play, pooled rings in use.");
        }

        private void LateUpdate()
        {
            if (_rings == null) return;

            float d = cart.Distance;

            Recycle(_rings, d);
            Recycle(_rails, d);
            Recycle(_frames, d);
        }

        private void Recycle(Pool pool, float d)
        {
            if (pool == null) return;

            for (int i = 0; i < pool.Items.Length; i++)
            {
                // While, not if, a frame hitch at speed can drop a piece more than one span behind,
                // and a single jump would tear a visible gap in the tunnel
                bool moved = false;
                while (pool.Distances[i] < d - visibleBehind)
                {
                    pool.Distances[i] += pool.Span;
                    moved = true;
                }
                if (moved) Place(pool, i);
            }
        }

        private void Place(Pool pool, int i)
        {
            float dist = pool.Distances[i];
            var item = pool.Items[i];

            // Past the end of the track there is nothing to show, park it rather than smearing
            // pieces on the clamped final point
            if (dist > track.Length)
            {
                item.gameObject.SetActive(false);
                return;
            }

            if (!item.gameObject.activeSelf) item.gameObject.SetActive(true);

            item.position = track.PositionAt(dist);
            item.rotation = track.RotationAt(dist, true);
        }
    }
}
