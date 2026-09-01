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
        [SerializeField] private SpawnDirector director;

        [Header("Drop placement (mine cart design)")]
        [SerializeField] private float leadTime = 2.2f;
        [SerializeField] private float interceptLead = 2.5f;
        [SerializeField] private float minFallTime = 0.5f;
        [SerializeField] private float minLeadDistance = 6f;
        [SerializeField] private float ceilingHeight = 3f;
        [SerializeField] private float swingHeight = 1.2f;
        [SerializeField] private float lateralSpread = 1.1f;

        [Header("Concurrency")]
        [SerializeField] private int maxConcurrent = 6;

        [Header("Spawn portals (data)")]
        [SerializeField] private float portalLead = 0.5f;

        [Header("Pooled prefabs (one per colour)")]
        [SerializeField] private Crystal[] crystalPrefabs;   // Blue, Green, Purple, Gold
        [SerializeField] private int poolPerColour = 16;

        [Header("Specials: when they enter the game")]
        [SerializeField] private int specialsFromRound = 2;

        [Header("Special items (power ups + hazards)")]
        [SerializeField] private SpecialItem[] specialPrefabs;    // A mix, sorted into two pools by IsHazard
        [SerializeField] private int copiesPerSpecial = 3;
        [SerializeField] private AnimationCurve hazardShare =
            new AnimationCurve(new Keyframe(0f, 0.15f), new Keyframe(1f, 0.5f));

        [Header("Ramp (data)")]
        [SerializeField] private AnimationCurve spawnInterval = AnimationCurve.Linear(0, 0.6f, 1, 0.55f);
        [SerializeField] private AnimationCurve specialChance = AnimationCurve.Linear(0, 0.05f, 1, 0.18f);

        [Header("Lead in (data)")]
        [SerializeField] private float leadInSeconds = 3f;
        [SerializeField] private float minCartSpeed = 2.5f;
        [SerializeField] private float maxSpeedWait = 8f;

        // How far past the emit horizon the schedule is kept generated
        [SerializeField] private float scheduleHeadroom = 60f;

        // Colour selection + value now live in the CrystalEconomy asset (the 'richer late game' arc)
        private Queue<Crystal>[] _pools;
        private readonly Dictionary<SpecialKind, Queue<SpecialItem>> _specialPools =
            new Dictionary<SpecialKind, Queue<SpecialItem>>();
        private float _elapsedNorm;
        private int _active;                                 // In flight crystals + items
        private bool _spawning;
        private Coroutine _loop;
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

            foreach (var prefab in specialPrefabs)
            {
                if (prefab == null) continue;

                Queue<SpecialItem> pool;
                if (!_specialPools.TryGetValue(prefab.Kind, out pool))
                {
                    pool = new Queue<SpecialItem>();
                    _specialPools[prefab.Kind] = pool;
                }

                for (int i = 0; i < copiesPerSpecial; i++)
                {
                    var s = Instantiate(prefab, transform);
                    s.gameObject.SetActive(false);
                    pool.Enqueue(s);
                }
            }
        }

        public void StartSpawning() { StartSpawning(false); }

        public void StartSpawning(bool withLeadIn)
        {
            if (_spawning) return;
            _spawning = true;
            _loop = StartCoroutine(SpawnLoop(withLeadIn ? leadInSeconds : 0f));
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

        private IEnumerator SpawnLoop(float leadIn)
        {
            if (leadIn > 0f) yield return new WaitForSeconds(leadIn);

            yield return WaitForRollingCart();

            if (director != null) yield return ScheduledLoop();
            else yield return LegacyLoop();
        }

        private IEnumerator ScheduledLoop()
        {
            while (_spawning)
            {
                _elapsedNorm = game.ElapsedNormalized;

                float here = cart != null ? cart.Distance : 0f;
                float speed = cart != null ? cart.CurrentSpeed : 0f;
                float lead = Mathf.Max(minLeadDistance, speed * leadTime);

                // Generated well past the horizon so growing the schedule never coincides with
                // needing to read from it
                director.EnsureScheduledTo(here + lead + scheduleHeadroom);

                ScheduledItem item;
                while (!AtCap && director.TryTake(here + lead, out item))
                {
                    if (!CanStillLand(item.Distance, here, speed)) continue;

                    Emit(item, here, speed);
                }

                yield return null;
            }
        }

        /// The pre director behaviour, roll a kind on a timer, drop it a fixed lead ahead
        private IEnumerator LegacyLoop()
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

        private bool CanStillLand(float distance, float here, float speed)
        {
            if (speed <= 0.05f) return true;         // Pre roll, PlaceAt falls back to leadTime

            float interceptAt = Mathf.Min(distance, track != null ? track.Length : distance)
                              - Mathf.Max(0f, interceptLead);

            // The portal has to finish opening before the fall even starts
            float needed = speed * (minFallTime + PortalHold);

            return interceptAt - here >= needed;
        }

        private void Emit(ScheduledItem item, float here, float speed)
        {
            switch (item.Kind)
            {
                case SpawnSlotKind.Crystal:
                    EmitCrystalAt(item, here, speed);
                    break;

                default:
                    EmitSpecialAt(item, here, speed);
                    break;
            }
        }

        private IEnumerator WaitForRollingCart()
        {
            if (cart == null) yield break;

            float waited = 0f;
            while (_spawning && cart.CurrentSpeed < minCartSpeed && waited < maxSpeedWait)
            {
                waited += Time.deltaTime;
                yield return null;
            }

            if (waited >= maxSpeedWait)
                Debug.LogWarning("[CrystalSpawner] Cart never reached minCartSpeed (" + minCartSpeed +
                                 " m/s) in " + maxSpeedWait + " s, it is at " +
                                 cart.CurrentSpeed.ToString("0.0") + ". Spawning anyway, but drops " +
                                 "will land short of the cart. Lower minCartSpeed or raise cart speed.");
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
            bool portal = OpenCrystalPortal(colour, cr.transform.position);
            cr.gameObject.SetActive(true);
            cr.Launch(fallSpeed, despawnY, game, PortalHold, portal);
            _active++;
            Emitted?.Invoke();                     // Core pulses on each emit (CrystalCoreFX)
        }

        private void EmitSpecial()
        {
            if (AtCap) return;

            // Choose power up vs hazard by the late rising hazard share
            bool wantHazard = Random.value < hazardShare.Evaluate(_elapsedNorm);

            // Legacy path: no director means no scheduled kind, so take anything of the category
            var s = TakeSpecial(wantHazard ? SpecialKind.Bomb : SpecialKind.ScoreGem, wantHazard);
            if (s == null) { EmitCrystal(); return; }

            float fallSpeed, despawnY;
            PlaceAhead(s.transform, out fallSpeed, out despawnY);
            bool portal = OpenSpecialPortal(s.IsHazard, s.transform.position);
            s.Configure(game, this);
            s.gameObject.SetActive(true);
            s.Launch(fallSpeed, despawnY, game, PortalHold, portal);
            _active++;
            Emitted?.Invoke();
        }

        private void EmitCrystalAt(ScheduledItem item, float here, float speed)
        {
            CrystalColour colour = item.ForceColour
                ? item.Colour
                : economy.WeightedColour(_elapsedNorm);

            int c = (int)colour;
            if (c < 0 || c >= _pools.Length || _pools[c].Count == 0) return;

            var cr = _pools[c].Dequeue();
            cr.SetValue(economy.Points(colour));

            float fallSpeed, despawnY;
            PlaceAt(cr.transform, item.Distance, item.Lateral, here, speed, out fallSpeed, out despawnY);

            bool portal = OpenCrystalPortal(colour, cr.transform.position);
            cr.gameObject.SetActive(true);
            cr.Launch(fallSpeed, despawnY, game, PortalHold, portal);
            _active++;
            Emitted?.Invoke();
        }

        private void EmitSpecialAt(ScheduledItem item, float here, float speed)
        {
            // Not yet in play: spend the slot on a crystal so the ride keeps its shape
            if (game != null && game.RoundNumber < specialsFromRound)
            {
                EmitCrystalAt(item, here, speed);
                return;
            }

            bool wantHazard = item.Kind == SpawnSlotKind.Hazard;
            SpecialKind wanted = item.Special;

            // Repayment outranks the schedule. A power up slot becomes a Time Orb whenever the
            // player is owed time, which is the ONLY way a Time Orb ever reaches the track
            bool repaying = false;
            if (!wantHazard && game != null && game.TimeDebt > 0.01f && Available(SpecialKind.TimeOrb))
            {
                wanted = SpecialKind.TimeOrb;
                repaying = true;
            }

            var s = TakeSpecial(wanted, wantHazard);
            if (s == null) { EmitCrystalAt(item, here, speed); return; }

            if (repaying && s.Kind == SpecialKind.TimeOrb) game.CommitTimeRepayment(s.TimeDelta);

            float fallSpeed, despawnY;
            PlaceAt(s.transform, item.Distance, item.Lateral, here, speed, out fallSpeed, out despawnY);

            LogSpecialEmit(s, here);

            bool portal = OpenSpecialPortal(s.IsHazard, s.transform.position);
            s.Configure(game, this);
            s.gameObject.SetActive(true);
            s.Launch(fallSpeed, despawnY, game, PortalHold, portal);
            _active++;
            Emitted?.Invoke();
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        private void LogSpecialEmit(SpecialItem s, float here)
        {
            Debug.Log("[CrystalSpawner] SPECIAL spawned: " + s.name +
                      (s.IsHazard ? " (hazard)" : " (power up)") +
                      " at " + s.transform.position.ToString("0.0") +
                      ", cart at " + here.ToString("0") + " m");
        }

        private bool Available(SpecialKind kind)
        {
            Queue<SpecialItem> pool;
            return _specialPools.TryGetValue(kind, out pool) && pool.Count > 0;
        }

        /// The requested kind if it is in stock, otherwise anything of the same CATEGORY, running
        /// a pool dry should cost the exact item, never the beat itself
        private SpecialItem TakeSpecial(SpecialKind wanted, bool hazard)
        {
            Queue<SpecialItem> pool;
            if (_specialPools.TryGetValue(wanted, out pool) && pool.Count > 0)
                return pool.Dequeue();

            foreach (var entry in _specialPools)
            {
                if (entry.Value.Count == 0) continue;

                // Never substitute a Time Orb for a scheduled power up, that is exactly the
                // "time extend spawning for no reason" the debt system exists to prevent
                if (entry.Key == SpecialKind.TimeOrb) continue;

                var candidate = entry.Value.Peek();
                if (candidate.IsHazard != hazard) continue;

                return entry.Value.Dequeue();
            }
            return null;
        }

        private void PlaceAt(Transform t, float distance, float lateral, float here, float speed,
                             out float fallSpeed, out float despawnY)
        {
            fallSpeed = 0f;
            despawnY = -50f;
            if (track == null) { t.position = transform.position; return; }

            float d = Mathf.Min(distance, track.Length);

            Vector3 basePos = track.PositionAt(d);
            Vector3 right = track.RightAt(d);

            t.position = basePos + right * (Mathf.Clamp(lateral, -1f, 1f) * lateralSpread)
                       + Vector3.up * ceilingHeight;
            t.rotation = Random.rotation;

            float interceptAt = d - Mathf.Max(0f, interceptLead);
            float remaining = Mathf.Max(0f, interceptAt - here);
            float travelTime = speed > 0.05f ? remaining / speed : leadTime;

            // The portal is open for part of the window, so the FALL gets what is left. Floored at
            // minFallTime, which also stops a short lead turning into an instant drop
            travelTime = Mathf.Max(travelTime - PortalHold, minFallTime);

            fallSpeed = FallingMover.SpeedForIntercept(ceilingHeight, swingHeight, travelTime);
            despawnY = basePos.y - 1.5f;
        }

        /// True when a portal actually opened. False means the item must stay VISIBLE through its
        /// hold, or it appears out of nothing when the hold ends
        private bool OpenCrystalPortal(CrystalColour colour, Vector3 at)
        {
            return SpawnPortalPool.Instance != null && SpawnPortalPool.Instance.PlayCrystal(colour, at);
        }

        private bool OpenSpecialPortal(bool hazard, Vector3 at)
        {
            return SpawnPortalPool.Instance != null && SpawnPortalPool.Instance.PlaySpecial(hazard, at);
        }

        /// 0 when there is no pool in the scene, so removing the pool cleanly restores the old timing
        private float PortalHold
        {
            get { return SpawnPortalPool.Instance != null ? Mathf.Max(0f, portalLead) : 0f; }
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

            // The legacy path has to honour the portal too, or items emitted through it fall late
            travelTime = Mathf.Max(travelTime - PortalHold, minFallTime);

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

            Queue<SpecialItem> pool;
            if (_specialPools.TryGetValue(s.Kind, out pool)) pool.Enqueue(s);
            _active = Mathf.Max(0, _active - 1);
        }
    }
}
