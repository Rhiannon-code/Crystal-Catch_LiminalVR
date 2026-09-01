using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace IntuitiveDesigns.CrystalCatch.EditorTools
{
    public static class CCPerfHud
    {
        private const string ObjectName = "PerfReadout";

        // Quest 2 runs its display at 72 Hz on this SDK's legacy Oculus path, so 72 is the budget
        // that matters (13.9 ms). Aiming at 90 would paint everything red for no reason
        private const float QuestRefreshHz = 72f;

        [MenuItem("Crystal Catch/Quest/Add Perf Readout To HUD")]
        public static void Add()
        {
            var hud = Object.FindObjectOfType<HeadLockedHud>();
            if (hud == null)
            {
                Debug.LogError("[CCPerfHud] No HeadLockedHud in the open scene. Open " +
                               "Assets/Scenes/MineCart.unity first.");
                return;
            }

            var existing = hud.transform.Find(ObjectName);
            var go = existing != null
                ? existing.gameObject
                : new GameObject(ObjectName, typeof(RectTransform));

            if (existing == null)
            {
                Undo.RegisterCreatedObjectUndo(go, "Add Perf Readout");
                go.transform.SetParent(hud.transform, false);
            }

            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = new Vector2(0f, 0f);
            rect.anchoredPosition = new Vector2(24f, 24f);
            rect.sizeDelta = new Vector2(420f, 120f);
            rect.localScale = Vector3.one;

            var label = go.GetComponent<TextMeshProUGUI>();
            if (label == null) label = go.AddComponent<TextMeshProUGUI>();
            label.fontSize = 28f;
            label.alignment = TextAlignmentOptions.BottomLeft;
            label.enableWordWrapping = false;
            label.raycastTarget = false;   // The gaze pointer must pass straight through it
            label.text = "-- fps";

            var readout = go.GetComponent<PerfReadout>();
            if (readout == null) readout = go.AddComponent<PerfReadout>();

            // The interesting fields are private [SerializeField], so they are set through the
            // serialised object rather than by widening their access for a tool's benefit
            var so = new SerializedObject(readout);
            so.FindProperty("text").objectReferenceValue = label;
            so.FindProperty("targetFps").floatValue = QuestRefreshHz;
            so.FindProperty("warnBelowFps").floatValue = QuestRefreshHz - 6f;
            so.FindProperty("showWorstFrame").boolValue = true;
            so.FindProperty("showOverBudgetCount").boolValue = true;
            so.FindProperty("logEachSample").boolValue = true;   // For adb logcat -s Unity
            so.ApplyModifiedProperties();

            EditorSceneManager.MarkSceneDirty(hud.gameObject.scene);
            Selection.activeGameObject = go;

            Debug.Log("[CCPerfHud] Perf readout " + (existing != null ? "updated" : "added") +
                      " under '" + hud.name + "'. Budget " + QuestRefreshHz + " fps (" +
                      (1000f / QuestRefreshHz).ToString("0.0") + " ms). Save the scene, then build.");
        }

        [MenuItem("Crystal Catch/Quest/Remove Perf Readout From HUD")]
        public static void Remove()
        {
            var hud = Object.FindObjectOfType<HeadLockedHud>();
            if (hud == null) return;

            var existing = hud.transform.Find(ObjectName);
            if (existing == null)
            {
                Debug.Log("[CCPerfHud] No perf readout in the scene, nothing to remove.");
                return;
            }

            Undo.DestroyObjectImmediate(existing.gameObject);
            EditorSceneManager.MarkSceneDirty(hud.gameObject.scene);
            Debug.Log("[CCPerfHud] Perf readout removed. Safe to build the .limapp.");
        }
    }
}
