using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.ARFoundation;

namespace Siege.EditorTools
{
    // Builds every generated asset (materials, prefabs, the AR scene) from the raw models, textures
    // and sounds. Idempotent: run "Siege/Build All" after changing anything it consumes.
    public static class SiegeAssetBuilder
    {
        const string Root = "Assets/Siege";
        const string Mats = Root + "/Materials";
        const string Prefabs = Root + "/Prefabs";
        const string Kingdom = "Assets/ithappy/Location_Kingdom/Prefabs/";
        const string ColorMatPath = "Assets/ithappy/Location_Kingdom/Materials/Color.mat";
        public const float KeepBack = 7f;   // keep sits this far behind the castle centre (GameManager.keepOffsetUnits)

        static Material s_Color, s_Debris;

        [MenuItem("Siege/Build All")]
        public static void BuildAll()
        {
            ProjectSetup.Run();
            Directory.CreateDirectory(Mats);
            Directory.CreateDirectory(Prefabs);
            Directory.CreateDirectory(Root + "/Scenes");
            SetupLayers();
            ImportSettings();
            var m = BuildMaterials();
            var arrow = BuildArrow(m);
            var fireball = BuildFireball(m);
            var marker = BuildMarker(m, "LandingMarker", m["Marker"], true);
            var coin = BuildCoin();
            var castle = BuildCastle(coin);
            var soldier = BuildSoldier("Soldier", "Soldier_Sword");
            var spearman = BuildSoldier("Soldier_Spearman", "Soldier_Spear");
            var ladder = BuildArmyProp("Ladder", "Ladder");
            var ram = BuildArmyProp("Ram", "Ram");
            var camp = BuildCamp();
            var catapult = BuildTrebuchet(m, fireball, marker, spearman);
            var bow = BuildBowRig(m);
            var plane = BuildPlanePrefab(m);
            var lidar = BuildLidarPrefab(m);
            BuildAppIcon();
            BuildScene(m, castle, catapult, arrow, bow, plane, lidar);
            WireRaid(soldier, ladder, ram, camp);
            AssetDatabase.SaveAssets();
            Debug.Log("[Siege] Build All complete");
        }

        // ---------------------------------------------------------------- settings
        static void SetupLayers()
        {
            var tm = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tm.FindProperty("layers");
            void Set(int i, string n) => layers.GetArrayElementAtIndex(i).stringValue = n;
            Set(Layers.Castle, "Castle");
            Set(Layers.Enemy, "Enemy");
            Set(Layers.Projectile, "Projectile");
            Set(Layers.Debris, "Debris");
            Set(Layers.Environment, "Environment");
            tm.ApplyModifiedPropertiesWithoutUndo();
            Physics.IgnoreLayerCollision(Layers.Projectile, Layers.Projectile);
            Physics.IgnoreLayerCollision(Layers.Projectile, Layers.Debris);
        }

        static void ImportSettings()
        {
            foreach (var path in Directory.GetFiles(Root + "/Textures", "*.png"))
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath(path);
                bool ui = Path.GetFileName(path).StartsWith("ui_");
                bool noise = path.Contains("noise") || path.Contains("fx_dot");   // tiled (fire surface, dotted aim arc)
                ti.textureType = TextureImporterType.Default;
                ti.alphaIsTransparency = !path.Contains("noise");
                ti.sRGBTexture = !path.Contains("noise");   // data texture: keep values linear
                ti.wrapMode = noise ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
                ti.mipmapEnabled = !ui;
                ti.isReadable = false;
                ti.textureCompression = TextureImporterCompression.CompressedHQ;
                ti.SaveAndReimport();
            }
            foreach (var path in Directory.GetFiles(Root + "/Models", "*.fbx"))
            {
                var mi = (ModelImporter)AssetImporter.GetAtPath(path);
                mi.materialImportMode = ModelImporterMaterialImportMode.None;
                mi.isReadable = true;
                mi.importAnimation = false;
                mi.animationType = ModelImporterAnimationType.None;
                mi.importCameras = mi.importLights = false;
                mi.SaveAndReimport();
            }
            foreach (var path in Directory.GetFiles(Root + "/Audio", "*.wav"))
            {
                var ai = (AudioImporter)AssetImporter.GetAtPath(path);
                var s = ai.defaultSampleSettings;
                bool longClip = path.Contains("loop") || path.Contains("crackle") || path.Contains("victory") || path.Contains("defeat") || path.Contains("horn");
                s.loadType = longClip ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
                s.compressionFormat = AudioCompressionFormat.Vorbis;
                s.quality = 0.7f;
                ai.defaultSampleSettings = s;
                ai.SaveAndReimport();
            }
            s_Color = AssetDatabase.LoadAssetAtPath<Material>(ColorMatPath);
        }

        // ---------------------------------------------------------------- materials
        static Material Mat(string name, string shader)
        {
            var path = $"{Mats}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find(shader));
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = Shader.Find(shader);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Texture2D Tex(string n) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{Root}/Textures/{n}.png");

        static Material FxMat(string name, string tex, bool additive, Color tint, float soft = 1.5f)
        {
            var m = Mat(name, "Siege/Particle");
            m.SetTexture("_MainTex", Tex(tex));
            m.SetColor("_Tint", tint);
            m.SetFloat("_SrcBlend", additive ? (float)BlendMode.One : (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SoftEdge", soft);
            m.renderQueue = 3000;
            m.enableInstancing = true;
            return m;
        }

        static Dictionary<string, Material> BuildMaterials()
        {
            var d = new Dictionary<string, Material>
            {
                ["Glow"] = FxMat("FX_Glow", "fx_dot", true, Color.white),
                ["FireHalo"] = FxMat("FX_FireHalo", "fx_dot", true, new Color(2.6f, 1.2f, 0.35f, 0.9f)),
                ["Flame"] = FxMat("FX_Flame", "fx_flame", true, Color.white),
                ["Smoke"] = FxMat("FX_Smoke", "fx_smoke", false, Color.white),
                ["Spark"] = FxMat("FX_Spark", "fx_spark", true, Color.white),
                ["Ring"] = FxMat("FX_Ring", "fx_ring", true, Color.white),
                ["Chip"] = FxMat("FX_Chip", "fx_chip", false, Color.white),
                ["Scorch"] = FxMat("FX_Scorch", "fx_scorch", false, Color.white, 0.01f),
                ["Trail"] = FxMat("FX_Trail", "fx_dot", false, new Color(1f, 1f, 1f, 1f)),
                ["Dots"] = FxMat("FX_Dots", "fx_dot", false, Color.white, 0.01f),
                ["Reticle"] = FxMat("FX_Reticle", "ui_ring", true, new Color(1.6f, 1.3f, 0.6f, 1f), 0.01f),
                ["Marker"] = FxMat("FX_Marker", "fx_dot", false, new Color(0f, 0f, 0f, 0.5f), 0.01f),
                ["Rope"] = FxMat("FX_Rope", "fx_chip", false, new Color(0.62f, 0.5f, 0.36f, 1f), 0.01f),
                ["BowString"] = FxMat("FX_BowString", "fx_chip", false, new Color(0.92f, 0.88f, 0.78f, 1f), 0.01f),
            };
            d["Scorch"].renderQueue = 2900;
            var fire = Mat("FireCore", "Siege/FireCore");
            fire.SetTexture("_Noise", Tex("fx_noise"));
            d["FireCore"] = fire;
            d["Shadow"] = Mat("ShadowCatcher", "Siege/ShadowCatcher");
            d["Shadow"].SetFloat("_ShadowStrength", 0.5f);
            d["Grid"] = Mat("PlaneGrid", "Siege/PlaneGrid");

            // Double-sided copy of the Kingdom palette material for open-shell rubble chunks.
            var debrisPath = $"{Mats}/DebrisColor.mat";
            s_Debris = AssetDatabase.LoadAssetAtPath<Material>(debrisPath);
            if (s_Debris == null)
            {
                s_Debris = new Material(s_Color);
                AssetDatabase.CreateAsset(s_Debris, debrisPath);
            }
            s_Debris.CopyPropertiesFromMaterial(s_Color);
            s_Debris.SetFloat("_Cull", 0f);
            s_Debris.doubleSidedGI = true;
            s_Debris.enableInstancing = true;
            EditorUtility.SetDirty(s_Debris);
            d["Debris"] = s_Debris;

            var table = Mat("SimTable", "Universal Render Pipeline/Lit");
            table.SetColor("_BaseColor", new Color(0.52f, 0.38f, 0.27f));
            table.SetFloat("_Smoothness", 0.35f);
            d["Table"] = table;
            s_Color.enableInstancing = true;
            return d;
        }

        // ---------------------------------------------------------------- helpers
        static GameObject Model(string path)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true)) r.sharedMaterial = s_Color;
            return go;
        }

        static GameObject Kit(string name, Transform parent, Vector3 localPos, float yaw = 0f, float scale = 1f)
        {
            var go = Model(Kingdom + name + ".prefab");
            foreach (var a in go.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(a);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            go.transform.localScale = Vector3.one * scale;
            return go;
        }

        static Transform Find(GameObject root, string name) =>
            root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t != root.transform && t.name == name);

        static void SetLayer(GameObject go, int layer)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        }

        static T Save<T>(GameObject go, string name) where T : Component
        {
            var path = $"{Prefabs}/{name}.prefab";
            var p = PrefabUtility.SaveAsPrefabAsset(go, path);
            if (p == null)
            {
                // An older prefab whose source model was removed can block overwriting; replace it.
                AssetDatabase.DeleteAsset(path);
                p = PrefabUtility.SaveAsPrefabAsset(go, path);
            }
            Object.DestroyImmediate(go);
            return p.GetComponent<T>();
        }

        static GameObject SaveGo(GameObject go, string name)
        {
            var p = PrefabUtility.SaveAsPrefabAsset(go, $"{Prefabs}/{name}.prefab");
            Object.DestroyImmediate(go);
            return p;
        }

        static void BoxFromMesh(GameObject go)
        {
            var mf = go.GetComponent<MeshFilter>();
            if (!mf || !mf.sharedMesh) return;
            var b = go.AddComponent<BoxCollider>();
            b.center = mf.sharedMesh.bounds.center;
            b.size = mf.sharedMesh.bounds.size;
        }

        // ---------------------------------------------------------------- small prefabs
        static Arrow BuildArrow(Dictionary<string, Material> m)
        {
            var go = Model(Root + "/Models/Arrow.fbx");
            go.name = "Arrow";
            // A single-object FBX imports with the mesh on the root itself.
            var mesh = Find(go, "Arrow") ?? go.transform;
            var root = new GameObject("Arrow");
            mesh.SetParent(root.transform, false);
            mesh.localPosition = Vector3.zero;
            mesh.name = "Mesh";
            if (mesh != go.transform) Object.DestroyImmediate(go);
            var arrow = root.AddComponent<Arrow>();
            var trailGo = new GameObject("Trail");
            trailGo.transform.SetParent(root.transform, false);
            trailGo.transform.localPosition = new Vector3(0, 0, 0.15f);
            var tr = trailGo.AddComponent<TrailRenderer>();
            tr.sharedMaterial = m["Trail"];
            // Faint air streak, not a glowing tracer.
            tr.time = 0.07f;
            tr.minVertexDistance = 0.3f;
            tr.widthCurve = new AnimationCurve(new Keyframe(0, 0.035f), new Keyframe(1, 0f));
            tr.colorGradient = Grad(new Color(0.85f, 0.85f, 0.85f, 0.18f), new Color(0.85f, 0.85f, 0.85f, 0f));
            tr.shadowCastingMode = ShadowCastingMode.Off;
            tr.emitting = false;
            arrow.trail = tr;
            arrow.tipLength = 1.15f;
            root.layer = Layers.Projectile;
            return Save<Arrow>(root, "Arrow");
        }

        static Gradient Grad(Color a, Color b)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(a, 0), new GradientColorKey(b, 1) }, new[] { new GradientAlphaKey(a.a, 0), new GradientAlphaKey(b.a, 1) });
            return g;
        }

        static Fireball BuildFireball(Dictionary<string, Material> m)
        {
            var root = new GameObject("Fireball");
            root.layer = Layers.Projectile;
            var rb = root.AddComponent<Rigidbody>();
            rb.mass = 20f;
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            var col = root.AddComponent<SphereCollider>();
            col.radius = 0.55f;
            col.enabled = false;
            var core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(core.GetComponent<Collider>());
            core.name = "Core";
            core.transform.SetParent(root.transform, false);
            core.transform.localScale = Vector3.one * 1.15f;
            var mr = core.GetComponent<MeshRenderer>();
            mr.sharedMaterial = m["FireCore"];
            mr.shadowCastingMode = ShadowCastingMode.Off;
            var halo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(halo.GetComponent<Collider>());
            halo.name = "Halo";
            halo.transform.SetParent(root.transform, false);
            halo.transform.localScale = Vector3.one * 4.2f;
            halo.AddComponent<Billboard>();
            var hr = halo.GetComponent<MeshRenderer>();
            hr.sharedMaterial = m["FireHalo"];
            hr.shadowCastingMode = ShadowCastingMode.Off;
            var lightGo = new GameObject("Light");
            lightGo.transform.SetParent(root.transform, false);
            var l = lightGo.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1f, 0.55f, 0.2f);
            l.range = 16f;
            l.intensity = 5f;
            l.shadows = LightShadows.None;
            var fb = root.AddComponent<Fireball>();
            fb.glow = l;
            fb.core = core.transform;
            return Save<Fireball>(root, "Fireball");
        }

        static GameObject BuildMarker(Dictionary<string, Material> m, string name, Material mat, bool landing)
        {
            var root = new GameObject(name);
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(q.GetComponent<Collider>());
            q.transform.SetParent(root.transform, false);
            q.transform.localRotation = Quaternion.Euler(90, 0, 0);
            var r = q.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            if (landing)
            {
                root.transform.localScale = Vector3.one * 5f;
                root.AddComponent<LandingMarker>();
            }
            return SaveGo(root, name);
        }

        static GameObject BuildRubble(string kit, float scale)
        {
            var r = Kit(kit, null, Vector3.zero, 0, scale);
            r.name = "Rubble_" + kit;
            foreach (var col in r.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(col);
            var mf = r.GetComponentInChildren<MeshFilter>();
            var mc = mf.gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = mf.sharedMesh;
            mc.convex = true;
            var rb = r.AddComponent<Rigidbody>();
            rb.mass = 3f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            r.AddComponent<Debris>();
            SetLayer(r, Layers.Debris);
            return SaveGo(r, r.name);
        }

        static GameObject BuildCoin()
        {
            var c = Kit("coin_001", null, Vector3.zero, 0, 0.55f);
            foreach (var col in c.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(col);
            c.AddComponent<BoxCollider>().size = new Vector3(0.9f, 0.9f, 0.25f);
            var rb = c.AddComponent<Rigidbody>();
            rb.mass = 0.2f;
            return SaveGo(c, "Coin");
        }

        // ---------------------------------------------------------------- castle
        static Castle BuildCastle(GameObject coin)
        {
            var root = new GameObject("Castle");
            var castle = root.AddComponent<Castle>();

            var island = Kit("ground_002", root.transform, Vector3.zero, 0, 1f);
            island.name = "Island";
            island.transform.localScale = new Vector3(2.8f, 0.25f, 2.8f);   // low mound: soldiers can walk up to the gate
            SetLayer(island, Layers.Environment);
            castle.island = island.transform;
            Physics.SyncTransforms();

            float Ground(float x, float z)
            {
                var o = root.transform.TransformPoint(new Vector3(x, 50f, z));
                return Physics.Raycast(o, Vector3.down, out var hit, 100f, 1 << Layers.Environment) ? hit.point.y : 2.3f;
            }

            var pieces = new GameObject("Pieces").transform;
            pieces.SetParent(root.transform, false);
            const float D = 6f;
            float baseY = Ground(0, 0) - 0.15f;

            CastlePiece Piece(string kit, PieceKind kind, Vector3 pos, float yaw, float scale, float hp, int cells)
            {
                var go = Kit(kit, pieces, new Vector3(pos.x, baseY, pos.z), yaw, scale);
                go.name = kind + "_" + kit;
                SetLayer(go, Layers.Castle);
                var p = go.AddComponent<CastlePiece>();
                p.kind = kind;
                p.maxHealth = hp;
                p.shatterCells = cells * 2;
                p.debrisMaterial = s_Debris;
                return p;
            }

            // Clash Royale layout: the keep (king tower, where the player stands) at the back, flanked by
            // two towers; two more towers and the gate at the front, walls down the sides, courtyard between.
            var towers = new List<CastlePiece>();
            foreach (var (x, z) in new[] { (-6.6f, -6.4f), (6.6f, -6.4f), (-D, D), (D, D) })
            {
                float yaw = Mathf.Atan2(x, z) * Mathf.Rad2Deg;
                towers.Add(Piece("tower_blue_001", PieceKind.Tower, new Vector3(x, 0, z), yaw, 0.92f, 3f, 10));
            }
            Piece("wall_002", PieceKind.Gate, new Vector3(0, 0, D), 0, 0.95f, 14f, 9);
            Piece("wall_001", PieceKind.Wall, new Vector3(-D, 0, 0), -90, 0.95f, 2.5f, 8);
            Piece("wall_001", PieceKind.Wall, new Vector3(D, 0, 0), 90, 0.95f, 2.5f, 8);
            var keep = Piece("tower_blue_001", PieceKind.Keep, new Vector3(0, 0, -KeepBack), 0, 1.3f, 5f, 12);
            castle.keep = keep;
            castle.keepTopHeight = keep.GetComponent<Renderer>().bounds.max.y - root.transform.position.y;

            // Flags on every tower, crown on the keep: they tumble off when their tower falls.
            foreach (var t in towers)
            {
                var flag = Kit("flag_blue_001", t.transform, new Vector3(0, 6.7f, 0), Random.Range(0, 360f), 0.85f);
                SetLayer(flag, Layers.Castle);
                foreach (var c in flag.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                t.dropOnDestroy = new[] { flag.transform };
            }
            // The player stands on the keep, so no flagpole through the view; the crown sits at your feet.
            var crown = Kit("crown_001", keep.transform, new Vector3(0, 6.75f, 1.2f), 20, 1.25f);
            foreach (var c in crown.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            SetLayer(crown, Layers.Castle);
            keep.dropOnDestroy = new[] { crown.transform };

            // Courtyard treasure
            var treasure = new GameObject("Treasure").transform;
            treasure.SetParent(root.transform, false);
            Kit("chest_gold_001", treasure, new Vector3(2.6f, Ground(2.6f, 3.4f), 3.4f), -25, 0.7f);
            Kit("coin_001", treasure, new Vector3(1.9f, Ground(1.9f, 3.9f) + 0.3f, 3.9f), 60, 0.4f);
            Kit("coin_001", treasure, new Vector3(3.2f, Ground(3.2f, 4.2f) + 0.3f, 4.2f), -10, 0.4f);
            SetLayer(treasure.gameObject, Layers.Environment);
            castle.treasure = treasure;

            // Diorama dressing around the walls
            var decor = new GameObject("Decor").transform;
            decor.SetParent(island.transform.parent, false);
            void Dress(string kit, float x, float z, float yaw, float s)
            {
                var g = Kit(kit, decor, new Vector3(x, Ground(x, z) - 0.1f, z), yaw, s);
                SetLayer(g, Layers.Environment);
            }
            Dress("tree_001", -10.6f, -9.2f, 20, 0.55f);
            Dress("tree_002", 10.4f, -9.8f, 140, 0.5f);
            Dress("fir_001", -11.2f, 3.5f, 0, 0.5f);
            Dress("fir_001", 11.3f, -3.2f, 70, 0.42f);
            Dress("tree_001", 10.2f, 8.8f, 200, 0.42f);
            Dress("fir_001", -2.5f, -12.2f, 0, 0.38f);
            Dress("bush_001", -8.6f, 10.8f, 30, 0.5f);
            Dress("bush_002", 11.8f, 4.5f, 0, 0.45f);
            Dress("bush_001", -12.2f, -2.6f, 80, 0.45f);
            Dress("bush_002", 4.2f, -12.4f, 10, 0.45f);
            Dress("bush_001", 7.5f, -12f, 10, 0.35f);
            Dress("stone_001", -6.2f, -12.4f, 30, 0.35f);
            Dress("stone_003", 12.5f, 0.8f, 0, 0.3f);
            Dress("stone_002", 7.6f, 12.1f, 60, 0.28f);
            Dress("barrel_001", 3.6f, 9.4f, 0, 0.55f);
            Dress("barrel_002", 4.6f, 8.9f, 40, 0.5f);
            Dress("box_001", -3.9f, 9.3f, 15, 0.55f);
            Dress("fence_001", -7.2f, 11.6f, 8, 0.55f);
            Dress("fence_001", 8.3f, 11.2f, -12, 0.55f);
            for (int i = 0; i < 10; i++)
            {
                float a = i * 36f * Mathf.Deg2Rad + 0.3f;
                float r = 11.5f + (i % 3) * 0.6f;
                Dress("flower_00" + (1 + i % 6), Mathf.Cos(a) * r, Mathf.Sin(a) * r, i * 47, 0.5f);
            }

            BuildTown(root.transform);
            castle.ruinPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Kingdom + "ruin_001.prefab");
            castle.coinPrefab = coin;
            castle.rubblePrefabs = new[] { BuildRubble("ruin_001", 0.6f), BuildRubble("ruin_002", 0.6f), BuildRubble("stone_001", 0.22f), BuildRubble("stone_003", 0.25f), BuildRubble("stone_002", 0.18f) };
            return Save<Castle>(root, "Castle");
        }

        // ---------------------------------------------------------------- town on the real floor
        // Houses, market and allied watchtowers ring the castle (front corridor left open for the battle).
        // They are destructible castle pieces: stray fireballs set the town ablaze.
        static void BuildTown(Transform castleRoot)
        {
            var town = new GameObject("Town").transform;
            town.SetParent(castleRoot, false);

            void Building(string name, float angleDeg, float radius, float s, float hp = 1.5f)
            {
                var model = Model(Root + "/Models/Town.fbx");
                var b = Find(model, name);
                b.SetParent(town, false);
                Object.DestroyImmediate(model);
                float a = angleDeg * Mathf.Deg2Rad;
                b.localPosition = new Vector3(Mathf.Sin(a) * radius, 0, Mathf.Cos(a) * radius);
                b.localRotation = Quaternion.Euler(0, angleDeg + 180f + Random.Range(-12f, 12f), 0);   // face the castle
                b.localScale = Vector3.one * s;
                b.gameObject.layer = Layers.Castle;
                b.gameObject.AddComponent<MeshCollider>().sharedMesh = b.GetComponent<MeshFilter>().sharedMesh;
                var p = b.gameObject.AddComponent<CastlePiece>();
                p.kind = PieceKind.House;
                p.maxHealth = hp;
                p.debrisMaterial = s_Debris;
            }

            Random.InitState(42);
            string[] kinds = { "House_Cottage", "House_Tall", "House_Turret", "House_Cottage", "Market_Stall", "House_Tall" };
            int k = 0;
            // inner ring (sides and back), outer ring a little further out
            for (float ang = 55f; ang <= 305f; ang += 26f) Building(kinds[k++ % kinds.Length], ang + Random.Range(-5f, 5f), 19.5f + Random.Range(-1f, 1f), Random.Range(0.95f, 1.15f));
            for (float ang = 68f; ang <= 292f; ang += 32f) Building(kinds[k++ % kinds.Length], ang + Random.Range(-5f, 5f), 27f + Random.Range(-1.5f, 1.5f), Random.Range(1.0f, 1.25f));
            Building("Town_Well", 120f, 15.5f, 0.9f, 1f);
            Building("Market_Stall", 238f, 15.5f, 0.9f, 1f);

            // Allied watchtowers out on the flanks - "other castles" of the realm
            foreach (var x in new[] { -21f, 21f })
            {
                var t = Kit("tower_001", town, new Vector3(x, 0, 11f), x < 0 ? 40 : -40, 1.0f);
                t.name = "Watchtower";
                SetLayer(t, Layers.Castle);
                var p = t.AddComponent<CastlePiece>();
                p.kind = PieceKind.Tower;
                p.maxHealth = 3f;
                p.debrisMaterial = s_Debris;
                var flag = Kit("flag_blue_001", t.transform, new Vector3(0, 7.2f, 0), 0, 0.85f);
                foreach (var c in flag.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                SetLayer(flag, Layers.Castle);
                p.dropOnDestroy = new[] { flag.transform };
            }
            // A few trees on the floor between houses
            for (int i = 0; i < 8; i++)
            {
                float a = (70f + i * 31f) * Mathf.Deg2Rad;
                var tr = Kit(i % 2 == 0 ? "fir_001" : "tree_001", town, new Vector3(Mathf.Sin(a) * 23.5f, 0, Mathf.Cos(a) * 23.5f), i * 53, 0.5f);
                SetLayer(tr, Layers.Environment);
            }
        }

        // ---------------------------------------------------------------- catapult + knight
        // ---------------------------------------------------------------- army (Army.fbx, Stream B)
        static Transform Take(string fbx, string name)
        {
            var model = Model(fbx);
            var t = Find(model, name) ?? (model.name == name ? model.transform : null);
            if (t == null) { Object.DestroyImmediate(model); throw new System.Exception($"[Siege] {fbx} has no object '{name}'"); }
            t.SetParent(null, true);
            if (t.gameObject != model) Object.DestroyImmediate(model);
            return t;
        }

        // Life-size enemy footman: parts from Army.fbx re-parented into an animatable rig at the feet.
        static Soldier BuildSoldier(string prefabName, string weaponName)
        {
            const string fbx = Root + "/Models/Army.fbx";
            var root = new GameObject(prefabName);
            var s = root.AddComponent<Soldier>();
            Transform Part(string n) { var t = Take(fbx, n); return t; }
            s.hips = Part("Soldier_Hips"); s.torso = Part("Soldier_Torso"); s.head = Part("Soldier_Head");
            s.armL = Part("Soldier_ArmL"); s.armR = Part("Soldier_ArmR"); s.legL = Part("Soldier_LegL"); s.legR = Part("Soldier_LegR");
            s.weapon = Part(weaponName); s.shield = Part("Soldier_Shield");
            // Pieces come out of the FBX at their authored positions; re-centre the whole figure on the root.
            var hipsPos = s.hips.position;
            var offset = new Vector3(hipsPos.x, 0f, hipsPos.z);
            foreach (var t in new[] { s.hips, s.torso, s.head, s.armL, s.armR, s.legL, s.legR, s.weapon, s.shield }) t.position -= offset;
            s.hips.SetParent(root.transform, true);
            s.legL.SetParent(s.hips, true); s.legR.SetParent(s.hips, true); s.torso.SetParent(s.hips, true);
            s.head.SetParent(s.torso, true); s.armL.SetParent(s.torso, true); s.armR.SetParent(s.torso, true);
            s.weapon.SetParent(s.armR, true); s.shield.SetParent(s.armL, true);
            foreach (var t in new[] { s.hips, s.torso, s.head, s.armL, s.armR, s.legL, s.legR }) BoxFromMesh(t.gameObject);
            SetLayer(root, Layers.Enemy);
            return Save<Soldier>(root, prefabName);
        }

        static GameObject BuildArmyProp(string prefabName, string objectName)
        {
            var t = Take(Root + "/Models/Army.fbx", objectName);
            var root = new GameObject(prefabName);
            t.SetParent(root.transform, false);
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            BoxFromMesh(t.gameObject);
            SetLayer(root, Layers.Enemy);
            return SaveGo(root, prefabName);
        }

        // Siege camp beside the trebuchet: tents, campfire, supplies and banners. Soldiers march out of it.
        static GameObject BuildCamp()
        {
            const string fbx = Root + "/Models/Army.fbx";
            var root = new GameObject("SiegeCamp");
            void Put(string n, Vector3 p, float yaw, float s = 1f)
            {
                var t = Take(fbx, n);
                t.SetParent(root.transform, false);
                t.localPosition = p; t.localRotation = Quaternion.Euler(0, yaw, 0); t.localScale = Vector3.one * s;
                t.gameObject.AddComponent<MeshCollider>().sharedMesh = t.GetComponent<MeshFilter>().sharedMesh;
            }
            Put("Tent", new Vector3(0, 0, -3f), 0);
            Put("Tent_Ridge", new Vector3(-6.5f, 0, -1.5f), 70);
            Put("Tent_Ridge", new Vector3(6.5f, 0, -2.5f), -60);
            Put("Tent_Ridge", new Vector3(-3.5f, 0, -8.5f), 15);
            Put("Tent", new Vector3(5f, 0, -9f), 30, 0.85f);
            Put("Campfire", new Vector3(0, 0, 3f), 0);
            Put("Crate_Stack", new Vector3(-4f, 0, 3.5f), 20);
            Put("Crate_Stack", new Vector3(4.5f, 0, 2.5f), -35);
            var fire = new GameObject("Fire");
            fire.transform.SetParent(root.transform, false);
            fire.transform.localPosition = new Vector3(0, 0.3f, 3f);
            fire.AddComponent<FireSource>().scale = 0.9f;
            foreach (var (x, z) in new[] { (-8f, 4f), (8f, 4f), (0f, -13f) })
            {
                var f = Kit("flag_red_001", root.transform, new Vector3(x, 0, z), Random.Range(0, 360f), 1.3f);
                foreach (var c in f.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            }
            var spawn = new GameObject("SpawnPoint").transform;
            spawn.SetParent(root.transform, false);
            spawn.localPosition = new Vector3(0, 0, 6f);
            SetLayer(root, Layers.Environment);
            return SaveGo(root, "SiegeCamp");
        }

        // ---------------------------------------------------------------- trebuchet (Trebuchet.fbx, Stream A)
        static Trebuchet BuildTrebuchet(Dictionary<string, Material> m, Fireball fireball, GameObject marker, Soldier crewPrefab)
        {
            var go = Model(Root + "/Models/Trebuchet.fbx");
            go.name = "Trebuchet";
            var t = go.AddComponent<Trebuchet>();
            var body = new GameObject("Body").transform;
            body.SetParent(go.transform, false);
            foreach (var n in new[] { "Frame", "Arm", "Windlass", "Pouch", "RopeWinch", "BannerSpot" })
                Find(go, n).SetParent(body, true);
            var arm = Find(go, "Arm");
            foreach (var n in new[] { "SlingTip", "RopeArm", "Counterweight" }) Find(go, n).SetParent(arm, true);
            t.body = body;
            t.arm = arm;
            t.counterweight = Find(go, "Counterweight");
            t.windlass = Find(go, "Windlass");
            t.slingTip = Find(go, "SlingTip");
            t.pouch = Find(go, "Pouch");
            t.ropeWinch = Find(go, "RopeWinch");
            t.ropeArm = Find(go, "RopeArm");
            t.frame = Find(go, "Frame").GetComponent<MeshFilter>();
            t.debrisMaterial = s_Debris;
            t.fireballPrefab = fireball;
            t.landingMarkerPrefab = marker;
            foreach (var n in new[] { "Frame", "Arm", "Counterweight", "Windlass" })
            {
                var mf = Find(go, n).GetComponent<MeshFilter>();
                mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
            }
            LineRenderer Line(string n, float width)
            {
                var lgo = new GameObject(n);
                lgo.transform.SetParent(go.transform, false);
                var lr = lgo.AddComponent<LineRenderer>();
                lr.positionCount = 3;
                lr.widthMultiplier = width;
                lr.sharedMaterial = m["Rope"];
                lr.numCapVertices = 2;
                lr.shadowCastingMode = ShadowCastingMode.On;
                return lr;
            }
            t.sling = Line("Sling", 0.08f);
            t.rope = Line("Rope", 0.12f);

            var banner = Kit("flag_red_001", body, Vector3.zero, 0, 1.4f);
            banner.transform.position = Find(go, "BannerSpot").position;
            foreach (var c in banner.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            t.banner = banner.transform;

            // Powder kegs and spare stone shot by the ammo spot
            var ammo = Find(go, "AmmoSpot");
            var barrels = new List<TNTBarrel>();
            void Keg(string kit, Vector3 off, float yaw, float s)
            {
                var k = Kit(kit, go.transform, ammo.localPosition + off, yaw, s);
                barrels.Add(k.AddComponent<TNTBarrel>());
                k.AddComponent<AimTarget>().offset = Vector3.up * 0.5f;
            }
            Keg("box_tnt_001", new Vector3(0, 0, 0), 10, 0.95f);
            Keg("box_tnt_002", new Vector3(1.1f, 0, 0.4f), -20, 0.9f);
            Keg("barrel_001", new Vector3(0.2f, 0, -1.2f), 0, 0.85f);
            Keg("box_tnt_001", new Vector3(0.45f, 0.74f, -0.1f), 50, 0.8f);
            t.barrels = barrels.ToArray();
            for (int i = 0; i < 5; i++)
                Kit("stone_003", go.transform, ammo.localPosition + new Vector3(-1.2f + (i % 3) * 0.55f, 0.15f + (i / 3) * 0.4f, 1.4f + (i % 2) * 0.35f), i * 70, 0.3f);

            var knight = BuildKnight();
            knight.transform.SetParent(go.transform, false);
            knight.transform.localPosition = Find(go, "KnightSpot").localPosition;
            knight.transform.localRotation = Quaternion.Euler(0, -25, 0);
            knight.transform.localScale = Vector3.one * 0.75f;   // life size (~1.8 m)
            t.knight = knight;

            var crew = new List<Soldier>();
            var winch = Find(go, "RopeWinch").position;
            for (int i = 1; i <= 4; i++)
            {
                var spot = Find(go, "CrewSpot" + i);
                if (!spot) continue;
                var c = ((GameObject)PrefabUtility.InstantiatePrefab(crewPrefab.gameObject)).GetComponent<Soldier>();
                PrefabUtility.UnpackPrefabInstance(c.gameObject, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                c.transform.SetParent(go.transform, false);
                c.transform.localPosition = spot.localPosition;
                var look = winch - spot.position; look.y = 0;
                if (look.sqrMagnitude > 0.01f) c.transform.rotation = Quaternion.LookRotation(look);
                crew.Add(c);
            }
            t.crew = crew.ToArray();

            SetLayer(go, Layers.Enemy);
            return Save<Trebuchet>(go, "Trebuchet");
        }

        // Scene wiring for the raid: wave manager prefabs and the siege camp.
        static void WireRaid(Soldier soldier, GameObject ladder, GameObject ram, GameObject camp)
        {
            var scene = EditorSceneManager.OpenScene($"{Root}/Scenes/Main.unity");
            var game = Object.FindAnyObjectByType<GameManager>();
            var waves = game.GetComponent<WaveManager>() ?? game.gameObject.AddComponent<WaveManager>();
            waves.soldierPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{Prefabs}/Soldier.prefab").GetComponent<Soldier>();
            waves.ladderPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{Prefabs}/Ladder.prefab");
            waves.ramPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{Prefabs}/Ram.prefab");
            game.waves = waves;
            game.campPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{Prefabs}/SiegeCamp.prefab");
            game.trebuchetPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{Prefabs}/Trebuchet.prefab").GetComponent<Trebuchet>();
            EditorUtility.SetDirty(game);
            EditorSceneManager.SaveScene(scene);
        }

        static Knight BuildKnight()
        {
            var go = Model(Root + "/Models/Knight.fbx");
            go.name = "Knight";
            var k = go.AddComponent<Knight>();
            k.hips = Find(go, "Hips"); k.torso = Find(go, "Torso"); k.head = Find(go, "Head");
            k.armL = Find(go, "Arm_L"); k.armR = Find(go, "Arm_R"); k.legL = Find(go, "Leg_L"); k.legR = Find(go, "Leg_R");
            k.cape = Find(go, "Cape");
            k.legL.SetParent(k.hips, true); k.legR.SetParent(k.hips, true); k.torso.SetParent(k.hips, true);
            k.head.SetParent(k.torso, true); k.armL.SetParent(k.torso, true); k.armR.SetParent(k.torso, true); k.cape.SetParent(k.torso, true);
            foreach (var t in new[] { k.hips, k.torso, k.head, k.armL, k.armR, k.legL, k.legR }) BoxFromMesh(t.gameObject);
            return k;
        }

        // ---------------------------------------------------------------- bow view model
        const string PackBow = "Assets/StylizedCharacter/Prefabs/Item/Weapon/Bow/Bow_Newbie_03_Bl.prefab";

        static BowController BuildBowRig(Dictionary<string, Material> m)
        {
            // View-model root; the pack bow lies along Z with the archer on +Y, so tip it upright facing forward.
            var go = new GameObject("BowRig");
            var ctrl = go.AddComponent<BowController>();
            var bow = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PackBow));
            PrefabUtility.UnpackPrefabInstance(bow, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            foreach (var a in bow.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(a);
            // The pack's demo scripts aren't imported (they don't compile on Unity 6.6); drop their components.
            foreach (var t in bow.GetComponentsInChildren<Transform>(true)) GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
            bow.name = "Model";
            bow.transform.SetParent(go.transform, false);
            bow.transform.localRotation = Quaternion.Euler(-90, 0, 0);
            ctrl.limbTop = Find(bow, "Arm_Up");
            ctrl.limbBottom = Find(bow, "Arm_Low");
            ctrl.stringBone = Find(bow, "String");
            go.transform.localScale = Vector3.one * 0.12f;
            ctrl.bowRoot = go.transform;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                r.shadowCastingMode = ShadowCastingMode.Off;
                if (r is SkinnedMeshRenderer smr) { smr.updateWhenOffscreen = true; smr.quality = SkinQuality.Bone2; }
            }

            var arrowModel = Model(Root + "/Models/Arrow.fbx");
            var nocked = new GameObject("NockedArrow").transform;
            nocked.SetParent(go.transform, false);
            var arrowMesh = Find(arrowModel, "Arrow") ?? arrowModel.transform;
            arrowMesh.SetParent(nocked, false);
            arrowMesh.localPosition = new Vector3(0.035f, 0, 0);
            arrowMesh.localScale = Vector3.one * 0.8f;
            arrowMesh.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            if (arrowMesh != arrowModel.transform) Object.DestroyImmediate(arrowModel);
            ctrl.nockedArrow = nocked;
            return Save<BowController>(go, "BowRig");
        }

        // ---------------------------------------------------------------- AR surfaces
        static GameObject BuildPlanePrefab(Dictionary<string, Material> m)
        {
            var go = new GameObject("ARPlane", typeof(ARPlane), typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider), typeof(ARPlaneMeshVisualizer));
            var s = go.AddComponent<ARSurface>();
            s.gridMaterial = m["Grid"];
            s.shadowMaterial = m["Shadow"];
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = m["Grid"];
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = true;
            go.layer = Layers.Environment;
            return SaveGo(go, "ARPlane");
        }

        static GameObject BuildLidarPrefab(Dictionary<string, Material> m)
        {
            var go = new GameObject("LidarMesh", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
            var s = go.AddComponent<ARSurface>();
            s.isLidarMesh = true;
            s.gridMaterial = m["Shadow"];
            s.shadowMaterial = m["Shadow"];
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = m["Shadow"];
            r.shadowCastingMode = ShadowCastingMode.Off;
            go.layer = Layers.Environment;
            return SaveGo(go, "LidarMesh");
        }

        // ---------------------------------------------------------------- app icon
        // The castle diorama, floating on a sky gradient, rendered as the iOS app icon.
        static void BuildAppIcon()
        {
            PreviewRenderer.NewStage();
            var floor = GameObject.Find("Plane");
            if (floor) Object.DestroyImmediate(floor);
            var castle = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>($"{Prefabs}/Castle.prefab"));
            castle.transform.rotation = Quaternion.Euler(0, 205, 0);
            PreviewRenderer.BackgroundColor = new Color(0.42f, 0.68f, 0.96f);
            PreviewRenderer.Shoot("AppIcon", new Vector3(30f, 27f, 41f), new Vector3(0, 2.6f, 0), 36f, 1024, 1024);
            var dst = $"{Root}/Textures/AppIcon.png";
            File.Copy(Path.Combine(PreviewRenderer.OutDir, "AppIcon.png"), dst, true);
            AssetDatabase.ImportAsset(dst, ImportAssetOptions.ForceUpdate);
            var ti = (TextureImporter)AssetImporter.GetAtPath(dst);
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = false;
            ti.SaveAndReimport();
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(dst);
            PlayerSettings.SetIcons(UnityEditor.Build.NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
        }

        // ---------------------------------------------------------------- scene
        static void BuildScene(Dictionary<string, Material> m, Castle castle, Trebuchet catapult, Arrow arrow, BowController bowPrefab, GameObject planePrefab, GameObject lidarPrefab)
        {
            // Prefab asset handles can go stale after later imports; reload them by path.
            T P<T>(string n) where T : Object => AssetDatabase.LoadAssetAtPath<GameObject>($"{Prefabs}/{n}.prefab") is GameObject g
                ? (typeof(T) == typeof(GameObject) ? g as T : g.GetComponent(typeof(T)) as T) : null;
            castle = P<Castle>("Castle"); catapult = P<Trebuchet>("Trebuchet"); arrow = P<Arrow>("Arrow");
            bowPrefab = P<BowController>("BowRig"); planePrefab = P<GameObject>("ARPlane"); lidarPrefab = P<GameObject>("LidarMesh");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.78f, 0.82f, 0.9f);
            RenderSettings.ambientEquatorColor = new Color(0.62f, 0.6f, 0.58f);
            RenderSettings.ambientGroundColor = new Color(0.35f, 0.32f, 0.3f);
            RenderSettings.skybox = null;

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.4f;
            sun.color = new Color(1f, 0.96f, 0.9f);
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.85f;
            sun.transform.rotation = Quaternion.Euler(64, -30, 0);   // indoor lighting comes from above

            // AR session + origin
            var session = new GameObject("AR Session", typeof(ARSession), typeof(ARInputManager)).GetComponent<ARSession>();
            var originGo = new GameObject("XR Origin", typeof(XROrigin), typeof(ARRaycastManager), typeof(ARPlaneManager));
            var origin = originGo.GetComponent<XROrigin>();
            var offset = new GameObject("Camera Offset").transform;
            offset.SetParent(originGo.transform, false);
            var camGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener), typeof(ARCameraManager), typeof(ARCameraBackground), typeof(AROcclusionManager), typeof(TrackedPoseDriver));
            camGo.tag = "MainCamera";
            // Screen shake gets its own node so it never fights XR Origin's camera-offset handling.
            var shake = new GameObject("Shake").transform;
            shake.SetParent(offset, false);
            camGo.transform.SetParent(shake, false);
            var cam = camGo.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            var camData = cam.GetUniversalAdditionalCameraData();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.None;
            var tpd = camGo.GetComponent<TrackedPoseDriver>();
            var pos = new InputAction("Position", binding: "<XRHMD>/centerEyePosition", expectedControlType: "Vector3");
            pos.AddBinding("<HandheldARInputDevice>/devicePosition");
            var rot = new InputAction("Rotation", binding: "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion");
            rot.AddBinding("<HandheldARInputDevice>/deviceRotation");
            tpd.positionInput = new InputActionProperty(pos);
            tpd.rotationInput = new InputActionProperty(rot);
            var occ = camGo.GetComponent<AROcclusionManager>();
            occ.requestedEnvironmentDepthMode = UnityEngine.XR.ARSubsystems.EnvironmentDepthMode.Best;
            occ.environmentDepthTemporalSmoothingRequested = true;
            occ.requestedOcclusionPreferenceMode = UnityEngine.XR.ARSubsystems.OcclusionPreferenceMode.PreferEnvironmentOcclusion;
            var camMgr = camGo.GetComponent<ARCameraManager>();
            camMgr.requestedLightEstimation = UnityEngine.XR.ARFoundation.LightEstimation.AmbientIntensity | UnityEngine.XR.ARFoundation.LightEstimation.AmbientColor;
            // ARCameraManager migrates the legacy m_LightEstimationMode field on load; left at its default
            // (Disabled) it silently overwrites the request above. -1 marks it as already migrated.
            var camSo = new SerializedObject(camMgr);
            camSo.FindProperty("m_LightEstimationMode").intValue = -1;
            camSo.FindProperty("m_FocusMode").intValue = -1;
            camSo.ApplyModifiedPropertiesWithoutUndo();
            camMgr.autoFocusRequested = true;
            origin.CameraFloorOffsetObject = offset.gameObject;
            origin.Camera = cam;
            // Phone AR: the camera pose is already the real device pose. The default 1.12 m VR height
            // offset would lift the camera above the tracked world (content looks small and slides).
            origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Device;
            origin.CameraYOffset = 0f;
            var planes = originGo.GetComponent<ARPlaneManager>();
            planes.planePrefab = planePrefab;
            planes.requestedDetectionMode = UnityEngine.XR.ARSubsystems.PlaneDetectionMode.Horizontal;
            var meshGo = new GameObject("LiDAR Meshing", typeof(ARMeshManager));
            meshGo.transform.SetParent(originGo.transform, false);
            var meshMgr = meshGo.GetComponent<ARMeshManager>();
            meshMgr.meshPrefab = lidarPrefab.GetComponent<MeshFilter>();
            meshMgr.density = 0.4f;
            meshMgr.normals = true;
            // Real reflections/ambient from ARKit's environment texturing of the room.
            var probes = originGo.AddComponent<AREnvironmentProbeManager>();
            probes.automaticPlacementRequested = true;
            probes.environmentTextureHDRRequested = true;
            var lightMatch = originGo.AddComponent<ARLightMatch>();
            lightMatch.cameraManager = camMgr;
            lightMatch.sun = sun;

            // Simulated room for the editor / non-AR devices
            var simRig = new GameObject("SimRig").AddComponent<SimCameraRig>();
            var simOffset = new GameObject("Offset").transform;
            simOffset.SetParent(simRig.transform, false);
            var simCamGo = new GameObject("SimCamera", typeof(Camera), typeof(AudioListener));
            simCamGo.transform.SetParent(simOffset, false);
            simCamGo.tag = "MainCamera";
            var simCam = simCamGo.GetComponent<Camera>();
            simCam.clearFlags = CameraClearFlags.SolidColor;
            simCam.backgroundColor = new Color(0.62f, 0.68f, 0.74f);
            simCam.fieldOfView = 38f;   // close to the iPhone's landscape AR camera
            simCam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            simRig.cam = simCamGo.transform;
            simRig.gameObject.SetActive(false);
            var room = GameObject.CreatePrimitive(PrimitiveType.Plane);
            room.name = "SimRoom";
            room.transform.localScale = Vector3.one * 60f;
            room.GetComponent<MeshRenderer>().sharedMaterial = m["Table"];
            room.layer = Layers.Environment;
            room.SetActive(false);

            // Post-processing
            var profilePath = $"{Root}/Settings_PostFX.asset";
            AssetDatabase.DeleteAsset(profilePath);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, profilePath);
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1.2f);
            bloom.intensity.Override(0.45f);
            bloom.scatter.Override(0.65f);
            var vig = profile.Add<Vignette>(true);
            vig.intensity.Override(0.18f);
            vig.smoothness.Override(0.45f);
            vig.color.Override(Color.black);
            var ca = profile.Add<ChromaticAberration>(true);
            ca.intensity.Override(0f);
            foreach (var c in profile.components) AssetDatabase.AddObjectToAsset(c, profile);
            EditorUtility.SetDirty(profile);
            var fxGo = new GameObject("ScreenFX", typeof(Volume), typeof(ScreenFX));
            var vol = fxGo.GetComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = profile;
            var screenFx = fxGo.GetComponent<ScreenFX>();
            screenFx.volume = vol;
            screenFx.shakeTarget = shake;

            // Managers (reload prefab handles: creating the profile above triggers an import pass)
            castle = P<Castle>("Castle"); catapult = P<Trebuchet>("Trebuchet"); arrow = P<Arrow>("Arrow");
            bowPrefab = P<BowController>("BowRig");
            var vfx = new GameObject("VFX").AddComponent<VFX>();
            vfx.glow = m["Glow"]; vfx.flame = m["Flame"]; vfx.smoke = m["Smoke"]; vfx.spark = m["Spark"];
            vfx.ring = m["Ring"]; vfx.chip = m["Chip"]; vfx.scorch = m["Scorch"];
            var sfx = new GameObject("Sfx").AddComponent<Sfx>();
            foreach (Sound s in System.Enum.GetValues(typeof(Sound)))
            {
                var file = System.Text.RegularExpressions.Regex.Replace(s.ToString(), "(?<!^)([A-Z])", "_$1").ToLowerInvariant();
                sfx.clips[(int)s] = AssetDatabase.LoadAssetAtPath<AudioClip>($"{Root}/Audio/{file}.wav");
                if (!sfx.clips[(int)s]) Debug.LogWarning("[Siege] missing sound " + file);
            }
            var hud = new GameObject("HUD").AddComponent<HUD>();
            hud.crosshairTex = Tex("ui_crosshair"); hud.ringTex = Tex("ui_ring"); hud.vignetteTex = Tex("ui_vignette");
            hud.panelTex = Tex("ui_panel"); hud.dotTex = Tex("fx_dot");
            hud.screenFX = screenFx;

            var bow = ((GameObject)PrefabUtility.InstantiatePrefab(bowPrefab.gameObject)).GetComponent<BowController>();
            bow.cam = camGo.transform;
            bow.arrowPrefab = arrow;
            var input = bow.gameObject.AddComponent<TouchBowInput>();
            bow.inputSource = input;
            var trajGo = new GameObject("Trajectory");
            var traj = trajGo.AddComponent<LineRenderer>();
            traj.widthMultiplier = 1f;
            traj.widthCurve = new AnimationCurve(new Keyframe(0, 0.35f), new Keyframe(1, 1.4f));
            traj.sharedMaterial = m["Dots"];
            traj.textureMode = LineTextureMode.Tile;
            traj.textureScale = new Vector2(0.45f, 1f);
            traj.shadowCastingMode = ShadowCastingMode.Off;
            traj.enabled = false;
            bow.trajectory = traj;

            var reticle = BuildMarker(m, "Reticle", m["Reticle"], false);
            var reticleInst = (GameObject)PrefabUtility.InstantiatePrefab(reticle);
            reticleInst.SetActive(false);

            var game = new GameObject("Game").AddComponent<GameManager>();
            game.xrOrigin = origin;
            game.arSession = session;
            game.raycastManager = originGo.GetComponent<ARRaycastManager>();
            game.planeManager = planes;
            game.meshManager = meshMgr;
            game.cam = cam;
            game.shakeTarget = shake;
            game.sun = sun;
            game.hud = hud;
            game.bow = bow;
            game.bowInput = input;
            game.reticle = reticleInst.transform;
            game.reticleRenderer = reticleInst.GetComponentInChildren<Renderer>();
            game.simRig = simRig;
            game.simRoom = room;
            game.castlePrefab = castle;
            game.trebuchetPrefab = catapult;

            var path = $"{Root}/Scenes/Main.unity";
            EditorSceneManager.SaveScene(scene, path);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(path, true) };
        }
    }
}
