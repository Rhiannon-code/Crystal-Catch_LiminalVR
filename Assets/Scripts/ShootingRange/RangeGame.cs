using System;
using System.Collections;
using UnityEngine;
using Liminal.SDK.Core;
using Liminal.Core.Fader;

namespace IntuitiveDesigns.ShootingRange
{
    public class RangeGame : MonoBehaviour
    {
        public static RangeGame Instance { get; private set; }

        public enum State { WaitingForPickup, Intro, Playing, RoundEnd, Ended }

        [Header("Refs")]
        [SerializeField] private Arena arena;
        [SerializeField] private ComboTracker combo;

        [Header("Session (data)")]
        [SerializeField] private float roundSeconds = 60f;
        [SerializeField] private int maxRounds = 5;
        [SerializeField] private int countdownFrom = 3;
        [SerializeField] private float countdownStep = 1f;
        [SerializeField] private float roundTallySeconds = 4f;
        [SerializeField] private float endHoldSeconds = 3f;
        [SerializeField] private float endFadeSeconds = 2f;

        [Header("Start")]
        [SerializeField] private bool requirePickupToStart = true;

        [Header("Clearing (data)")]
        [SerializeField] private float clearBonusPerSecond = 20f;

        // A practice scene wants to keep running after the last plate goes over
        [SerializeField] private bool endRoundOnClear = true;

        public State Current { get; private set; } = State.WaitingForPickup;
        public float TimeRemaining { get; private set; }
        public int RoundScore { get; private set; }
        public int TotalScore { get; private set; }
        public int RoundNumber { get; private set; }
        public int MaxRounds { get { return maxRounds; } }
        public float RoundSeconds { get { return roundSeconds; } }
        public bool IsFinalRound { get { return maxRounds > 0 && RoundNumber >= maxRounds; } }

        public event Action<int> ScoreChanged;
        public event Action<float> TimeChanged;
        public event Action<State> StateChanged;
        public event Action<int> CountdownTick;
        public event Action CountdownGo;
        public event Action<int> RoundStarted;
        public event Action<int, int> RoundEnded;
        public event Action<int> RoundCleared;
        public event Action<int> FinalScore;
        public event Action<Vector3, bool> Scoring;

        private bool _begun;

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            TimeRemaining = roundSeconds;

            if (requirePickupToStart && FindObjectOfType<PistolPickup>() == null)
            {
                Debug.LogWarning("[RangeGame] requirePickupToStart is on but there is no active " +
                                 "PistolPickup in the scene. Starting immediately instead.");
                requirePickupToStart = false;
            }

            if (requirePickupToStart) SetState(State.WaitingForPickup);
            else BeginFromPickup();
        }

        public void BeginFromPickup()
        {
            if (_begun) return;

            _begun = true;
            StartCoroutine(RunSession());
        }

        /// Called by every Knockable that is hit or goes over. Multiplied here rather than at the
        /// prop, so the combo rule lives in one place
        public void Scored(int points, Vector3 where, bool chained)
        {
            if (Current != State.Playing) return;

            if (combo != null) combo.Register(chained);
            float multiplier = combo != null ? combo.Multiplier : 1f;

            AddScore(Mathf.RoundToInt(points * multiplier));

            if (Scoring != null) Scoring(where, chained);
        }

        private void AddScore(int points)
        {
            RoundScore += points;
            TotalScore += points;
            if (ScoreChanged != null) ScoreChanged(TotalScore);
        }

        private IEnumerator RunSession()
        {
            SetState(State.Intro);

            for (int n = countdownFrom; n >= 1; n--)
            {
                if (CountdownTick != null) CountdownTick(n);
                yield return new WaitForSecondsRealtime(countdownStep);
            }

            if (CountdownGo != null) CountdownGo();

            for (RoundNumber = 1; RoundNumber <= maxRounds; RoundNumber++)
            {
                yield return StartCoroutine(RunRound());

                if (RoundNumber >= maxRounds) break;

                SetState(State.RoundEnd);
                yield return new WaitForSecondsRealtime(roundTallySeconds);
            }

            yield return StartCoroutine(EndExperience());
        }

        private IEnumerator RunRound()
        {
            RoundScore = 0;
            TimeRemaining = roundSeconds;

            if (arena != null) arena.PresentRound(RoundNumber);
            if (combo != null) combo.Reset(true);

            SetState(State.Playing);
            if (RoundStarted != null) RoundStarted(RoundNumber);
            if (TimeChanged != null) TimeChanged(TimeRemaining);

            bool cleared = false;

            while (TimeRemaining > 0f)
            {
                TimeRemaining -= Time.unscaledDeltaTime;
                if (TimeChanged != null) TimeChanged(Mathf.Max(0f, TimeRemaining));

                if (endRoundOnClear && arena != null && arena.IsCleared)
                {
                    cleared = true;
                    AddScore(Mathf.RoundToInt(Mathf.Max(0f, TimeRemaining) * clearBonusPerSecond));
                    break;
                }

                yield return null;
            }

            TimeRemaining = Mathf.Max(0f, TimeRemaining);

            if (cleared && RoundCleared != null) RoundCleared(RoundNumber);
            if (RoundEnded != null) RoundEnded(RoundNumber, RoundScore);
        }

        private IEnumerator EndExperience()
        {
            SetState(State.Ended);
            if (SlowMotion.Instance != null) SlowMotion.Instance.Restore();
            if (FinalScore != null) FinalScore(TotalScore);

            yield return new WaitForSecondsRealtime(endHoldSeconds);

            if (ScreenFader.Instance != null) ScreenFader.Instance.FadeTo(Color.black, endFadeSeconds);
            yield return new WaitForSecondsRealtime(endFadeSeconds);

            if (!ExperienceApp.IsEnding) ExperienceApp.End();
        }

        private void SetState(State next)
        {
            if (Current == next) return;

            Current = next;
            if (StateChanged != null) StateChanged(next);
        }
    }
}
