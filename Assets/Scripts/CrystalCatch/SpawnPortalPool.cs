using System.Collections.Generic;
using UnityEngine;

namespace IntuitiveDesigns.CrystalCatch
{
    public class SpawnPortalPool : MonoBehaviour
    {
        public static SpawnPortalPool Instance { get; private set; }

        [Header("Prefabs (one per crystal colour: Blue, Green, Purple, Gold)")]
        [SerializeField] private GameObject[] crystalPortals;

        [Header("Prefabs (specials)")]
        [SerializeField] private GameObject powerUpPortal;
        [SerializeField] private GameObject hazardPortal;

        [Header("Pool (data)")]
        [SerializeField] private int copiesPerPortal = 3;
        [SerializeField] private float lifetime = 1.6f;
        [SerializeField] private int maxConcurrent = 4;

        [Header("Placement")]
        [SerializeField] private bool facePlayer = true;

        private class Pool
        {
            public Queue<GameObject> Free = new Queue<GameObject>();
            public List<ParticleSystem[]> Systems = new List<ParticleSystem[]>();
        }

        private readonly Dictionary<GameObject, Pool> _pools = new Dictionary<GameObject, Pool>();
        private readonly Dictionary<GameObject, GameObject> _origin = new Dictionary<GameObject, GameObject>();
        private readonly Dictionary<GameObject, ParticleSystem[]> _cached =
            new Dictionary<GameObject, ParticleSystem[]>();
        private int _live;

        private void Awake()
        {
            Instance = this;

            for (int i = 0; crystalPortals != null && i < crystalPortals.Length; i++)
                Build(crystalPortals[i]);

            Build(powerUpPortal);
            Build(hazardPortal);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Build(GameObject prefab)
        {
            if (prefab == null || _pools.ContainsKey(prefab)) return;

            var pool = new Pool();
            for (int i = 0; i < copiesPerPortal; i++)
            {
                var go = Instantiate(prefab, transform);
                go.SetActive(false);

                // The pack's portals ship LOOPING, which would never return to the pool
                var systems = go.GetComponentsInChildren<ParticleSystem>(true);
                for (int s = 0; s < systems.Length; s++)
                {
                    var main = systems[s].main;
                    main.loop = false;
                    main.playOnAwake = false;
                }

                _cached[go] = systems;
                _origin[go] = prefab;
                pool.Free.Enqueue(go);
            }

            _pools[prefab] = pool;
        }

        public void PlayCrystal(CrystalColour colour, Vector3 position)
        {
            int i = (int)colour;
            if (crystalPortals == null || i < 0 || i >= crystalPortals.Length) return;
            Play(crystalPortals[i], position);
        }

        public void PlaySpecial(bool hazard, Vector3 position)
        {
            Play(hazard ? hazardPortal : powerUpPortal, position);
        }

        private void Play(GameObject prefab, Vector3 position)
        {
            if (prefab == null || _live >= maxConcurrent) return;

            Pool pool;
            if (!_pools.TryGetValue(prefab, out pool) || pool.Free.Count == 0) return;

            var go = pool.Free.Dequeue();
            go.transform.position = position;

            if (facePlayer)
            {
                var cam = Camera.main;
                if (cam != null)
                {
                    Vector3 toCam = cam.transform.position - position;
                    if (toCam.sqrMagnitude > 1e-4f) go.transform.rotation = Quaternion.LookRotation(-toCam);
                }
            }

            go.SetActive(true);

            var systems = _cached[go];
            for (int s = 0; s < systems.Length; s++)
            {
                systems[s].Clear(true);
                systems[s].Play(true);
            }

            _live++;
            StartCoroutine(Recycle(go, prefab));
        }

        private System.Collections.IEnumerator Recycle(GameObject go, GameObject prefab)
        {
            yield return new WaitForSeconds(lifetime);

            go.SetActive(false);
            _pools[prefab].Free.Enqueue(go);
            _live = Mathf.Max(0, _live - 1);
        }
    }
}
