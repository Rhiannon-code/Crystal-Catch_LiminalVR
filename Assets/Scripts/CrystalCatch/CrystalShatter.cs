using UnityEngine;

namespace IntuitiveDesigns.CrystalCatch
{
    public class CrystalShatter : MonoBehaviour
    {
        public static CrystalShatter Instance { get; private set; }

        [Header("Look")]
        [SerializeField] private Mesh shardMesh;
        [SerializeField] private Material shardMaterial;
        [SerializeField] private Color[] tints = new Color[4];

        [Header("Break up (data)")]
        [SerializeField] private int shardsPerHit = 4;
        [SerializeField] private int simultaneousBursts = 4;
        [SerializeField] private float shardScale = 0.12f;

        [Header("Throw (data)")]
        [SerializeField] private float speed = 2.5f;
        [SerializeField] private float inheritHitVelocity = 0.35f;
        [SerializeField] private float spreadDegrees = 45f;
        [SerializeField] private float spinDegreesPerSecond = 540f;
        [SerializeField] private float gravity = 6f;
        [SerializeField] private float life = 0.7f;

        private struct Shard
        {
            public Transform T;
            public Vector3 Velocity;
            public Vector3 SpinAxis;
            public float Age;
            public bool Live;
        }

        private Shard[] _shards;
        private MaterialPropertyBlock _mpb;
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private void Awake()
        {
            Instance = this;
            _mpb = new MaterialPropertyBlock();

            int count = Mathf.Max(1, shardsPerHit * simultaneousBursts);
            _shards = new Shard[count];

            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("Shard_" + i);
                go.transform.SetParent(transform, false);

                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = shardMesh;

                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = shardMaterial;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

                go.SetActive(false);
                _shards[i] = new Shard { T = go.transform };
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void Shatter(CrystalColour colour, Vector3 position, Vector3 hitVelocity)
        {
            if (_shards == null || shardMesh == null) return;

            Color tint = tints != null && (int)colour < tints.Length ? tints[(int)colour] : Color.white;

            // The swing carries the shards, but only partly, fully inheriting a 6 m/s swing throws
            // them out of view before they read as shards at all
            Vector3 carried = hitVelocity * inheritHitVelocity;

            int spawned = 0;
            for (int i = 0; i < _shards.Length && spawned < shardsPerHit; i++)
            {
                if (_shards[i].Live) continue;

                Vector3 dir = Random.onUnitSphere;
                dir.y = Mathf.Abs(dir.y) * 0.6f + 0.4f;          // Biased upward, so they arc
                dir = Vector3.Slerp(dir.normalized, Random.onUnitSphere,
                                    Mathf.Clamp01(spreadDegrees / 180f)).normalized;

                _shards[i].Velocity = carried + dir * speed;
                _shards[i].SpinAxis = Random.onUnitSphere;
                _shards[i].Age = 0f;
                _shards[i].Live = true;

                var t = _shards[i].T;
                t.position = position;
                t.rotation = Random.rotation;
                t.localScale = Vector3.one * shardScale;
                t.gameObject.SetActive(true);

                var mr = t.GetComponent<MeshRenderer>();
                mr.GetPropertyBlock(_mpb);
                _mpb.SetColor(ColorId, tint);
                mr.SetPropertyBlock(_mpb);

                spawned++;
            }
        }

        private void Update()
        {
            if (_shards == null) return;

            float dt = Time.deltaTime;

            for (int i = 0; i < _shards.Length; i++)
            {
                if (!_shards[i].Live) continue;

                _shards[i].Age += dt;
                float t01 = _shards[i].Age / Mathf.Max(0.01f, life);

                if (t01 >= 1f)
                {
                    _shards[i].Live = false;
                    _shards[i].T.gameObject.SetActive(false);
                    continue;
                }

                _shards[i].Velocity += Vector3.down * (gravity * dt);

                var t = _shards[i].T;
                t.position += _shards[i].Velocity * dt;
                t.Rotate(_shards[i].SpinAxis, spinDegreesPerSecond * dt, Space.World);

                // Shrinking to nothing rather than fading, because the shards are opaque and unlit,
                // fading would mean a transparent material and more overdraw for no gain
                t.localScale = Vector3.one * (shardScale * (1f - t01));
            }
        }
    }
}
