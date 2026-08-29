using System;
using System.Collections;
using UnityEngine;
using Liminal.SDK.Core;
using Liminal.Core.Fader;

namespace IntuitiveDesigns.CrystalCatch
{
    public class CrystalCatchGame : MonoBehaviour
    {
        public enum State { WaitingForPickup, Intro, Playing, RoundEnd, Ended }

        [Header("Session (data, not magic numbers)")]
        [SerializeField] private float startSeconds = 60f;   // Length of ONE round
        [SerializeField] private int countdownFrom = 3;      // 3-2-1-GO onboarding, clock starts after it
        [SerializeField] private float countdownStep = 1f;
        [SerializeField] private float endHoldSeconds = 3f;  // Hold the final score flourish before fading
        [SerializeField] private float endFadeSeconds = 2f;

        [Header("Rounds")]
        [SerializeField] private CartController cart;
        [SerializeField] private float roundTallySeconds = 4f;
        [SerializeField] private float speedIncreasePerRound = 0.15f;
        [SerializeField] private float maxSpeedScale = 2.0f;
        [SerializeField] private int maxRounds = 5;

        [Header("Start")]
        [SerializeField] private bool requirePickupToStart = true;

        [Header("Scoring")]
        [SerializeField] private float baseMultiplier = 1f;

        public State Current { get; private set; } = State.WaitingForPickup;
        public float TimeRemaining { get; private set; }
        public int Score { get; private set; }
        public int TotalScore { get; private set; }
        public int RoundNumber { get; private set; }
        public int MaxRounds { get { return maxRounds; } }

        /// True while the round in progress is the last one. The HUD reads this to say so
        public bool IsFinalRound { get { return maxRounds > 0 && RoundNumber >= maxRounds; } }
        public float Multiplier { get; private set; }
        public float StartSeconds => startSeconds;
        public float ElapsedNormalized =>
            startSeconds <= 0f ? 0f : 1f - Mathf.Clamp01(TimeRemaining / startSeconds);

        public bool IsShielded { get; private set; }
        public bool CollectionEnabled { get; private set; } = true;
        public bool HandsImpaired { get; private set; }   // Legacy catch era flag, kept for HandCollector
        public float ReachMultiplier { get; private set; } = 1f;
        public float ArcMultiplier { get; private set; } = 1f;
        public float FallSpeedScale { get; private set; } = 1f;
        public float TimeDebt { get; private set; }

        public enum EffectKind
        {
            Shield,      // Hazard immunity
            ScoreBoost,  // Score multiplier
            Reach,       // Longer pickaxe
            Arc,         // Wider swing assist
            SwingsMiss,  // Bomb, collection disabled
            SlowFall     // Slow Time, items hang out of reach overhead
        }
        public static bool IsHazardEffect(EffectKind kind)
        {
            return kind == EffectKind.SwingsMiss || kind == EffectKind.SlowFall;
        }

        public event Action<int> ScoreChanged;
        public event Action<float> TimeChanged;
        public event Action<float> MultiplierChanged;
        public event Action<State> StateChanged;
        public event Action<int> CountdownTick;   // 3, 2, 1
        public event Action CountdownGo;          // GO
        public event Action<int> FinalScore;      // Only fired by an explicit EndExperience()
        public event Action<int, int> RoundEnded;
        public event Action<string, bool> Notice;
        public event Action<int> RoundStarted;

        public CrystalSpawner Spawner;

        private const int EffectSlots = 6;
        private readonly float[] _effectRemaining = new float[EffectSlots];
        private readonly float[] _effectDuration = new float[EffectSlots];
        private readonly float[] _effectMagnitude = new float[EffectSlots];

        public float EffectRemaining(EffectKind kind) { return _effectRemaining[(int)kind]; }
        public float EffectDuration(EffectKind kind) { return _effectDuration[(int)kind]; }
        public float EffectMagnitude(EffectKind kind) { return _effectMagnitude[(int)kind]; }
        public bool IsEffectActive(EffectKind kind) { return _effectRemaining[(int)kind] > 0f; }

        private bool _begun;

        private void Start()
        {
            TimeRemaining = startSeconds;
            Multiplier = baseMultiplier;

            if (requirePickupToStart && FindObjectOfType<PickaxePickup>() == null)
            {
                Debug.LogWarning("[CrystalCatchGame] requirePickupToStart is on but there is no active " +
                                 "PickaxePickup in the scene. Starting immediately instead.");
                requirePickupToStart = false;
            }

            if (requirePickupToStart) SetState(State.WaitingForPickup);
            else BeginFromPickup();
        }

        public void BeginFromPickup()
        {
            if (_begun) return;
            _begun = true;
            StartCoroutine(IntroCountdown());
        }

        private IEnumerator IntroCountdown()
        {
            SetState(State.Intro);

            for (int n = countdownFrom; n >= 1; n--)
            {
                CountdownTick?.Invoke(n);
                yield return new WaitForSeconds(countdownStep);
            }
            CountdownGo?.Invoke();

            StartRound();
        }

        private void StartRound()
        {
            RoundNumber++;
            Score = 0;
            TimeRemaining = startSeconds;
            Multiplier = baseMultiplier;

            // The clock is reset, so a debt carried from the previous round would be repaying time
            // the player is no longer short of
            TimeDebt = 0f;

            ReapplyActiveEffects();

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

            if (Spawner != null) Spawner.StartSpawning(RoundNumber == 1);
        }

        private IEnumerator EndRound()
        {
            SetState(State.RoundEnd);
            if (Spawner != null) Spawner.StopSpawning();

            int roundScore = Score;
            TotalScore += roundScore;
            Score = 0;

            RoundEnded?.Invoke(roundScore, TotalScore);

            yield return new WaitForSeconds(roundTallySeconds);

            // The cap is tested AFTER the tally so the last round still gets its score flourish
            // before the session ends, rather than being cut off by the fade
            if (maxRounds > 0 && RoundNumber >= maxRounds)
            {
                EndExperience();
                yield break;
            }

            StartRound();
        }

        private void Update()
        {
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

        /// Announce a one shot event. Public because the CALLER is the only thing that knows what it
        /// was, see SpecialItem, which names the pickup that caused it
        public void RaiseNotice(string message, bool hazard)
        {
            var handler = Notice;
            if (handler != null) handler(message, hazard);
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

        public void SetReachBoost(float multiplier, float seconds)
        {
            BeginEffect(EffectKind.Reach, seconds, multiplier);
        }

        public void SetArcBoost(float multiplier, float seconds)
        {
            BeginEffect(EffectKind.Arc, seconds, multiplier);
        }

        // Called by hazards (all gated by the shield). A blocked hazard announces itself as a GOOD
        // event, the shield did its job, and the player needs to see that it did
        public void ApplyHazardTime(float seconds)   // Pass negative
        {
            if (IsShielded) return;
            if (seconds < 0f) TimeDebt += -seconds;
            AddTime(seconds);
        }

        public void CommitTimeRepayment(float seconds)
        {
            TimeDebt = Mathf.Max(0f, TimeDebt - Mathf.Abs(seconds));
        }

        public void DisableCollection(float seconds)
        {
            if (IsShielded) return;
            BeginEffect(EffectKind.SwingsMiss, seconds, 0f);
        }
        public void ImpairHands(float seconds)       { if (!IsShielded) StartCoroutine(TimedFlag(v => HandsImpaired = v, seconds)); } // Legacy

        /// Slow Time. scale below 1 slows the fall, leaving items out of reach overhead
        public void SlowFalling(float scale, float seconds)
        {
            if (IsShielded) return;
            BeginEffect(EffectKind.SlowFall, seconds, scale);
        }
        
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
