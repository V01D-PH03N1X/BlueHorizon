using System.Collections.Generic;
using BlueHorizon.Core;
using BlueHorizon.Networking;
using BlueHorizon.Objectives;
using BlueHorizon.UI;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace BlueHorizon.EditorTools
{
    /// <summary>
    /// Generates the Bootstrap, MainMenu and VerticalSlice scenes. The map ("Horizon Depot") is a graybox
    /// described as data below so it can be rebuilt deterministically and reviewed in code.
    ///
    /// Layout (top-down, +Z = north, metres):
    ///   Attacker spawn (south, z≈-44) → three lanes (west, mid, east) → junction (z 2..12) →
    ///   Site A (north-west) / Site B (north-east) → corridors → Defender spawn (north, z≈44).
    ///   The centre building and site cover block every straight spawn-to-spawn sightline.
    /// </summary>
    public static class MapBuilder
    {
        public const string ScenesRoot = ProjectSetup.Root + "/Scenes";
        public const string BootstrapScene = ScenesRoot + "/Bootstrap/Bootstrap.unity";
        public const string MenuScene = ScenesRoot + "/MainMenu/MainMenu.unity";
        public const string MapScene = ScenesRoot + "/VerticalSlice/VerticalSlice.unity";

        const float WallHeight = 5.5f;

        static Material _floor, _wall, _wallDark, _trim, _crate, _siteA, _siteB, _attackZone, _defenseZone, _accent, _boundary;

        public static void BuildScenes(GameContent content)
        {
            ContentFactory.EnsureFolder(ScenesRoot + "/Bootstrap");
            ContentFactory.EnsureFolder(ScenesRoot + "/MainMenu");
            ContentFactory.EnsureFolder(ScenesRoot + "/VerticalSlice");

            // NewScene(Single) unloads unused assets, so every scene re-loads what it references afterwards.
            BuildBootstrap();
            BuildMenuScene();
            BuildMapScene();

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(BootstrapScene, true),
                new EditorBuildSettingsScene(MenuScene, true),
                new EditorBuildSettingsScene(MapScene, true),
            };
            AssetDatabase.SaveAssets();
        }

        static void CreateMapMaterials()
        {
            Texture2D grid = GridTexture();
            _floor = Textured(ContentFactory.CreateLit("Map_Floor", new Color(0.4f, 0.42f, 0.46f), 0.15f), grid);
            _wall = Textured(ContentFactory.CreateLit("Map_Wall", new Color(0.8f, 0.82f, 0.86f), 0.2f), grid);
            _wallDark = Textured(ContentFactory.CreateLit("Map_WallDark", new Color(0.46f, 0.5f, 0.56f), 0.2f), grid);
            _trim = ContentFactory.CreateLit("Map_Trim", new Color(0.12f, 0.14f, 0.17f), 0.4f);
            _crate = Textured(ContentFactory.CreateLit("Map_Crate", new Color(0.62f, 0.52f, 0.36f), 0.1f), grid);
            _siteA = ContentFactory.CreateEmissive("Map_SiteA", new Color(1f, 0.72f, 0.18f));
            _siteB = ContentFactory.CreateEmissive("Map_SiteB", new Color(0.95f, 0.3f, 0.65f));
            _attackZone = ContentFactory.CreateLit("Map_AttackZone", new Color(0.55f, 0.32f, 0.22f), 0.1f);
            _defenseZone = ContentFactory.CreateLit("Map_DefenseZone", new Color(0.2f, 0.33f, 0.52f), 0.1f);
            _accent = ContentFactory.CreateEmissive("Map_Accent", new Color(0.3f, 0.75f, 1f));
            _boundary = Textured(ContentFactory.CreateLit("Map_Boundary", new Color(0.66f, 0.7f, 0.76f), 0.2f), grid);
            _attackZone = Textured(_attackZone, grid);
            _defenseZone = Textured(_defenseZone, grid);
        }

        static Material Textured(Material m, Texture2D texture)
        {
            m.SetTexture("_BaseMap", texture);
            EditorUtility.SetDirty(m);
            return m;
        }

        // ───────────── Bootstrap ─────────────

        static void BuildBootstrap()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var content = AssetDatabase.LoadAssetAtPath<GameContent>(ContentFactory.ContentPath);

            var networkGo = new GameObject("NetworkManager");
            var transport = networkGo.AddComponent<UnityTransport>();
            var network = networkGo.AddComponent<NetworkManager>();
            network.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport,
                TickRate = 60,
                ConnectionApproval = true,
                EnableSceneManagement = true,
                ClientConnectionBufferTimeout = 15,
            };
            var prefabList = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(ContentFactory.NetworkPrefabsPath);
            network.NetworkConfig.Prefabs.NetworkPrefabsLists = new List<NetworkPrefabsList> { prefabList };
            var bootstrap = networkGo.AddComponent<NetworkBootstrap>();
            SetPrivate(bootstrap, "content", content);

            var uiGo = new GameObject("UI");
            var doc = uiGo.AddComponent<UIDocument>();
            doc.panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(ContentFactory.PanelSettingsPath);
            var hud = uiGo.AddComponent<HudController>();
            SetPrivate(hud, "styleSheet", AssetDatabase.LoadAssetAtPath<StyleSheet>(ContentFactory.StyleSheetPath));
            var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            eventSystem.transform.SetParent(uiGo.transform, false);

            var camGo = new GameObject("BootCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.03f, 0.05f, 0.09f);

            EditorSceneManager.SaveScene(scene, BootstrapScene);
        }

        static void SetPrivate(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ───────────── Menu backdrop ─────────────

        static void BuildMenuScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CreateMapMaterials();
            AddLighting();
            var root = new GameObject("Backdrop").transform;
            Block(root, "Ground", new Vector3(0f, -0.25f, 0f), new Vector3(200f, 0.5f, 200f), _floor);
            for (int i = 0; i < 9; i++)
            {
                float x = -24f + i * 6f;
                float h = 4f + (i * 37 % 7);
                Block(root, $"Tower{i}", new Vector3(x, h * 0.5f, 30f + (i % 3) * 6f), new Vector3(4f, h, 4f), i % 2 == 0 ? _wall : _wallDark);
            }
            Block(root, "AccentStrip", new Vector3(0f, 0.02f, 18f), new Vector3(60f, 0.04f, 0.4f), _accent);
            CreateCamera(new Vector3(-6f, 3.5f, -6f), Quaternion.Euler(6f, 18f, 0f));
            EditorSceneManager.SaveScene(scene, MenuScene);
        }

        // ───────────── Map ─────────────

        struct Rect2
        {
            public float x0, x1, z0, z1;
            public Rect2(float x0, float x1, float z0, float z1) { this.x0 = x0; this.x1 = x1; this.z0 = z0; this.z1 = z1; }
            public Vector3 Center(float y) => new((x0 + x1) * 0.5f, y, (z0 + z1) * 0.5f);
            public Vector3 Size(float h) => new(x1 - x0, h, z1 - z0);
        }

        static void BuildMapScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CreateMapMaterials();
            AddLighting();

            var map = new GameObject("HorizonDepot").transform;
            Transform geometry = new GameObject("Geometry").transform;
            geometry.SetParent(map, false);

            // Bounds: x [-36, 36], z [-50, 48].
            Block(geometry, "Floor", new Vector3(0f, -0.25f, -1f), new Vector3(72f, 0.5f, 98f), _floor);
            Block(geometry, "Boundary_W", new Vector3(-37f, WallHeight * 0.5f, -1f), new Vector3(2f, WallHeight, 100f), _boundary);
            Block(geometry, "Boundary_E", new Vector3(37f, WallHeight * 0.5f, -1f), new Vector3(2f, WallHeight, 100f), _boundary);
            Block(geometry, "Boundary_S", new Vector3(0f, WallHeight * 0.5f, -51f), new Vector3(76f, WallHeight, 2f), _boundary);
            Block(geometry, "Boundary_N", new Vector3(0f, WallHeight * 0.5f, 49f), new Vector3(76f, WallHeight, 2f), _boundary);

            // Solid buildings between lanes.
            var solids = new (string name, Rect2 r, Material m)[]
            {
                ("Block_West", new Rect2(-20f, -6f, -30f, 2f), _wall),
                ("Block_East", new Rect2(6f, 20f, -30f, 2f), _wall),
                ("Centre", new Rect2(-14f, 14f, 12f, 30f), _wallDark),
                ("Divider_W", new Rect2(-36f, -26f, 30f, 36f), _wall),
                ("Divider_C", new Rect2(-18f, 18f, 30f, 36f), _wall),
                ("Divider_E", new Rect2(26f, 36f, 30f, 36f), _wall),
            };
            foreach (var s in solids)
            {
                Block(geometry, s.name, s.r.Center(WallHeight * 0.5f), s.r.Size(WallHeight), s.m);
                // Dark base trim for readability of the floor line.
                Block(geometry, s.name + "_Trim", s.r.Center(0.15f), s.r.Size(0.3f) + new Vector3(0.1f, 0f, 0.1f), _trim);
            }

            // Cover at different heights (1.0 = crouch cover, 1.6 = standing cover, 2.6 = full).
            var cover = new (string name, Rect2 r, float h, Material m)[]
            {
                ("SiteA_Cover", new Rect2(-31f, -18f, 16f, 22f), 2.6f, _wallDark),
                ("SiteB_Cover", new Rect2(18f, 31f, 14f, 20f), 2.6f, _wallDark),
                ("WestLane_Cover", new Rect2(-28f, -24f, -24f, -20f), 1.6f, _crate),
                ("EastLane_Cover", new Rect2(24f, 28f, -24f, -20f), 1.6f, _crate),
                ("WestLane_Crate", new Rect2(-24f, -22f, -8f, -6f), 1.0f, _crate),
                ("EastLane_Crate", new Rect2(22f, 24f, -10f, -8f), 1.0f, _crate),
                ("Mid_Low", new Rect2(-1.5f, 1.5f, -14f, -12f), 1.0f, _crate),
                ("Mid_Pillar", new Rect2(-3f, 3f, 4f, 7f), 3.2f, _wallDark),
                ("Junction_W", new Rect2(-12f, -9f, 5f, 8f), 1.6f, _crate),
                ("Junction_E", new Rect2(9f, 12f, 4f, 7f), 1.6f, _crate),
                ("SiteA_Crate1", new Rect2(-35f, -33f, 24f, 26f), 1.0f, _crate),
                ("SiteA_Crate2", new Rect2(-17.5f, -15.5f, 26f, 28f), 1.6f, _crate),
                ("SiteB_Crate1", new Rect2(33f, 35f, 22f, 24f), 1.0f, _crate),
                ("SiteB_Crate2", new Rect2(15.5f, 17.5f, 24f, 26f), 1.6f, _crate),
                ("AttackSpawn_Shield", new Rect2(-4f, 4f, -33f, -31f), 3f, _wall),
                ("DefSpawn_Shield_A", new Rect2(-24f, -20f, 39f, 40f), 3f, _wall),
                ("DefSpawn_Shield_B", new Rect2(20f, 24f, 39f, 40f), 3f, _wall),
            };
            foreach (var c in cover)
                Block(geometry, c.name, c.r.Center(c.h * 0.5f), c.r.Size(c.h), c.m);

            // Spawn zones (floor tint) and spawn points.
            Transform markers = new GameObject("Markers").transform;
            markers.SetParent(map, false);
            Decal(geometry, "AttackSpawn_Floor", new Rect2(-34f, 34f, -49f, -36f), _attackZone);
            Decal(geometry, "DefenseSpawn_Floor", new Rect2(-34f, 34f, 41f, 47f), _defenseZone);
            for (int i = 0; i < 5; i++)
            {
                float x = -8f + i * 4f;
                Spawn(markers, Side.Attack, new Vector3(x, 0f, -44f), 0f, i);
                Spawn(markers, Side.Defense, new Vector3(x, 0f, 44f), 180f, i);
            }

            // Bomb sites.
            Site(markers, 0, "A", new Vector3(-25f, 0f, 20f), new Vector3(20f, 4f, 14f), _siteA, new Color(1f, 0.72f, 0.18f));
            Site(markers, 1, "B", new Vector3(25f, 0f, 18f), new Vector3(20f, 4f, 14f), _siteB, new Color(0.95f, 0.3f, 0.65f));

            // Landmarks: giant site letters and lane signage.
            Letter(geometry, 'A', new Vector3(-31f, 1.6f, 30f), 0.45f, _siteA, facingSouth: true);
            Letter(geometry, 'B', new Vector3(31f, 1.6f, 30f), 0.45f, _siteB, facingSouth: true);
            Letter(geometry, 'A', new Vector3(-20f, 1.4f, -12f), 0.3f, _siteA, facingSouth: false, faceEast: true);
            Letter(geometry, 'B', new Vector3(20f, 1.4f, -12f), 0.3f, _siteB, facingSouth: false, faceEast: false);
            Block(geometry, "Accent_Mid", new Vector3(0f, 0.02f, -10f), new Vector3(0.3f, 0.04f, 36f), _accent);
            Block(geometry, "Accent_West", new Vector3(-28f, 0.02f, -10f), new Vector3(0.3f, 0.04f, 36f), _siteA);
            Block(geometry, "Accent_East", new Vector3(28f, 0.02f, -10f), new Vector3(0.3f, 0.04f, 36f), _siteB);
            for (int i = 0; i < 6; i++)
            {
                // Wall-top light strips for depth cues.
                float z = -26f + i * 5f;
                Block(geometry, $"Lamp_W{i}", new Vector3(-20.05f, 4.2f, z), new Vector3(0.12f, 0.12f, 1.6f), _accent);
                Block(geometry, $"Lamp_E{i}", new Vector3(20.05f, 4.2f, z), new Vector3(0.12f, 0.12f, 1.6f), _accent);
            }

            // Test-bot routes (spawn → site). Attackers finish inside the site on the junction side of the cover.
            Route(markers, Side.Attack, 0, new Vector3(-4f, 0, -42f), new Vector3(-31f, 0, -38f), new Vector3(-32f, 0, -20f), new Vector3(-32f, 0, 6f), new Vector3(-17f, 0, 10f), new Vector3(-17f, 0, 14.5f));
            Route(markers, Side.Attack, 1, new Vector3(4f, 0, -42f), new Vector3(31f, 0, -38f), new Vector3(32f, 0, -20f), new Vector3(32f, 0, 6f), new Vector3(17f, 0, 10f), new Vector3(17f, 0, 12.5f));
            Route(markers, Side.Defense, 0, new Vector3(-4f, 0, 44f), new Vector3(-16.5f, 0, 41f), new Vector3(-16.5f, 0, 37.5f), new Vector3(-22f, 0, 37f), new Vector3(-22f, 0, 28f), new Vector3(-17f, 0, 24f));
            Route(markers, Side.Defense, 1, new Vector3(4f, 0, 44f), new Vector3(16.5f, 0, 41f), new Vector3(16.5f, 0, 37.5f), new Vector3(22f, 0, 37f), new Vector3(22f, 0, 28f), new Vector3(17f, 0, 22f));

            // Overview / spectator camera.
            CreateCamera(new Vector3(0f, 55f, -38f), Quaternion.Euler(58f, 0f, 0f));

            GameObjectUtility.SetStaticEditorFlags(geometry.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
            foreach (Transform t in geometry)
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);

            EditorSceneManager.SaveScene(scene, MapScene);
        }

        static void AddLighting()
        {
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.25f;
            sun.color = new Color(1f, 0.96f, 0.9f);
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(52f, -35f, 0f);

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.65f, 0.8f);
            RenderSettings.ambientEquatorColor = new Color(0.42f, 0.45f, 0.5f);
            RenderSettings.ambientGroundColor = new Color(0.2f, 0.2f, 0.22f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 60f;
            RenderSettings.fogEndDistance = 220f;
            RenderSettings.fogColor = new Color(0.62f, 0.72f, 0.85f);

            var volumeProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/Settings/SampleSceneProfile.asset");
            if (volumeProfile != null)
            {
                var volume = new GameObject("GlobalVolume").AddComponent<Volume>();
                volume.isGlobal = true;
                volume.sharedProfile = volumeProfile;
            }
        }

        static void CreateCamera(Vector3 position, Quaternion rotation)
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            go.transform.SetPositionAndRotation(position, rotation);
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = 60f;
            cam.farClipPlane = 400f;
            go.AddComponent<AudioListener>();
            go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
        }

        static GameObject Block(Transform parent, string name, Vector3 center, Vector3 size, Material material)
        {
            var go = new GameObject(name) { layer = GameLayers.Default };
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            go.AddComponent<MeshFilter>().sharedMesh = BoxMesh(size);
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            go.AddComponent<BoxCollider>().size = size;
            return go;
        }

        /// <summary>
        /// Axis-aligned box with UVs in world units (one texture repeat per 2 m), so the grid texture keeps a
        /// constant scale on every wall regardless of its size. Meshes are cached as assets by size.
        /// </summary>
        static Mesh BoxMesh(Vector3 size)
        {
            ContentFactory.EnsureFolder(GeneratedMeshes);
            string key = $"Box_{size.x:0.###}x{size.y:0.###}x{size.z:0.###}".Replace('.', '_');
            string path = $"{GeneratedMeshes}/{key}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) return existing;

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            Vector3 h = size * 0.5f;
            void Face(Vector3 n, Vector3 u, Vector3 v)
            {
                Vector3 c = Vector3.Scale(n, h);
                Vector3 du = Vector3.Scale(u, h), dv = Vector3.Scale(v, h);
                Vector3[] corners = { c - du - dv, c + du - dv, c + du + dv, c - du + dv };
                int start = vertices.Count;
                foreach (Vector3 p in corners)
                {
                    vertices.Add(p);
                    normals.Add(n);
                    uvs.Add(new Vector2(Vector3.Dot(p + h, u), Vector3.Dot(p + h, v)) * UvPerMetre);
                }
                bool flip = Vector3.Dot(Vector3.Cross(corners[1] - corners[0], corners[2] - corners[0]), n) < 0f;
                int[] quad = flip ? new[] { 0, 2, 1, 0, 3, 2 } : new[] { 0, 1, 2, 0, 2, 3 };
                foreach (int i in quad) triangles.Add(start + i);
            }
            Face(Vector3.right, Vector3.forward, Vector3.up);
            Face(Vector3.left, Vector3.forward, Vector3.up);
            Face(Vector3.up, Vector3.right, Vector3.forward);
            Face(Vector3.down, Vector3.right, Vector3.forward);
            Face(Vector3.forward, Vector3.right, Vector3.up);
            Face(Vector3.back, Vector3.right, Vector3.up);

            var mesh = new Mesh { name = key };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        const float UvPerMetre = 0.5f;
        const string GeneratedArt = ProjectSetup.Root + "/Art/Generated";
        const string GeneratedMeshes = GeneratedArt + "/Meshes";

        /// <summary>Panel-grid albedo: subtle major lines every 2 m, minor lines every 0.5 m, light noise.</summary>
        static Texture2D GridTexture()
        {
            string path = GeneratedArt + "/T_Grid.png";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex != null) return tex;

            ContentFactory.EnsureFolder(GeneratedArt);
            const int n = 512;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
            var rng = new System.Random(7);
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float v = 0.92f + (float)rng.NextDouble() * 0.05f;
                    bool major = x < 3 || y < 3;
                    bool minor = x % 128 < 1 || y % 128 < 1;
                    if (major) v = 0.62f;
                    else if (minor) v = 0.8f;
                    t.SetPixel(x, y, new Color(v, v, v, 1f));
                }
            }
            System.IO.File.WriteAllBytes(path, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.anisoLevel = 8;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static void Decal(Transform parent, string name, Rect2 r, Material material)
        {
            GameObject go = Block(parent, name, r.Center(0.01f), r.Size(0.02f), material);
            Object.DestroyImmediate(go.GetComponent<Collider>());
        }

        static void Spawn(Transform parent, Side side, Vector3 position, float yaw, int index)
        {
            var go = new GameObject($"Spawn_{side}_{index}");
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            go.AddComponent<SpawnPoint>().side = side;
        }

        static void Site(Transform parent, int index, string label, Vector3 center, Vector3 size, Material material, Color color)
        {
            var go = new GameObject($"BombSite_{label}");
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            var site = go.AddComponent<BombSite>();
            site.index = index;
            site.label = label;
            site.size = size;
            site.color = color;

            // Painted outline so the plantable area is readable.
            const float t = 0.25f;
            float hx = size.x * 0.5f, hz = size.z * 0.5f;
            Object.DestroyImmediate(Block(go.transform, "Edge_N", new Vector3(0f, 0.02f, hz), new Vector3(size.x, 0.03f, t), material).GetComponent<Collider>());
            Object.DestroyImmediate(Block(go.transform, "Edge_S", new Vector3(0f, 0.02f, -hz), new Vector3(size.x, 0.03f, t), material).GetComponent<Collider>());
            Object.DestroyImmediate(Block(go.transform, "Edge_W", new Vector3(-hx, 0.02f, 0f), new Vector3(t, 0.03f, size.z), material).GetComponent<Collider>());
            Object.DestroyImmediate(Block(go.transform, "Edge_E", new Vector3(hx, 0.02f, 0f), new Vector3(t, 0.03f, size.z), material).GetComponent<Collider>());
        }

        static void Route(Transform parent, Side side, int site, params Vector3[] points)
        {
            var go = new GameObject($"BotRoute_{side}_{site}");
            go.transform.SetParent(parent, false);
            var route = go.AddComponent<BotRoute>();
            route.side = side;
            route.siteIndex = site;
            route.points = points;
        }

        // 5x7 block glyphs for landmark signage.
        static readonly Dictionary<char, string[]> Glyphs = new()
        {
            ['A'] = new[] { ".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" },
            ['B'] = new[] { "####.", "#...#", "#...#", "####.", "#...#", "#...#", "####." },
        };

        static void Letter(Transform parent, char c, Vector3 origin, float cell, Material material, bool facingSouth, bool faceEast = false)
        {
            if (!Glyphs.TryGetValue(c, out string[] rows)) return;
            var root = new GameObject($"Sign_{c}").transform;
            root.SetParent(parent, false);
            root.position = origin;
            root.rotation = facingSouth ? Quaternion.identity : Quaternion.Euler(0f, faceEast ? 90f : -90f, 0f);
            for (int y = 0; y < rows.Length; y++)
            {
                for (int x = 0; x < rows[y].Length; x++)
                {
                    if (rows[y][x] != '#') continue;
                    // Facing south: viewers look north, so +x on screen is +x in the world.
                    var local = new Vector3((x - 2) * cell, (rows.Length - 1 - y) * cell, -0.06f);
                    GameObject px = Block(root, $"px{x}{y}", Vector3.zero, Vector3.one * cell * 0.92f + Vector3.forward * 0.02f, material);
                    px.transform.localPosition = local;
                    Object.DestroyImmediate(px.GetComponent<Collider>());
                }
            }
        }
    }
}
