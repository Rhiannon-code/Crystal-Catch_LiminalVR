using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace IntuitiveDesigns.CrystalCatch
{
    public class EffectStatusHUD : MonoBehaviour
    {
        [SerializeField] private CrystalCatchGame game;

        [Header("Layout (data, canvas units)")]
        [SerializeField] private float rowWidth = 340f;
        [SerializeField] private float rowHeight = 42f;
        [SerializeField] private float rowSpacing = 6f;
        [SerializeField] private float cornerMargin = 20f;
        [SerializeField] private float fontSize = 24f;
        [SerializeField] private float sidePadding = 12f;

        [Header("Arrival")]
        [SerializeField] private float punchScale = 1.25f;
        [SerializeField] private float punchSeconds = 0.18f;

        [Header("Expiry warning")]
        [SerializeField] private float warnSeconds = 3f;
        [SerializeField, Range(0.1f, 1f)] private float warnMaxFraction = 0.35f;
        [SerializeField] private float warnPulsesPerSecond = 2.5f;
        [SerializeField] private float warnMinAlpha = 0.55f;

        [Header("Notices (one-shot pickups)")]
        [SerializeField] private float noticeSeconds = 2.4f;
        [SerializeField] private Color noticeTint = new Color(0.55f, 0.80f, 0.55f);
        [SerializeField] private Color noticeHazardTint = new Color(0.85f, 0.45f, 0.25f);

        [Header("Colour")]
        [SerializeField] private Color backing = new Color(0.04f, 0.04f, 0.07f, 0.74f);
        [SerializeField] private float fillAlpha = 0.55f;

        private static readonly string[] Names =
        {
            "SHIELD", "SCORE", "LONG PICK", "WIDE SWING", "SWINGS MISS", "SLOW FALL"
        };

        private static readonly Color[] Tints =
        {
            new Color(0.20f, 0.72f, 0.85f),   // Shield, cyan
            new Color(0.90f, 0.70f, 0.15f),   // Score, gold
            new Color(0.30f, 0.78f, 0.35f),   // Reach, green
            new Color(0.35f, 0.62f, 0.90f),   // Arc, blue
            new Color(0.85f, 0.22f, 0.22f),   // Swings miss, red
            new Color(0.70f, 0.35f, 0.85f)    // Slow fall, purple
        };

        private class Row
        {
            public GameObject Root;
            public RectTransform Rect;
            public RectTransform Fill;
            public CanvasGroup Group;
            public TMP_Text Label;
            public TMP_Text Seconds;
            public int LastSecondShown = -1;
            public float LastMagnitude = float.NaN;
            public float AppearedAt;
        }

        private Row[] _rows;
        private RectTransform _helpfulColumn;
        private RectTransform _harmfulColumn;

        // Notices reuse the row visuals but are driven by a wall clock rather than by an effect's
        // remaining time, so they need their own expiry
        private Row _helpfulNotice;
        private Row _harmfulNotice;
        private float _helpfulNoticeUntil;
        private float _harmfulNoticeUntil;

        private void Awake()
        {
            if (game == null)
            {
                game = Object.FindObjectOfType<CrystalCatchGame>();
                if (game == null)
                    Debug.LogWarning("[EffectStatusHUD] No CrystalCatchGame in the scene, the effect readout will stay empty.");
            }

            _helpfulColumn = BuildColumn("Helpful", true);
            _harmfulColumn = BuildColumn("Harmful", false);

            _rows = new Row[Names.Length];
            for (int i = 0; i < _rows.Length; i++)
            {
                var kind = (CrystalCatchGame.EffectKind)i;
                bool hazard = CrystalCatchGame.IsHazardEffect(kind);
                _rows[i] = BuildRow(Names[i], Tints[i], hazard ? _harmfulColumn : _helpfulColumn, hazard);
                _rows[i].Root.SetActive(false);
            }

            _helpfulNotice = BuildRow("Notice", noticeTint, _helpfulColumn, false);
            _harmfulNotice = BuildRow("NoticeHazard", noticeHazardTint, _harmfulColumn, true);
            _helpfulNotice.Root.SetActive(false);
            _harmfulNotice.Root.SetActive(false);
        }

        private void OnEnable()
        {
            if (game != null) game.Notice += OnNotice;
        }

        private void OnDisable()
        {
            if (game != null) game.Notice -= OnNotice;
        }

        /// A one shot pickup with no timed effect behind it. Shown in the same columns as the effect
        /// rows so there is a single place to look, but with the label carrying the whole message
        private void OnNotice(string message, bool hazard)
        {
            var row = hazard ? _harmfulNotice : _helpfulNotice;
            if (row == null) return;

            row.Label.text = message;
            row.Seconds.text = string.Empty;
            row.Fill.sizeDelta = new Vector2(rowWidth, 0f);   // Full bar, there is nothing to count down
            row.Group.alpha = 1f;
            row.Root.SetActive(true);
            row.AppearedAt = Time.time;

            if (hazard) _harmfulNoticeUntil = Time.time + noticeSeconds;
            else _helpfulNoticeUntil = Time.time + noticeSeconds;
        }

        private void Update()
        {
            if (game == null || _rows == null) return;

            // Each column packs from its own top, so the two sides fill independently
            int helpfulSlot = 0;
            int harmfulSlot = 0;

            for (int i = 0; i < _rows.Length; i++)
            {
                var kind = (CrystalCatchGame.EffectKind)i;
                float remaining = game.EffectRemaining(kind);

                if (remaining <= 0f)
                {
                    if (_rows[i].Root.activeSelf) Hide(_rows[i]);
                    continue;
                }

                bool hazard = CrystalCatchGame.IsHazardEffect(kind);
                ShowRow(_rows[i], kind, remaining, hazard ? harmfulSlot++ : helpfulSlot++);
            }

            // Notices sit BELOW whatever effects are live, so an arriving one never shoves a
            // running countdown down the column
            PlaceNotice(_helpfulNotice, _helpfulNoticeUntil, helpfulSlot);
            PlaceNotice(_harmfulNotice, _harmfulNoticeUntil, harmfulSlot);
        }

        private void PlaceNotice(Row row, float until, int slot)
        {
            if (row == null) return;

            if (Time.time >= until)
            {
                if (row.Root.activeSelf) row.Root.SetActive(false);
                return;
            }

            row.Rect.anchoredPosition = new Vector2(0f, -slot * (rowHeight + rowSpacing));

            float age = Time.time - row.AppearedAt;
            float punch = age < punchSeconds ? Mathf.Lerp(punchScale, 1f, age / punchSeconds) : 1f;
            row.Rect.localScale = new Vector3(punch, punch, 1f);

            // Fades over its last third rather than vanishing, so it reads as leaving rather than
            // as a flicker
            float tail = Mathf.Max(0.01f, noticeSeconds * 0.35f);
            row.Group.alpha = Mathf.Clamp01((until - Time.time) / tail);
        }

        private void ShowRow(Row row, CrystalCatchGame.EffectKind kind, float remaining, int slot)
        {
            if (!row.Root.activeSelf)
            {
                row.Root.SetActive(true);
                row.AppearedAt = Time.time;
            }

            // Rows only move when an effect starts or ends, never while one is counting down
            row.Rect.anchoredPosition = new Vector2(0f, -slot * (rowHeight + rowSpacing));

            float age = Time.time - row.AppearedAt;
            float punch = age < punchSeconds ? Mathf.Lerp(punchScale, 1f, age / punchSeconds) : 1f;
            row.Rect.localScale = new Vector3(punch, punch, 1f);

            float duration = game.EffectDuration(kind);
            float fraction = duration > 0.01f ? Mathf.Clamp01(remaining / duration) : 1f;
            row.Fill.sizeDelta = new Vector2(rowWidth * fraction, 0f);
            int seconds = Mathf.CeilToInt(remaining);
            if (seconds != row.LastSecondShown)
            {
                row.LastSecondShown = seconds;
                row.Seconds.text = seconds + "s";
            }

            float magnitude = game.EffectMagnitude(kind);
            if (!Mathf.Approximately(magnitude, row.LastMagnitude))
            {
                row.LastMagnitude = magnitude;
                row.Label.text = LabelFor(kind, magnitude);
            }

            float warnWindow = Mathf.Min(warnSeconds, duration * warnMaxFraction);
            row.Group.alpha = remaining <= warnWindow ? PulseAlpha() : 1f;
        }

        private void Hide(Row row)
        {
            row.Root.SetActive(false);
            row.LastSecondShown = -1;
            row.LastMagnitude = float.NaN;
        }

        private float PulseAlpha()
        {
            float wave = 0.5f + 0.5f * Mathf.Cos(Time.time * warnPulsesPerSecond * 2f * Mathf.PI);
            return Mathf.Lerp(warnMinAlpha, 1f, wave);
        }

        private static string LabelFor(CrystalCatchGame.EffectKind kind, float magnitude)
        {
            string name = Names[(int)kind];
            if (kind == CrystalCatchGame.EffectKind.ScoreBoost && magnitude > 1.001f)
                return name + " x" + magnitude.ToString("0.#");
            return name;
        }

        private RectTransform BuildColumn(string name, bool left)
        {
            var go = NewUIObject(name, transform);
            var rect = (RectTransform)go.transform;

            // Pinned to a CORNER of the canvas, so the readout does not drift as rows come and go
            float x = left ? 0f : 1f;
            rect.anchorMin = new Vector2(x, 1f);
            rect.anchorMax = new Vector2(x, 1f);
            rect.pivot = new Vector2(x, 1f);
            rect.anchoredPosition = new Vector2(left ? cornerMargin : -cornerMargin, -cornerMargin);
            rect.sizeDelta = new Vector2(rowWidth, 0f);
            return rect;
        }

        private Row BuildRow(string label, Color tint, RectTransform column, bool hazard)
        {
            var row = new Row();
            float edge = hazard ? 1f : 0f;   // Hazard rows hang off the right edge of their column

            row.Root = NewUIObject("Effect_" + label, column);
            row.Rect = (RectTransform)row.Root.transform;
            row.Rect.anchorMin = new Vector2(edge, 1f);
            row.Rect.anchorMax = new Vector2(edge, 1f);
            row.Rect.pivot = new Vector2(edge, 1f);
            row.Rect.sizeDelta = new Vector2(rowWidth, rowHeight);

            row.Group = row.Root.AddComponent<CanvasGroup>();
            row.Group.interactable = false;
            row.Group.blocksRaycasts = false;

            // Backing first so the bar and the text draw over it. Child order IS draw order in uGUI
            var backingGo = NewUIObject("Backing", row.Rect);
            Stretch((RectTransform)backingGo.transform);
            AddImage(backingGo, backing);

            var fillGo = NewUIObject("Fill", row.Rect);
            row.Fill = (RectTransform)fillGo.transform;
            row.Fill.anchorMin = new Vector2(edge, 0f);
            row.Fill.anchorMax = new Vector2(edge, 1f);
            row.Fill.pivot = new Vector2(edge, 0.5f);
            row.Fill.anchoredPosition = Vector2.zero;
            row.Fill.sizeDelta = new Vector2(rowWidth, 0f);

            tint.a = fillAlpha;
            AddImage(fillGo, tint);

            // Label on the outer side, countdown on the inner side, mirrored per column
            row.Label = AddText(row.Rect, "Label",
                                hazard ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft);
            row.Seconds = AddText(row.Rect, "Seconds",
                                  hazard ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.MidlineRight);

            return row;
        }

        private static GameObject NewUIObject(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        private static Image AddImage(GameObject go, Color colour)
        {
            var image = go.AddComponent<Image>();
            image.color = colour;

            // Nothing in this readout is a target. The gaze pointer must pass straight through it
            image.raycastTarget = false;
            return image;
        }

        private TMP_Text AddText(RectTransform parent, string name, TextAlignmentOptions alignment)
        {
            var go = NewUIObject(name, parent);
            var rect = (RectTransform)go.transform;
            Stretch(rect);
            rect.offsetMin = new Vector2(sidePadding, 0f);
            rect.offsetMax = new Vector2(-sidePadding, 0f);

            var text = go.AddComponent<TextMeshProUGUI>();
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = Color.white;
            text.enableWordWrapping = false;
            text.raycastTarget = false;
            return text;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
