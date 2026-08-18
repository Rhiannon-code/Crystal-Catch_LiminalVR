using System;
using System.Collections;
using UnityEngine;
using Liminal.SDK.Core;
using Liminal.Core.Fader;

namespace IntuitiveDesigns.CrystalCatch
{
    public class CrystalCatchGame : MonoBehaviour
    {
        /// RoundEnd is the tally interlude BETWEEN rounds. The cart keeps rolling through it and the
        /// view is never blacked out, the ride is continuous
        public enum State { Intro, Playing, RoundEnd, Ended }

        [Header("Session (data, not magic numbers)")]
        [SerializeField] private float startSeconds = 60f;   // Length of ONE round
        [SerializeField] private int countdownFrom = 3;      // 3-2-1-GO onboarding, clock starts after it
        [SerializeField] private float countdownStep = 1f;
        [SerializeField] private float endHoldSeconds = 3f;  // Hold the final score flourish before fading
        [SerializeField] private float endFadeSeconds = 2f;

        [Header("Endless rounds")]
        [SerializeField] private CartController cart;
        [SerializeField] private float roundTallySeconds = 4f;
        [SerializeField] private float speedIncreasePerRound = 0.15f;

        // Ceiling on the per round scale. CartController multiplies its base speed by this, and the
        // product must stay under TrackPath.topSpeed or the generated curves exceed the 30 deg/sec
        // yaw comfort limit. 4 m/s base x 2.0 = the 8 m/s the track was laid out for
        [SerializeField] private float maxSpeedScale = 2.0f;

        [Header("Scoring")]
        [SerializeField] private float baseMultiplier = 1f;

        public State Current { get; private set; } = State.Intro;
        public float TimeRemaining { get; private set; }

        /// Score for the CURRENT round only. Resets each round
        public int Score { get; private set; }

        /// Running total across all COMPLETED rounds. Score is added to this at each round end
        public int TotalScore { get; private set; }

        /// 1-based. Increments as each new round starts
        public int RoundNumber { get; private set; }

        public float Multiplier { get; private set; }

        /// Full session length in seconds (60 s). Used by the spawner's pacing curves
        public float StartSeconds => startSeconds;

        /// 0 at session start to 1 at the final second. Drives the richer late game value ramp
        public float ElapsedNormalized =>
            startSeconds <= 0f ? 0f : 1f - Mathf.Clamp01(TimeRemaining / startSeconds);

        // Flags other systems query. Kept here so all rules live in one place
        public bool IsShielded { get; private set; }
        public bool CollectionEnabled { get; private set; } = true;
        public bool HandsImpaired { get; private set; }   // Legacy catch era flag, kept for HandCollector

        /// Bat length multiplier. The Reach power up drives this above 1
        public float ReachMultiplier { get; private set; } = 1f;

        /// Bat radius multiplier, a wider swing arc. The Arc power up drives this above 1
        public float ArcMultiplier { get; private set; } = 1f;

        /// Multiplier on how fast items fall. The Slow Time hazard drives this BELOW 1, which leaves
        /// items still up near the ceiling as the cart passes under them, physically unreachable
        public float FallSpeedScale { get; private set; } = 1f;

        /// Every effect that runs on a clock. Order is display order in the HUD
        public enum EffectKind
        {
            Shield,      // Hazard immunity
            ScoreBoost,  // Score multiplier
            Reach,       // Longer pickaxe
            Arc,         // Wider swing assist
            SwingsMiss,  // Bomb, collection disabled
            SlowFall     // Slow Time, items hang out of reach overhead
        }

        /// Hazards read as "something is being done to you", power ups as "something is helping".
        /// The HUD splits the two into opposite corners on this, so the rule lives here with the
        /// other rules rather than being re-guessed in the UI
        public static bool IsHazardEffect(EffectKind kind)
        {
            return kind == EffectKind.SwingsMiss || kind == EffectKind.SlowFall;
        }

        public event Action<int> ScoreChanged;
        public event Action<float> TimeChanged;
        public event Action<float> MultiplierChanged;
        public event Action<State> StateChanged;

        // Session flow signals for the HUD
        public event Action<int> CountdownTick;   // 3, 2, 1
        public event Action CountdownGo;          // GO
        public event Action<int> FinalScore;      // Only fired by an explicit EndExperience()

        /// (roundScore, newTotal) fired when a round's clock runs out, before the tally interlude
        public event Action<int, int> RoundEnded;

        /// (roundNumber) fired as each new round's clock starts
        public event Action<int> RoundStarted;

        // Set by the spawner so hazards/power ups can affect spawning through the manager
        public CrystalSpawner Spawner;

        // One timer table instead of five coroutine handles. Coroutines could hold the CURRENT value
        // of an effect but never how long was left on it, which is exactly what the HUD has to show
        private const int EffectSlots = 6;
        private readonly float[] _effectRemaining = new float[EffectSlots];
        private readonly float[] _effectDuration = new float[EffectSlots];
        private readonly float[] _effectMagnitude = new float[EffectSlots];

        /// Seconds left on an effect, 0 when it is not running
        public float EffectRemaining(EffectKind kind) { return _effectRemaining[(int)kind]; }

        /// Full length the effect was last granted for. The HUD divides by this for its bar
        public float EffectDuration(EffectKind kind) { return _effectDuration[(int)kind]; }

        /// The effect's strength where it has one (x2 score, x1.6 reach), 0 where it does not
        public float EffectMagnitude(EffectKind kind) { return _effectMagnitude[(int)kind]; }

        public bool IsEffectActive(EffectKind kind) { return _effectRemaining[(int)kind] > 0f; }

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

            StartRound();
        }

        /// Begins a round. Round 1 follows the countdown, every later round follows the tally
        /// interlude, with the cart still rolling and the track never interrupted
        private void StartRound()
        {
            RoundNumber++;
            Score = 0;
            TimeRemaining = startSeconds;
            Multiplier = baseMultiplier;

            // That reset is round-local scoring, but an effect the player earned seconds before the
            // round boundary is still running on its own clock. Re-assert those, or the HUD counts
            // down a score boost that the reset above has quietly already cancelled
            ReapplyActiveEffects();

            // Speed up for the new round. SetSpeedScale is safe to step, CartController's
            // acceleration clamp ramps it, so this never reads as a jolt
            if (cart != null)
            {
                float scale = Mathf.Min(1f + (RoundNumber - 1) * speedIncreasePerRound, maxSpeedScale);
                cart.SetSpeedScale(scale);
            }

            SetState(State.Playing);
            ScoreChanged?.Invoke(Score);
            TimeChanged?.Invoke(TimeRemaining);
            MultiplierChanged?.Invoke(Multiplier);
            RoundStarted?.Invoke(RoundNumber);

            if (Spawner != null) Spawner.StartSpawning();
        }

        /// A round's clock ran out. Bank the score, hold for a tally, then roll straight into the
        /// next round. Deliberately NO fade and NO ExperienceApp.End(), the cart keeps moving and
        /// the track stays visible throughout
        private IEnumerator EndRound()
        {
            SetState(State.RoundEnd);
            if (Spawner != null) Spawner.StopSpawning();

            TotalScore += Score;
            RoundEnded?.Invoke(Score, TotalScore);

            yield return new WaitForSeconds(roundTallySeconds);

            StartRound();
        }

        private void Update()
        {
            // Ticked in EVERY state, not just Playing. A shield picked up in the last second of a
            // round has to keep running through the tally interlude, the way the coroutines did
            TickEffects(Time.deltaTime);

            if (Current != State.Playing) return;

            TimeRemaining -= Time.deltaTime;
            TimeChanged?.Invoke(TimeRemaining);

            if (TimeRemaining <= 0f)
            {
                TimeRemaining = 0f;
                StartCoroutine(EndRound());
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
            BeginEffect(EffectKind.Shield, seconds, 0f);
        }

        public void SetScoreMultiplier(float multiplier, float seconds)
        {
            BeginEffect(EffectKind.ScoreBoost, seconds, multiplier);
        }

        public void SpawnBurst(int count)
        {
            if (Spawner != null) Spawner.EmitBurst(count);
        }

        // Bat power ups
        // Timed like the score multiplier: set, wait, restore. Each keeps its own coroutine handle
        // so re-collecting the same power up refreshes the duration instead of stacking timers that
        // race to restore 1.0 while the other is still meant to be running

        public void SetReachBoost(float multiplier, float seconds)
        {
            BeginEffect(EffectKind.Reach, seconds, multiplier);
        }

        public void SetArcBoost(float multiplier, float seconds)
        {
            BeginEffect(EffectKind.Arc, seconds, multiplier);
        }

        // Called by hazards (all gated by the shield)
        public void ApplyHazardTime(float seconds)   { if (!IsShielded) AddTime(seconds); }        // Pass negative
        public void DisableCollection(float seconds) { if (!IsShielded) BeginEffect(EffectKind.SwingsMiss, seconds, 0f); }
        public void ImpairHands(float seconds)       { if (!IsShielded) StartCoroutine(TimedFlag(v => HandsImpaired = v, seconds)); } // Legacy

        /// Slow Time. scale below 1 slows the fall, leaving items out of reach overhead
        public void SlowFalling(float scale, float seconds)
        {
            if (IsShielded) return;
            BeginEffect(EffectKind.SlowFall, seconds, scale);
        }

        /// Starts or REFRESHES a timed effect. Re-collecting the same pickup never stacks a second
        /// timer, and the longer of the two windows wins so a fresh one cannot be cut short by an
        /// older one expiring underneath it
        private void BeginEffect(EffectKind kind, float seconds, float magnitude)
        {
            if (seconds <= 0f) return;

            int i = (int)kind;
            _effectRemaining[i] = Mathf.Max(_effectRemaining[i], seconds);
            _effectDuration[i] = _effectRemaining[i];
            _effectMagnitude[i] = magnitude;
            ApplyEffect(kind, true);
        }

        private void ReapplyActiveEffects()
        {
            for (int i = 0; i < EffectSlots; i++)
                if (_effectRemaining[i] > 0f) ApplyEffect((EffectKind)i, true);
        }

        private void TickEffects(float deltaTime)
        {
            for (int i = 0; i < EffectSlots; i++)
            {
                if (_effectRemaining[i] <= 0f) continue;

                _effectRemaining[i] -= deltaTime;
                if (_effectRemaining[i] > 0f) continue;

                // Clear the magnitude BEFORE restoring, ApplyEffect reads it for the on branch only
                _effectRemaining[i] = 0f;
                _effectDuration[i] = 0f;
                _effectMagnitude[i] = 0f;
                ApplyEffect((EffectKind)i, false);
            }
        }

        /// The single place an effect is allowed to touch the values other systems read
        private void ApplyEffect(EffectKind kind, bool active)
        {
            float magnitude = _effectMagnitude[(int)kind];

            switch (kind)
            {
                case EffectKind.Shield:
                    IsShielded = active;
                    break;

                case EffectKind.ScoreBoost:
                    Multiplier = active ? magnitude : baseMultiplier;
                    MultiplierChanged?.Invoke(Multiplier);
                    break;

                case EffectKind.Reach:
                    ReachMultiplier = active ? magnitude : 1f;
                    break;

                case EffectKind.Arc:
                    ArcMultiplier = active ? magnitude : 1f;
                    break;

                case EffectKind.SwingsMiss:
                    CollectionEnabled = !active;
                    break;

                case EffectKind.SlowFall:
                    FallSpeedScale = active ? magnitude : 1f;
                    break;
            }
        }

        private IEnumerator TimedFlag(Action<bool> set, float seconds, bool invert = false)
        {
            set(!invert ? true : false);
            yield return new WaitForSeconds(seconds);
            set(!invert ? false : true);
        }

        /// Ends the whole experience and hands control back to the Liminal shell
        public void EndExperience()
        {
            if (Current == State.Ended) return;
            StartCoroutine(EndSessionRoutine());
        }

        private IEnumerator EndSessionRoutine()
        {
            if (Current == State.Ended) yield break;
            SetState(State.Ended);
            if (Spawner != null) Spawner.StopSpawning();

            // Final flourish uses the cumulative total, not just the last round
            FinalScore?.Invoke(TotalScore + Score);
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
