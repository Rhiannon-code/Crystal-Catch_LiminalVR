using UnityEngine;

namespace IntuitiveDesigns.CrystalCatch
{
    public enum SpawnSlotKind
    {
        Crystal,
        PowerUp,
        Hazard
    }

    [CreateAssetMenu(fileName = "SpawnPattern", menuName = "Crystal Catch/Spawn Pattern")]
    public class SpawnPattern : ScriptableObject
    {
        [System.Serializable]
        public struct Slot
        {
            public SpawnSlotKind kind;

            /// Metres past the pattern's anchor point on the track
            public float alongTrack;
            public float lateral;
            public bool forceColour;
            public CrystalColour colour;
        }
        
        public string label = "Set piece";

        [Header("When this may be used")]
        // Difficulty is derived from distance travelled, so a pattern's window is a statement about
        // how deep into the ride it belongs
        [Range(0f, 1f)] public float minDifficulty;
        [Range(0f, 1f)] public float maxDifficulty = 1f;

        [Header("Contents")]
        public Slot[] slots = new Slot[0];

        /// How much track this pattern claims, measured from its anchor. Nothing else is scheduled
        /// inside that stretch, so a set piece is never diluted by a stray baseline crystal
        public float Length
        {
            get
            {
                float max = 0f;
                for (int i = 0; i < slots.Length; i++)
                    if (slots[i].alongTrack > max) max = slots[i].alongTrack;
                return max;
            }
        }

        public bool AllowedAt(float difficulty)
        {
            return slots.Length > 0 && difficulty >= minDifficulty && difficulty <= maxDifficulty;
        }
    }
}
