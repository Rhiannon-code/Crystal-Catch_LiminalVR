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
        private const string PickaxeModel = "Assets/Mine/Models/Props/Pickaxe.fbx";
        private const float EffectHudWidth = 1000f;
        private const float EffectHudHeight = 600f;
        private const float EffectHudScale = 0.002f;

        private const float PickaxeGripFromButt = 0.074f;
        private const float PickaxeHeadFromTop = 0.083f;

        // Fallbacks, only used if the mesh cannot be read. Metres, at the pack's normal import scale
        private const float PickaxeGrip = -0.50f;
        private const float PickaxeHead = 0.214f;

        // 4 m is the Loafbrr kit's module size, and it was already the ring spacing, so every ring
        // is exactly one module deep and the pieces tile without a scale factor anywhere
        private const float RingLength = 4f;
        private const float TunnelHalfWidth = 4.5f;

        // Was 5.5 m of greybox box. Now it is the height the kit's wall panels actually are, so the
        // walls meet the ceiling instead of stopping 1.3 m short of it
        private const float CeilingHeight = 3.9f;

        // The mine floor sits BELOW the track, so the rails rise to just under the cart rather than
        // through its floor
        private const float FloorY = -0.3f;

        private const string KitPrefabs = "Assets/LoafbrrAssets/MInesAndCaveSet/prefabs/";
        private const string KitModel = "Assets/LoafbrrAssets/MInesAndCaveSet/fbx/MinesSet.fbx";
        private const string RingMeshes = PrefabDir + "/TunnelRingMeshes.asset";

        // Measured off MinesSet.fbx. The kit is authored in metres with pivots on the module grid
        private const float FloorTileTop = 0.057f;    // Ground_Mines_A's surface, above its pivot
        private const float CeilingRockDrop = 0.4f;   // How far Ground_Cave_A's rock hangs once flipped
        private const float PostHeight = 3.8f;        // Post_A
        private const float BeamHalfThickness = 0.1f; // Beam_A

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
                SetFloat(spawner, "ceilingHeight", CeilingHeight - CeilingRockDrop);
                SetFloat(spawner, "swingHeight", SwingHeight);
                SetFloat(spawner, "lateralSpread", LateralSpread);

                Debug.Log("[CCCartBuilder] Spawner repointed at the track; drop height set to " +
                          (CeilingHeight - CeilingRockDrop) + " m, just under the tunnel's rock ceiling.");
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

            BuildEffectHud(game);

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
                      "  Seed 0 means Play generates a DIFFERENT track than this preview, set a " +
                      "non zero seed to compare like for like.\n" +
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

            // Visual runs along local +Z from the grip. BatSwinger rescales it live for reach
            float grip = PickaxeGrip;
            float head = PickaxeHead;

            var visual = MakePickaxeVisual(root.transform, ref grip, ref head);
            bool isModel = visual != null;
            if (!isModel) visual = MakeCapsuleVisual(root.transform, mat);

            var capsule = root.AddComponent<CapsuleCollider>();
            capsule.isTrigger = true;

            var renderers = visual.GetComponentsInChildren<MeshRenderer>();

            // Ghosting swaps sharedMaterial, so "normal" has to be whatever the visual actually
            // wears, the pickaxe's own textured material for the model, BatWood for the fallback
            var normal = renderers.Length > 0 && renderers[0].sharedMaterial != null
                ? renderers[0].sharedMaterial
                : mat;

            SetRef(swinger, "batVisual", visual.transform);
            SetRef(swinger, "hitVolume", capsule);
            SetRef(swinger, "normalMaterial", normal);
            SetRef(swinger, "ghostedMaterial", ghost);
            SetArray(swinger, "ghostRenderers", renderers);
            SetBool(swinger, "visualIsModel", isModel);
            SetFloat(swinger, "modelGripAlongShaft", grip);
            SetFloat(swinger, "modelHeadAlongShaft", head);

            // A trigger needs a kinematic Rigidbody on one side or OnTriggerEnter never fires,
            // the crystals deliberately have none, so it has to live here
            var rb = root.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            return root;
        }

        /// Instantiates the Mine pack pickaxe and rotates it so its shaft points down the bat's
        /// local +Z. Returns null if the pack is missing so the build still produces a usable bat
        private static GameObject MakePickaxeVisual(Transform parent, ref float grip, ref float head)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(PickaxeModel);
            if (source == null)
            {
                Debug.LogWarning("[CCCartBuilder] " + PickaxeModel + " not found, falling back to the capsule bat.");
                return null;
            }

            var visual = Object.Instantiate(source, parent, false);
            visual.name = "PickaxeVisual";

            foreach (var col in visual.GetComponentsInChildren<Collider>())
                Object.DestroyImmediate(col);

            // Keep the pickaxe's own textured material, only drop the lighting work the rest of
            // the greybox has already dropped
            foreach (var mr in visual.GetComponentsInChildren<MeshRenderer>())
                StripLighting(mr, mr.sharedMaterial);

            // Read the shaft off the mesh while the instance is still unrotated, and map it up to
            // the visual root in case the import parked the mesh on a child with its own offset
            var mf = visual.GetComponentInChildren<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                var bounds = mf.sharedMesh.bounds;
                float butt = ToVisualY(visual.transform, mf.transform, bounds.min.y);
                float top = ToVisualY(visual.transform, mf.transform, bounds.max.y);
                float height = top - butt;

                if (height > 0.0001f)
                {
                    grip = butt + height * PickaxeGripFromButt;
                    head = top - height * PickaxeHeadFromTop;
                }
            }

            // The mesh is authored +Y up. +90 on X lays the shaft down +Z, which leaves the pick
            // blades in the vertical plane, the way a pick is actually held. Roll on Z to change it
            // Position and scale stay BatSwinger's, it drives them every frame from reach
            visual.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            return visual;
        }

        private static float ToVisualY(Transform visual, Transform mesh, float meshY)
        {
            return visual.InverseTransformPoint(mesh.TransformPoint(new Vector3(0f, meshY, 0f))).y;
        }

        private static GameObject MakeCapsuleVisual(Transform parent, Material mat)
        {
            var visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            visual.name = "BatVisual";
            visual.transform.SetParent(parent, false);
            visual.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Object.DestroyImmediate(visual.GetComponent<Collider>());
            StripLighting(visual.GetComponent<MeshRenderer>(), mat);
            return visual;
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

        /// The active effect readout, its own canvas, locked to the head rather than riding the
        /// world panel, because the ask is for it to be in a fixed place on screen at all times
        /// Power ups fill the top left corner, hazards the top right
        private static void BuildEffectHud(CrystalCatchGame game)
        {
            var existing = Object.FindObjectOfType<EffectStatusHUD>();
            if (existing != null) Object.DestroyImmediate(existing.gameObject);

            var go = new GameObject("EffectHUD", typeof(Canvas));

            // World space, NOT Screen Space Overlay. Overlay canvases render to the flat screen and
            // never reach either eye, so in a headset they are simply invisible
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(EffectHudWidth, EffectHudHeight);
            rect.localScale = Vector3.one * EffectHudScale;

            go.AddComponent<HeadLockedHud>();

            var readout = go.AddComponent<EffectStatusHUD>();
            if (game != null) SetRef(readout, "game", game);

            // Number keys fire each effect on demand. Without it, checking the readout means waiting
            // for a rare pickup to spawn AND connecting with it, which is a slow way to test a HUD
            if (game != null && game.GetComponent<KeyboardTestEffects>() == null)
                game.gameObject.AddComponent<KeyboardTestEffects>();

            Debug.Log("[CCCartBuilder] Head locked effect HUD built: power ups top left, hazards top right.");
        }

        private static GameObject BuildRingPrefab()
        {
            if (!Directory.Exists(PrefabDir)) Directory.CreateDirectory(PrefabDir);

            // CombineMeshes reads vertices off the source meshes, which the kit ships turned off
            EnsureModelReadable(KitModel);

            var root = new GameObject("TunnelRing");
            var loose = new GameObject("Loose");
            loose.transform.SetParent(root.transform, false);

            BuildRingPieces(loose.transform);
            CombineInto(root.transform, loose.transform);
            Object.DestroyImmediate(loose);

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, RingPrefab);
            Object.DestroyImmediate(root);
            return prefab;
        }

        /// Local space of a ring: +Z is the direction of travel, the origin sits on the track
        /// centreline, and the slice spans z = -2 to +2
        private static void BuildRingPieces(Transform parent)
        {
            // Floor. Three 4 m tiles across, overlapping the 4 m grid slightly so no seam lines up
            // with a ring boundary
            for (int i = -1; i <= 1; i++)
                PlaceKit(parent, "Ground/Ground_Mines_A", "Floor",
                         new Vector3(i * 4f, FloorY - FloorTileTop, 0f), Quaternion.identity);

            // Ceiling. Cave ground tiles turned upside down, which is what makes it read as rock
            // hanging over you rather than a lid. These are one sided too, facing +Y, so the flip
            // is what points them down at the player as well as what puts the relief overhead
            for (int i = -1; i <= 1; i++)
                PlaceKit(parent, "Ground/Ground_Cave_A", "Ceiling",
                         new Vector3(i * 4f, CeilingHeight, 0f), Quaternion.Euler(180f, 0f, 0f));

            // Walls. Measured off the mesh: the panels run along their own X and are ONE SIDED,
            // facing -Z (19.9 of face area on -Z, essentially none on +Z). Get the yaw backwards and
            // they are not "wrong way round", they are invisible, culled from inside the tunnel
            // -90 turns the face to +X for the left wall, +90 turns it to -X for the right
            PlaceKit(parent, "Wall/Wall_Mines_A", "WallL",
                     new Vector3(-TunnelHalfWidth, FloorY, 0f), Quaternion.Euler(0f, -90f, 0f));
            PlaceKit(parent, "Wall/Wall_Mines_A", "WallR",
                     new Vector3(TunnelHalfWidth, FloorY, 0f), Quaternion.Euler(0f, 90f, 0f));

            // Rail, same yaw so its 4 m length runs down the track and its gauge sits across it
            PlaceKit(parent, "Rails/Rail_A", "Rail",
                     new Vector3(0f, FloorY, 0f), Quaternion.Euler(0f, 90f, 0f));

            // Support frame. This is the greybox ribs' real job, a smooth featureless tunnel
            // produces almost no vection and you cannot tell you are moving, so something has to
            // pass you at a steady rhythm. A post and beam set every 4 m is that, and it is what a
            // mine would actually have
            PlaceKit(parent, "Posts/Post_A", "PostL",
                     new Vector3(-TunnelHalfWidth + 0.2f, FloorY, 0f), Quaternion.identity);
            PlaceKit(parent, "Posts/Post_A", "PostR",
                     new Vector3(TunnelHalfWidth - 0.2f, FloorY, 0f), Quaternion.identity);

            // Three 4 m beams overlapping into one 9.8 m span, so the ends bury themselves in the
            // walls instead of stopping short of the posts
            float beamY = FloorY + PostHeight + BeamHalfThickness;
            PlaceKit(parent, "Posts/Beam_A", "Beam_L", new Vector3(-2.9f, beamY, 0f), Quaternion.identity);
            PlaceKit(parent, "Posts/Beam_A", "Beam_M", new Vector3(0f, beamY, 0f), Quaternion.identity);
            PlaceKit(parent, "Posts/Beam_A", "Beam_R", new Vector3(2.9f, beamY, 0f), Quaternion.identity);
        }

        private static void PlaceKit(Transform parent, string kitPath, string name,
                                     Vector3 localPos, Quaternion localRot)
        {
            string path = KitPrefabs + kitPath + ".prefab";
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (source == null)
            {
                Debug.LogWarning("[CCCartBuilder] Kit piece missing: " + path);
                return;
            }

            var go = Object.Instantiate(source, parent, false);
            go.name = name;
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
        }

        /// Merges every piece under `loose` into one child of `parent` per material
        private static void CombineInto(Transform parent, Transform loose)
        {
            var filters = loose.GetComponentsInChildren<MeshFilter>();
            var byMaterial = new System.Collections.Generic.Dictionary<Material,
                             System.Collections.Generic.List<CombineInstance>>();

            foreach (var filter in filters)
            {
                if (filter.sharedMesh == null) continue;

                var renderer = filter.GetComponent<MeshRenderer>();
                var material = renderer != null ? renderer.sharedMaterial : null;
                if (material == null) continue;

                if (!byMaterial.ContainsKey(material))
                    byMaterial[material] = new System.Collections.Generic.List<CombineInstance>();

                var instance = new CombineInstance();
                instance.mesh = filter.sharedMesh;

                // Relative to the RING, not the world, or every ring bakes in wherever the builder
                // happened to leave the temporary root
                instance.transform = parent.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                byMaterial[material].Add(instance);
            }

            if (byMaterial.Count == 0)
            {
                Debug.LogWarning("[CCCartBuilder] No kit geometry combined, the tunnel will be empty.");
                return;
            }

            // Combined meshes have to live in an asset or the prefab points at nothing once the
            // build finishes. One file, one sub-asset per material
            if (File.Exists(RingMeshes)) AssetDatabase.DeleteAsset(RingMeshes);
            bool created = false;

            foreach (var pair in byMaterial)
            {
                var mesh = new Mesh();
                mesh.name = "Ring_" + pair.Key.name;
                mesh.CombineMeshes(pair.Value.ToArray(), true, true);
                mesh.RecalculateBounds();

                if (!created)
                {
                    AssetDatabase.CreateAsset(mesh, RingMeshes);
                    created = true;
                }
                else
                {
                    AssetDatabase.AddObjectToAsset(mesh, RingMeshes);
                }

                var go = new GameObject(mesh.name);
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                StripLighting(go.AddComponent<MeshRenderer>(), pair.Key);
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[CCCartBuilder] Tunnel ring combined into " + byMaterial.Count +
                      " mesh(es), one per material.");
        }

        private static void EnsureModelReadable(string modelPath)
        {
            var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
            if (importer == null)
            {
                Debug.LogWarning("[CCCartBuilder] " + modelPath + " not found, cannot enable Read/Write.");
                return;
            }

            if (importer.isReadable) return;

            importer.isReadable = true;
            importer.SaveAndReimport();
            Debug.Log("[CCCartBuilder] Enabled Read/Write on " + modelPath + " so its meshes can be combined.");
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

        private static void SetBool(Object target, string field, bool value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null)
            {
                Debug.LogWarning("[CCCartBuilder] bool field '" + field + "' not found on " +
                                 target.GetType().Name);
                return;
            }
            prop.boolValue = value;
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
