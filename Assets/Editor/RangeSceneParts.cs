using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using TMPro;
using Liminal.SDK.VR.Avatars;
using IntuitiveDesigns.CrystalCatch;

namespace IntuitiveDesigns.ShootingRange.EditorTools
{
    public static class RangeSceneParts
    {
        private const int ProjectilePoolSize = 96;

        public class Managers
        {
            public RangeGame Game;
            public Arena Arena;
            public ComboTracker Combo;
            public PowerUps PowerUps;
            public SlowMotion SlowMotion;
            public GameObject Root;
        }

        /// The SDK example ships a floating demo sphere at head height, exactly where the range wants
        /// to be
        public static void StripDemoProps()
        {
            foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root.name != "Sphere") continue;

                Object.DestroyImmediate(root);
                Debug.Log("[RangeSceneParts] Removed the SDK example's demo sphere.");
            }
        }

        public static Managers BuildManagers(RangeBuildKit.Prefabs prefabs, RangeBuildKit.Effects fx,
                                             RangeBuildKit.Sounds sounds, bool withShatterPool)
        {
            var go = new GameObject("Managers");
            go.transform.position = RangeBuildKit.Origin;

            var game = go.AddComponent<RangeGame>();
            var combo = go.AddComponent<ComboTracker>();
            var arena = go.AddComponent<Arena>();
            var pool = go.AddComponent<ProjectilePool>();
            var impacts = go.AddComponent<ImpactFX>();
            go.AddComponent<HapticPulse>();
            var reset = go.AddComponent<KeyboardTestRange>();
            var slowMotion = go.AddComponent<SlowMotion>();
            var powerUps = go.AddComponent<PowerUps>();
            var rangeAudio = go.AddComponent<RangeAudio>();

            RangeBuildKit.SetRef(pool, "projectilePrefab", prefabs.Round.GetComponent<Projectile>());
            RangeBuildKit.SetInt(pool, "poolSize", ProjectilePoolSize);
            RangeBuildKit.SetRef(game, "arena", arena);
            RangeBuildKit.SetRef(game, "combo", combo);
            RangeBuildKit.SetRef(reset, "arena", arena);

            // Practice, not a session: it starts on Play and does not end under you mid-tune
            RangeBuildKit.SetBool(game, "requirePickupToStart", false);
            RangeBuildKit.SetBool(game, "endRoundOnClear", false);

            RangeBuildKit.SetRef(impacts, "impactPrefab", fx.Impact);
            RangeBuildKit.SetArray(impacts, "impactClips", sounds.WoodImpacts ?? new AudioClip[0]);

            RangeBuildKit.SetRef(powerUps, "game", game);
            RangeBuildKit.SetRef(powerUps, "combo", combo);
            RangeBuildKit.SetRef(powerUps, "slowMotion", slowMotion);

            RangeBuildKit.SetRef(rangeAudio, "game", game);
            RangeBuildKit.SetRef(rangeAudio, "countdownTickClip", First(sounds.Ding));
            RangeBuildKit.SetRef(rangeAudio, "roundStartClip", sounds.RoundStart);
            RangeBuildKit.SetRef(rangeAudio, "roundEndClip", sounds.RoundEnd);

            if (withShatterPool)
            {
                var shatter = go.AddComponent<ShatterPool>();
                RangeBuildKit.SetRef(shatter, "fragmentPrefab", prefabs.Fragment.GetComponent<Rigidbody>());
                RangeBuildKit.SetRef(shatter, "popPrefab", fx.TargetPop);
                RangeBuildKit.SetArray(shatter, "shatterClips", sounds.Ding ?? new AudioClip[0]);
            }

            return new Managers
            {
                Game = game, Arena = arena, Combo = combo,
                PowerUps = powerUps, SlowMotion = slowMotion, Root = go,
            };
        }

        public static PropStackBuilder BuildStacks(RangeBuildKit.Prefabs prefabs, int stacks,
                                                   float spacing, float shelfTop, float ahead)
        {
            var go = new GameObject("Stacks");
            go.transform.position = RangeBuildKit.Origin;

            var builder = go.AddComponent<PropStackBuilder>();

            var anchors = new Object[stacks];
            float span = (stacks - 1) * spacing;

            for (int i = 0; i < stacks; i++)
            {
                var anchor = new GameObject("Anchor " + i);
                anchor.transform.SetParent(go.transform, false);
                anchor.transform.position = RangeBuildKit.At(i * spacing - span * 0.5f,
                                                             shelfTop + RangeBuildKit.PropSize * 0.5f,
                                                             ahead);
                anchor.transform.rotation = RangeBuildKit.Facing();
                anchors[i] = anchor.transform;
            }

            RangeBuildKit.SetArray(builder, "anchors", anchors);
            RangeBuildKit.SetArray(builder, "propPrefabs",
                                   new Object[] { prefabs.Crate, prefabs.Can, prefabs.Bottle });
            RangeBuildKit.SetRef(builder, "capPrefab", prefabs.Target);

            // Inside the 1 cm default contact offset, so rows register contact without ever falling
            // into each other. A visible gap here is a visible collapse on Play
            RangeBuildKit.SetFloat(builder, "propSpacing", RangeBuildKit.PropSize + 0.01f);
            RangeBuildKit.SetFloat(builder, "rowHeight", RangeBuildKit.PropSize + 0.002f);

            return builder;
        }

        /// side is +1 for the right hand and -1 for the left, mirroring both the held pose and where
        /// the emulator parks the gun on screen
        public static Pistol BuildPistol(RangeBuildKit.Palette palette, int noShootLayer,
                                         RangeBuildKit.Effects fx, RangeBuildKit.Sounds sounds,
                                         string name, VRAvatarLimbType hand, float side)
        {
            var go = new GameObject(name);
            go.transform.position = RangeBuildKit.At(0.25f * side, 1.15f, 0.3f);
            go.transform.rotation = RangeBuildKit.Facing();

            var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "Visual";
            visual.transform.SetParent(go.transform, false);
            visual.transform.localPosition = new Vector3(0f, 0f, 0.06f);
            visual.transform.localScale = new Vector3(0.04f, 0.05f, 0.18f);
            Object.DestroyImmediate(visual.GetComponent<Collider>());
            RangeBuildKit.Paint(visual, palette.Gun);

            var muzzle = new GameObject("Muzzle");
            muzzle.transform.SetParent(go.transform, false);
            muzzle.transform.localPosition = new Vector3(0f, 0f, 0.17f);

            var pistol = go.AddComponent<Pistol>();
            pistol.AssignHand(hand);
            RangeBuildKit.SetRef(pistol, "muzzle", muzzle.transform);
            RangeBuildKit.SetRef(pistol, "visual", visual.transform);
            RangeBuildKit.SetBool(pistol, "automatic", false);
            RangeBuildKit.SetArray(pistol, "fireClips", sounds.Gunshots ?? new AudioClip[0]);
            RangeBuildKit.SetArray(pistol, "emptyClips", sounds.Dry ?? new AudioClip[0]);
            RangeBuildKit.SetRef(pistol, "reloadClip", sounds.Reload);

            if (fx.MuzzleFlash != null)
            {
                var flash = (GameObject)PrefabUtility.InstantiatePrefab(fx.MuzzleFlash.gameObject);
                flash.transform.SetParent(muzzle.transform, false);
                RangeBuildKit.SetRef(pistol, "muzzleFlash", flash.GetComponent<ParticleSystem>());
            }

            // No PistolPickup, its Start would un-hold the gun, and practice should not make you take
            // it every time you press Play
            RangeBuildKit.SetBool(pistol, "startHeld", true);
            EditorUtility.SetDirty(pistol);

            var beamGo = new GameObject("LaserBeam");
            beamGo.transform.SetParent(go.transform, false);
            var beam = beamGo.AddComponent<LineRenderer>();
            beam.useWorldSpace = true;
            beam.positionCount = 2;
            beam.startWidth = 0.006f;
            beam.endWidth = 0.003f;
            beam.numCapVertices = 0;
            beam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            beam.receiveShadows = false;
            beam.sharedMaterial = palette.Laser;

            var dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dot.name = "LaserDot";
            dot.transform.SetParent(go.transform, false);
            Object.DestroyImmediate(dot.GetComponent<Collider>());
            RangeBuildKit.Paint(dot, palette.Laser);
            dot.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var sight = go.AddComponent<AimSight>();
            RangeBuildKit.SetRef(sight, "beam", beam);
            RangeBuildKit.SetRef(sight, "dot", dot.transform);
            RangeBuildKit.SetInt(sight, "aimMask", ~(1 << noShootLayer));

            var mouse = go.AddComponent<MouseTestPistol>();
            RangeBuildKit.SetInt(mouse, "aimMask", ~(1 << noShootLayer));
            RangeBuildKit.SetVector(mouse, "viewOffset", new Vector3(0.22f * side, -0.20f, 0.34f));

            RangeBuildKit.SetLayerRecursive(go, noShootLayer);
            return pistol;
        }

        /// Done after both pistols exist, since power ups need the off hand and feedback needs both muzzles
        public static void WirePlayer(Managers managers, Pistol mainPistol, Pistol offHandPistol,
                                      RangeBuildKit.Effects fx, RangeBuildKit.Sounds sounds)
        {
            RangeBuildKit.SetRef(managers.PowerUps, "mainPistol", mainPistol);
            RangeBuildKit.SetRef(managers.PowerUps, "offHandPistol", offHandPistol);
            offHandPistol.gameObject.SetActive(false);

            var floor = new GameObject("PlayerFloor");
            floor.transform.position = RangeBuildKit.Origin;

            var feedback = managers.Root.AddComponent<PowerUpFeedback>();
            RangeBuildKit.SetRef(feedback, "powerUps", managers.PowerUps);
            RangeBuildKit.SetRef(feedback, "combo", managers.Combo);
            RangeBuildKit.SetRef(feedback, "slowMotion", managers.SlowMotion);
            RangeBuildKit.SetArray(feedback, "guns", new Object[] { mainPistol, offHandPistol });
            RangeBuildKit.SetRef(feedback, "floorPoint", floor.transform);

            RangeBuildKit.SetArray(feedback, "milestoneClips", sounds.Milestone ?? new AudioClip[0]);
            RangeBuildKit.SetArray(feedback, "fullAutoClips", sounds.FullAuto ?? new AudioClip[0]);
            RangeBuildKit.SetArray(feedback, "scattershotClips", sounds.Scattershot ?? new AudioClip[0]);
            RangeBuildKit.SetArray(feedback, "dualWieldClips", sounds.DualWield ?? new AudioClip[0]);
            RangeBuildKit.SetArray(feedback, "slowMotionClips", sounds.SlowMotion ?? new AudioClip[0]);
            RangeBuildKit.SetRef(feedback, "slowEnterClip", sounds.SlowEnter);
            RangeBuildKit.SetRef(feedback, "slowExitClip", sounds.SlowExit);

            RangeBuildKit.SetRef(feedback, "grantBurstPrefab", fx.Grant);
            RangeBuildKit.SetRef(feedback, "gunAuraPrefab", fx.GunAura);
            RangeBuildKit.SetRef(feedback, "slowCirclePrefab", fx.SlowCircle);
        }

        public static void BuildHud(Managers managers, Pistol pistol, Vector3 position, float scale,
                                    TrackDirector director = null)
        {
            // RectTransform is supplied at construction. Adding a Canvas to a plain GameObject and
            // then fishing for the RectTransform it swapped in is version sensitive
            var canvasGo = new GameObject("HUD", typeof(RectTransform), typeof(Canvas));

            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            canvasGo.GetComponent<RectTransform>().sizeDelta = new Vector2(900f, 620f);

            canvasGo.transform.position = position;
            canvasGo.transform.rotation = RangeBuildKit.Facing();
            canvasGo.transform.localScale = Vector3.one * scale;

            var panelGo = new GameObject("PlayPanel", typeof(RectTransform), typeof(CanvasGroup));
            panelGo.transform.SetParent(canvasGo.transform, false);
            RangeBuildKit.Stretch(panelGo.GetComponent<RectTransform>());

            var timer = RangeBuildKit.Label(panelGo.transform, "TimerText", new Vector2(-330f, 150f), 64, TextAlignmentOptions.Left);
            var score = RangeBuildKit.Label(panelGo.transform, "ScoreText", new Vector2(330f, 150f), 64, TextAlignmentOptions.Right);
            var round = RangeBuildKit.Label(panelGo.transform, "RoundText", new Vector2(0f, 150f), 40, TextAlignmentOptions.Center);
            var ammo = RangeBuildKit.Label(panelGo.transform, "AmmoText", new Vector2(-330f, -150f), 44, TextAlignmentOptions.Left);
            var combo = RangeBuildKit.Label(panelGo.transform, "ComboText", new Vector2(330f, -150f), 52, TextAlignmentOptions.Right);
            var pattern = RangeBuildKit.Label(panelGo.transform, "PatternText", new Vector2(0f, -150f), 46, TextAlignmentOptions.Center);
            var active = RangeBuildKit.Label(panelGo.transform, "PowerUpActiveText", new Vector2(0f, -215f), 40, TextAlignmentOptions.Center);
            var callout = RangeBuildKit.Label(panelGo.transform, "PowerUpCalloutText", new Vector2(0f, -270f), 64, TextAlignmentOptions.Center);
            var centre = RangeBuildKit.Label(canvasGo.transform, "CenterText", Vector2.zero, 110, TextAlignmentOptions.Center);

            var hud = canvasGo.AddComponent<RangeHUD>();
            RangeBuildKit.SetRef(hud, "game", managers.Game);
            RangeBuildKit.SetRef(hud, "combo", managers.Combo);
            RangeBuildKit.SetRef(hud, "pistol", pistol);
            RangeBuildKit.SetRef(hud, "playPanel", panelGo.GetComponent<CanvasGroup>());
            RangeBuildKit.SetRef(hud, "timerText", timer);
            RangeBuildKit.SetRef(hud, "scoreText", score);
            RangeBuildKit.SetRef(hud, "roundText", round);
            RangeBuildKit.SetRef(hud, "ammoText", ammo);
            RangeBuildKit.SetRef(hud, "comboText", combo);
            RangeBuildKit.SetRef(hud, "patternText", pattern);
            RangeBuildKit.SetRef(hud, "centerText", centre);
            if (director != null) RangeBuildKit.SetRef(hud, "director", director);

            var powerUpHud = canvasGo.AddComponent<PowerUpHUD>();
            RangeBuildKit.SetRef(powerUpHud, "powerUps", managers.PowerUps);
            RangeBuildKit.SetRef(powerUpHud, "activeText", active);
            RangeBuildKit.SetRef(powerUpHud, "calloutText", callout);
        }

        private static AudioClip First(AudioClip[] clips)
        {
            return clips != null && clips.Length > 0 ? clips[0] : null;
        }
    }
}
