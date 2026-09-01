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
        [SerializeField] private int copiesPerPortal = 4;
        [SerializeField] private float lifetime = 1.6f;
        [SerializeField] private int maxConcurrent = 8;

        [Header("Opening")]
        [SerializeField] private float warmupSeconds = 0.2f;

        [Header("Placement")]
        [SerializeField] private bool facePlayer = true;

        [Header("Reuse")]
        [SerializeField, Range(0f, 1f)] private float reclaimAfterFraction = 0.6f;

        [Header("Fill rate (data)")]
        [SerializeField, Range(0.2f, 1f)] private float portalScale = 1f;

        private class Pool
        {
            public Queue<GameObject> Free = new Queue<GameObject>();
        }

        private struct Live
        {
            public GameObject Go;
            public int Slot;
            public float StartedAt;
            public float ExpiresAt;
        }

        private Pool[] _slots;
        private readonly Dictionary<GameObject, ParticleSystem[]> _cached =
            new Dictionary<GameObject, ParticleSystem[]>();
        private readonly List<Live> _live = new List<Live>();
        private Transform _cam;

        private int CrystalSlots { get { return crystalPortals != null ? crystalPortals.Length : 0; } }
        private int PowerUpSlot { get { return CrystalSlots; } }
        private int HazardSlot { get { return CrystalSlots + 1; } }

        private void Awake()
        {
            Instance = this;

            _slots = new Pool[CrystalSlots + 2];
            for (int i = 0; i < _slots.Length; i++) _slots[i] = new Pool();

            for (int i = 0; i < CrystalSlots; i++) Build(crystalPortals[i], i);
            Build(powerUpPortal, PowerUpSlot);
            Build(hazardPortal, HazardSlot);

            WarnOnSharedPrefabs();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Build(GameObject prefab, int slot)
        {
            if (prefab == null) return;

            var pool = _slots[slot];
            for (int i = 0; i < copiesPerPortal; i++)
            {
                var go = Instantiate(prefab, transform);
                go.SetActive(false);
                if (portalScale < 0.999f) go.transform.localScale = Vector3.one * portalScale;

                // The pack's portals ship LOOPING, which would never return to the pool
                var systems = go.GetComponentsInChildren<ParticleSystem>(true);
                for (int s = 0; s < systems.Length; s++)
                {
                    var main = systems[s].main;
                    main.loop = false;
                    main.playOnAwake = false;
                }

                _cached[go] = systems;
                pool.Free.Enqueue(go);
            }
        }

        private void WarnOnSharedPrefabs()
        {
            var seen = new Dictionary<GameObject, string>();
            for (int i = 0; crystalPortals != null && i < crystalPortals.Length; i++)
                Check(seen, crystalPortals[i], "crystalPortals[" + i + "]");
            Check(seen, powerUpPortal, "powerUpPortal");
            Check(seen, hazardPortal, "hazardPortal");
        }

        private static void Check(Dictionary<GameObject, string> seen, GameObject prefab, string slot)
        {
            if (prefab == null) return;
            string first;
            if (seen.TryGetValue(prefab, out first))
                Debug.Log("[SpawnPortalPool] " + slot + " and " + first + " both use '" +
                          prefab.name + "'. Pools are per slot so they no longer starve each other, " +
                          "but the two will look identical in play. Give each its own prefab when art allows.");
            else
                seen[prefab] = slot;
        }

        public bool PlayCrystal(CrystalColour colour, Vector3 position)
        {
            int i = (int)colour;
            if (crystalPortals == null || i < 0 || i >= crystalPortals.Length) return false;
            return Play(i, position);
        }

        public bool PlaySpecial(bool hazard, Vector3 position)
        {
            return Play(hazard ? HazardSlot : PowerUpSlot, position);
        }

        private void Update()
        {
            for (int i = _live.Count - 1; i >= 0; i--)
                if (Time.time >= _live[i].ExpiresAt) Retire(i);
        }

        private bool Play(int slot, Vector3 position)
        {
            if (_slots == null || slot < 0 || slot >= _slots.Length) return false;
            var pool = _slots[slot];
            if (pool.Free.Count == 0 && !Reclaim(slot)) return false;
            if (_live.Count >= maxConcurrent && !Reclaim(-1)) return false;
            if (pool.Free.Count == 0) return false;

            var go = pool.Free.Dequeue();
            go.transform.position = position;

            if (facePlayer)
            {
                var cam = Head();
                if (cam != null)
                {
                    Vector3 toCam = cam.position - position;
                    if (toCam.sqrMagnitude > 1e-4f) go.transform.rotation = Quaternion.LookRotation(-toCam);
                }
            }

            go.SetActive(true);

            // withChildren is false throughout because we are already iterating every system in the
            // prefab, letting a parent also drive its children would warm them up twice
            var systems = _cached[go];
            for (int s = 0; s < systems.Length; s++)
            {
                if (warmupSeconds > 0f) systems[s].Simulate(warmupSeconds, false, true, false);
                else systems[s].Clear(false);

                systems[s].Play(false);
            }

            Live live;
            live.Go = go;
            live.Slot = slot;
            live.StartedAt = Time.time;
            live.ExpiresAt = Time.time + lifetime;
            _live.Add(live);
            return true;
        }

        private bool Reclaim(int slotFilter)
        {
            float minAge = lifetime * reclaimAfterFraction;

            int best = -1;
            for (int i = 0; i < _live.Count; i++)
            {
                if (slotFilter >= 0 && _live[i].Slot != slotFilter) continue;
                if (Time.time - _live[i].StartedAt < minAge) continue;
                if (best < 0 || _live[i].ExpiresAt < _live[best].ExpiresAt) best = i;
            }

            if (best < 0) return false;
            Retire(best);
            return true;
        }

        private void Retire(int index)
        {
            var live = _live[index];
            _live.RemoveAt(index);

            if (live.Go == null) return;
            live.Go.SetActive(false);
            _slots[live.Slot].Free.Enqueue(live.Go);
        }

        /// Camera.main is an untagged object search on 2019.1 and this runs on every spawn, so cache it
        private Transform Head()
        {
            if (_cam != null) return _cam;

            var c = Camera.main;
            if (c == null) c = Object.FindObjectOfType<Camera>();
            if (c != null) _cam = c.transform;
            return _cam;
        }
    }
}
