using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Liminal.SDK.VR.Avatars;

namespace IntuitiveDesigns.ShootingRange.EditorTools
{
    public static class CarnivalRangeBuilder
    {
        private const string SourceScene = "Assets/Scenes/LiminalExample.unity";
        private const string TargetScene = "Assets/Scenes/ShootingRangeCarnival.unity";
        private const float HalfWidth = 4f;
        private const float Setback = 2f;

        private const float FrontZ = 2f + Setback;
        private const float RailSpan = 8f;

        private const float FloorRailHeight = 0.2f;
        private const float RoofRailHeight = 3f;
        private const float RoomHeight = 3.4f;
        private const float WallX = 4.6f;

        private const float RiserFoot = 0.35f;
        private const float RiserSpan = 2.5f;

        private const float ShelfAhead = 10.8f + Setback;
        private const float ShelfTop = 0.9f;
        private const float BackWallZ = 11.6f + Setback;
        private const int ShelfStacks = 2;
        private const float ShelfStackSpacing = 1.1f;

        private const float RoundSeconds = 90f;
        private const int Rounds = 5;

        private static readonly float[] CrossZ = { 4f + Setback, 6f + Setback, 8f + Setback };
        private static readonly float[] CrossX = { -2.4f, 0f, 2.4f };

        [MenuItem("Shooting Range/Build Carnival Range")]
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

            BuildRoom(palette);

            var managers = RangeSceneParts.BuildManagers(prefabs, fx, sounds, true);
            var grid = BuildTracks(palette);
            var director = BuildDirector(managers, grid, prefabs);

            var stacks = RangeSceneParts.BuildStacks(prefabs, ShelfStacks, ShelfStackSpacing,
                                                     ShelfTop, ShelfAhead);
            RangeBuildKit.SetRef(managers.Arena, "builder", stacks);
            RangeBuildKit.SetInt(managers.Arena, "stacksInFirstRound", ShelfStacks);
            RangeBuildKit.SetInt(managers.Arena, "extraStacksPerRound", 0);

            var pistol = RangeSceneParts.BuildPistol(palette, noShootLayer, fx, sounds, "Pistol",
                                                     VRAvatarLimbType.RightHand, 1f);
            var offHand = RangeSceneParts.BuildPistol(palette, noShootLayer, fx, sounds, "PistolOffHand",
                                                      VRAvatarLimbType.LeftHand, -1f);
            RangeSceneParts.WirePlayer(managers, pistol, offHand, fx, sounds);

            // On the back wall, above the shelf, so the score never sits over the traffic
            RangeSceneParts.BuildHud(managers, pistol, RangeBuildKit.At(0f, 2.35f, BackWallZ - 0.2f), 0.004f * (BackWallZ - 0.2f) / 11.4f, director);

            RangeBuildKit.SetFloat(managers.Game, "roundSeconds", RoundSeconds);
            RangeBuildKit.SetInt(managers.Game, "maxRounds", Rounds);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, TargetScene);
            AssetDatabase.SaveAssets();

            Debug.Log("[CarnivalRange] Built " + TargetScene + ": 6 floor rails + 6 roof rails (9 " +
                      "crossings each) + 6 wall risers = 18 rails. " + Rounds + " rounds of " +
                      RoundSeconds + " s. Check the console at Play for the junction count.");
        }

        private static void BuildRoom(RangeBuildKit.Palette palette)
        {
            // From just behind the player to the back wall, so moving the range out never leaves the
            // player standing off the end of the floor
            const float behindPlayer = -1f;
            float backWall = BackWallZ + 0.1f;
            float midZ = (behindPlayer + backWall) * 0.5f;
            float depth = backWall - behindPlayer;

            RangeBuildKit.Block("Floor", RangeBuildKit.At(0f, -0.05f, midZ),
                                new Vector3(WallX * 2f, 0.1f, depth), palette.Ground);
            RangeBuildKit.Block("Ceiling", RangeBuildKit.At(0f, RoomHeight + 0.05f, midZ),
                                new Vector3(WallX * 2f, 0.1f, depth), palette.Wall);
            RangeBuildKit.Block("WallLeft", RangeBuildKit.At(-WallX, RoomHeight * 0.5f, midZ),
                                new Vector3(0.2f, RoomHeight, depth), palette.Wall);
            RangeBuildKit.Block("WallRight", RangeBuildKit.At(WallX, RoomHeight * 0.5f, midZ),
                                new Vector3(0.2f, RoomHeight, depth), palette.Wall);
            RangeBuildKit.Block("WallBack", RangeBuildKit.At(0f, RoomHeight * 0.5f, BackWallZ),
                                new Vector3(WallX * 2f, RoomHeight, 0.2f), palette.Wall);

            RangeBuildKit.Block("ShootingBench", RangeBuildKit.At(0f, 0.6f, 0.55f),
                                new Vector3(1.6f, 0.8f, 0.35f), palette.Furniture);
            RangeBuildKit.Block("PropShelf", RangeBuildKit.At(0f, ShelfTop - 0.25f, ShelfAhead),
                                new Vector3(2.4f, 0.5f, 0.6f), palette.Furniture);
        }

        private static TrackGrid BuildTracks(RangeBuildKit.Palette palette)
        {
            var go = new GameObject("Tracks");
            go.transform.position = RangeBuildKit.Origin;

            var grid = go.AddComponent<TrackGrid>();
            var rails = new List<Object>();

            AddGridPlane(go.transform, rails, palette, FloorRailHeight, "Floor",
                         TrackGroup.FloorAcross, TrackGroup.FloorAlong);
            AddGridPlane(go.transform, rails, palette, RoofRailHeight, "Roof",
                         TrackGroup.RoofAcross, TrackGroup.RoofAlong);
            AddRisers(go.transform, rails, palette);

            RangeBuildKit.SetArray(grid, "rails", rails.ToArray());
            return grid;
        }

        private static void AddGridPlane(Transform parent, List<Object> rails,
                                         RangeBuildKit.Palette palette, float height, string label,
                                         TrackGroup across, TrackGroup along)
        {
            // Across: start at the left wall, run right
            for (int i = 0; i < CrossZ.Length; i++)
            {
                rails.Add(Rail(parent, palette, label + "_Across_" + i,
                               RangeBuildKit.At(-HalfWidth, height, CrossZ[i]),
                               Quaternion.LookRotation(RangeBuildKit.Right, Vector3.up),
                               HalfWidth * 2f, across));
            }

            // Along: start at the front, run away from the player
            for (int i = 0; i < CrossX.Length; i++)
            {
                rails.Add(Rail(parent, palette, label + "_Along_" + i,
                               RangeBuildKit.At(CrossX[i], height, FrontZ),
                               Quaternion.LookRotation(RangeBuildKit.Forward, Vector3.up),
                               RailSpan, along));
            }
        }

        private static void AddRisers(Transform parent, List<Object> rails, RangeBuildKit.Palette palette)
        {
            for (int side = 0; side < 2; side++)
            {
                float x = side == 0 ? -(WallX - 0.4f) : (WallX - 0.4f);

                for (int i = 0; i < CrossZ.Length; i++)
                {
                    // Alternate so the wall reads as traffic rather than a lift queue
                    bool upward = (i + side) % 2 == 0;

                    Vector3 foot = RangeBuildKit.At(x, upward ? RiserFoot : RiserFoot + RiserSpan, CrossZ[i]);
                    var rotation = Quaternion.LookRotation(upward ? Vector3.up : Vector3.down,
                                                           RangeBuildKit.Forward);

                    rails.Add(Rail(parent, palette, "Riser_" + (side == 0 ? "L" : "R") + "_" + i,
                                   foot, rotation, RiserSpan, TrackGroup.Riser));
                }
            }
        }

        private static TrackRail Rail(Transform parent, RangeBuildKit.Palette palette, string name,
                                      Vector3 origin, Quaternion rotation, float length,
                                      TrackGroup group)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = origin;
            go.transform.rotation = rotation;

            var rail = go.AddComponent<TrackRail>();
            RangeBuildKit.SetFloat(rail, "length", length);
            RangeBuildKit.SetEnum(rail, "group", (int)group);

            var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "Beam";
            visual.transform.SetParent(go.transform, false);
            visual.transform.localPosition = new Vector3(0f, 0f, length * 0.5f);
            visual.transform.localScale = new Vector3(0.04f, 0.04f, length);

            // The rail must not eat the shot meant for what is riding it
            Object.DestroyImmediate(visual.GetComponent<Collider>());
            RangeBuildKit.Paint(visual, palette.Rail);

            return rail;
        }

        private static TrackDirector BuildDirector(RangeSceneParts.Managers managers, TrackGrid grid,
                                                   RangeBuildKit.Prefabs prefabs)
        {
            var director = managers.Root.AddComponent<TrackDirector>();
            RangeBuildKit.SetRef(director, "grid", grid);
            RangeBuildKit.SetRef(director, "game", managers.Game);
            RangeBuildKit.SetRef(director, "moverPrefab", prefabs.Mover.GetComponent<TrackMover>());
            return director;
        }
    }
}
