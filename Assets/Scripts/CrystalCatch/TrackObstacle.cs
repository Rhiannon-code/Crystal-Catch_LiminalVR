using UnityEngine;

namespace IntuitiveDesigns.CrystalCatch
{
    public class TrackObstacle : MonoBehaviour
    {
        public enum Kind
        {
            DuckBeam,   // A low beam across the track, get your head below it
            LeanLeft,   // Rock hanging into the right of the corridor, lean left
            LeanRight   // ... and the mirror of it
        }

        [SerializeField] private Kind kind = Kind.DuckBeam;

        [Header("Danger volume (local metres)")]
        // On while tuning the dodge: the volume is the actual rule, and eyeballing whether a beam
        // "looks duckable" against a mesh is not the same as seeing the box you have to stay out of
        [SerializeField] private bool alwaysDrawVolume = true;
        [SerializeField] private Vector3 dangerCentre = new Vector3(0f, 1.7f, 0f);
        [SerializeField] private Vector3 dangerHalfExtents = new Vector3(2f, 0.5f, 0.35f);

        public Kind ObstacleKind { get { return kind; } }

        // Read by the scene view preview, which has to draw the volume without an instance existing
        public Vector3 DangerCentre { get { return dangerCentre; } }
        public Vector3 DangerHalfExtents { get { return dangerHalfExtents; } }

        /// Which way the player has to move to clear it. Used for the approach telegraph
        public Vector3 ClearDirection
        {
            get
            {
                if (kind == Kind.LeanLeft) return Vector3.left;
                if (kind == Kind.LeanRight) return Vector3.right;
                return Vector3.down;
            }
        }

        public bool ContainsHead(Vector3 worldHead)
        {
            Vector3 local = transform.InverseTransformPoint(worldHead) - dangerCentre;

            return Mathf.Abs(local.x) <= dangerHalfExtents.x
                && Mathf.Abs(local.y) <= dangerHalfExtents.y
                && Mathf.Abs(local.z) <= dangerHalfExtents.z;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            DrawVolume();
        }

        private void OnDrawGizmos()
        {
            if (alwaysDrawVolume) DrawVolume();
        }

        private void DrawVolume()
        {
            Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.35f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawCube(dangerCentre, dangerHalfExtents * 2f);
        }
#endif
    }
}
