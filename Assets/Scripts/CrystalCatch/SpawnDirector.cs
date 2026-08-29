using System.Collections.Generic;
using UnityEngine;

namespace IntuitiveDesigns.CrystalCatch
{
    /// One item, already committed to a place on the track
    public struct ScheduledItem
    {
        public float Distance;        // Arc length along the track, the shared coordinate
        public SpawnSlotKind Kind;
        public float Lateral;         // Fraction of reach, -1 to 1
        public bool ForceColour;
        public CrystalColour Colour;

        /// Which special this slot is, decided HERE rather than at emit time. The director has to
        /// commit to a concrete kind or it cannot pair a Shield with the Bomb it answers
        public SpecialKind Special;
    }

    public class SpawnDirector : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private TrackObstacles obstacles;

        [Header("Difficulty")]
        [SerializeField] private float fullDifficultyAt = 900f;

        [Header("Baseline spacing (data, metres)")]
        [SerializeField] private float firstItemAt = 40f;
        [SerializeField] private float easyGap = 8f;
        [SerializeField] private float hardGap = 4.5f;
        [SerializeField, Range(0f, 0.5f)] private float gapJitter = 0.25f;

        [Header("Mix")]
        [SerializeField] private AnimationCurve specialChance =
            new AnimationCurve(new Keyframe(0f, 0.06f), new Keyframe(1f, 0.2f));
        [SerializeField] private AnimationCurve hazardShare =
            new AnimationCurve(new Keyframe(0f, 0.15f), new Keyframe(1f, 0.5f));

        [Header("Which special (relative weights, 0 = never)")]
        [SerializeField] private float weightScoreGem = 5f;
        [SerializeField] private float weightReachBoost = 2f;
        [SerializeField] private float weightArcBoost = 2f;
        [SerializeField] private float weightCrashCrystals = 1f;

        [SerializeField] private float weightBomb = 5f;
        [SerializeField] private float weightSlowHourglass = 2f;
        [SerializeField] private float weightTimeDrain = 1f;

        [Header("Shield pairing (data, metres)")]
        [SerializeField] private float shieldLead = 32f;
        [SerializeField] private bool pairShieldWithBomb = true;

        [Header("Reach")]
        [SerializeField] private float sameSideWithin = 12f;

        [Header("Set pieces")]
        [SerializeField] private SpawnPattern[] patterns = new SpawnPattern[0];
        [SerializeField] private float patternEvery = 260f;
        [SerializeField] private float patternJitter = 80f;

        [Header("Obstacle exclusion")]
        [SerializeField] private float obstacleClearance = 14f;

        [Header("Determinism")]
        [SerializeField] private int seed = 90210;
        [SerializeField] private bool logSchedule = false;

        private readonly List<ScheduledItem> _items = new List<ScheduledItem>();
        private readonly List<float> _obstacleAt = new List<float>();
        private readonly List<TrackObstacle.Kind> _obstacleKinds = new List<TrackObstacle.Kind>();

        private System.Random _rng;
        private float _scheduledTo;
        private float _nextPatternAt;
        private float _lastLateral;
        private float _lastDistance = float.NegativeInfinity;
        private int _cursor;
        private bool _ready;

        private void Awake()
        {
            Rebuild();
        }

        /// Wipes the schedule and starts again from the seed. Safe to call at any point
        public void Rebuild()
        {
            _items.Clear();
            _obstacleAt.Clear();
            _obstacleKinds.Clear();

            _rng = new System.Random(seed);
            _scheduledTo = firstItemAt;
            _nextPatternAt = firstItemAt + patternEvery;
            _lastLateral = 0f;
            _lastDistance = float.NegativeInfinity;
            _cursor = 0;
            _ready = true;
        }

        /// The next item due at or before maxDistance, if there is one
        public bool TryTake(float maxDistance, out ScheduledItem item)
        {
            item = default(ScheduledItem);
            if (!_ready || _cursor >= _items.Count) return false;
            if (_items[_cursor].Distance > maxDistance) return false;

            item = _items[_cursor];
            _cursor++;
            return true;
        }

        /// Grows the schedule far enough ahead that the spawner never runs out mid frame
        public void EnsureScheduledTo(float required)
        {
            if (!_ready) Rebuild();

            RefreshObstacles(required + obstacleClearance + 40f);

            int guard = 0;
            while (_scheduledTo < required && guard++ < 4000)
            {
                float difficulty = DifficultyAt(_scheduledTo);

                // A set piece is due, and the track here is clear enough to hold one
                if (_scheduledTo >= _nextPatternAt)
                {
                    var pattern = ChoosePattern(difficulty);
                    if (pattern != null && !SectionBlocked(_scheduledTo, _scheduledTo + pattern.Length))
                    {
                        PlacePattern(pattern, _scheduledTo);

                        _scheduledTo += pattern.Length + Mathf.Lerp(easyGap, hardGap, difficulty);
                        _nextPatternAt = _scheduledTo + patternEvery + NextFloat() * patternJitter;
                        continue;
                    }

                    // Nothing fits here (usually an obstacle). Try again shortly rather than
                    // abandoning the set piece for this whole cycle
                    _nextPatternAt = _scheduledTo + 30f;
                }

                float gap = Mathf.Lerp(easyGap, hardGap, difficulty);
                gap *= 1f + (NextFloat() * 2f - 1f) * gapJitter;

                float consumed = 0f;
                if (!Blocked(_scheduledTo))
                    consumed = PlaceBaseline(_scheduledTo, difficulty);

                _scheduledTo += consumed + Mathf.Max(1f, gap);
            }
        }

        /// 0 at the start of the ride to 1 once the player is deep into it
        public float DifficultyAt(float distance)
        {
            if (fullDifficultyAt <= 1f) return 1f;
            return Mathf.Clamp01(distance / fullDifficultyAt);
        }

        /// Returns how much extra track this placement consumed, so a Bomb that had to be given a
        /// lead in Shield does not have the next baseline item land on top of its own bomb
        private float PlaceBaseline(float distance, float difficulty)
        {
            if (NextFloat() >= specialChance.Evaluate(difficulty))
            {
                AddSlot(distance, SpawnSlotKind.Crystal, SpecialKind.TimeOrb);
                return 0f;
            }

            bool hazard = NextFloat() < hazardShare.Evaluate(difficulty);

            if (!hazard)
            {
                AddSlot(distance, SpawnSlotKind.PowerUp, RollPowerUp());
                return 0f;
            }

            var rolled = RollHazard();

            if (rolled == SpecialKind.Bomb && pairShieldWithBomb && !Blocked(distance + shieldLead))
            {
                AddSlot(distance, SpawnSlotKind.PowerUp, SpecialKind.Shield);
                AddSlot(distance + shieldLead, SpawnSlotKind.Hazard, SpecialKind.Bomb);
                return shieldLead;
            }

            AddSlot(distance, SpawnSlotKind.Hazard, rolled);
            return 0f;
        }

        private void AddSlot(float distance, SpawnSlotKind slot, SpecialKind special)
        {
            Add(new ScheduledItem
            {
                Distance = distance,
                Kind = slot,
                Lateral = ChooseLateral(distance),
                ForceColour = false,
                Special = special
            });
        }

        private SpecialKind RollPowerUp()
        {
            return WeightedPick(
                new[] { SpecialKind.ScoreGem, SpecialKind.ReachBoost,
                        SpecialKind.ArcBoost, SpecialKind.CrashCrystals },
                new[] { weightScoreGem, weightReachBoost, weightArcBoost, weightCrashCrystals },
                SpecialKind.ScoreGem);
        }

        private SpecialKind RollHazard()
        {
            return WeightedPick(
                new[] { SpecialKind.Bomb, SpecialKind.SlowHourglass, SpecialKind.TimeDrainClock },
                new[] { weightBomb, weightSlowHourglass, weightTimeDrain },
                SpecialKind.Bomb);
        }

        /// Draws from the seeded sequence like everything else here, so the schedule stays a pure
        /// function of the seed and the distance
        private SpecialKind WeightedPick(SpecialKind[] kinds, float[] weights, SpecialKind fallback)
        {
            float total = 0f;
            for (int i = 0; i < weights.Length; i++) total += Mathf.Max(0f, weights[i]);
            if (total <= 0f) return fallback;

            float roll = NextFloat() * total;
            for (int i = 0; i < kinds.Length; i++)
            {
                roll -= Mathf.Max(0f, weights[i]);
                if (roll <= 0f) return kinds[i];
            }
            return kinds[kinds.Length - 1];
        }

        private void PlacePattern(SpawnPattern pattern, float anchor)
        {
            for (int i = 0; i < pattern.slots.Length; i++)
            {
                var slot = pattern.slots[i];
                float at = anchor + slot.alongTrack;
                if (Blocked(at)) continue;

                // Authored laterals are taken as written. The whole point of a set piece is that its
                // shape survives contact with the generator
                Add(new ScheduledItem
                {
                    Distance = at,
                    Kind = slot.kind,
                    Lateral = Mathf.Clamp(slot.lateral, -1f, 1f),
                    ForceColour = slot.forceColour,
                    Colour = slot.colour,

                    // Patterns author the CATEGORY, not the item, so the weights still choose
                    Special = slot.kind == SpawnSlotKind.Hazard ? RollHazard() : RollPowerUp()
                });
            }

            LogSetPiece(pattern, anchor);
        }

        /// Compiled out of a non development build, call site and all
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        private void LogSetPiece(SpawnPattern pattern, float anchor)
        {
            if (!logSchedule) return;

            Debug.Log("[SpawnDirector] Set piece '" + pattern.label + "' at " +
                      anchor.ToString("0") + " m (" + pattern.slots.Length + " slots)");
        }

        private void Add(ScheduledItem item)
        {
            _items.Add(item);
            _lastLateral = item.Lateral;
            _lastDistance = item.Distance;
        }

        /// Keeps consecutive items reachable from one another. Anything closer than sameSideWithin
        /// stays on the side the last one was on
        private float ChooseLateral(float distance)
        {
            float wanted = NextFloat() * 2f - 1f;

            bool crowded = distance - _lastDistance < sameSideWithin;
            if (crowded && Mathf.Sign(wanted) != Mathf.Sign(_lastLateral) && _lastLateral != 0f)
                wanted = -wanted;

            return wanted;
        }

        private SpawnPattern ChoosePattern(float difficulty)
        {
            // Reservoir pick over the legal patterns, so no allocation and no bias toward the front
            SpawnPattern chosen = null;
            int seen = 0;

            for (int i = 0; i < patterns.Length; i++)
            {
                var p = patterns[i];
                if (p == null || !p.AllowedAt(difficulty)) continue;

                seen++;
                if (NextFloat() < 1f / seen) chosen = p;
            }

            return chosen;
        }

        /// True if this point on the track belongs to an obstacle section
        public bool Blocked(float distance)
        {
            return SectionBlocked(distance, distance);
        }

        private bool SectionBlocked(float from, float to)
        {
            const float Window = 80f;

            for (int i = 0; i < _obstacleAt.Count; i++)
            {
                if (_obstacleAt[i] < from - Window) continue;
                if (_obstacleAt[i] > to + Window) break;

                float half = SectionHalfLength(_obstacleKinds[i]) + obstacleClearance;
                if (_obstacleAt[i] + half >= from && _obstacleAt[i] - half <= to) return true;
            }
            return false;
        }

        private float SectionHalfLength(TrackObstacle.Kind kind)
        {
            if (obstacles == null) return 11.5f;

            var prefab = obstacles.PrefabFor(kind);
            return prefab != null ? prefab.SectionHalfLength : 11.5f;
        }

        /// Replays the obstacle course's own seeded walk, so the exclusion zones are derived from
        /// the same sequence the obstacles are placed by rather than from a second guess at it
        private void RefreshObstacles(float toDistance)
        {
            if (obstacles == null) return;
            if (_obstacleAt.Count > 0 && _obstacleAt[_obstacleAt.Count - 1] >= toDistance) return;

            obstacles.PreviewSequence(toDistance, _obstacleAt, _obstacleKinds);
        }

        private float NextFloat()
        {
            return (float)_rng.NextDouble();
        }
    }
}
