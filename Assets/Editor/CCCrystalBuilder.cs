using UnityEditor;
using UnityEngine;

namespace IntuitiveDesigns.CrystalCatch.EditorTools
{
    public static class CCCrystalBuilder
    {
        private const string SourcePrefab =
            "Assets/Magic effects pack/Prefabs/Environment/Crystal effect blue.prefab";

        private const string PrefabDir = "Assets/Prefabs";

        /// Hit radius. Generous compared to the old touch to collect crystals, a swung bat needs a
        /// bigger target than a fingertip did
        private const float HitRadius = 0.16f;

        /// The pack effects were authored as ground scenery and are large for a falling collectible
        private const float CrystalScale = 0.45f;

        private struct Spec
        {
            public CrystalColour Colour;
            public Color Tint;
            public int Value;

            public Spec(CrystalColour c, Color t, int v) { Colour = c; Tint = t; Value = v; }
        }

        private static readonly Spec[] Specs =
        {
            new Spec(CrystalColour.Blue,   new Color(0.10f, 0.45f, 1.00f), 1),
            new Spec(CrystalColour.Green,  new Color(0.10f, 1.00f, 0.25f), 3),
            new Spec(CrystalColour.Purple, new Color(0.65f, 0.20f, 1.00f), 5),
            new Spec(CrystalColour.Gold,   new Color(1.00f, 0.78f, 0.10f), 10),
        };

        [MenuItem("Crystal Catch/Build Crystal Prefabs From Effects Pack")]
        public static void Build()
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePrefab);
            if (source == null)
            {
                Debug.LogError("[CCCrystalBuilder] Source not found: " + SourcePrefab);
                return;
            }

            var built = new GameObject[Specs.Length];

            for (int i = 0; i < Specs.Length; i++)
            {
                var spec = Specs[i];
                string path = PrefabDir + "/Crystal_" + spec.Colour + ".prefab";

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
                PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely,
                                                   InteractionMode.AutomatedAction);

                instance.name = "Crystal_" + spec.Colour;
                instance.transform.localScale = Vector3.one * CrystalScale;

                Recolour(instance, spec.Tint);
                AddGameplay(instance, spec);

                var saved = PrefabUtility.SaveAsPrefabAsset(instance, path);
                Object.DestroyImmediate(instance);

                built[i] = saved;
                Debug.Log("[CCCrystalBuilder] Built " + path);
            }

            RepointSpawner(built);

            Debug.Log("[CCCrystalBuilder] Done. " + built.Length + " crystals rebuilt from the effects pack.\n" +
                      "  Scale " + CrystalScale + ", hit radius " + HitRadius + " m, both likely need tuning " +
                      "once you see them at speed.\n" +
                      "  PERF: each crystal is now 4 particle systems instead of 1 flat cube. At the " +
                      "concurrency cap that is ~24 systems of transparent overdraw on a mobile GPU, " +
                      "this is the most likely thing to cost you the 72 fps claim, so profile on device.");
        }

        /// Shifts every particle system's start colour to the target hue while keeping that layer's
        /// own value and alpha, so the dark base/bright core layering survives the recolour
        private static void Recolour(GameObject root, Color tint)
        {
            float th, ts, tv;
            Color.RGBToHSV(tint, out th, out ts, out tv);

            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                var start = main.startColor;

                start.colorMin = ShiftHue(start.colorMin, th, ts);
                start.colorMax = ShiftHue(start.colorMax, th, ts);
                main.startColor = start;
            }
        }

        private static Color ShiftHue(Color original, float hue, float saturation)
        {
            float h, s, v;
            Color.RGBToHSV(original, out h, out s, out v);

            // Near greyscale layers (the white cores and dark bases) are left alone, tinting them
            // would lose the highlight that reads as "crystal"
            if (s < 0.08f) return original;

            var shifted = Color.HSVToRGB(hue, Mathf.Max(s, saturation * 0.75f), v);
            shifted.a = original.a;
            return shifted;
        }

        private static void AddGameplay(GameObject root, Spec spec)
        {
            // The pack prefab is scenery and has no collider, without one the bat cannot hit it
            var col = root.GetComponent<SphereCollider>();
            if (col == null) col = root.AddComponent<SphereCollider>();
            col.radius = HitRadius / Mathf.Max(0.0001f, CrystalScale);   // Undo the root scale
            col.isTrigger = false;

            if (root.GetComponent<FallingMover>() == null) root.AddComponent<FallingMover>();

            var crystal = root.GetComponent<Crystal>();
            if (crystal == null) crystal = root.AddComponent<Crystal>();

            var so = new SerializedObject(crystal);
            var colourProp = so.FindProperty("colour");
            if (colourProp != null) colourProp.enumValueIndex = (int)spec.Colour;
            var valueProp = so.FindProperty("value");
            if (valueProp != null) valueProp.intValue = spec.Value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// Points the spawner at the rebuilt prefabs. Order is load bearing, the pools are indexed
        /// by the CrystalColour enum
        private static void RepointSpawner(GameObject[] built)
        {
            var spawner = Object.FindObjectOfType<CrystalSpawner>();
            if (spawner == null)
            {
                Debug.LogWarning("[CCCrystalBuilder] No CrystalSpawner in the open scene, open " +
                                 "MineCart.unity and run this again to repoint crystalPrefabs.");
                return;
            }

            var so = new SerializedObject(spawner);
            var arr = so.FindProperty("crystalPrefabs");
            if (arr == null)
            {
                Debug.LogWarning("[CCCrystalBuilder] crystalPrefabs not found on the spawner.");
                return;
            }

            arr.arraySize = built.Length;
            for (int i = 0; i < built.Length; i++)
            {
                var c = built[i] != null ? built[i].GetComponent<Crystal>() : null;
                arr.GetArrayElementAtIndex(i).objectReferenceValue = c;
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(spawner);
            Debug.Log("[CCCrystalBuilder] Spawner crystalPrefabs repointed in enum order " +
                      "(Blue, Green, Purple, Gold).");
        }
    }
}
