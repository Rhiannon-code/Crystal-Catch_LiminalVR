using System.Collections;
using UnityEngine;
using TMPro;
using Liminal.SDK.VR.Avatars;

namespace IntuitiveDesigns.CrystalCatch
{
    public class CrystalCatchHUD : MonoBehaviour
    {
        [SerializeField] private CrystalCatchGame game;

        [Header("Panel pieces")]
        [SerializeField] private CanvasGroup playPanel;     // Holds timer + score + multiplier
        [SerializeField] private TMP_Text timerText;
        [SerializeField] private TMP_Text scoreText;
        [SerializeField] private TMP_Text multiplierText;
        [SerializeField] private TMP_Text centerText;       // Countdown/GO/final score

        [Header("Round counter")]
        [SerializeField] private TMP_Text roundText;
        [SerializeField] private RectTransform roundFill;

        [Header("Onboarding")]
        [SerializeField] private string pickupPrompt = "TAKE THE PICKAXE";

        [Header("Billboard (optional)")]
        [SerializeField] private bool facePlayer = true;

        private Transform _head;

        private int _lastSecondShown = -1;

        private void OnEnable()
        {
            game.CountdownTick += OnCountdownTick;
            game.CountdownGo += OnGo;
            game.FinalScore += OnFinalScore;
            game.StateChanged += OnStateChanged;
            game.ScoreChanged += OnScoreChanged;
            game.MultiplierChanged += OnMultiplierChanged;
            game.RoundEnded += OnRoundEnded;
            game.RoundStarted += OnRoundStarted;
        }

        private void OnDisable()
        {
            game.CountdownTick -= OnCountdownTick;
            game.CountdownGo -= OnGo;
            game.FinalScore -= OnFinalScore;
            game.StateChanged -= OnStateChanged;
            game.ScoreChanged -= OnScoreChanged;
            game.MultiplierChanged -= OnMultiplierChanged;
            game.RoundEnded -= OnRoundEnded;
            game.RoundStarted -= OnRoundStarted;
        }

        private void Start()
        {
            ShowPlayPanel(false);
            OnStateChanged(game.Current);
        }

        private void Update()
        {
            if (game.Current == CrystalCatchGame.State.Playing && timerText != null)
            {
                int sec = Mathf.CeilToInt(game.TimeRemaining);
                if (sec != _lastSecondShown)
                {
                    _lastSecondShown = sec;
                    timerText.text = FormatTime(game.TimeRemaining);
                    UpdateRoundReadout();
                }
            }

            if (facePlayer) BillboardToHead();
        }

        private void OnScoreChanged(int score)
        {
            if (scoreText != null) scoreText.text = score.ToString("N0");
        }

        private void OnMultiplierChanged(float multiplier)
        {
            if (multiplierText == null) return;
            bool boosted = multiplier > 1.001f;
            multiplierText.gameObject.SetActive(boosted);
            if (boosted) multiplierText.text = "x" + multiplier.ToString("0.#");
        }

        private void OnStateChanged(CrystalCatchGame.State s)
        {
            ShowPlayPanel(s == CrystalCatchGame.State.Playing);

            if (centerText == null) return;

            if (s == CrystalCatchGame.State.WaitingForPickup) centerText.text = pickupPrompt;

            else if (s == CrystalCatchGame.State.Intro) centerText.text = string.Empty;
        }

        /// Round over. Show what was scored and the running total, then get out of the way
        private void OnRoundEnded(int roundScore, int total)
        {
            if (centerText == null) return;
            centerText.text = "Round " + game.RoundNumber + "\n+" + roundScore.ToString("N0") +
                              "\nTotal " + total.ToString("N0");
        }

        private void UpdateRoundReadout()
        {
            int max = game.MaxRounds;

            if (roundText != null)
            {
                roundText.text = max > 0
                    ? "ROUND " + game.RoundNumber + " / " + max
                    : "ROUND " + game.RoundNumber;
            }

            if (roundFill == null) return;

            // Rounds already banked, plus how far into the current one. ElapsedNormalized is 0 at
            // the start of a round and 1 at its final second
            float progress = max > 0
                ? Mathf.Clamp01(((game.RoundNumber - 1) + game.ElapsedNormalized) / max)
                : game.ElapsedNormalized;

            roundFill.localScale = new Vector3(progress, 1f, 1f);
        }

        private void OnRoundStarted(int roundNumber)
        {
            _lastSecondShown = -1;   // Force the timer string to refresh for the new round
            UpdateRoundReadout();
            if (centerText == null) return;

            // Round 1 is introduced by the 3-2-1 countdown, so it needs no banner of its own
            if (roundNumber > 1) StartCoroutine(FlashCenter("Round " + roundNumber, 1.2f));
            else centerText.text = string.Empty;
        }

        private void OnCountdownTick(int n)
        {
            if (centerText != null) centerText.text = n.ToString();
            // TODO: pulse/scale punch the number for arcade feel
        }

        private void OnGo()
        {
            if (centerText != null) StartCoroutine(FlashCenter("GO", 0.6f));
        }

        private void OnFinalScore(int score)
        {
            ShowPlayPanel(false);
            if (centerText != null) centerText.text = "Final\n" + score.ToString("N0");
            // TODO: celebratory burst + colour flourish behind the number
        }

        private IEnumerator FlashCenter(string text, float seconds)
        {
            centerText.text = text;
            yield return new WaitForSeconds(seconds);
            if (game.Current == CrystalCatchGame.State.Playing) centerText.text = string.Empty;
        }

        private void ShowPlayPanel(bool visible)
        {
            if (playPanel == null) return;
            playPanel.alpha = visible ? 1f : 0f;
            playPanel.gameObject.SetActive(visible);

            // The panel spends the countdown deactivated, so the round readout would otherwise show
            // whatever it held at the end of the PREVIOUS round for the first second of this one
            if (visible) UpdateRoundReadout();
        }

        private void BillboardToHead()
        {
            if (_head == null)
            {
                var avatar = VRAvatar.Active;
                if (avatar == null) return;
                var limb = avatar.GetLimb(VRAvatarLimbType.Head);
                if (limb == null) return;
                _head = limb.Transform;
            }
            Vector3 toHead = transform.position - _head.position;
            toHead.y = 0f;
            if (toHead.sqrMagnitude > 1e-4f)
                transform.rotation = Quaternion.LookRotation(toHead);
        }

        private static string FormatTime(float seconds)
        {
            seconds = Mathf.Max(0f, seconds);
            int m = Mathf.FloorToInt(seconds / 60f);
            int s = Mathf.FloorToInt(seconds % 60f);
            return string.Format("{0}:{1:00}", m, s);
        }
    }
}
