using System.IO;
using TMPro;
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
        private const string PatternDir = "Assets/SpawnPatterns";

        // Must match CaveAtmosphere.sightLimit. Everything the player could watch pop is drawn to
        // this distance plus a margin, so it is always fully fogged by the time it changes state
        private const float SightLimit = 65f;
        private const float SightMargin = 8f;
        private const float KitOverlap = 0.91f;

        private static float Step(float measured)
        {
            return Mathf.Max(0.25f, measured * KitOverlap);
        }
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
        private const float RingLength = 4f;
        private const float TunnelHalfWidth = 10f;
        private const float CeilingHeight = 12f;
        private const float FrameHalfWidth = 3.2f;
        private const float WallFrameInset = 0.3f;
        private const int WallPostTiers = 3;
        private const float SeamFillOffset = RingLength * 0.5f;
        private const float SeamFillDepth = 0.06f;
        private const int SurfaceRows = 2;
        private const float CrystalDropHeight = 4f;

        // Wall_Caves_A is 4.86 m tall, so three stacked reach 13 m and cover the roof line
        private const float WallPanelHeight = 4.44f;
        private const int WallTiers = 3;

        // Half the cavern width in 4 m tiles, so floor and roof always reach the walls
        private const int SurfaceTiles = 3;

        // The mine floor sits BELOW the track, so the rails rise to just under the cart rather than
        // through its floor
        private const float FloorY = -0.3f;

        private const string KitPrefabs = "Assets/LoafbrrAssets/MInesAndCaveSet/prefabs/";
        private const string KitModel = "Assets/LoafbrrAssets/MInesAndCaveSet/fbx/MinesSet.fbx";
        private const string RingMeshes = PrefabDir + "/TunnelRingMeshes.asset";
        private const string RailPrefab = PrefabDir + "/TunnelRail.prefab";
        private const string RailMeshes = PrefabDir + "/TunnelRailMeshes.asset";

        // Must match TunnelBuilder.railSpacing, Rail_B is the kit's 2 m piece, so it tiles exactly
        private const float RailSpacing = 2f;
        private const string FramePrefab = PrefabDir + "/TunnelFrame.prefab";
        private const string FrameMeshes = PrefabDir + "/TunnelFrameMeshes.asset";
        private const float FrameSpacing = 16f;
        // Pinch shaft obstacles
        private const float ShaftHalfWidth = 3f;
        private const float ShaftHeight = 4.2f;
        private const float ShaftLength = 9f;
        private const float TaperLength = 7f;

        // The blocker is a thin gate in the middle of the shaft, not the whole shaft. The shaft is
        // the frame, the gate is the rule
        private const float BlockerHalfDepth = 0.6f;

        // How much clear corridor is left on the open side of a lean. Wide enough to pass, narrow
        // enough that standing on the centreline does not
        private const float LeanClearHalfWidth = 1.15f;

        private const float DuckBeamHeight = 1.25f;   // Underside of the low beam
        private const float LeanIntrusion = 1.0f;     // How far the hanging rock crosses the centre
        private const float LeanRockPivot = 3.9f;     // Puts its underside near head height

        // Measured off MinesSet.fbx. The kit is authored in metres with pivots on the module grid
        private const float FloorTileTop = 0.057f;    // Ground_Mines_A's surface, above its pivot
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

            // Baked here, not left to Awake. The points serialize into the scene, so the track is
            // visible and editable in the editor immediately and Play rides exactly what you see
            track.Generate();

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
            var cartBody = BuildCartBody(cartGo.transform);

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

            // Lean the cart onto two wheels when the player leans. Body takes the full tip, the view
            // takes 30% of it
            var tilt = cartGo.AddComponent<CartTilt>();
            SetRef(tilt, "cart", cart);
            SetRef(tilt, "cartBody", cartBody);
            if (rig != null) SetRef(tilt, "rig", rig);

            // The fog, built EARLY because the tunnel and obstacle pools both take their draw
            // distances from it and both are wired below
            var atmosphereGo = new GameObject("CaveAtmosphere");
            _atmosphere = atmosphereGo.AddComponent<CaveAtmosphere>();
            SetFloat(_atmosphere, "sightLimit", SightLimit);
            SetFloat(_atmosphere, "drawMargin", SightMargin);

            // Tunnel
            var tunnelGo = new GameObject("Tunnel");
            var tunnel = tunnelGo.AddComponent<TunnelBuilder>();
            SetRef(tunnel, "cart", cart);
            SetRef(tunnel, "track", track);
            SetRef(tunnel, "ringPrefab", ring);
            SetRef(tunnel, "railPrefab", BuildRailPrefab());
            SetRef(tunnel, "framePrefab", BuildFramePrefab());

            // Pool spacing comes from what the pieces MEASURE, not from what they were assumed to
            // be. Both prefabs are built above, so the measurements exist by now
            SetFloat(tunnel, "ringSpacing", _measuredRingLength);
            SetFloat(tunnel, "railSpacing", _measuredRailLength);

            // The fog owns the sight limit and the pools read it at Start, so this is a reference
            // rather than three baked copies. Behind matters as much as ahead: this is VR, and
            // turning round to watch the tunnel delete itself was half the original complaint
            SetRef(tunnel, "atmosphere", _atmosphere);

            Debug.Log("[CCCartBuilder] Measured spacing: ring " + _measuredRingLength.ToString("0.00") +
                      " m (was " + RingLength + "), rail " + _measuredRailLength.ToString("0.00") +
                      " m (was " + RailSpacing + "). Mismatches here were the wall seams and the " +
                      "doubled track.");

            // Pickaxe waits in the cart. Taking it is what starts the ride, and either hand can
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
                // Deliberately NOT the cavern roof — see CrystalDropHeight
                SetFloat(spawner, "ceilingHeight", CrystalDropHeight);
                SetFloat(spawner, "swingHeight", SwingHeight);
                SetFloat(spawner, "lateralSpread", LateralSpread);

                Debug.Log("[CCCartBuilder] Spawner repointed at the track; crystals drop from " +
                          CrystalDropHeight + " m, independent of the " + CeilingHeight + " m roof.");
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

            // Environmental hazards, pooled along the track like the tunnel itself
            TrackObstacle leanLeft, leanRight;
            var duck = BuildObstaclePrefabs(out leanLeft, out leanRight);

            var obstacleGo = new GameObject("Obstacles");

            // Measured across the countdown, so a duck asks the same MOVEMENT of every player rather
            // than the same absolute height
            var calibration = obstacleGo.AddComponent<PlayerHeightCalibration>();
            SetRef(calibration, "cart", cart);

            // So it does not start sampling until the player has the pickaxe. Reaching for it would
            // otherwise be measured as standing height
            if (game != null) SetRef(calibration, "game", game);

            var obstacles = obstacleGo.AddComponent<TrackObstacles>();
            SetRef(obstacles, "calibration", calibration);
            SetRef(obstacles, "cart", cart);
            SetRef(obstacles, "track", track);
            if (game != null) SetRef(obstacles, "game", game);
            SetRef(obstacles, "duckPrefab", duck);
            SetRef(obstacles, "leanLeftPrefab", leanLeft);
            SetRef(obstacles, "leanRightPrefab", leanRight);
            SetRef(obstacles, "atmosphere", _atmosphere);

            // Desktop dodge testing. The emulator gives a camera you cannot crouch, so without this
            // the duck and lean obstacles cannot be tested at all outside a headset
            var dodge = obstacleGo.AddComponent<KeyboardTestDodge>();
            if (rig != null) SetRef(dodge, "rig", rig);
            SetDodgeTestExecutionOrder();

            // Decides WHERE every item goes, ahead of the cart, and keeps them out of the obstacle
            // sections. Built after the obstacles because it replays their sequence to find them
            var directorGo = new GameObject("SpawnDirector");
            var director = directorGo.AddComponent<SpawnDirector>();
            SetRef(director, "obstacles", obstacles);
            SetArray(director, "patterns", BuildSpawnPatterns());

            if (spawner != null)
            {
                SetRef(spawner, "director", director);
                Debug.Log("[CCCartBuilder] Spawner is now schedule driven. Item positions come from " +
                          "SpawnDirector; the old per-tick rolls are the fallback if it is unassigned.");
            }

            BuildEffectHud(game);
            BuildPerfHud();

            // Controller vibration on a hit. BatSwinger finds this through HapticPulse.Instance, so
            // it just has to exist somewhere in the scene
            if (Object.FindObjectOfType<HapticPulse>() == null)
            {
                new GameObject("Haptics").AddComponent<HapticPulse>();
                Debug.Log("[CCCartBuilder] Haptics added. NOTE, verified to compile and expected to " +
                          "work in a standalone APK, but the Liminal shell owns the OVR session in a " +
                          "hosted .limapp, recheck there before relying on it.");
            }

            // The old touch collectors are superseded by the bat.
            foreach (var hc in Object.FindObjectsOfType<HandCollector>())
                hc.gameObject.SetActive(false);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, TargetScene);

            Debug.Log("[CCCartBuilder] Built " + TargetScene +
                      "\n  procedural track + cart + pooled tunnel + bat (" + (bat != null ? bat.name : "none") + ")" +
                      "\n  " + migrated + " prefab(s) migrated to FallingMover" +
                      "\n  cavern " + (TunnelHalfWidth * 2f) + " m wide x " + CeilingHeight + " m tall" +
                      "\n  CrystalCatch.unity NOT modified.");
        }

        [MenuItem("Crystal Catch/Bake Tunnel Along Full Track")]
        public static void BakeTunnel()
        {
            ClearBakedTunnel();

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

            // Only generate if there is nothing baked. Regenerating here would silently throw away
            // any hand editing done through TrackPath's scene handles
            if (!track.IsGenerated)
            {
                track.Generate();
                EditorUtility.SetDirty(track);
            }

            var root = new GameObject(TunnelBuilder.AuthoringRootName);

            // Unity strips EditorOnly objects and their children at build time, so a baked tunnel
            // can be committed and saved without ever reaching the headset
            root.tag = "EditorOnly";

            // Rail and frames are pooled at their own spacings at runtime, so the bake has to match
            // or the authored view would show a tunnel the game does not actually have
            var rail = AssetDatabase.LoadAssetAtPath<GameObject>(RailPrefab);

            int count = Mathf.FloorToInt(track.Length / RingLength);

            try
            {
                for (int i = 0; i < count; i++)
                {
                    if (i % 64 == 0)
                    {
                        EditorUtility.DisplayProgressBar("Baking tunnel",
                                                         "Section " + i + " of " + count,
                                                         i / (float)count);
                    }

                    float d = i * RingLength;
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(ring, root.transform);
                    go.name = "Section_" + i.ToString("0000");
                    go.transform.position = track.PositionAt(d);
                    go.transform.rotation = track.RotationAt(d, true);
                }

                if (rail != null)
                {
                    int rails = Mathf.FloorToInt(track.Length / RailSpacing);
                    for (int i = 0; i < rails; i++)
                    {
                        float d = i * RailSpacing;
                        var go = (GameObject)PrefabUtility.InstantiatePrefab(rail, root.transform);
                        go.name = "Rail_" + i.ToString("0000");
                        go.transform.position = track.PositionAt(d);
                        go.transform.rotation = track.RotationAt(d, true);
                    }
                }

                var frame = AssetDatabase.LoadAssetAtPath<GameObject>(FramePrefab);
                if (frame != null)
                {
                    int frames = Mathf.FloorToInt(track.Length / FrameSpacing);
                    for (int i = 0; i < frames; i++)
                    {
                        float d = i * FrameSpacing;
                        var go = (GameObject)PrefabUtility.InstantiatePrefab(frame, root.transform);
                        go.name = "Frame_" + i.ToString("0000");
                        go.transform.position = track.PositionAt(d);
                        go.transform.rotation = track.RotationAt(d, true);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            EditorSceneManager.MarkSceneDirty(root.scene);

            Debug.Log("[CCCartBuilder] Baked " + count + " tunnel sections over " +
                      track.Length.ToString("0") + " m.\n" +
                      "  Each section is a prefab instance: select one and edit it, or unpack it to " +
                      "change that stretch on its own.\n" +
                      "  EditorOnly, so builds strip it, and TunnelBuilder hides it on Play, the " +
                      "runtime tunnel is still pooled rings, so hand edits here are look dev only " +
                      "until we feed authored sections back into TunnelBuilder.\n" +
                      "  Re-bake after changing the track shape, the sections do not follow it.");
        }

        [MenuItem("Crystal Catch/Clear Baked Tunnel")]
        public static void ClearBakedTunnel()
        {
            var existing = GameObject.Find(TunnelBuilder.AuthoringRootName);
            while (existing != null)
            {
                Object.DestroyImmediate(existing);
                existing = GameObject.Find(TunnelBuilder.AuthoringRootName);
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

            // It starts in the holster, not in a hand. The pickup below is what hands it over, and
            // handing it over is what starts the cart
            SetBool(swinger, "startHeld", false);

            var pickup = root.AddComponent<PickaxePickup>();
            SetRef(pickup, "pickaxe", swinger);
            if (game != null) SetRef(pickup, "game", game);

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

        private static Transform BuildCartBody(Transform parent)
        {
            // A cockpit reference frame is the biggest comfort win available in vehicular VR, a
            // stable visual anchor in the lower field of view
            var mat = GetOrCreateUnlit("CartGrey", new Color(0.30f, 0.30f, 0.34f));

            var body = new GameObject("CartBody").transform;
            body.SetParent(parent, false);

            MakeBox(body, "Cart_Floor", new Vector3(0f, -0.05f, 0f), new Vector3(1.4f, 0.1f, 2.0f), mat);
            MakeBox(body, "Cart_WallL", new Vector3(-0.7f, 0.4f, 0f), new Vector3(0.1f, 0.9f, 2.0f), mat);
            MakeBox(body, "Cart_WallR", new Vector3(0.7f, 0.4f, 0f), new Vector3(0.1f, 0.9f, 2.0f), mat);
            MakeBox(body, "Cart_Front", new Vector3(0f, 0.4f, 1.0f), new Vector3(1.4f, 0.9f, 0.1f), mat);
            MakeBox(body, "Cart_Back", new Vector3(0f, 0.4f, -1.0f), new Vector3(1.4f, 0.9f, 0.1f), mat);

            return body;
        }

        private static void BuildPerfHud()
        {
            var existing = Object.FindObjectOfType<PerfReadout>();
            if (existing != null) Object.DestroyImmediate(existing.gameObject);

            var go = new GameObject("PerfHUD", typeof(Canvas));

            // World space, NOT Screen Space Overlay, an overlay canvas renders to the flat screen
            // and never reaches either eye, so in a headset it is simply invisible
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(600f, 260f);
            rect.localScale = Vector3.one * EffectHudScale;

            var lockedTo = go.AddComponent<HeadLockedHud>();
            SetFloat(lockedTo, "distance", 1.6f);

            // Down and to the left, readable with a glance, clear of the pickaxe and of the crystals
            SetVector(lockedTo, "localOffset", new Vector3(-0.55f, -0.42f, 0f));

            var textGo = new GameObject("PerfText", typeof(RectTransform));
            textGo.transform.SetParent(go.transform, false);

            var textRect = (RectTransform)textGo.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            var label = textGo.AddComponent<TextMeshProUGUI>();
            label.fontSize = 72f;
            label.alignment = TextAlignmentOptions.TopLeft;
            label.text = "-- fps";
            label.color = Color.white;

            // Reads against both the dark cave and a bright crystal burst passing behind it
            label.fontStyle = FontStyles.Bold;
            label.outlineWidth = 0.2f;
            label.outlineColor = new Color32(0, 0, 0, 255);

            var readout = go.AddComponent<PerfReadout>();
            SetRef(readout, "text", label);

            Debug.Log("[CCCartBuilder] In-scene perf readout built (fps/ms/worst frame/over budget). " +
                      "Drawn in the scene, so it WILL appear in headset recordings.");
        }

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
            if (game != null)
            {
                var keys = game.GetComponent<KeyboardTestEffects>();
                if (keys == null) keys = game.gameObject.AddComponent<KeyboardTestEffects>();

                // Was being added but never wired, so every one of keys 1-6 threw a null reference
                // the moment it was pressed
                SetRef(keys, "game", game);
            }

            Debug.Log("[CCCartBuilder] Head locked effect HUD built, power ups top left, hazards top right.");
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
            CombineInto(root.transform, loose.transform, RingMeshes);
            Object.DestroyImmediate(loose);

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, RingPrefab);
            Object.DestroyImmediate(root);
            return prefab;
        }

        private static float _measuredRingLength = RingLength;
        private static float _measuredRailLength = RailSpacing;

        // The scene's single source of truth for how far the player can see
        private static CaveAtmosphere _atmosphere;
        private static void BuildRingPieces(Transform parent)
        {
            var wallRot = Quaternion.Euler(0f, 90f, 0f);

            Vector3 wall = RotatedSize("Wall/Wall_Caves_A", wallRot);
            Vector3 floor = KitBounds("Ground/Ground_Mines_A").size;
            Vector3 roof = KitBounds("Ground/Ground_Cave_A").size;

            float ringLength = Step(wall.z);
            _measuredRingLength = ringLength;

            float halfLength = ringLength * 0.5f;
            float halfWidth = TunnelHalfWidth;

            // Floor and ceiling tile in BOTH axes now. They only tiled across before, on the
            // assumption that one tile's depth matched the ring, which it does not
            TileSurface(parent, "Ground/Ground_Mines_A", "Floor", Quaternion.identity,
                        floor, -halfWidth, halfWidth, -halfLength, halfLength,
                        FloorY - FloorTileTop);

            TileSurface(parent, "Ground/Ground_Cave_A", "Ceiling", Quaternion.Euler(180f, 0f, 0f),
                        roof, -halfWidth, halfWidth, -halfLength, halfLength,
                        CeilingHeight);

            // Enough tiers to reach the roof, from the panel's measured height rather than a constant,
            // stepped short of it so each tier overlaps the one below
            float panelHeight = Step(wall.y);
            int tiers = Mathf.Max(1, Mathf.CeilToInt((CeilingHeight - FloorY) / panelHeight));

            for (int tier = 0; tier < tiers; tier++)
            {
                float y = FloorY + tier * panelHeight;

                for (int row = 0; row < SurfaceRows; row++)
                {
                    float x = halfWidth + row * SeamFillDepth;
                    float z = -halfLength + row * halfLength;

                    PlaceAligned(parent, "Wall/Wall_Caves_A", "WallL_" + tier + "_" + row,
                                 new Vector3(-x - wall.x, y, z),
                                 Quaternion.Euler(0f, -90f, 0f), Vector3.one);

                    PlaceAligned(parent, "Wall/Wall_Caves_A", "WallR_" + tier + "_" + row,
                                 new Vector3(x, y, z), wallRot, Vector3.one);
                }
            }
        }

        /// Tiles a floor or ceiling piece over a rectangle in the XZ plane, butting each tile against
        /// the last from its measured footprint
        private static void TileSurface(Transform parent, string kitPath, string name,
                                        Quaternion rot, Vector3 size,
                                        float minX, float maxX, float minZ, float maxZ, float y)
        {
            float stepX = Step(size.x);
            float stepZ = Step(size.z);

            int across = Mathf.CeilToInt((maxX - minX) / stepX);
            int along = Mathf.CeilToInt((maxZ - minZ) / stepZ);

            for (int i = 0; i < across; i++)
            {
                for (int j = 0; j < along; j++)
                {
                    PlaceAligned(parent, kitPath, name + "_" + i + "_" + j,
                                 new Vector3(minX + i * stepX, y, minZ + j * stepZ),
                                 rot, Vector3.one);
                }
            }
        }

        // Measured once per piece per build. Instantiating a prefab to read its bounds is cheap, but
        // the shaft asks for the same handful of pieces dozens of times
        private static readonly System.Collections.Generic.Dictionary<string, Bounds> KitBoundsCache =
            new System.Collections.Generic.Dictionary<string, Bounds>();

        private static Bounds KitBounds(string kitPath)
        {
            Bounds cached;
            if (KitBoundsCache.TryGetValue(kitPath, out cached)) return cached;

            var result = new Bounds(Vector3.zero, Vector3.one);

            string path = KitPrefabs + kitPath + ".prefab";
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (source == null)
            {
                Debug.LogWarning("[CCCartBuilder] Cannot measure missing kit piece: " + path);
                KitBoundsCache[kitPath] = result;
                return result;
            }

            var probe = Object.Instantiate(source);
            probe.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            probe.transform.localScale = Vector3.one;

            var renderers = probe.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                // Renderer.bounds is world space, and the probe sits at the origin unrotated, so
                // world and local are the same thing here
                result = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) result.Encapsulate(renderers[i].bounds);
            }
            else
            {
                Debug.LogWarning("[CCCartBuilder] " + path + " has no renderers to measure.");
            }

            Object.DestroyImmediate(probe);
            KitBoundsCache[kitPath] = result;
            return result;
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

        private static GameObject BuildFramePrefab()
        {
            EnsureModelReadable(KitModel);

            var root = new GameObject("TunnelFrame");
            var loose = new GameObject("Loose");
            loose.transform.SetParent(root.transform, false);

            float wallX = TunnelHalfWidth - WallFrameInset;

            for (int tier = 0; tier < WallPostTiers; tier++)
            {
                float y = FloorY + tier * PostHeight;
                PlaceKit(loose.transform, "Posts/Post_A", "PostL_" + tier,
                         new Vector3(-wallX, y, 0f), Quaternion.identity);
                PlaceKit(loose.transform, "Posts/Post_A", "PostR_" + tier,
                         new Vector3(wallX, y, 0f), Quaternion.identity);
            }

            // Five 4 m beams overlapping into one 20 m span, wall to wall
            float beamY = FloorY + WallPostTiers * PostHeight + BeamHalfThickness;
            for (int i = -2; i <= 2; i++)
                PlaceKit(loose.transform, "Posts/Beam_A", "Beam_" + (i + 2),
                         new Vector3(i * 4f, beamY, 0f), Quaternion.identity);

            CombineInto(root.transform, loose.transform, FrameMeshes);
            Object.DestroyImmediate(loose);

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, FramePrefab);
            Object.DestroyImmediate(root);
            return prefab;
        }

        private static GameObject BuildRailPrefab()
        {
            EnsureModelReadable(KitModel);

            var root = new GameObject("TunnelRail");
            var loose = new GameObject("Loose");
            loose.transform.SetParent(root.transform, false);

            var railRot = Quaternion.Euler(0f, 90f, 0f);

            // Same yaw as the walls, so the piece's own X length runs down the track and its gauge
            // sits across it
            PlaceKit(loose.transform, "Rails/Rail_B", "Rail",
                     new Vector3(0f, FloorY, 0f), railRot);

            var rail = loose.transform.Find("Rail");
            if (rail != null)
            {
                var renderers = rail.GetComponentsInChildren<Renderer>();
                if (renderers.Length > 0)
                {
                    var b = renderers[0].bounds;
                    for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);

                    _measuredRailLength = Mathf.Max(0.25f, b.size.z);

                    // Only Z, the gauge and the ride height are already where they belong
                    rail.localPosition -= new Vector3(0f, 0f, b.center.z);
                }
            }

            CombineInto(root.transform, loose.transform, RailMeshes);
            Object.DestroyImmediate(loose);

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, RailPrefab);
            Object.DestroyImmediate(root);
            return prefab;
        }

        private static Object[] BuildSpawnPatterns()
        {
            Directory.CreateDirectory(PatternDir);

            var made = new System.Collections.Generic.List<Object>();

            // Reach across the body, three times, early enough to teach the movement before it costs
            made.Add(MakePattern("Sweep", "Sweep across", 0f, 0.5f, new[]
            {
                Slot(SpawnSlotKind.Crystal, 0f, -0.85f),
                Slot(SpawnSlotKind.Crystal, 8f, 0f),
                Slot(SpawnSlotKind.Crystal, 16f, 0.85f),
            }));

            // Stay on one side and keep swinging. The reward for reading it early is a clean run
            made.Add(MakePattern("Run", "One-side run", 0f, 0.7f, new[]
            {
                Slot(SpawnSlotKind.Crystal, 0f, 0.6f),
                Slot(SpawnSlotKind.Crystal, 7f, 0.7f),
                Slot(SpawnSlotKind.Crystal, 14f, 0.6f),
                Slot(SpawnSlotKind.PowerUp, 22f, 0.65f),
            }));

            // The actual decision: a gold sitting between two hazards. Taking it is a choice, and
            // leaving it is a legitimate one
            made.Add(MakePatternWithColour("Gauntlet", "Gold between hazards", 0.25f, 1f, new[]
            {
                Slot(SpawnSlotKind.Hazard, 0f, -0.7f),
                Slot(SpawnSlotKind.Crystal, 9f, 0f, true, CrystalColour.Gold),
                Slot(SpawnSlotKind.Hazard, 18f, 0.7f),
            }));

            // Reward on one side, risk on the other, at the SAME distance. There is no swing that
            // takes both, which is the entire point
            made.Add(MakePattern("Fork", "Reward or risk", 0.2f, 1f, new[]
            {
                Slot(SpawnSlotKind.PowerUp, 0f, -0.8f),
                Slot(SpawnSlotKind.Hazard, 0f, 0.8f),
            }));

            // Tightening spacing. Late only: it is the one pattern that is genuinely fast
            made.Add(MakePattern("Crescendo", "Tightening run", 0.55f, 1f, new[]
            {
                Slot(SpawnSlotKind.Crystal, 0f, -0.5f),
                Slot(SpawnSlotKind.Crystal, 9f, 0.4f),
                Slot(SpawnSlotKind.Crystal, 16f, -0.35f),
                Slot(SpawnSlotKind.Crystal, 22f, 0.3f),
                Slot(SpawnSlotKind.Crystal, 27f, 0f),
            }));

            AssetDatabase.SaveAssets();
            return made.ToArray();
        }

        private static SpawnPattern.Slot Slot(SpawnSlotKind kind, float along, float lateral,
                                              bool forceColour = false,
                                              CrystalColour colour = CrystalColour.Blue)
        {
            return new SpawnPattern.Slot
            {
                kind = kind,
                alongTrack = along,
                lateral = lateral,
                forceColour = forceColour,
                colour = colour
            };
        }

        private static SpawnPattern MakePattern(string file, string label, float minD, float maxD,
                                                SpawnPattern.Slot[] slots)
        {
            return MakePatternWithColour(file, label, minD, maxD, slots);
        }

        private static SpawnPattern MakePatternWithColour(string file, string label,
                                                          float minD, float maxD,
                                                          SpawnPattern.Slot[] slots)
        {
            string path = PatternDir + "/" + file + ".asset";

            var asset = AssetDatabase.LoadAssetAtPath<SpawnPattern>(path);
            bool isNew = asset == null;
            if (isNew) asset = ScriptableObject.CreateInstance<SpawnPattern>();

            asset.label = label;
            asset.minDifficulty = minD;
            asset.maxDifficulty = maxD;
            asset.slots = slots;

            if (isNew) AssetDatabase.CreateAsset(asset, path);
            else EditorUtility.SetDirty(asset);

            return asset;
        }

        private static TrackObstacle BuildObstaclePrefabs(out TrackObstacle leanLeft,
                                                          out TrackObstacle leanRight)
        {
            EnsureModelReadable(KitModel);

            var duck = BuildObstacle("Obstacle_DuckBeam", TrackObstacle.Kind.DuckBeam);
            leanLeft = BuildObstacle("Obstacle_LeanLeft", TrackObstacle.Kind.LeanLeft);
            leanRight = BuildObstacle("Obstacle_LeanRight", TrackObstacle.Kind.LeanRight);
            return duck;
        }

        private static TrackObstacle BuildObstacle(string name, TrackObstacle.Kind kind)
        {
            var root = new GameObject(name);
            var loose = new GameObject("Loose");
            loose.transform.SetParent(root.transform, false);

            // Order matters only for readability, everything is combined into one mesh set anyway
            BuildShaftWalls(loose.transform);
            BuildShaftRoof(loose.transform);
            BuildShaftTimbers(loose.transform);
            BuildBulkhead(loose.transform, -1f);   // Approach face, sealed except for the shaft mouth
            BuildBulkhead(loose.transform, 1f);    // Exit face
            BuildTaper(loose.transform, -1f);      // Rubble banked against the approach face
            BuildTaper(loose.transform, 1f);

            Vector3 dangerCentre;
            Vector3 dangerHalf;

            if (kind == TrackObstacle.Kind.DuckBeam)
                BuildDuckGate(loose.transform, out dangerCentre, out dangerHalf);
            else
                BuildLeanGate(loose.transform, kind, out dangerCentre, out dangerHalf);

            CombineInto(root.transform, loose.transform, PrefabDir + "/" + name + "Meshes.asset");
            Object.DestroyImmediate(loose);

            var obstacle = root.AddComponent<TrackObstacle>();
            SetEnum(obstacle, "kind", (int)kind);
            SetVector(obstacle, "dangerCentre", dangerCentre);
            SetVector(obstacle, "dangerHalfExtents", dangerHalf);
            SetFloat(obstacle, "sectionHalfLength", ShaftLength * 0.5f + TaperLength);

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabDir + "/" + name + ".prefab");
            Object.DestroyImmediate(root);

            return prefab != null ? prefab.GetComponent<TrackObstacle>() : null;
        }

        /// Both walls of the shaft, tiled from the panel's MEASURED size so the courses meet rather
        /// than leaving a seam at whatever height a hardcoded guess happened to land on
        private static void BuildShaftWalls(Transform parent)
        {
            const string panel = "Wall/Wall_Mines_A";

            for (int s = 0; s < 2; s++)
            {
                float side = s == 0 ? -1f : 1f;

                // Panels face inward, so each side is turned to look at the track
                var rot = Quaternion.Euler(0f, side < 0f ? 90f : -90f, 0f);
                Vector3 size = RotatedSize(panel, rot);

                float stepZ = Step(size.z);
                float stepY = Step(size.y);

                int columns = Mathf.Max(1, Mathf.CeilToInt(ShaftLength / stepZ));
                int rows = Mathf.Max(1, Mathf.CeilToInt(ShaftHeight / stepY));

                // Aligned by min corner: the panel's own thickness sits OUTSIDE the shaft width
                // either way, so the clear corridor is what ShaftHalfWidth says it is
                float x = side < 0f ? -ShaftHalfWidth - size.x : ShaftHalfWidth;

                for (int c = 0; c < columns; c++)
                {
                    for (int r = 0; r < rows; r++)
                    {
                        PlaceAligned(parent, panel,
                                     "ShaftWall_" + (side < 0f ? "L" : "R") + c + "_" + r,
                                     new Vector3(x,
                                                 FloorY + r * stepY,
                                                 -ShaftLength * 0.5f + c * stepZ),
                                     rot, Vector3.one);
                    }
                }
            }
        }

        /// A boarded roof over the shaft. This is what actually stops the player seeing over the top
        /// of the gate and out into the open cavern beyond, which is the whole point of the pinch
        private static void BuildShaftRoof(Transform parent)
        {
            const string plank = "WOodPlatforms/Panel_Wood_A";
            Vector3 size = RotatedSize(plank, Quaternion.identity);

            float plankWidth = Step(size.x);
            float plankDepth = Step(size.z);

            int across = Mathf.Max(1, Mathf.CeilToInt(ShaftHalfWidth * 2f / plankWidth));
            int along = Mathf.Max(1, Mathf.CeilToInt(ShaftLength / plankDepth));

            for (int a = 0; a < across; a++)
            {
                for (int b = 0; b < along; b++)
                {
                    PlaceAligned(parent, plank, "ShaftRoof_" + a + "_" + b,
                                 new Vector3(-ShaftHalfWidth + a * plankWidth,
                                             FloorY + ShaftHeight,
                                             -ShaftLength * 0.5f + b * plankDepth),
                                 Quaternion.identity, Vector3.one);
                }
            }
        }

        private static void BuildShaftTimbers(Transform parent)
        {
            const string post = "Posts/Post_Reinforced_A";
            const string beam = "Posts/Beam_A";

            Bounds postSize = KitBounds(post);
            Bounds beamSize = KitBounds(beam);

            float beamLength = Mathf.Max(0.5f, beamSize.size.x);
            int beamsAcross = Mathf.Max(1, Mathf.CeilToInt(ShaftHalfWidth * 2f / beamLength));
            float beamStartX = -ShaftHalfWidth + beamLength * 0.5f;

            // Three sets: mouth, gate, and exit. Enough to read as structure, few enough that they
            // do not become a picket fence strobing past at speed (the ADR 0014 frame-spacing trap)
            for (int i = 0; i < 3; i++)
            {
                float z = -ShaftLength * 0.5f + ShaftLength * 0.5f * i;

                PlaceKit(parent, post, "Timber_PostL_" + i,
                         new Vector3(-ShaftHalfWidth + 0.15f, FloorY, z), Quaternion.identity);
                PlaceKit(parent, post, "Timber_PostR_" + i,
                         new Vector3(ShaftHalfWidth - 0.15f, FloorY, z), Quaternion.identity);

                float beamY = FloorY + Mathf.Min(postSize.size.y, ShaftHeight) - beamSize.size.y * 0.5f;

                for (int b = 0; b < beamsAcross; b++)
                {
                    PlaceKit(parent, beam, "Timber_Beam_" + i + "_" + b,
                             new Vector3(beamStartX + b * beamLength, beamY, z), Quaternion.identity);
                }
            }
        }

        private static void BuildBulkhead(Transform parent, float direction)
        {
            const string face = "Wall/Wall_Mines_A";

            string label = direction < 0f ? "In" : "Out";
            float z = direction * ShaftLength * 0.5f;

            // Faces the player, the approach bulkhead looks back down the track, the exit one ahead
            var rot = Quaternion.Euler(0f, direction < 0f ? 180f : 0f, 0f);

            float mouthTop = FloorY + ShaftHeight;

            // Either side of the mouth, full height
            FillRect(parent, face, "Bulkhead" + label + "_L",
                     -TunnelHalfWidth, -ShaftHalfWidth, FloorY, CeilingHeight, z, rot);

            FillRect(parent, face, "Bulkhead" + label + "_R",
                     ShaftHalfWidth, TunnelHalfWidth, FloorY, CeilingHeight, z, rot);

            // And the lintel above it, so the mouth is a hole rather than a slot open to the roof
            FillRect(parent, face, "Bulkhead" + label + "_Top",
                     -ShaftHalfWidth, ShaftHalfWidth, mouthTop, CeilingHeight, z, rot);
        }

        private static void BuildTaper(Transform parent, float direction)
        {
            const string mound = "Ground/Ground_Mound_A";
            const string rocks = "Wall/Cave_Rocks_A";

            string label = direction < 0f ? "In" : "Out";
            const int steps = 4;

            for (int i = 0; i < steps; i++)
            {
                // t = 0 at the shaft mouth, 1 out at the cavern wall
                float t = (i + 1f) / steps;
                float z = direction * (ShaftLength * 0.5f + t * TaperLength);
                float x = Mathf.Lerp(ShaftHalfWidth, TunnelHalfWidth - 1f, t);

                // Mounds shrink as they approach the shaft, so the funnel reads as continuous
                float scale = Mathf.Lerp(1.4f, 0.8f, t);

                for (int s = 0; s < 2; s++)
                {
                    float side = s == 0 ? -1f : 1f;

                    PlaceScaledKit(parent, mound, "Taper" + label + "_Mound_" + i + "_" + s,
                                   new Vector3(side * x, FloorY, z),
                                   Quaternion.Euler(0f, (i * 57f + s * 130f) % 360f, 0f),
                                   Vector3.one * scale);

                    // Rock chunks piled against the shaft mouth itself, hiding the seam where the
                    // built shaft meets the pooled cavern wall
                    if (i >= steps - 2) continue;

                    PlaceScaledKit(parent, rocks, "Taper" + label + "_Rock_" + i + "_" + s,
                                   new Vector3(side * (x - 0.8f), FloorY + 0.4f + i * 0.5f, z),
                                   Quaternion.Euler(0f, (i * 93f + s * 41f) % 360f, 0f),
                                   Vector3.one * Mathf.Lerp(1.1f, 0.7f, t));
                }
            }
        }

        /// Boards the shaft up from head height to the roof, leaving only a low gap. The player is
        /// looking at a wall of collapsed timber with a hole at knee to chest height
        private static void BuildDuckGate(Transform parent, out Vector3 dangerCentre,
                                          out Vector3 dangerHalf)
        {
            const string plank = "WOodPlatforms/Panel_Wood_B";
            const string beam = "Posts/Beam_A";

            float gapTop = FloorY + DuckBeamHeight;
            float boardedHeight = (FloorY + ShaftHeight) - gapTop;

            // Boarding stands upright across the shaft, so it is turned to face down the track
            var boardRot = Quaternion.Euler(0f, 90f, 0f);
            FillRect(parent, plank, "DuckBoard",
                     -ShaftHalfWidth, ShaftHalfWidth, gapTop, gapTop + boardedHeight, 0f, boardRot);

            // The lip you actually judge the duck against. A boarded wall has no clear edge; a beam
            // across the bottom of it does
            Bounds beamSize = KitBounds(beam);
            float beamLength = Mathf.Max(0.5f, beamSize.size.x);
            int beamsAcross = Mathf.Max(1, Mathf.CeilToInt(ShaftHalfWidth * 2f / beamLength));
            float beamStartX = -ShaftHalfWidth + beamLength * 0.5f;

            for (int b = 0; b < beamsAcross; b++)
            {
                PlaceKit(parent, beam, "DuckLip_" + b,
                         new Vector3(beamStartX + b * beamLength, gapTop, 0f), Quaternion.identity);
            }

            // Everything from the lip up is a head that did not get down in time. Full shaft width,
            // because there is no longer anywhere to the side to escape to
            float halfHeight = boardedHeight * 0.5f;
            dangerCentre = new Vector3(0f, gapTop + halfHeight, 0f);
            dangerHalf = new Vector3(ShaftHalfWidth, halfHeight, BlockerHalfDepth);
        }

        private static void BuildLeanGate(Transform parent, TrackObstacle.Kind kind,
                                          out Vector3 dangerCentre, out Vector3 dangerHalf)
        {
            // LeanLeft means the player leans LEFT, so the rock fills the RIGHT of the shaft
            float side = kind == TrackObstacle.Kind.LeanLeft ? 1f : -1f;

            const string rocks = "Wall/Cave_Rocks_B";
            const string wallRocks = "Wall/Cave_Wall_Rocks_A";

            Bounds rockSize = KitBounds(rocks);
            float rockWidth = Mathf.Max(0.5f, rockSize.size.x);
            float rockHeight = Mathf.Max(0.5f, rockSize.size.y);

            // The blocked span runs from the far wall in to the edge of the clear gap
            float innerEdge = side * LeanClearHalfWidth;
            float outerEdge = side * ShaftHalfWidth;
            float blockedWidth = Mathf.Abs(outerEdge - innerEdge);

            const float Overlap = 0.7f;
            rockWidth *= Overlap;
            rockHeight *= Overlap;

            int across = Mathf.Max(1, Mathf.CeilToInt(blockedWidth / rockWidth));
            int up = Mathf.Max(1, Mathf.CeilToInt(ShaftHeight / rockHeight));

            for (int a = 0; a < across; a++)
            {
                float t = (a + 0.5f) / across;
                float x = Mathf.Lerp(innerEdge, outerEdge, t);

                for (int u = 0; u < up; u++)
                {
                    // Rotated per piece so a stack of the same mesh does not read as a brick wall
                    PlaceScaledKit(parent, rocks, "LeanRock_" + a + "_" + u,
                                   new Vector3(x, FloorY + u * rockHeight + rockHeight * 0.5f, 0f),
                                   Quaternion.Euler(0f, (a * 71f + u * 113f) % 360f, 0f),
                                   Vector3.one * Mathf.Lerp(1.15f, 0.9f, t));
                }
            }

            // A slab hanging over the gap from the blocked side. This is what makes standing upright
            // in the gap wrong: the hole is not just narrow, it is also low on the rock side
            PlaceKit(parent, wallRocks, "LeanOverhang",
                     new Vector3(innerEdge, FloorY + ShaftHeight - 1.2f, 0f),
                     Quaternion.Euler(0f, side * 90f, 0f));

            float halfWidth = blockedWidth * 0.5f;
            dangerCentre = new Vector3(Mathf.Lerp(innerEdge, outerEdge, 0.5f),
                                       FloorY + ShaftHeight * 0.5f, 0f);
            dangerHalf = new Vector3(halfWidth, ShaftHeight * 0.5f, BlockerHalfDepth);
        }

        private static void PlaceScaledKit(Transform parent, string kitPath, string name,
                                            Vector3 localPos, Quaternion localRot, Vector3 scale)
        {
            PlaceKit(parent, kitPath, name, localPos, localRot);

            var placed = parent.Find(name);
            if (placed != null) placed.localScale = scale;
        }

        private static void PlaceAligned(Transform parent, string kitPath, string name,
                                         Vector3 targetMin, Quaternion rot, Vector3 scale)
        {
            PlaceKit(parent, kitPath, name, Vector3.zero, rot);

            var placed = parent.Find(name);
            if (placed == null) return;

            placed.localScale = scale;

            // The obstacle is assembled at the origin with an unrotated parent chain, so world and
            // local are the same frame here. Anything that changes that breaks this correction
            var renderers = placed.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) { placed.localPosition = targetMin; return; }

            var b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);

            placed.localPosition += targetMin - b.min;
        }

        /// Size of a kit piece after a rotation, for working out how many fit across a span. Only
        /// correct for axis-aligned rotations, which is all this builder uses
        private static Vector3 RotatedSize(string kitPath, Quaternion rot)
        {
            Vector3 s = rot * KitBounds(kitPath).size;
            return new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
        }

        private static void FillRect(Transform parent, string kitPath, string name,
                                     float minX, float maxX, float minY, float maxY,
                                     float z, Quaternion rot)
        {
            if (maxX - minX <= 0.01f || maxY - minY <= 0.01f) return;

            Vector3 size = RotatedSize(kitPath, rot);
            float stepX = Step(size.x);
            float stepY = Step(size.y);

            int columns = Mathf.CeilToInt((maxX - minX) / stepX);
            int rows = Mathf.CeilToInt((maxY - minY) / stepY);

            for (int c = 0; c < columns; c++)
                for (int r = 0; r < rows; r++)
                    PlaceAligned(parent, kitPath, name + "_" + c + "_" + r,
                                 new Vector3(minX + c * stepX, minY + r * stepY, z),
                                 rot, Vector3.one);
        }

        private static void CombineInto(Transform parent, Transform loose, string meshAssetPath)
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
                Debug.LogWarning("[CCCartBuilder] No kit geometry combined for " + meshAssetPath);
                return;
            }

            // Combined meshes have to live in an asset or the prefab points at nothing once the
            // build finishes. One file, one sub asset per material
            if (File.Exists(meshAssetPath)) AssetDatabase.DeleteAsset(meshAssetPath);
            bool created = false;

            foreach (var pair in byMaterial)
            {
                var combines = pair.Value.ToArray();

                int vertices = 0;
                for (int i = 0; i < combines.Length; i++)
                    if (combines[i].mesh != null) vertices += combines[i].mesh.vertexCount;

                var mesh = new Mesh();
                mesh.name = "Ring_" + pair.Key.name;

                if (vertices > 65535)
                {
                    // 32 bit indices cost memory and bandwidth on a mobile GPU, so this is a fallback
                    // that keeps the geometry correct rather than a default worth being casual about
                    mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

                    Debug.LogWarning("[CCCartBuilder] " + meshAssetPath + " / " + pair.Key.name +
                                     " combines to " + vertices + " vertices, over the 65535 16-bit " +
                                     "limit. Using 32-bit indices. On Quest 2 this is worth avoiding: " +
                                     "consider splitting the piece or using a lower-density kit mesh.");
                }

                mesh.CombineMeshes(combines, true, true);
                mesh.RecalculateBounds();

                if (!created)
                {
                    AssetDatabase.CreateAsset(mesh, meshAssetPath);
                    created = true;
                }
                else
                {
                    AssetDatabase.AddObjectToAsset(mesh, meshAssetPath);
                }

                var go = new GameObject(mesh.name);
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                StripLighting(go.AddComponent<MeshRenderer>(), pair.Key);
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[CCCartBuilder] " + meshAssetPath + " combined into " + byMaterial.Count +
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

        private static void SetDodgeTestExecutionOrder()
        {
            var script = MonoImporter.GetAllRuntimeMonoScripts();
            for (int i = 0; i < script.Length; i++)
            {
                if (script[i] == null || script[i].GetClass() != typeof(KeyboardTestDodge)) continue;
                if (MonoImporter.GetExecutionOrder(script[i]) == -100) return;

                MonoImporter.SetExecutionOrder(script[i], -100);
                return;
            }
        }

        private static void SetEnum(Object target, string field, int value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null) return;
            prop.enumValueIndex = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetVector(Object target, string field, Vector3 value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null) return;
            prop.vector3Value = value;
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
