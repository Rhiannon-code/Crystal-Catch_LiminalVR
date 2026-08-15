using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace IntuitiveDesigns.CrystalCatch
{
    public class CrystalSpawner : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private CrystalCatchGame game;
        [SerializeField] private CrystalEconomy economy;     // Value curve + time varying colour weights
        [SerializeField] private CartController cart;        // Supplies arc length position and speed
        [SerializeField] private TrackPath track;            // Supplies where the track is ahead

        [Header("Drop placement (mine cart design)")]
        [SerializeField] private float leadTime = 2.2f;
        [SerializeField] private float minLeadDistance = 6f;
        [SerializeField] private float ceilingHeight = 3f;
        [SerializeField] private float swingHeight = 1.2f;
        [SerializeField] private float lateralSpread = 1.1f;

        [Header("Concurrency")]
        [SerializeField] private int maxConcurrent = 6;

        [Header("Pooled prefabs (one per colour)")]
        [SerializeField] private Crystal[] crystalPrefabs;   // Blue, Green, Purple, Gold
        [SerializeField] private int poolPerColour = 16;

        [Header("Special items (power ups + hazards)")]
        [SerializeField] private SpecialItem[] specialPrefabs;    // A mix, sorted into two pools by IsHazard
        [SerializeField] private int copiesPerSpecial = 3;
        [SerializeField] private AnimationCurve hazardShare =
            new AnimationCurve(new Keyframe(0f, 0.15f), new Keyframe(1f, 0.5f));

        [Header("Ramp (data)")]
        [SerializeField] private AnimationCurve spawnInterval = AnimationCurve.Linear(0, 0.6f, 1, 0.55f);
        [SerializeField] private AnimationCurve specialChance = AnimationCurve.Linear(0, 0.05f, 1, 0.18f);

        // Colour selection + value now live in the CrystalEconomy asset (the 'richer late game' arc)
        private Queue<Crystal>[] _pools;
        private Queue<SpecialItem> _powerUpQueue;
        private Queue<SpecialItem> _hazardQueue;
        private float _elapsedNorm;
        private int _active;                                 // In flight crystals + items
        private bool _spawning;
        private Coroutine _loop;

        /// Raised each time a crystal or item is emitted, so the core can pulse (CrystalCoreFX)
        public event System.Action Emitted;

        private void Awake()
        {
            game.Spawner = this;

            _pools = new Queue<Crystal>[crystalPrefabs.Length];
            for (int c = 0; c < crystalPrefabs.Length; c++)
            {
                _pools[c] = new Queue<Crystal>();
                for (int i = 0; i < poolPerColour; i++)
                {
                    var cr = Instantiate(crystalPrefabs[c], transform);
                    cr.Configure(this);
                    cr.gameObject.SetActive(false);
                    _pools[c].Enqueue(cr);
                }
            }

            // Two pools so we can emit a chosen category (power up vs hazard) at spawn time
            _powerUpQueue = new Queue<SpecialItem>();
            _hazardQueue = new Queue<SpecialItem>();
            foreach (var prefab in specialPrefabs)
            {
                if (prefab == null) continue;
                for (int i = 0; i < copiesPerSpecial; i++)
                {
                    var s = Instantiate(prefab, transform);
                    s.gameObject.SetActive(false);
                    (s.IsHazard ? _hazardQueue : _powerUpQueue).Enqueue(s);
                }
            }
        }

        public void StartSpawning()
        {
            if (_spawning) return;
            _spawning = true;
            _loop = StartCoroutine(SpawnLoop());
        }

        public void StopSpawning()
        {
            _spawning = false;
            if (_loop != null) StopCoroutine(_loop);
        }

        public void EmitBurst(int count)
        {
            for (int i = 0; i < count; i++) EmitCrystal();
        }

        private IEnumerator SpawnLoop()
        {
            while (_spawning)
            {
                _elapsedNorm = game.ElapsedNormalized;   // 0 at start to 1 at the final second

                if (Random.value < specialChance.Evaluate(_elapsedNorm))
                    EmitSpecial();
                else
                    EmitCrystal();

                float interval = spawnInterval.Evaluate(_elapsedNorm);
                yield return new WaitForSeconds(interval);
            }
        }

        private void EmitCrystal()
        {
            if (AtCap) return;                     // Concurrency guardrail
            CrystalColour colour = economy.WeightedColour(_elapsedNorm);  // Richer late game arc
            int c = (int)colour;
            if (_pools[c].Count == 0) return;      // Pool exhausted, skip rather than allocate
            var cr = _pools[c].Dequeue();
            cr.SetValue(economy.Points(colour));   // Value comes from the central economy

            float fallSpeed, despawnY;
            PlaceAhead(cr.transform, out fallSpeed, out despawnY);
            cr.gameObject.SetActive(true);
            cr.Launch(fallSpeed, despawnY, game);
            _active++;
            Emitted?.Invoke();                     // Core pulses on each emit (CrystalCoreFX)
        }

        private void EmitSpecial()
        {
            if (AtCap) return;

            // Choose power up vs hazard by the late rising hazard share
            bool wantHazard = Random.value < hazardShare.Evaluate(_elapsedNorm);
            var queue = wantHazard ? _hazardQueue : _powerUpQueue;
            if (queue.Count == 0) queue = wantHazard ? _powerUpQueue : _hazardQueue; // Fall back to the other
            if (queue.Count == 0) { EmitCrystal(); return; }

            var s = queue.Dequeue();

            float fallSpeed, despawnY;
            PlaceAhead(s.transform, out fallSpeed, out despawnY);
            s.Configure(game, this);
            s.gameObject.SetActive(true);
            s.Launch(fallSpeed, despawnY, game);
            _active++;
            Emitted?.Invoke();
        }

        private bool AtCap => _active >= maxConcurrent;

        private void PlaceAhead(Transform t, out float fallSpeed, out float despawnY)
        {
            fallSpeed = 0f;
            despawnY = -50f;
            if (track == null) { t.position = transform.position; return; }

            float speed = cart != null ? cart.CurrentSpeed : 0f;
            float here = cart != null ? cart.Distance : 0f;
            float lead = Mathf.Max(minLeadDistance, speed * leadTime);
            float d = Mathf.Min(here + lead, track.Length);

            Vector3 basePos = track.PositionAt(d);
            Vector3 right = track.RightAt(d);

            float x = Random.Range(-lateralSpread, lateralSpread);
            t.position = basePos + right * x + Vector3.up * ceilingHeight;
            t.rotation = Random.rotation;

            // Time the cart will actually take to cover the lead, so a slow cart gets a slow drop
            float travelTime = speed > 0.05f ? lead / speed : leadTime;

            fallSpeed = FallingMover.SpeedForIntercept(ceilingHeight, swingHeight, travelTime);
            despawnY = basePos.y - 1.5f;
        }

        public void Return(Crystal cr)
        {
            cr.gameObject.SetActive(false);
            _pools[(int)cr.Colour].Enqueue(cr);
            _active = Mathf.Max(0, _active - 1);
        }

        public void Return(SpecialItem s)
        {
            s.gameObject.SetActive(false);
            (s.IsHazard ? _hazardQueue : _powerUpQueue).Enqueue(s);
            _active = Mathf.Max(0, _active - 1);
        }
    }
}
