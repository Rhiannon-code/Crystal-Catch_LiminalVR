using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace IntuitiveDesigns.CrystalCatch
{
    public class CrystalSpawner : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private CrystalCatchGame game;
        [SerializeField] private CrystalEconomy economy;     // Value curve + time-varying colour weights
        [SerializeField] private Transform playerTarget;     // Player's catch zone (chest/head anchor), the centre

        [Header("Spawn ring (deck: 'from all directions', tuned for standing comfort")]
        [SerializeField] private float spawnRadius = 2.2f;
        [SerializeField] private float arcDegrees = 220f;
        [SerializeField] private float spawnHeightMin = -0.3f;
        [SerializeField] private float spawnHeightMax = 0.7f;

        [Header("Fly in")]
        [SerializeField] private int maxConcurrent = 6;
        [SerializeField] private float launchSpeed = 1.1f;   // Controlled approach speed
        [SerializeField] private float spreadAngle = 18f;

        [Header("Pooled prefabs (one per colour)")]
        [SerializeField] private Crystal[] crystalPrefabs;   // Blue, Green, Purple, Gold
        [SerializeField] private int poolPerColour = 16;

        [Header("Special items (power ups + hazards)")]
        [SerializeField] private SpecialItem[] specialPrefabs;    // A mix, sorted into two pools by IsHazard
        [SerializeField] private int copiesPerSpecial = 3;
        [Tooltip("Hazard share of specials over normalised time — rises late so the gold-flurry finish is " +
                 "also the riskiest (ADR 0004).")]
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

        /// <summary>Raised each time a crystal or item is emitted, so the core can pulse (CrystalCoreFX).</summary>
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
                _elapsedNorm = game.ElapsedNormalized;   // 0 at start → 1 at the final second

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
            PlaceAround(cr.transform);
            cr.gameObject.SetActive(true);
            cr.Launch(playerTarget, LaunchDirection(cr.transform.position), launchSpeed);
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
            PlaceAround(s.transform);
            s.Configure(game, this);
            s.gameObject.SetActive(true);
            s.Launch(playerTarget, LaunchDirection(s.transform.position), launchSpeed);
            _active++;
            Emitted?.Invoke();
        }

        private bool AtCap => _active >= maxConcurrent;

        // Place on a ring/shell around the player so items approach from all directions
        // LaunchDirection then aims each one inward at the catch zone
        private void PlaceAround(Transform t)
        {
            Vector3 centre = playerTarget != null ? playerTarget.position : transform.position;
            float half = arcDegrees * 0.5f * Mathf.Deg2Rad;   // Arc=360, full circle
            float theta = Random.Range(-half, half);
            Vector3 dir = new Vector3(Mathf.Sin(theta), 0f, Mathf.Cos(theta));
            float h = Random.Range(spawnHeightMin, spawnHeightMax);
            t.position = centre + dir * spawnRadius + Vector3.up * h;
            t.rotation = Random.rotation;
        }

        // Heading from the spawn point toward the player, fanned by a random cone so crystals spread
        // through the catch zone instead of stacking on one line
        private Vector3 LaunchDirection(Vector3 from)
        {
            Vector3 toPlayer = (playerTarget != null ? playerTarget.position : from + Vector3.forward) - from;
            if (toPlayer.sqrMagnitude < 1e-4f) toPlayer = Vector3.forward;
            Quaternion spread = Quaternion.Euler(
                Random.Range(-spreadAngle, spreadAngle),
                Random.Range(-spreadAngle, spreadAngle), 0f);
            return (spread * toPlayer).normalized;
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
