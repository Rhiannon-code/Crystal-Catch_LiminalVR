using UnityEngine;

namespace IntuitiveDesigns.ShootingRange
{
    public class Arena : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private PropStackBuilder builder;

        [Header("Escalation (data)")]
        [SerializeField] private int stacksInFirstRound = 2;
        [SerializeField] private int extraStacksPerRound = 1;

        [Header("Clear test (data)")]
        [SerializeField] private float checkInterval = 0.2f;

        public bool IsCleared { get; private set; }

        private bool _live;
        private float _nextCheck;
        private int _goalsAtStart;
        private void Start()
        {
            PresentRound(1);
        }

        public void PresentRound(int round)
        {
            IsCleared = false;
            _live = false;

            if (builder != null)
            {
                int stacks = stacksInFirstRound + extraStacksPerRound * Mathf.Max(0, round - 1);
                builder.ActivateStacks(stacks);
            }

            var all = Knockable.All;
            _goalsAtStart = 0;

            for (int i = 0; i < all.Count; i++)
            {
                all[i].ResetToRest();
                if (all[i].CountsTowardRoundGoal) _goalsAtStart++;
            }

            if (_goalsAtStart == 0)
                Debug.LogWarning("[Arena] Round " + round + " has no knockable marked " +
                                 "countsTowardRoundGoal, so it can only end on the clock.");

            // A prop reset on top of a stack needs a step to settle before "still standing" means
            // anything
            _nextCheck = Time.time + checkInterval;
            _live = true;
        }

        private void Update()
        {
            // With no goals at all, "everything is down" is true on frame one and would hand the
            // player a full clear bonus for an empty arena
            if (!_live || IsCleared || _goalsAtStart == 0 || Time.time < _nextCheck) return;

            _nextCheck = Time.time + checkInterval;

            var all = Knockable.All;
            int standing = 0;

            for (int i = 0; i < all.Count; i++)
            {
                var k = all[i];
                if (k.CountsTowardRoundGoal && !k.IsDown) standing++;
            }

            IsCleared = standing == 0;
        }
    }
}
