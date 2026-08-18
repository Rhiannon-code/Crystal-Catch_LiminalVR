using UnityEngine;

namespace IntuitiveDesigns.CrystalCatch
{
    /// Desktop test driver for the effect strip, in the same spirit as MouseTestBat
    public class KeyboardTestEffects : MonoBehaviour
    {
        [SerializeField] private CrystalCatchGame game;
        [SerializeField] private bool editorOnly = true;
        [SerializeField] private bool logFires = true;

        [Header("Durations (mirror the Special_ prefabs)")]
        [SerializeField] private float shieldSeconds = 30f;
        [SerializeField] private float scoreMultiplier = 2f;
        [SerializeField] private float scoreSeconds = 10f;
        [SerializeField] private float reachMultiplier = 1.6f;
        [SerializeField] private float reachSeconds = 10f;
        [SerializeField] private float arcMultiplier = 2f;
        [SerializeField] private float arcSeconds = 10f;
        [SerializeField] private float bombSeconds = 3f;
        [SerializeField] private float slowFallScale = 0.45f;
        [SerializeField] private float slowFallSeconds = 4f;

        private bool Active { get { return !editorOnly || Application.isEditor; } }

        private void Awake()
        {
            if (game == null) game = Object.FindObjectOfType<CrystalCatchGame>();
        }

        private void Update()
        {
            if (!Active || game == null) return;

            if (Input.GetKeyDown(KeyCode.Alpha1)) Fire("SHIELD", () => game.SetShield(shieldSeconds));
            if (Input.GetKeyDown(KeyCode.Alpha2)) Fire("SCORE", () => game.SetScoreMultiplier(scoreMultiplier, scoreSeconds));
            if (Input.GetKeyDown(KeyCode.Alpha3)) Fire("LONG PICK", () => game.SetReachBoost(reachMultiplier, reachSeconds));
            if (Input.GetKeyDown(KeyCode.Alpha4)) Fire("WIDE SWING", () => game.SetArcBoost(arcMultiplier, arcSeconds));
            if (Input.GetKeyDown(KeyCode.Alpha5)) Fire("SWINGS MISS", () => game.DisableCollection(bombSeconds));
            if (Input.GetKeyDown(KeyCode.Alpha6)) Fire("SLOW FALL", () => game.SlowFalling(slowFallScale, slowFallSeconds));
        }

        private void Fire(string label, System.Action apply)
        {
            apply();

            if (!logFires) return;

            // The two hazards no op while a shield is up, which is correct but looks identical to a
            // broken key press, so say so rather than leaving it a mystery
            Debug.Log("[KeyboardTestEffects] " + label +
                      (game.IsShielded ? "  (shield is up, hazards are being blocked)" : ""));
        }
    }
}
