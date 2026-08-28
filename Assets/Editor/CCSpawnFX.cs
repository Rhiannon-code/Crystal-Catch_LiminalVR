using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace IntuitiveDesigns.CrystalCatch.EditorTools
{
    /// Wires spawn portals, fall trails and the shatter. Idempotent - safe to re-run after the
    /// crystal or special prefabs are swapped out, which is exactly what happened on 2026-08-25
    /// when the hand built Crystal_* prefabs were replaced by the pack's "Crystal effect *" ones.
    ///
    /// The glob is deliberately "Crystal*" and not "Crystal_*". The first version of this tool
    /// missed the new prefabs entirely because of that underscore, so they kept Unity's DEFAULT
    /// TrailRenderer - 0.6 m wide, 5 seconds long, and emitting.
    public static class CCSpawnFX
    {
        private const string PortalDir = "Assets/Magic effects pack/Prefabs/Portals/";
        private const string TrailMaterialPath = "Assets/Materials/SpawnTrail.mat";
        private const string ShardMaterialPath = "Assets/Materials/CrystalShard.mat";
        private const string ShardMeshPath = "Assets/Magic effects pack/Models/Crystal1.fbx";
        private const string ScenePath = "Assets/Scenes/MineCart.unity";

        // Trail width as a fraction of the item's own radius. Measured per prefab rather than
        // hardcoded, same principle as CCCartBuilder measuring the kit instead of assuming sizes
        private const float TrailWidthPerRadius = 0.35f;

        public static void RunHeadless()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Run();
        }

        [MenuItem("Crystal Catch/FX/Wire Spawn Portals, Trails And Shatter")]
        public static void Run()
        {
            var trailMat = LoadOrCreate(TrailMaterialPath, "Mobile/Particles/Additive",
                                        "Assets/Magic effects pack/Textures/Circle.png");
            var shardMat = LoadOrCreate(ShardMaterialPath, "Unlit/Color", null);

            int trails = 0;
            foreach (var path in ItemPrefabPaths())
                if (WireItemPrefab(path, trailMat)) trails++;

            bool pool = WirePortalPool();
            bool shatter = WireShatter(shardMat);
            bool colours = FixCrystalPrefabArray();
            bool bat = TuneBat();

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();

            Debug.Log("[CCSpawnFX] trails=" + trails + "  portals=" + pool + "  shatter=" + shatter +
                      "  colourArray=" + colours + "  bat=" + bat);
        }

        private static IEnumerable<string> ItemPrefabPaths()
        {
            foreach (var p in Directory.GetFiles("Assets/Prefabs", "Crystal*.prefab")) yield return p;
            foreach (var p in Directory.GetFiles("Assets/Prefabs", "Special_*.prefab")) yield return p;
        }

        private static Material LoadOrCreate(string path, string shaderName, string texturePath)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find(shaderName);
            if (existing != null)
            {
                if (shader != null && existing.shader != shader) existing.shader = shader;
                return existing;
            }

            var mat = new Material(shader != null ? shader : Shader.Find("Unlit/Color"));
            if (texturePath != null)
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                if (tex != null) mat.SetTexture("_MainTex", tex);
            }
            Directory.CreateDirectory("Assets/Materials");
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        private static bool WireItemPrefab(string path, Material trailMat)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            if (root == null) return false;

            try
            {
                var mover = root.GetComponent<FallingMover>();
                if (mover == null) return false;

                // The "Crystal effect" prefabs have no MeshRenderer at all, so the collider is the
                // only honest measure of how big the thing reads as
                float radius = 0.2f;
                var sphere = root.GetComponent<SphereCollider>();
                if (sphere != null)
                    radius = sphere.radius * Mathf.Abs(root.transform.localScale.x);

                var trail = root.GetComponent<TrailRenderer>();
                if (trail == null) trail = root.AddComponent<TrailRenderer>();

                trail.time = 0.35f;
                trail.minVertexDistance = 0.02f;
                trail.alignment = LineAlignment.View;
                trail.textureMode = LineTextureMode.Stretch;
                trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                trail.receiveShadows = false;
                trail.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                trail.sharedMaterial = trailMat;
                trail.autodestruct = false;

                // FallingMover.Launch owns this. Left ticked, a recycled item draws a line across
                // the whole cave from wherever it last despawned
                trail.emitting = false;

                var width = new AnimationCurve();
                width.AddKey(0f, 1f);
                width.AddKey(1f, 0f);
                trail.widthCurve = width;
                trail.widthMultiplier = Mathf.Max(0.02f, radius * TrailWidthPerRadius);

                var gradient = new Gradient();
                gradient.SetKeys(
                    new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                    new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
                trail.colorGradient = gradient;

                var hidden = new List<Renderer>();
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                    if (!(r is TrailRenderer)) hidden.Add(r);

                var so = new SerializedObject(mover);
                so.FindProperty("trail").objectReferenceValue = trail;
                var list = so.FindProperty("hiddenWhileHeld");
                list.arraySize = hidden.Count;
                for (int i = 0; i < hidden.Count; i++)
                    list.GetArrayElementAtIndex(i).objectReferenceValue = hidden[i];
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, path);
                return true;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        /// CrystalColour has FOUR values, so a five entry array leaves index 4 unreachable while
        /// still pooling poolPerColour copies of it, and pushes Gold onto whatever sits at index 3
        private static bool FixCrystalPrefabArray()
        {
            var spawner = Object.FindObjectOfType<CrystalSpawner>();
            if (spawner == null) return false;

            var wanted = new[] { "blue", "green", "purple", "yellow" };   // Blue, Green, Purple, Gold
            var so = new SerializedObject(spawner);
            var arr = so.FindProperty("crystalPrefabs");

            arr.arraySize = 4;
            for (int i = 0; i < 4; i++)
            {
                var p = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Prefabs/Crystal effect " + wanted[i] + ".prefab");
                if (p == null) return false;
                arr.GetArrayElementAtIndex(i).objectReferenceValue = p.GetComponent<Crystal>();
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }

        private static bool TuneBat()
        {
            var bat = Object.FindObjectOfType<BatSwinger>();
            if (bat == null) return false;

            var so = new SerializedObject(bat);
            // Reach out of the cart. The hit capsule follows this through ApplyShape()
            so.FindProperty("baseLength").floatValue = 0.9f;
            // Holster only - once held, FollowHand overwrites position from the real hand
            so.FindProperty("fallbackLocalPosition").vector3Value = new Vector3(0.35f, 1.1f, 0.5f);
            so.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }

        private static bool WireShatter(Material shardMat)
        {
            Mesh mesh = null;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(ShardMeshPath))
            {
                mesh = o as Mesh;
                if (mesh != null) break;
            }
            if (mesh == null) return false;

            var go = GameObject.Find("SpawnFX");
            if (go == null) go = new GameObject("SpawnFX");

            var shatter = go.GetComponent<CrystalShatter>();
            if (shatter == null) shatter = go.AddComponent<CrystalShatter>();

            var so = new SerializedObject(shatter);
            so.FindProperty("shardMesh").objectReferenceValue = mesh;
            so.FindProperty("shardMaterial").objectReferenceValue = shardMat;

            var tints = so.FindProperty("tints");
            tints.arraySize = 4;
            tints.GetArrayElementAtIndex(0).colorValue = new Color(0.35f, 0.65f, 1f);
            tints.GetArrayElementAtIndex(1).colorValue = new Color(0.35f, 0.9f, 0.45f);
            tints.GetArrayElementAtIndex(2).colorValue = new Color(0.7f, 0.4f, 0.95f);
            tints.GetArrayElementAtIndex(3).colorValue = new Color(1f, 0.82f, 0.25f);
            so.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }

        private static bool WirePortalPool()
        {
            var blue = Portal("Portal blue");
            var green = Portal("Portal green");
            var red = Portal("Portal red");
            var yellow = Portal("Portal yellow");
            if (blue == null || green == null || red == null || yellow == null) return false;

            var go = GameObject.Find("SpawnFX");
            if (go == null) go = new GameObject("SpawnFX");

            var pool = go.GetComponent<SpawnPortalPool>();
            if (pool == null) pool = go.AddComponent<SpawnPortalPool>();

            var so = new SerializedObject(pool);
            var arr = so.FindProperty("crystalPortals");
            arr.arraySize = 4;
            arr.GetArrayElementAtIndex(0).objectReferenceValue = blue;
            arr.GetArrayElementAtIndex(1).objectReferenceValue = green;
            arr.GetArrayElementAtIndex(2).objectReferenceValue = blue;   // Purple, red is reserved
            arr.GetArrayElementAtIndex(3).objectReferenceValue = yellow; // Gold
            so.FindProperty("powerUpPortal").objectReferenceValue = green;
            so.FindProperty("hazardPortal").objectReferenceValue = red;
            so.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }

        private static GameObject Portal(string name)
        {
            return AssetDatabase.LoadAssetAtPath<GameObject>(PortalDir + name + ".prefab");
        }
    }
}
