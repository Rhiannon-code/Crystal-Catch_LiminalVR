using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Liminal.SDK.VR.Avatars;
using TMPro;
using IntuitiveDesigns.CrystalCatch;

namespace IntuitiveDesigns.ShootingRange.EditorTools
{
    public static class RangePracticeBuilder
    {
        private const string SourceScene = "Assets/Scenes/LiminalExample.unity";
        private const string TargetScene = "Assets/Scenes/ShootingRangePractice.unity";

        private const float ShelfDistance = 4f;
        private const float ShelfTop = 0.9f;
        private const float PracticeRoundSeconds = 300f;
        private const int StackCount = 3;
        private const float StackSpacing = 1.2f;

        [MenuItem("Shooting Range/Build Practice Arena")]
        public static void Build()
        {
            int propLayer = RangeBuildKit.EnsureLayer(RangeBuildKit.PropLayer);
            int noShootLayer = RangeBuildKit.EnsureLayer(RangeBuildKit.NoShootLayer);

            EditorSceneManager.OpenScene(SourceScene, OpenSceneMode.Single);
            var scene = EditorSceneManager.GetActiveScene();

            RangeSceneParts.StripDemoProps();
            RangeBuildKit.EstablishPlayerFrame();

            var palette = RangeBuildKit.BuildPalette();
            var sounds = RangeBuildKit.LoadSounds();
            var fx = RangeBuildKit.BuildEffects();
            var prefabs = RangeBuildKit.BuildPrefabs(palette, propLayer, noShootLayer, sounds);

            RangeBuildKit.Block("Ground", RangeBuildKit.At(0f, -0.05f, ShelfDistance * 0.5f),
                                new Vector3(20f, 0.1f, 20f), palette.Ground);
            RangeBuildKit.Block("ShootingBench", RangeBuildKit.At(0f, 0.6f, 0.55f),
                                new Vector3(1.6f, 0.8f, 0.35f), palette.Furniture);
            RangeBuildKit.Block("PropShelf", RangeBuildKit.At(0f, ShelfTop - 0.25f, ShelfDistance),
                                new Vector3(4f, 0.5f, 0.6f), palette.Furniture);

            var managers = RangeSceneParts.BuildManagers(prefabs, fx, sounds, false);
            var builder = RangeSceneParts.BuildStacks(prefabs, StackCount, StackSpacing,
                                                      ShelfTop, ShelfDistance);
            var pistol = RangeSceneParts.BuildPistol(palette, noShootLayer, fx, sounds, "Pistol",
                                                     VRAvatarLimbType.RightHand, 1f);
            var offHand = RangeSceneParts.BuildPistol(palette, noShootLayer, fx, sounds, "PistolOffHand",
                                                      VRAvatarLimbType.LeftHand, -1f);
            RangeSceneParts.WirePlayer(managers, pistol, offHand, fx, sounds);

            RangeSceneParts.BuildHud(managers, pistol, RangeBuildKit.At(0f, 2.2f, ShelfDistance * 0.85f), 0.0022f);

            RangeBuildKit.SetRef(managers.Arena, "builder", builder);
            RangeBuildKit.SetInt(managers.Arena, "stacksInFirstRound", StackCount);
            RangeBuildKit.SetInt(managers.Arena, "extraStacksPerRound", 0);

            RangeBuildKit.SetFloat(managers.Game, "roundSeconds", PracticeRoundSeconds);
            RangeBuildKit.SetInt(managers.Game, "maxRounds", 1);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, TargetScene);
            AssetDatabase.SaveAssets();

            Debug.Log("[RangePractice] Built " + TargetScene + ": " + StackCount +
                      " stacks, pistol in hand, one " + PracticeRoundSeconds +
                      " s round. Play, hold left mouse to fire, R to re-stack.");
        }

        [MenuItem("Shooting Range/Rewrite Prefabs Only")]
        public static void RebuildPrefabsOnly()
        {
            int propLayer = RangeBuildKit.EnsureLayer(RangeBuildKit.PropLayer);
            int noShootLayer = RangeBuildKit.EnsureLayer(RangeBuildKit.NoShootLayer);

            RangeBuildKit.BuildEffects();
            RangeBuildKit.BuildPrefabs(RangeBuildKit.BuildPalette(), propLayer, noShootLayer,
                                       RangeBuildKit.LoadSounds());
            AssetDatabase.SaveAssets();

            Debug.Log("[RangePractice] Prefabs rewritten in " + RangeBuildKit.PrefabDir +
                      ". Open scenes keep their references, re-enter Play to see the change.");
        }
    }
}
