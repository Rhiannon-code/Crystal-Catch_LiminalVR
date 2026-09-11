using System.Collections;
using UnityEngine;
using TMPro;

namespace IntuitiveDesigns.ShootingRange
{
    public class RangeHUD : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private RangeGame game;
        [SerializeField] private ComboTracker combo;
        [SerializeField] private Pistol pistol;
        [SerializeField] private TrackDirector director;

        [Header("Panel pieces")]
        [SerializeField] private CanvasGroup playPanel;
        [SerializeField] private TMP_Text timerText;
        [SerializeField] private TMP_Text scoreText;
        [SerializeField] private TMP_Text roundText;
        [SerializeField] private TMP_Text ammoText;
        [SerializeField] private TMP_Text comboText;
        [SerializeField] private TMP_Text patternText;
        [SerializeField] private TMP_Text centerText;

        [Header("Copy (data)")]
        [SerializeField] private string pickupPrompt = "TAKE THE PISTOL";
        [SerializeField] private string clearedMessage = "RANGE CLEAR";
        [SerializeField] private string reloadingMessage = "RELOADING";
        [SerializeField] private float messageSeconds = 1.6f;
        [SerializeField] private float patternSeconds = 1.4f;

        private int _lastSecondShown = -1;
        private Coroutine _message;
        private Coroutine _pattern;

        private void OnEnable()
        {
            if (game != null)
            {
                game.StateChanged += OnStateChanged;
                game.ScoreChanged += OnScoreChanged;
                game.TimeChanged += OnTimeChanged;
                game.CountdownTick += OnCountdownTick;
                game.CountdownGo += OnGo;
                game.RoundStarted += OnRoundStarted;
                game.RoundEnded += OnRoundEnded;
                game.RoundCleared += OnRoundCleared;
                game.FinalScore += OnFinalScore;
            }

            if (combo != null) combo.Changed += OnComboChanged;
            if (pistol != null) pistol.AmmoChanged += OnAmmoChanged;
            if (director != null) director.PatternStarted += OnPatternStarted;
        }

        private void OnDisable()
        {
            if (game != null)
            {
                game.StateChanged -= OnStateChanged;
                game.ScoreChanged -= OnScoreChanged;
                game.TimeChanged -= OnTimeChanged;
                game.CountdownTick -= OnCountdownTick;
                game.CountdownGo -= OnGo;
                game.RoundStarted -= OnRoundStarted;
                game.RoundEnded -= OnRoundEnded;
                game.RoundCleared -= OnRoundCleared;
                game.FinalScore -= OnFinalScore;
            }

            if (combo != null) combo.Changed -= OnComboChanged;
            if (pistol != null) pistol.AmmoChanged -= OnAmmoChanged;
            if (director != null) director.PatternStarted -= OnPatternStarted;
        }

        private void Start()
        {
            ShowPlayPanel(false);
            OnComboChanged(1f, 0);
            if (game != null) OnStateChanged(game.Current);
        }

        private void Update()
        {
            if (ammoText == null || pistol == null || !pistol.IsHeld) return;

            if (pistol.IsReloading) ammoText.text = reloadingMessage;
        }

        private void OnStateChanged(RangeGame.State state)
        {
            switch (state)
            {
                case RangeGame.State.WaitingForPickup:
                    ShowPlayPanel(false);
                    SetCenter(pickupPrompt);
                    break;
                case RangeGame.State.Intro:
                    ShowPlayPanel(false);
                    break;
                case RangeGame.State.Playing:
                    ShowPlayPanel(true);
                    break;
                case RangeGame.State.RoundEnd:
                    ShowPlayPanel(true);
                    break;
                case RangeGame.State.Ended:
                    ShowPlayPanel(false);
                    break;
            }
        }

        private void OnScoreChanged(int total)
        {
            if (scoreText != null) scoreText.text = total.ToString();
        }

        private void OnTimeChanged(float remaining)
        {
            if (timerText == null) return;

            int seconds = Mathf.CeilToInt(remaining);
            if (seconds == _lastSecondShown) return;

            _lastSecondShown = seconds;
            timerText.text = seconds.ToString();
        }

        private void OnCountdownTick(int n)
        {
            SetCenter(n.ToString());
        }

        private void OnGo()
        {
            Flash("GO");
        }

        private void OnRoundStarted(int round)
        {
            if (roundText != null && game != null)
                roundText.text = "ROUND " + round + " / " + game.MaxRounds;

            _lastSecondShown = -1;
        }

        private void OnRoundEnded(int round, int roundScore)
        {
            Flash("+" + roundScore);
        }

        private void OnRoundCleared(int round)
        {
            Flash(clearedMessage);
        }

        private void OnFinalScore(int total)
        {
            SetCenter(total.ToString());
        }

        private void OnPatternStarted(TrackPatternKind kind)
        {
            if (patternText == null) return;

            // Announcing every ordinary wave turns the callout into wallpaper
            if (kind == TrackPatternKind.Single) return;

            if (_pattern != null) StopCoroutine(_pattern);
            _pattern = StartCoroutine(ShowPattern(Readable(kind)));
        }

        private IEnumerator ShowPattern(string label)
        {
            patternText.text = label;
            yield return new WaitForSecondsRealtime(patternSeconds);
            patternText.text = string.Empty;
            _pattern = null;
        }

        private static string Readable(TrackPatternKind kind)
        {
            switch (kind)
            {
                case TrackPatternKind.RoofRush: return "ROOF RUSH";
                case TrackPatternKind.RiserVolley: return "RISER VOLLEY";
                default: return kind.ToString().ToUpperInvariant();
            }
        }

        private void OnComboChanged(float multiplier, int chain)
        {
            if (comboText == null) return;

            comboText.text = chain > 1 ? "x" + multiplier.ToString("0.0") : string.Empty;
        }

        private void OnAmmoChanged(int rounds, int magazine)
        {
            if (ammoText == null) return;

            ammoText.text = magazine > 0 ? rounds + " / " + magazine : string.Empty;
        }

        private void ShowPlayPanel(bool visible)
        {
            if (playPanel == null) return;

            playPanel.alpha = visible ? 1f : 0f;
            playPanel.blocksRaycasts = visible;
        }

        private void SetCenter(string text)
        {
            if (centerText != null) centerText.text = text;
        }

        private void Flash(string text)
        {
            if (centerText == null) return;

            if (_message != null) StopCoroutine(_message);
            _message = StartCoroutine(FlashRoutine(text));
        }

        private IEnumerator FlashRoutine(string text)
        {
            SetCenter(text);
            yield return new WaitForSecondsRealtime(messageSeconds);
            SetCenter(string.Empty);
            _message = null;
        }
    }
}
