using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace IntuitiveDesigns.CrystalCatch.EditorTools
{
    /// Builds the mine cart scene, procedural track, cart, tunnel ring prefab, bat, and a HUD that
    /// rides with the player
    public static class CCCartBuilder
    {
        private const string SourceScene = "Assets/Scenes/CrystalCatch.unity";
        private const string TargetScene = "Assets/Scenes/MineCart.unity";
        private const string MaterialDir = "Assets/Materials";
        private const string PrefabDir = "Assets/Prefabs";
        private const string RingPrefab = PrefabDir + "/TunnelRing.prefab";
        private const float RingLength = 4f;
        private const float TunnelHalfWidth = 4.5f;
        private const float CeilingHeight = 5.5f;

        // Items must still land where a bat can reach them, regardless of how tall the shaft gets
        private const float SwingHeight = 1.2f;
        private const float LateralSpread = 2.6f;

        [MenuItem("Crystal Catch/Build Mine Cart Scene")]
        public static void Build()
        {
            // Must run BEFORE the scene loads its prefab references, or pooled copies are cloned
            // from the unmigrated asset
            int migrated = MigratePrefabsToFalling();

            var scene = EditorSceneManager.OpenScene(SourceScene, OpenSceneMode.Single);

            var ring = BuildRingPrefab();

            // Track first — everything else references it.
            var trackGo = new GameObject("Track");
            var track = trackGo.AddComponent<TrackPath>();

            var game = Object.FindObjectOfType<CrystalCatchGame>();

            // Cart
            var cartGo = new GameObject("Cart");
            var cart = cartGo.AddComponent<CartController>();
            SetRef(cart, "track", track);
            if (game != null)
            {
                SetRef(cart, "game", game);
                // The game raises cart speed each round, so the link runs both ways
                SetRef(game, "cart", cart);
            }
            BuildCartBody(cartGo.transform);

            var rig = FindRigRoot();
            if (rig != null)
            {
                SetRef(cart, "carry", rig);
                Debug.Log("[CCCartBuilder] Carrying rig: '" + rig.name + "'. VERIFY, if the view does not " +
                          "move with the cart, clear CartController.carry and parent the rig under Cart.");
            }
            else
            {
                Debug.LogWarning("[CCCartBuilder] No VR rig found. Assign CartController.carry by hand, " +
                                 "or parent the rig under Cart.");
            }

            // Tunnel
            var tunnelGo = new GameObject("Tunnel");
            var tunnel = tunnelGo.AddComponent<TunnelBuilder>();
            SetRef(tunnel, "cart", cart);
            SetRef(tunnel, "track", track);
            SetRef(tunnel, "ringPrefab", ring);

            // Bat rides in the cart, following the right hand when one is tracked
            var bat = BuildBat(game, cartGo.transform);

            // Spawner, repoint the existing one at the track instead of the old ring placement
            // NOTE: the includeInactive overload of FindObjectOfType only exists from Unity 2020.1;
            // this project is pinned to 2019.1 by the Liminal SDK
            var spawner = Object.FindObjectOfType<CrystalSpawner>();
            if (spawner != null)
            {
                spawner.gameObject.SetActive(true);
                SetRef(spawner, "cart", cart);
                SetRef(spawner, "track", track);

                // The spawner's serialized values were baked when the component was first created,
                // so changing the C# defaults does NOT update an existing scene. Push them here or
                // the drop height silently disagrees with the tunnel
                SetFloat(spawner, "ceilingHeight", CeilingHeight);
                SetFloat(spawner, "swingHeight", SwingHeight);
                SetFloat(spawner, "lateralSpread", LateralSpread);

                Debug.Log("[CCCartBuilder] Spawner repointed at the track; drop height set to " +
                          CeilingHeight + " m to match the tunnel.");
            }
            else
            {
                Debug.LogWarning("[CCCartBuilder] No CrystalSpawner found, no crystals will spawn.");
            }

            // HUD rides with the cart
            var hud = Object.FindObjectOfType<CrystalCatchHUD>();
            if (hud != null)
            {
                var follower = hud.GetComponent<HudFollower>();
                if (follower == null) follower = hud.gameObject.AddComponent<HudFollower>();
                SetRef(follower, "cart", cartGo.transform);
                Debug.Log("[CCCartBuilder] HUD now follows the cart.");
            }
            else
            {
                Debug.LogWarning("[CCCartBuilder] No CrystalCatchHUD found, HUD will not follow.");
            }

            // The old touch collectors are superseded by the bat.
            foreach (var hc in Object.FindObjectsOfType<HandCollector>())
                hc.gameObject.SetActive(false);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, TargetScene);

            Debug.Log("[CCCartBuilder] Built " + TargetScene +
                      "\n  procedural track + cart + pooled tunnel + bat (" + (bat != null ? bat.name : "none") + ")" +
                      "\n  " + migrated + " prefab(s) migrated to FallingMover" +
                      "\n  ceiling " + CeilingHeight + " m, keep CrystalSpawner.ceilingHeight matching" +
                      "\n  CrystalCatch.unity NOT modified.");
        }

        private const string PreviewRoot = "TUNNEL PREVIEW (editor only)";

        /// How much of the track the editor preview draws. Enough to read a style's character
        /// without building thousands of rings
        private const float PreviewMetres = 400f;

        [MenuItem("Crystal Catch/Preview Tunnel In Editor")]
        public static void PreviewTunnel()
        {
            ClearPreview();

            var track = Object.FindObjectOfType<TrackPath>();
            if (track == null)
            {
                Debug.LogWarning("[CCCartBuilder] No TrackPath in the scene, open MineCart.unity first.");
                return;
            }

            var ring = AssetDatabase.LoadAssetAtPath<GameObject>(RingPrefab);
            if (ring == null)
            {
                Debug.LogWarning("[CCCartBuilder] TunnelRing.prefab missing, run Build Mine Cart Scene.");
                return;
            }

            // Generate now so the editor has the same track shape Play would produce. With seed = 0
            // this is a DIFFERENT random track each time, set a non-zero seed to preview the real one
            track.Generate();

            var root = new GameObject(PreviewRoot);

            // HARD CAP. The track is now kilometres long
            float previewLength = Mathf.Min(track.Length, PreviewMetres);
            int count = Mathf.FloorToInt(previewLength / RingLength);

            for (int i = 0; i < count; i++)
            {
                float d = i * RingLength;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(ring, root.transform);
                go.transform.position = track.PositionAt(d);
                go.transform.rotation = track.RotationAt(d, true);
            }

            Debug.Log("[CCCartBuilder] Previewed " + count + " rings over the first " +
                      previewLength.ToString("0") + " m of " + track.Length.ToString("0") + " m.\n" +
                      "  EDITOR ONLY, clear it before saving (Crystal Catch > Clear Tunnel Preview).\n" +
                      "  Seed 0 means Play generates a DIFFERENT track than this preview — set a " +
                      "non-zero seed to compare like for like.\n" +
                      "  Select the Track object to see the full path as a cyan gizmo line.");
        }

        [MenuItem("Crystal Catch/Clear Tunnel Preview")]
        public static void ClearPreview()
        {
            var existing = GameObject.Find(PreviewRoot);
            while (existing != null)
            {
                Object.DestroyImmediate(existing);
                existing = GameObject.Find(PreviewRoot);
            }
        }

        /// Swaps HomingMover for FallingMover on every crystal/special prefab
        private static int MigratePrefabsToFalling()
        {
            int changed = 0;
            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { PrefabDir });

            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var contents = PrefabUtility.LoadPrefabContents(path);
                if (contents == null) continue;

                bool isItem = contents.GetComponent<Crystal>() != null
                              || contents.GetComponent<SpecialItem>() != null;

                if (!isItem)
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                    continue;
                }

                bool dirty = false;

                var old = contents.GetComponent<HomingMover>();
                if (old != null) { Object.DestroyImmediate(old, true); dirty = true; }

                if (contents.GetComponent<FallingMover>() == null)
                {
                    contents.AddComponent<FallingMover>();
                    dirty = true;
                }

                if (dirty)
                {
                    PrefabUtility.SaveAsPrefabAsset(contents, path);
                    changed++;
                }

                PrefabUtility.UnloadPrefabContents(contents);
            }

            Debug.Log("[CCCartBuilder] Migrated " + changed + " prefab(s) from HomingMover to FallingMover.");
            return changed;
        }

        private static GameObject BuildBat(CrystalCatchGame game, Transform cart)
        {
            var root = new GameObject("Bat");

            // Parented to the cart, NOT left at the scene root
            if (cart != null)
            {
                root.transform.SetParent(cart, false);
                root.transform.localPosition = new Vector3(0.3f, 1.15f, 0.25f);
                root.transform.localRotation = Quaternion.Euler(-25f, 0f, 0f);
            }

            var swinger = root.AddComponent<BatSwinger>();
            if (game != null) SetRef(swinger, "game", game);

            // Without this the cart's own speed reads as swing speed and every touch is a hit
            if (cart != null) SetRef(swinger, "motionReference", cart);

            // Desktop swing testing. The emulator's single 3DOF controller cannot translate, so
            // without this the swing speed gate can never be satisfied in the editor
            root.AddComponent<MouseTestBat>();

            var mat = GetOrCreateUnlit("BatWood", new Color(0.55f, 0.36f, 0.18f));
            var ghost = GetOrCreateUnlit("BatGhost", new Color(0.35f, 0.35f, 0.42f));

            // Visual, a capsule along local +Z. BatSwinger rescales it live for reach/arc
            var visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            visual.name = "BatVisual";
            visual.transform.SetParent(root.transform, false);
            visual.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Object.DestroyImmediate(visual.GetComponent<Collider>());
            StripLighting(visual.GetComponent<MeshRenderer>(), mat);

            var capsule = root.AddComponent<CapsuleCollider>();
            capsule.isTrigger = true;

            SetRef(swinger, "batVisual", visual.transform);
            SetRef(swinger, "hitVolume", capsule);
            SetRef(swinger, "normalMaterial", mat);
            SetRef(swinger, "ghostedMaterial", ghost);
            SetArray(swinger, "ghostRenderers", new Object[] { visual.GetComponent<MeshRenderer>() });

            // A trigger needs a kinematic Rigidbody on one side or OnTriggerEnter never fires,
            // the crystals deliberately have none, so it has to live here
            var rb = root.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            return root;
        }

        private static void BuildCartBody(Transform parent)
        {
            // A cockpit reference frame is the biggest comfort win available in vehicular VR, a
            // stable visual anchor in the lower field of view
            var mat = GetOrCreateUnlit("CartGrey", new Color(0.30f, 0.30f, 0.34f));

            MakeBox(parent, "Cart_Floor", new Vector3(0f, -0.05f, 0f), new Vector3(1.4f, 0.1f, 2.0f), mat);
            MakeBox(parent, "Cart_WallL", new Vector3(-0.7f, 0.4f, 0f), new Vector3(0.1f, 0.9f, 2.0f), mat);
            MakeBox(parent, "Cart_WallR", new Vector3(0.7f, 0.4f, 0f), new Vector3(0.1f, 0.9f, 2.0f), mat);
            MakeBox(parent, "Cart_Front", new Vector3(0f, 0.4f, 1.0f), new Vector3(1.4f, 0.9f, 0.1f), mat);
            MakeBox(parent, "Cart_Back", new Vector3(0f, 0.4f, -1.0f), new Vector3(1.4f, 0.9f, 0.1f), mat);
        }

        private static GameObject BuildRingPrefab()
        {
            if (!Directory.Exists(PrefabDir)) Directory.CreateDirectory(PrefabDir);

            var wall = GetOrCreateUnlit("CaveWall", new Color(0.16f, 0.14f, 0.18f));
            var rib = GetOrCreateUnlit("CaveRib", new Color(0.44f, 0.39f, 0.30f));

            var root = new GameObject("TunnelRing");
            float mid = CeilingHeight * 0.5f;

            MakeBox(root.transform, "Floor", new Vector3(0f, -0.05f, 0f),
                    new Vector3(TunnelHalfWidth * 2f, 0.1f, RingLength), wall);
            MakeBox(root.transform, "Ceiling", new Vector3(0f, CeilingHeight, 0f),
                    new Vector3(TunnelHalfWidth * 2f, 0.1f, RingLength), wall);
            MakeBox(root.transform, "WallL", new Vector3(-TunnelHalfWidth, mid, 0f),
                    new Vector3(0.1f, CeilingHeight, RingLength), wall);
            MakeBox(root.transform, "WallR", new Vector3(TunnelHalfWidth, mid, 0f),
                    new Vector3(0.1f, CeilingHeight, RingLength), wall);

            // Ribs are what make motion perceptible. A smooth featureless tunnel produces almost no
            // vection, you genuinely cannot tell you are moving, and the comfort test returns a
            // false pass because there is nothing to feel
            MakeBox(root.transform, "Rib_L", new Vector3(-TunnelHalfWidth + 0.14f, mid, 0f),
                    new Vector3(0.18f, CeilingHeight, 0.25f), rib);
            MakeBox(root.transform, "Rib_R", new Vector3(TunnelHalfWidth - 0.14f, mid, 0f),
                    new Vector3(0.18f, CeilingHeight, 0.25f), rib);
            MakeBox(root.transform, "Rib_Top", new Vector3(0f, CeilingHeight - 0.1f, 0f),
                    new Vector3(TunnelHalfWidth * 1.6f, 0.16f, 0.25f), rib);

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, RingPrefab);
            Object.DestroyImmediate(root);
            return prefab;
        }

        private static GameObject MakeBox(Transform parent, string name, Vector3 localPos,
                                          Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;

            // Scenery never needs collision, and colliders would only cost broadphase time
            var col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);

            StripLighting(go.GetComponent<MeshRenderer>(), mat);
            return go;
        }

        private static void StripLighting(MeshRenderer mr, Material mat)
        {
            if (mr == null) return;
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        private static Material GetOrCreateUnlit(string name, Color colour)
        {
            if (!Directory.Exists(MaterialDir)) Directory.CreateDirectory(MaterialDir);
            string path = MaterialDir + "/" + name + ".mat";

            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            // Unlit/Color, no realtime lighting anywhere in this project
            var mat = new Material(Shader.Find("Unlit/Color"));
            mat.name = name;
            mat.SetColor("_Color", colour);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        private static Transform FindRigRoot()
        {
            var cam = Object.FindObjectOfType<Camera>();
            if (cam == null) return null;
            var t = cam.transform;
            while (t.parent != null) t = t.parent;
            return t;
        }

        private static void SetRef(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null)
            {
                Debug.LogWarning("[CCCartBuilder] field '" + field + "' not found on " + target.GetType().Name);
                return;
            }
            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetFloat(Object target, string field, float value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null)
            {
                Debug.LogWarning("[CCCartBuilder] float field '" + field + "' not found on " +
                                 target.GetType().Name);
                return;
            }
            prop.floatValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetArray(Object target, string field, Object[] values)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null) return;
            prop.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
