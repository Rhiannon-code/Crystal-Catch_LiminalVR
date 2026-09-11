using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace IntuitiveDesigns.ShootingRange.EditorTools
{
    public static class RangeBuildKit
    {
        public const string PrefabDir = "Assets/Prefabs/ShootingRange";
        public const string MaterialDir = "Assets/Materials";

        public const string PropLayer = "Prop";
        public const string NoShootLayer = "NoShoot";

        public const float PropSize = 0.14f;

        public static Vector3 Origin { get; private set; }
        public static Vector3 Forward { get; private set; }
        public static Vector3 Right { get; private set; }

        public class Palette
        {
            public Material Ground;
            public Material Furniture;
            public Material Wall;
            public Material Crate;
            public Material Can;
            public Material Bottle;
            public Material Target;
            public Material Mover;
            public Material Fragment;
            public Material Round;
            public Material Gun;
            public Material Rail;
            public Material Laser;
        }

        public class Prefabs
        {
            public GameObject Round;
            public GameObject Crate;
            public GameObject Can;
            public GameObject Bottle;
            public GameObject Target;
            public GameObject Mover;
            public GameObject Fragment;
        }

        public static void EstablishPlayerFrame()
        {
            var rig = FindRigRoot();

            Origin = rig != null ? new Vector3(rig.position.x, 0f, rig.position.z) : Vector3.zero;

            Vector3 flat = rig != null ? Vector3.ProjectOnPlane(rig.forward, Vector3.up) : Vector3.zero;
            Forward = flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.forward;
            Right = Vector3.Cross(Vector3.up, Forward);

            Debug.Log("[RangeBuildKit] Player frame: origin " + Origin + ", facing " + Forward +
                      (rig == null ? "  (NO RIG FOUND, assumed world origin, verify the range is in front of you)" : ""));
        }

        public static Vector3 At(float right, float up, float ahead)
        {
            return Origin + Right * right + Vector3.up * up + Forward * ahead;
        }

        public static Quaternion Facing()
        {
            return Quaternion.LookRotation(Forward, Vector3.up);
        }

        public static Palette BuildPalette()
        {
            EnsureDir(MaterialDir);
            return new Palette
            {
                Ground = Unlit("SR_Ground", new Color(0.20f, 0.21f, 0.23f)),
                Furniture = Unlit("SR_Furniture", new Color(0.34f, 0.30f, 0.26f)),
                Wall = Unlit("SR_Wall", new Color(0.26f, 0.22f, 0.30f)),
                Crate = Unlit("SR_Crate", new Color(0.72f, 0.52f, 0.28f)),
                Can = Unlit("SR_Can", new Color(0.62f, 0.66f, 0.70f)),
                Bottle = Unlit("SR_Bottle", new Color(0.30f, 0.55f, 0.42f)),
                Target = Unlit("SR_Target", new Color(0.85f, 0.24f, 0.24f)),
                Mover = Unlit("SR_Mover", new Color(0.95f, 0.72f, 0.16f)),
                Fragment = Unlit("SR_Fragment", new Color(0.80f, 0.60f, 0.14f)),
                Round = Unlit("SR_Round", new Color(1f, 0.85f, 0.35f)),
                Gun = Unlit("SR_Gun", new Color(0.16f, 0.17f, 0.19f)),
                Rail = Unlit("SR_Rail", new Color(0.42f, 0.45f, 0.50f)),
                Laser = Unlit("SR_Laser", new Color(1f, 0.15f, 0.12f)),
            };
        }

        public static Prefabs BuildPrefabs(Palette palette, int propLayer, int noShootLayer, Sounds sounds)
        {
            EnsureDir(PrefabDir);

            return new Prefabs
            {
                Round = BuildRound(palette, noShootLayer),
                Crate = BuildProp("Prop_Crate", PrimitiveType.Cube, new Vector3(PropSize, PropSize, PropSize),
                                  0.4f, palette.Crate, propLayer, false, 10, 50, 50, sounds.WoodImpacts),
                Can = BuildProp("Prop_Can", PrimitiveType.Cylinder, new Vector3(0.07f, PropSize * 0.5f, 0.07f),
                                0.15f, palette.Can, propLayer, false, 10, 40, 60, sounds.Tin),
                Bottle = BuildProp("Prop_Bottle", PrimitiveType.Capsule, new Vector3(0.07f, PropSize * 0.5f, 0.07f),
                                   0.25f, palette.Bottle, propLayer, false, 10, 45, 55, sounds.Tin),
                Target = BuildProp("Target_Plate", PrimitiveType.Cube, new Vector3(0.26f, PropSize, 0.03f),
                                   0.6f, palette.Target, propLayer, true, 25, 150, 150, sounds.Tin),
                Mover = BuildMover(palette, propLayer),
                Fragment = BuildFragment(palette, propLayer),
            };
        }

        private static GameObject BuildRound(Palette palette, int noShootLayer)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Round";
            go.transform.localScale = Vector3.one * 0.05f;

            // The round sweeps for its own hits. A collider here would let it shove props on contact
            // as well, and report the same impact twice
            Object.DestroyImmediate(go.GetComponent<Collider>());
            Paint(go, palette.Round);

            // A 5 cm sphere at speed is a couple of pixels a frame. The trail is what makes the shot
            // legible, and it is nearly free
            var trail = go.AddComponent<TrailRenderer>();
            trail.time = 0.14f;
            trail.startWidth = 0.035f;
            trail.endWidth = 0f;
            trail.numCapVertices = 0;
            trail.sharedMaterial = palette.Round;

            var projectile = go.AddComponent<Projectile>();
            SetInt(projectile, "hitMask", ~(1 << noShootLayer));

            return SavePrefab(go, "Round");
        }

        public static GameObject BuildProp(string name, PrimitiveType shape, Vector3 scale, float mass,
                                           Material material, int layer, bool goal,
                                           int hitScore, int knockScore, int chainBonus,
                                           AudioClip[] clatter)
        {
            var go = GameObject.CreatePrimitive(shape);
            go.name = name;
            go.transform.localScale = scale;
            go.layer = layer;
            Paint(go, material);

            BoxifyCollider(go);

            var body = go.AddComponent<Rigidbody>();
            body.mass = mass;

            var knockable = go.AddComponent<Knockable>();
            SetBool(knockable, "countsTowardRoundGoal", goal);
            SetInt(knockable, "directHitScore", hitScore);
            SetInt(knockable, "knockdownScore", knockScore);
            SetInt(knockable, "chainBonus", chainBonus);
            SetArray(knockable, "clatterClips", clatter ?? new AudioClip[0]);

            return SavePrefab(go, name);
        }

        private static GameObject BuildMover(Palette palette, int propLayer)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "TrackMover";
            go.transform.localScale = new Vector3(0.34f, 0.34f, 0.10f);
            go.layer = propLayer;
            Paint(go, palette.Mover);

            var body = go.AddComponent<Rigidbody>();
            body.isKinematic = true;

            var mover = go.AddComponent<TrackMover>();
            SetFloat(mover, "moverLength", 0.34f);

            return SavePrefab(go, "TrackMover");
        }

        private static GameObject BuildFragment(Palette palette, int propLayer)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Fragment";
            go.transform.localScale = Vector3.one * 0.06f;
            go.layer = propLayer;
            Paint(go, palette.Fragment);

            var body = go.AddComponent<Rigidbody>();
            body.mass = 0.04f;

            return SavePrefab(go, "Fragment");
        }

        /// A capsule or sphere collider has a ROUNDED BOTTOM and physically cannot stand on a shelf,
        /// it rolls off and takes the stack with it. A box also beats a convex mesh on mobile
        public static void BoxifyCollider(GameObject go)
        {
            Object.DestroyImmediate(go.GetComponent<Collider>());

            var filter = go.GetComponent<MeshFilter>();
            var box = go.AddComponent<BoxCollider>();
            if (filter != null && filter.sharedMesh != null) box.size = filter.sharedMesh.bounds.size;
        }

        public static GameObject Block(string name, Vector3 centre, Vector3 size, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = centre;
            go.transform.rotation = Facing();
            go.transform.localScale = size;
            Paint(go, material);
            return go;
        }

        public const string AudioRoot = "Assets/Sounds/ShootingRange";
        public const string MagicFxRoot = "Assets/Magic effects pack/Prefabs/";

        public class Sounds
        {
            public AudioClip[] Gunshots;
            public AudioClip[] Dry;
            public AudioClip Reload;
            public AudioClip[] WoodImpacts;
            public AudioClip[] Tin;
            public AudioClip[] Ding;
            public AudioClip[] FullAuto;
            public AudioClip[] Scattershot;
            public AudioClip[] DualWield;
            public AudioClip[] SlowMotion;
            public AudioClip[] Milestone;
            public AudioClip SlowEnter;
            public AudioClip SlowExit;
            public AudioClip RoundStart;
            public AudioClip RoundEnd;
        }

        public class Effects
        {
            public ParticleSystem MuzzleFlash;
            public ParticleSystem Impact;
            public ParticleSystem TargetPop;
            public ParticleSystem Grant;
            public ParticleSystem GunAura;
            public ParticleSystem SlowCircle;
        }

        /// Forces a reimport first, clips copied in while the editor was closed, or before
        /// RangeAudioImport compiled, would otherwise keep Unity's default desktop settings
        public static Sounds LoadSounds()
        {
            if (!AssetDatabase.IsValidFolder(AudioRoot))
            {
                Debug.LogWarning("[RangeBuildKit] " + AudioRoot + " is missing. The range will build silent.");
                return new Sounds();
            }

            AssetDatabase.ImportAsset(AudioRoot, ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);

            var sounds = new Sounds
            {
                Gunshots = Clips("Gun", "gunshot_"),
                Dry = Clips("Gun", "dry_"),
                Reload = First(Clips("Gun", "reload_")),
                WoodImpacts = Clips("World", "impact_wood_"),
                Tin = Clips("World", "clang_tin_"),
                Ding = Clips("World", "ding_"),
                FullAuto = Clips("PowerUps", "full_auto_"),
                Scattershot = Clips("PowerUps", "scattershot_"),
                DualWield = Clips("PowerUps", "dual_wield_"),
                SlowMotion = Clips("PowerUps", "slow_motion_"),
                Milestone = Clips("PowerUps", "milestone_"),
                SlowEnter = First(Clips("PowerUps", "slow_enter_")),
                SlowExit = First(Clips("PowerUps", "slow_exit_")),
                RoundStart = First(Clips("Session", "round_start_")),
                RoundEnd = First(Clips("Session", "round_end_")),
            };

            Debug.Log("[RangeBuildKit] Audio: " + sounds.Gunshots.Length + " gunshots, " +
                      sounds.WoodImpacts.Length + " impacts, " + sounds.FullAuto.Length + "/" +
                      sounds.Scattershot.Length + "/" + sounds.DualWield.Length + "/" +
                      sounds.SlowMotion.Length + " power-up stingers.");
            return sounds;
        }

        private static AudioClip[] Clips(string folder, string prefix)
        {
            var clips = new List<AudioClip>();
            string dir = AudioRoot + "/" + folder;
            if (!AssetDatabase.IsValidFolder(dir)) return clips.ToArray();

            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { dir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!Path.GetFileName(path).StartsWith(prefix)) continue;

                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip != null) clips.Add(clip);
            }

            clips.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return clips.ToArray();
        }

        private static AudioClip First(AudioClip[] clips)
        {
            return clips != null && clips.Length > 0 ? clips[0] : null;
        }

        /// Our own copies of the vendor effects, sized and capped for a headset. The vendor prefabs
        /// are never edited, so a pack update cannot silently undo any of this
        public static Effects BuildEffects()
        {
            EnsureDir(PrefabDir);
            return new Effects
            {
                MuzzleFlash = EffectDonor("Sparks/Sparks flashing yellow", "FX_MuzzleFlash", 0.35f, false, 40),
                Impact = EffectDonor("Sparks/Sparks explode yellow", "FX_Impact", 0.4f, false, 50),
                TargetPop = EffectDonor("Hits and explosions/Star hit", "FX_TargetPop", 0.5f, false, 40),
                Grant = EffectDonor("Hits and explosions/Holy hit", "FX_Grant", 0.35f, false, 40),

                // Buff's "Smoke" layer is a 2 m sprite at 30 a second, which is overdraw an inch from your face
                GunAura = EffectDonor("Character auras/Buff", "FX_GunAura", 0.3f, true, 30, "Smoke"),
                SlowCircle = EffectDonor("Magic circles/Magic circle", "FX_SlowCircle", 0.8f, true, 100),
            };
        }

        private static ParticleSystem EffectDonor(string source, string name, float scale, bool loop,
                                                  int maxParticles, params string[] stripChildren)
        {
            string path = MagicFxRoot + source + ".prefab";
            var original = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (original == null)
            {
                Debug.LogWarning("[RangeBuildKit] Missing effect " + path + ", " + name + " skipped.");
                return null;
            }

            var go = Object.Instantiate(original);
            go.name = name;
            go.transform.localScale = original.transform.localScale * scale;

            foreach (var child in go.GetComponentsInChildren<Transform>(true))
            {
                if (child == null || child == go.transform) continue;
                if (System.Array.IndexOf(stripChildren, child.name) >= 0) Object.DestroyImmediate(child.gameObject);
            }

            foreach (var light in go.GetComponentsInChildren<Light>(true)) Object.DestroyImmediate(light);

            foreach (var system in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = system.main;
                main.loop = loop;
                main.playOnAwake = false;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                main.maxParticles = Mathf.Min(main.maxParticles, maxParticles);
            }

            return SavePrefab(go, name).GetComponent<ParticleSystem>();
        }

        public static void SetVector(Object target, string field, Vector3 value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null) { Missing(target, field); return; }
            prop.vector3Value = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public static int EnsureLayer(string name)
        {
            var asset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (asset == null || asset.Length == 0)
            {
                Debug.LogError("[RangeBuildKit] Could not open TagManager.asset. Add the '" + name +
                               "' layer by hand in Edit > Project Settings > Tags and Layers.");
                return 0;
            }

            var tagManager = new SerializedObject(asset[0]);
            var layers = tagManager.FindProperty("layers");

            for (int i = 0; i < layers.arraySize; i++)
            {
                if (layers.GetArrayElementAtIndex(i).stringValue == name) return i;
            }

            // 0-7 are Unity's own and must not be touched
            for (int i = 8; i < layers.arraySize; i++)
            {
                var slot = layers.GetArrayElementAtIndex(i);
                if (!string.IsNullOrEmpty(slot.stringValue)) continue;

                slot.stringValue = name;
                tagManager.ApplyModifiedPropertiesWithoutUndo();
                Debug.LogWarning("[RangeBuildKit] Added layer '" + name + "' in slot " + i +
                                 ". This edits ProjectSettings/TagManager.asset, shared with the team.");
                return i;
            }

            Debug.LogError("[RangeBuildKit] No free layer slot for '" + name + "'.");
            return 0;
        }

        public static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform) SetLayerRecursive(child.gameObject, layer);
        }

        public static Transform FindRigRoot()
        {
            var cam = Object.FindObjectOfType<Camera>();
            if (cam == null) return null;

            var t = cam.transform;
            while (t.parent != null) t = t.parent;
            return t;
        }

        public static void Paint(GameObject go, Material material)
        {
            var renderer = go.GetComponent<Renderer>();
            if (renderer != null) renderer.sharedMaterial = material;
        }

        public static Material Unlit(string name, Color colour)
        {
            string path = MaterialDir + "/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                existing.SetColor("_Color", colour);
                return existing;
            }

            var mat = new Material(Shader.Find("Unlit/Color"));
            mat.name = name;
            mat.SetColor("_Color", colour);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        public static GameObject SavePrefab(GameObject go, string name)
        {
            string path = PrefabDir + "/" + name + ".prefab";
            var asset = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return asset;
        }

        public static void EnsureDir(string dir)
        {
            if (Directory.Exists(dir)) return;

            Directory.CreateDirectory(dir);
            AssetDatabase.Refresh();
        }

        public static TMP_Text Label(Transform parent, string name, Vector2 position, float size,
                                     TextAlignmentOptions alignment)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(880f, 140f);
            rect.anchoredPosition = position;

            var text = go.AddComponent<TextMeshProUGUI>();
            text.fontSize = size;
            text.alignment = alignment;
            text.color = Color.white;
            text.text = string.Empty;

            return text;
        }

        public static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        public static void SetRef(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null) { Missing(target, field); return; }
            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void SetFloat(Object target, string field, float value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null) { Missing(target, field); return; }
            prop.floatValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void SetInt(Object target, string field, int value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null) { Missing(target, field); return; }
            prop.intValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void SetBool(Object target, string field, bool value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null) { Missing(target, field); return; }
            prop.boolValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void SetEnum(Object target, string field, int value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null) { Missing(target, field); return; }
            prop.enumValueIndex = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void SetArray(Object target, string field, Object[] values)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null) { Missing(target, field); return; }

            prop.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i];

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Missing(Object target, string field)
        {
            Debug.LogWarning("[RangeBuildKit] field '" + field + "' not found on " + target.GetType().Name +
                             ". It was probably renamed; the scene will build with that slot empty.");
        }
    }
}
