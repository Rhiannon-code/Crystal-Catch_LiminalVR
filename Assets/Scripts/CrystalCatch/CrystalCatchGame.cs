using System;
using System.Collections;
using UnityEngine;
using Liminal.SDK.Core;
using Liminal.Core.Fader;

namespace IntuitiveDesigns.CrystalCatch
{
    public class CrystalCatchGame : MonoBehaviour
    {
        public enum State { Intro, Playing, Ended }

        [Header("Session (data, not magic numbers)")]
        [SerializeField] private float startSeconds = 60f;   // 60 second round sessions
        [SerializeField] private int countdownFrom = 3;      // 3-2-1-GO onboarding, clock starts after it
        [SerializeField] private float countdownStep = 1f;
        [SerializeField] private float endHoldSeconds = 3f;  // Hold the final score flourish before fading
        [SerializeField] private float endFadeSeconds = 2f;

        [Header("Scoring")]
        [SerializeField] private float baseMultiplier = 1f;

        public State Current { get; private set; } = State.Intro;
        public float TimeRemaining { get; private set; }
        public int Score { get; private set; }
        public float Multiplier { get; private set; }

        /// Full session length in seconds (60 s). Used by the spawner's pacing curves
        public float StartSeconds => startSeconds;

        /// 0 at session start to 1 at the final second. Drives the richer late game value ramp
        public float ElapsedNormalized =>
            startSeconds <= 0f ? 0f : 1f - Mathf.Clamp01(TimeRemaining / startSeconds);

        // Flags other systems query. Kept here so all rules live in one place
        public bool IsShielded { get; private set; }
        public bool CollectionEnabled { get; private set; } = true;
        public bool HandsImpaired { get; private set; }   // Slow Time hazard, catching gets imprecise

        public event Action<int> ScoreChanged;
        public event Action<float> TimeChanged;
        public event Action<float> MultiplierChanged;
        public event Action<State> StateChanged;

        // Session flow signals for the HUD
        public event Action<int> CountdownTick;   // 3, 2, 1
        public event Action CountdownGo;          // GO
        public event Action<int> FinalScore;      // Final score at end

        // Set by the spawner so hazards/power ups can affect spawning through the manager
        public CrystalSpawner Spawner;

        private Coroutine _multiplierRoutine;
        private Coroutine _shieldRoutine;

        private void Start()
        {
            TimeRemaining = startSeconds;
            Multiplier = baseMultiplier;
            StartCoroutine(IntroCountdown());
        }

        private IEnumerator IntroCountdown()
        {
            SetState(State.Intro);

            // One demo crystal to catch during the count (scores nothing, state isn't Playing yet)
            if (Spawner != null) Spawner.EmitBurst(1);

            for (int n = countdownFrom; n >= 1; n--)
            {
                CountdownTick?.Invoke(n);
                yield return new WaitForSeconds(countdownStep);
            }
            CountdownGo?.Invoke();

            // Clock starts now, the full 60s is scored time
            SetState(State.Playing);
            if (Spawner != null) Spawner.StartSpawning();
        }

        private void Update()
        {
            if (Current != State.Playing) return;

            TimeRemaining -= Time.deltaTime;
            TimeChanged?.Invoke(TimeRemaining);

            if (TimeRemaining <= 0f)
            {
                TimeRemaining = 0f;
                StartCoroutine(EndSession());
            }
        }

        // Called by crystals
        public void AddScore(int crystalValue)
        {
            if (Current != State.Playing || !CollectionEnabled) return;
            Score += Mathf.RoundToInt(crystalValue * Multiplier);
            ScoreChanged?.Invoke(Score);
        }

        // Called by power ups
        public void AddTime(float seconds)
        {
            if (Current != State.Playing) return;
            TimeRemaining += seconds;
            TimeChanged?.Invoke(TimeRemaining);
        }

        public void SetShield(float seconds)
        {
            if (_shieldRoutine != null) StopCoroutine(_shieldRoutine);
            _shieldRoutine = StartCoroutine(TimedFlag(v => IsShielded = v, seconds));
        }

        public void SetScoreMultiplier(float multiplier, float seconds)
        {
            if (_multiplierRoutine != null) StopCoroutine(_multiplierRoutine);
            _multiplierRoutine = StartCoroutine(TimedMultiplier(multiplier, seconds));
        }

        public void SpawnBurst(int count)
        {
            if (Spawner != null) Spawner.EmitBurst(count);
        }

        // Called by hazards (all gated by the shield)
        public void ApplyHazardTime(float seconds)   { if (!IsShielded) AddTime(seconds); }        // Pass negative
        public void DisableCollection(float seconds) { if (!IsShielded) StartCoroutine(TimedFlag(v => CollectionEnabled = v, seconds, invert: true)); }
        public void ImpairHands(float seconds)       { if (!IsShielded) StartCoroutine(TimedFlag(v => HandsImpaired = v, seconds)); } // Slow Time

        private IEnumerator TimedFlag(Action<bool> set, float seconds, bool invert = false)
        {
            set(!invert ? true : false);
            yield return new WaitForSeconds(seconds);
            set(!invert ? false : true);
        }

        private IEnumerator TimedMultiplier(float m, float seconds)
        {
            Multiplier = m;
            MultiplierChanged?.Invoke(Multiplier);
            yield return new WaitForSeconds(seconds);
            Multiplier = baseMultiplier;
            MultiplierChanged?.Invoke(Multiplier);
        }

        private IEnumerator EndSession()
        {
            if (Current == State.Ended) yield break;
            SetState(State.Ended);
            if (Spawner != null) Spawner.StopSpawning();

            // Simple final score flourish: the HUD shows the big number, we hold on it, then fade out
            FinalScore?.Invoke(Score);
            yield return new WaitForSeconds(endHoldSeconds);

            if (ScreenFader.Instance != null)
                ScreenFader.Instance.FadeTo(Color.black, endFadeSeconds);
            yield return new WaitForSeconds(endFadeSeconds);

            if (!ExperienceApp.IsEnding)
                ExperienceApp.End();
        }

        private void SetState(State s)
        {
            Current = s;
            StateChanged?.Invoke(s);
        }
    }
}
