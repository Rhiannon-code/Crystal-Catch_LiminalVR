using UnityEngine;
using TMPro;

namespace IntuitiveDesigns.ShootingRange
{
    public class RangeHUD : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private RangeGame game;
        [SerializeField] private ComboTracker combo;
        [SerializeField] private TrackDirector director;

        [Header("Panel pieces")]
        [SerializeField] private CanvasGroup playPanel;
        [SerializeField] private TMP_Text roundText;
        [SerializeField] private TMP_Text timerText;
        [SerializeField] private TMP_Text targetsText;
        [SerializeField] private TMP_Text comboText;

        private int _lastSecondShown = -1;

        private void OnEnable()
        {
            if (game != null)
            {
                game.StateChanged += OnStateChanged;
                game.TimeChanged += OnTimeChanged;
                game.RoundStarted += OnRoundStarted;
            }

            if (combo != null) combo.Changed += OnComboChanged;
            if (director != null) director.TargetsLeftChanged += OnTargetsLeftChanged;
        }

        private void OnDisable()
        {
            if (game != null)
            {
                game.StateChanged -= OnStateChanged;
                game.TimeChanged -= OnTimeChanged;
                game.RoundStarted -= OnRoundStarted;
            }

            if (combo != null) combo.Changed -= OnComboChanged;
            if (director != null) director.TargetsLeftChanged -= OnTargetsLeftChanged;
        }

        private void Start()
        {
            ShowPlayPanel(false);
            OnComboChanged(1f, 0);
            if (game != null) OnStateChanged(game.Current);
        }

        private void OnStateChanged(RangeGame.State state)
        {
            ShowPlayPanel(state == RangeGame.State.Playing || state == RangeGame.State.RoundEnd);
        }

        private void OnTimeChanged(float remaining)
        {
            if (timerText == null) return;

            int seconds = Mathf.CeilToInt(remaining);
            if (seconds == _lastSecondShown) return;

            _lastSecondShown = seconds;
            timerText.text = seconds / 60 + ":" + (seconds % 60).ToString("00");
        }

        private void OnRoundStarted(int round)
        {
            if (roundText != null && game != null) roundText.text = "ROUND " + round + " / " + game.MaxRounds;

            _lastSecondShown = -1;
        }

        private void OnTargetsLeftChanged(int left)
        {
            if (targetsText != null) targetsText.text = left.ToString();
        }

        private void OnComboChanged(float multiplier, int chain)
        {
            if (comboText != null) comboText.text = chain > 1 ? "COMBO " + chain : string.Empty;
        }

        private void ShowPlayPanel(bool visible)
        {
            if (playPanel == null) return;

            playPanel.alpha = visible ? 1f : 0f;
            playPanel.blocksRaycasts = visible;
        }
    }
}
