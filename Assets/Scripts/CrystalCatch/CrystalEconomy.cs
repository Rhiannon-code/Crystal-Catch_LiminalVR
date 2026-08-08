using UnityEngine;

namespace IntuitiveDesigns.CrystalCatch
{
    [CreateAssetMenu(menuName = "Crystal Catch/Economy", fileName = "CrystalEconomy")]
    public class CrystalEconomy : ScriptableObject
    {
        [Header("Score per colour (Middle spread)")]
        public int[] points = { 1, 3, 5, 10 };   // Blue, Green, Purple, Gold

        [Header("Spawn weight over normalised session time (richer late game)")]
        public AnimationCurve[] weightOverTime = new AnimationCurve[4];

        public int Points(CrystalColour c) => points[(int)c];
        public float Weight(CrystalColour c, float tNorm) => weightOverTime[(int)c].Evaluate(tNorm);

        /// Pick a colour by the time varying weights (the pacing arc lives here)
        public CrystalColour WeightedColour(float tNorm)
        {
            float total = 0f;
            for (int i = 0; i < 4; i++) total += Mathf.Max(0f, weightOverTime[i].Evaluate(tNorm));
            float r = Random.value * total;
            for (int i = 0; i < 4; i++)
            {
                float w = Mathf.Max(0f, weightOverTime[i].Evaluate(tNorm));
                if (r < w) return (CrystalColour)i;
                r -= w;
            }
            return CrystalColour.Blue;
        }

        // Sensible defaults so a freshly created asset already plays the chosen arc
        // Blue/green dominate early, purple climbsm gold is rare early and flurries at the end
        private void Reset()
        {
            points = new[] { 1, 3, 5, 10 };
            weightOverTime = new[]
            {
                Curve(6f, 2f),                 // Blue:  High to low
                Curve(4f, 3f),                 // Green: Gently falling
                Curve(1f, 4f),                 // Purple: Rising
                GoldCurve(),                   // Gold: ~0 early to flurry late (ease in)
            };
        }

        private static AnimationCurve Curve(float start, float end)
        {
            var c = new AnimationCurve(new Keyframe(0f, start), new Keyframe(1f, end));
            return c;
        }

        private static AnimationCurve GoldCurve()
        {
            // Stays low most of the session, then ramps up hard for the final gold flurry
            return new AnimationCurve(
                new Keyframe(0f, 0.2f),
                new Keyframe(0.6f, 0.6f),
                new Keyframe(1f, 3.5f));
        }
    }
}
