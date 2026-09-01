using UnityEditor;
using UnityEngine;

namespace IntuitiveDesigns.CrystalCatch.EditorTools
{
    [CustomEditor(typeof(TrackPath))]
    public class TrackPathEditor : Editor
    {
        // Every 25th point at 2 m spacing is a handle every 50 m. Dense enough to shape a corner,
        // sparse enough that the scene view is not a wall of dots
        private const int DefaultStride = 25;

        // A dragged handle pulls its neighbours with it on a smoothstep falloff. Moving a single
        // sample would put a spike in the track, which is a kink the cart would slam through
        private const int DefaultFalloff = 20;

        private static bool _editPoints;
        private static int _stride = DefaultStride;
        private static int _falloff = DefaultFalloff;
        private static float _handleRange = 200f;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var track = (TrackPath)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Track", EditorStyles.boldLabel);

            if (!track.IsGenerated)
            {
                EditorGUILayout.HelpBox(
                    "No track baked yet. Generate one here and it is serialized into the scene, so " +
                    "Play rides exactly what you see and preview and hand edits both survive.",
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.LabelField(
                    string.Format("{0:0} m over {1} points", track.Length, track.PointCount));
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Generate"))
                {
                    Undo.RecordObject(track, "Generate Track");
                    track.Generate();
                    MarkDirty(track);
                }

                if (GUILayout.Button("Clear"))
                {
                    Undo.RecordObject(track, "Clear Track");
                    track.Clear();
                    MarkDirty(track);
                }
            }

            if (track.IsGenerated) DrawComfortReport(track);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Manual editing", EditorStyles.boldLabel);

            _editPoints = EditorGUILayout.ToggleLeft("Show scene handles", _editPoints);
            if (_editPoints)
            {
                _stride = Mathf.Max(1, EditorGUILayout.IntField("Handle every N points", _stride));
                _falloff = Mathf.Max(1, EditorGUILayout.IntField("Falloff (points)", _falloff));
                _handleRange = EditorGUILayout.FloatField("Draw within (m)", _handleRange);

                EditorGUILayout.HelpBox(
                    "Dragging a handle pulls its neighbours along on a smooth falloff, so an edit " +
                    "bends the track instead of putting a kink in it. Re-check the comfort report " +
                    "afterwards, hand edits are not clamped to the limits.",
                    MessageType.None);
            }
        }

        private void DrawComfortReport(TrackPath track)
        {
            float worstYaw = 0f;
            float worstGradient = 0f;
            int over = 0;
            int longestTurnPoints = 0;
            int runPoints = 0;

            for (int i = 1; i < track.PointCount - 1; i++)
            {
                float yaw = track.YawRateAt(i);
                float gradient = Mathf.Abs(track.GradientAt(i));

                if (yaw > worstYaw) worstYaw = yaw;
                if (gradient > worstGradient) worstGradient = gradient;
                if (track.ComfortLoadAt(i) > 1.001f) over++;

                // "Turning at all" is the thing that accumulates, so measure the run, not the peak
                if (yaw > track.MaxYawDegreesPerSecond * 0.25f)
                {
                    runPoints++;
                    if (runPoints > longestTurnPoints) longestTurnPoints = runPoints;
                }
                else
                {
                    runPoints = 0;
                }
            }

            float longestTurnMetres = longestTurnPoints * track.PointSpacing;
            float longestTurnSeconds = track.TopSpeed > 0.01f ? longestTurnMetres / track.TopSpeed : 0f;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Comfort report (at top speed)", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(string.Format("Worst yaw:  {0:0.0} deg/s   (limit {1:0.0})",
                                       worstYaw, track.MaxYawDegreesPerSecond));
            EditorGUILayout.LabelField(string.Format("Worst grade: {0:0.000}   ({1:0.0} deg, limit {2:0.000})",
                                       worstGradient, Mathf.Atan(worstGradient) * Mathf.Rad2Deg, track.MaxGradient));
            EditorGUILayout.LabelField(string.Format("Longest continuous turn: {0:0} m  ({1:0.0} s)",
                                       longestTurnMetres, longestTurnSeconds));

            if (over > 0)
            {
                EditorGUILayout.HelpBox(
                    over + " segment(s) exceed the comfort limits. They are drawn RED in the scene. " +
                    "Generated track cannot do this, so these are hand edits, either pull them back " +
                    "or regenerate.",
                    MessageType.Warning);
            }
            else if (longestTurnSeconds > 12f)
            {
                EditorGUILayout.HelpBox(
                    "Every segment is within limits, but there is a turn lasting " +
                    longestTurnSeconds.ToString("0") + " s. Sustained rotation builds vection even " +
                    "at a legal rate, shorten maxTurnRun if testers report drift.",
                    MessageType.Warning);
            }
            else
            {
                EditorGUILayout.HelpBox("Within the comfort limits end to end.", MessageType.Info);
            }
        }

        private void OnSceneGUI()
        {
            if (!_editPoints) return;

            var track = (TrackPath)target;
            if (!track.IsGenerated) return;

            Transform t = track.transform;
            Camera cam = SceneView.lastActiveSceneView != null
                ? SceneView.lastActiveSceneView.camera
                : null;

            float rangeSqr = _handleRange * _handleRange;

            for (int i = 0; i < track.PointCount; i += _stride)
            {
                Vector3 world = t.position + track.GetLocalPoint(i);

                // The track is kilometres long. Only the stretch you are actually looking at gets
                // handles, or the scene view crawls
                if (cam != null && (cam.transform.position - world).sqrMagnitude > rangeSqr) continue;

                float size = HandleUtility.GetHandleSize(world) * 0.12f;

                EditorGUI.BeginChangeCheck();
                Vector3 moved = Handles.FreeMoveHandle(world, Quaternion.identity, size,
                                                       Vector3.zero, Handles.DotHandleCap);

                if (!EditorGUI.EndChangeCheck()) continue;

                Undo.RecordObject(track, "Move Track Point");
                ApplyWithFalloff(track, i, moved - world);
                MarkDirty(track);
            }
        }

        /// Spreads a handle's movement over its neighbours with a smoothstep weight, so the edit is
        /// a bend rather than a spike
        private static void ApplyWithFalloff(TrackPath track, int index, Vector3 delta)
        {
            for (int offset = -_falloff; offset <= _falloff; offset++)
            {
                int i = index + offset;
                if (i < 0 || i >= track.PointCount) continue;

                float normalised = 1f - Mathf.Abs(offset) / (float)(_falloff + 1);
                float weight = normalised * normalised * (3f - 2f * normalised);

                track.SetLocalPoint(i, track.GetLocalPoint(i) + delta * weight);
            }
        }

        private static void MarkDirty(TrackPath track)
        {
            EditorUtility.SetDirty(track);

            if (!Application.isPlaying)
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(track.gameObject.scene);
        }
    }
}
