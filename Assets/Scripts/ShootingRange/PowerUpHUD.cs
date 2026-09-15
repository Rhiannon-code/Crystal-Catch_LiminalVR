using System.Text;
using UnityEngine;
using TMPro;

namespace IntuitiveDesigns.ShootingRange
{
    public class PowerUpHUD : MonoBehaviour
    {
        [SerializeField] private PowerUps powerUps;
        [SerializeField] private TMP_Text activeText;
        [SerializeField] private TMP_Text calloutText;
        [SerializeField] private float calloutSeconds = 1.4f;
        [SerializeField] private float refreshInterval = 0.1f;

        private readonly StringBuilder _line = new StringBuilder(128);
        private string[] _hex;
        private float _nextRefresh;
        private float _calloutUntil;

        private void Awake()
        {
            _hex = new string[PowerUps.KindCount];
            for (int i = 0; i < _hex.Length; i++)
                _hex[i] = powerUps != null ? ColorUtility.ToHtmlStringRGB(powerUps.Colour((PowerUpKind)i)) : "FFFFFF";
        }

        private void OnEnable()
        {
            if (powerUps != null) powerUps.Granted += OnGranted;
        }

        private void OnDisable()
        {
            if (powerUps != null) powerUps.Granted -= OnGranted;
        }

        private void Start()
        {
            if (activeText != null) activeText.text = string.Empty;
            if (calloutText != null) calloutText.text = string.Empty;
        }

        private void OnGranted(PowerUpKind kind, float seconds)
        {
            if (calloutText == null) return;

            calloutText.text = "<color=#" + _hex[(int)kind] + ">" + PowerUps.Label(kind) + "!</color>";
            _calloutUntil = Time.unscaledTime + calloutSeconds;
        }

        private void Update()
        {
            float now = Time.unscaledTime;

            if (calloutText != null && _calloutUntil > 0f && now >= _calloutUntil)
            {
                calloutText.text = string.Empty;
                _calloutUntil = 0f;
            }

            if (activeText == null || powerUps == null || now < _nextRefresh) return;
            _nextRefresh = now + refreshInterval;

            _line.Length = 0;
            for (int i = 0; i < PowerUps.KindCount; i++)
            {
                var kind = (PowerUpKind)i;
                if (!powerUps.IsActive(kind)) continue;

                if (_line.Length > 0) _line.Append("   ");
                _line.Append("<color=#").Append(_hex[i]).Append('>')
                     .Append(PowerUps.Label(kind)).Append(' ').Append(Mathf.CeilToInt(powerUps.Remaining(kind)))
                     .Append("</color>");
            }

            activeText.text = _line.ToString();
        }
    }
}
