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
        [SerializeField] private TrackDirector director;

        [Header("Session (data)")]
        [SerializeField] private float roundSeconds = 60f;
        [SerializeField] private int maxRounds = 5;
        [SerializeField] private int countdownFrom = 3;
        [SerializeField] private float countdownStep = 1f;
        [SerializeField] private float roundTallySeconds = 3f;
        [SerializeField] private float endHoldSeconds = 3f;
        [SerializeField] private float endFadeSeconds = 2f;

        [Header("Start")]
        [SerializeField] private bool requirePickupToStart = true;

        [Header("Clearing (data)")]
        [SerializeField] private float clearBonusPerSecond = 20f;
        [SerializeField] private bool endRoundOnClear = true;

        public State Current { get; private set; } = State.WaitingForPickup;
        public float TimeRemaining { get; private set; }
        public int RoundScore { get; private set; }
        public int TotalScore { get; private set; }
        public int RoundNumber { get; private set; }
        public int MaxRounds { get { return maxRounds; } }
        public float RoundSeconds { get { return roundSeconds; } }
        public bool IsFinalRound { get { return maxRounds > 0 && RoundNumber >= maxRounds; } }
        public int NextRoundNumber { get { return RoundNumber + 1; } }

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
        public event Action<Vector3> PlayerHit;

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

        public void Scored(int points, Vector3 where, bool chained)
        {
            if (Current != State.Playing) return;

            if (combo != null) combo.Register(chained);
            float multiplier = combo != null ? combo.Multiplier : 1f;

            AddScore(Mathf.RoundToInt(points * multiplier));

            if (Scoring != null) Scoring(where, chained);
        }

        /// A vampire or a bat got to the player. It costs the combo and nothing else
        public void HitPlayer(Vector3 from)
        {
            if (Current != State.Playing) return;

            if (combo != null) combo.Reset(true);
            if (PlayerHit != null) PlayerHit(from);
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
            yield return StartCoroutine(Countdown());

            for (RoundNumber = 1; RoundNumber <= maxRounds; RoundNumber++)
            {
                yield return StartCoroutine(RunRound());

                if (RoundNumber >= maxRounds) break;

                SetState(State.RoundEnd);
                yield return new WaitForSecondsRealtime(roundTallySeconds);
                yield return StartCoroutine(Countdown());
            }

            yield return StartCoroutine(EndExperience());
        }

        private IEnumerator Countdown()
        {
            for (int n = countdownFrom; n >= 1; n--)
            {
                if (CountdownTick != null) CountdownTick(n);
                yield return new WaitForSecondsRealtime(countdownStep);
            }

            if (CountdownGo != null) CountdownGo();
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

                if (RoundGoalMet())
                {
                    cleared = true;

                    // A wave also runs out when targets escape, and that is not worth a bonus
                    if (director == null || director.Killed >= director.WaveSize)
                        AddScore(Mathf.RoundToInt(Mathf.Max(0f, TimeRemaining) * clearBonusPerSecond));
                    break;
                }

                yield return null;
            }

            TimeRemaining = Mathf.Max(0f, TimeRemaining);

            if (cleared && RoundCleared != null) RoundCleared(RoundNumber);
            if (RoundEnded != null) RoundEnded(RoundNumber, RoundScore);
        }

        /// A range with traffic ends on its wave, a practice shelf on its plates
        private bool RoundGoalMet()
        {
            if (director != null) return director.WaveCleared;
            return endRoundOnClear && arena != null && arena.IsCleared;
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
