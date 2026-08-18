using UnityEditor;
using UnityEngine;

namespace IntuitiveDesigns.CrystalCatch.EditorTools
{
    [InitializeOnLoad]
    public static class CCTunnelSceneView
    {
        private const string MenuPath = "Crystal Catch/Show Tunnel In Scene View";
        private const string EnabledPref = "CrystalCatch.ShowTunnelInSceneView";

        private const string RingPrefabPath = "Assets/Prefabs/TunnelRing.prefab";
        private const string RailPrefabPath = "Assets/Prefabs/TunnelRail.prefab";
        private const string FramePrefabPath = "Assets/Prefabs/TunnelFrame.prefab";

        private const float RingLength = 4f;
        private const float RailSpacing = 2f;
        private const float FrameSpacing = 16f;

        private const float RingCullRadius = 9f;

        // Pure safety valve against a pathological track length. 2000 rings is 8 km
        private const int MaxRingsPerRepaint = 2000;

        private static readonly System.Collections.Generic.List<float> _obstacleDistances =
            new System.Collections.Generic.List<float>();
        private static readonly System.Collections.Generic.List<TrackObstacle.Kind> _obstacleKinds =
            new System.Collections.Generic.List<TrackObstacle.Kind>();

        private static readonly System.Collections.Generic.Dictionary<string, Piece[]> _cache =
            new System.Collections.Generic.Dictionary<string, Piece[]>();
        private static readonly System.Collections.Generic.Dictionary<string, string> _cacheKeys =
            new System.Collections.Generic.Dictionary<string, string>();

        private struct Piece
        {
            public Mesh Mesh;
            public Material Material;
            public Matrix4x4 LocalToRing;
        }

        static CCTunnelSceneView()
        {
            SceneView.duringSceneGui += OnSceneGui;
        }

        private static bool Enabled
        {
            get { return EditorPrefs.GetBool(EnabledPref, true); }
            set { EditorPrefs.SetBool(EnabledPref, value); }
        }

        [MenuItem(MenuPath)]
        private static void Toggle()
        {
            Enabled = !Enabled;
            SceneView.RepaintAll();
        }

        [MenuItem(MenuPath, true)]
        private static bool ToggleValidate()
        {
            Menu.SetChecked(MenuPath, Enabled);
            return true;
        }

        private static void OnSceneGui(SceneView view)
        {
            if (!Enabled) return;

            // Repaint only. Drawing during layout/input events would fight the scene view's own
            // event handling and flicker
            if (Event.current == null || Event.current.type != EventType.Repaint) return;
            if (view == null || view.camera == null) return;

            var track = Object.FindObjectOfType<TrackPath>();
            if (track == null || !track.IsGenerated) return;

            // A baked tunnel is already real geometry in the scene. Drawing over it would double
            // every surface and z-fight, so the lightweight view stands down while one exists
            if (GameObject.Find(TunnelBuilder.AuthoringRootName) != null) return;

            var frustum = GeometryUtility.CalculateFrustumPlanes(view.camera);
            var bounds = new Bounds(Vector3.zero, Vector3.one * (RingCullRadius * 2f));

            DrawAlong(track, LoadPieces(RingPrefabPath), RingLength, frustum, bounds);
            DrawAlong(track, LoadPieces(RailPrefabPath), RailSpacing, frustum, bounds);
            DrawAlong(track, LoadPieces(FramePrefabPath), FrameSpacing, frustum, bounds);
            DrawObstacles(track, frustum, bounds);
        }

        private static void DrawObstacles(TrackPath track, Plane[] frustum, Bounds bounds)
        {
            var placer = Object.FindObjectOfType<TrackObstacles>();
            if (placer == null) return;

            placer.PreviewSequence(track.Length, _obstacleDistances, _obstacleKinds);

            for (int i = 0; i < _obstacleDistances.Count; i++)
            {
                Vector3 position = track.PositionAt(_obstacleDistances[i]);

                bounds.center = position;
                if (!GeometryUtility.TestPlanesAABB(frustum, bounds)) continue;

                var prefab = placer.PrefabFor(_obstacleKinds[i]);
                if (prefab == null) continue;

                Matrix4x4 at = Matrix4x4.TRS(position,
                                             track.RotationAt(_obstacleDistances[i], true),
                                             Vector3.one);

                var pieces = LoadPieces(AssetDatabase.GetAssetPath(prefab));
                if (pieces != null)
                {
                    for (int p = 0; p < pieces.Length; p++)
                    {
                        if (pieces[p].Mesh == null || pieces[p].Material == null) continue;
                        pieces[p].Material.SetPass(0);
                        Graphics.DrawMeshNow(pieces[p].Mesh, at * pieces[p].LocalToRing);
                    }
                }

                // The volume IS the rule. Judging whether a beam looks duckable against the mesh is
                // not the same as seeing the box the player's head has to stay out of
                Handles.matrix = at;
                Handles.color = new Color(1f, 0.35f, 0.2f, 0.9f);
                Handles.DrawWireCube(prefab.DangerCentre, prefab.DangerHalfExtents * 2f);
                Handles.matrix = Matrix4x4.identity;
            }
        }

        private static void DrawAlong(TrackPath track, Piece[] pieces, float spacing,
                                      Plane[] frustum, Bounds bounds)
        {
            if (pieces == null || pieces.Length == 0 || spacing <= 0.01f) return;

            int drawn = 0;

            for (float d = 0f; d <= track.Length; d += spacing)
            {
                if (drawn >= MaxRingsPerRepaint) break;

                Vector3 position = track.PositionAt(d);

                bounds.center = position;
                if (!GeometryUtility.TestPlanesAABB(frustum, bounds)) continue;

                Matrix4x4 at = Matrix4x4.TRS(position, track.RotationAt(d, true), Vector3.one);

                for (int i = 0; i < pieces.Length; i++)
                {
                    if (pieces[i].Mesh == null || pieces[i].Material == null) continue;

                    pieces[i].Material.SetPass(0);
                    Graphics.DrawMeshNow(pieces[i].Mesh, at * pieces[i].LocalToRing);
                }

                drawn++;
            }
        }

        private static Piece[] LoadPieces(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) return null;

            // Cheap cache key. A rebuild replaces the prefab asset, and re-reading it costs nothing
            // next to a repaint, so this only avoids doing it several times per frame
            string key = prefab.GetInstanceID() + ":" + prefab.transform.childCount;

            string cached;
            if (_cacheKeys.TryGetValue(path, out cached) && cached == key) return _cache[path];

            var filters = prefab.GetComponentsInChildren<MeshFilter>();
            var pieces = new System.Collections.Generic.List<Piece>();

            foreach (var filter in filters)
            {
                var renderer = filter.GetComponent<MeshRenderer>();
                if (filter.sharedMesh == null || renderer == null || renderer.sharedMaterial == null) continue;

                var piece = new Piece();
                piece.Mesh = filter.sharedMesh;
                piece.Material = renderer.sharedMaterial;
                piece.LocalToRing = prefab.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                pieces.Add(piece);
            }

            _cache[path] = pieces.ToArray();
            _cacheKeys[path] = key;
            return _cache[path];
        }
    }
}
