using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using CaseClosed.Game;

namespace CaseClosed.EditorTools
{
    /// <summary>
    /// Turns an exported Revit model (FBX/OBJ dropped in Assets/ImportedBuilding)
    /// into the playable courthouse: auto-scale (Revit exports feet/cm/mm),
    /// colliders, theme material mapping, interior light scatter, zone anchors,
    /// spawn point, systems, post-FX. Menu: Case Closed > Integrate Imported Building.
    /// Heuristics WILL need one tuning round against the real file - that's expected.
    /// </summary>
    public static class BuildingImportIntegrator
    {
        [MenuItem("Case Closed/Integrate Imported Building")]
        public static void Run()
        {
            // ---- find the model ----
            string[] guids = AssetDatabase.FindAssets("t:Model", new[] { "Assets/ImportedBuilding" });
            if (guids.Length == 0)
            {
                EditorUtility.DisplayDialog("Case Closed",
                    "No model found.\n\nExport omarproject.fbx from Revit (File > Export > FBX) " +
                    "and copy it into Assets/ImportedBuilding, then run this again.", "OK");
                return;
            }
            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Debug.Log($"[CaseClosed] Integrating building model: {path}");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ---- instantiate + normalize scale/position ----
            var building = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
            building.name = "OmarBuilding";
            Bounds b = WorldBounds(building);
            float h = b.size.y;
            float scale = h < 25f ? 1f : h < 200f ? 0.3048f : h < 2000f ? 0.01f : 0.001f; // m / ft / cm / mm
            building.transform.localScale = Vector3.one * scale;
            b = WorldBounds(building);
            building.transform.position -= new Vector3(b.center.x, b.min.y, b.center.z);
            b = WorldBounds(building);
            Debug.Log($"[CaseClosed] scale x{scale} -> footprint {b.size.x:F1} x {b.size.z:F1} m, height {b.size.y:F1} m");

            // ---- colliders ----
            int colliders = 0;
            foreach (var mf in building.GetComponentsInChildren<MeshFilter>())
                if (mf.sharedMesh != null && mf.GetComponent<Collider>() == null)
                { mf.gameObject.AddComponent<MeshCollider>(); colliders++; }

            // ---- theme material mapping (keyword heuristics on renderer + material names) ----
            var map = new (string[] keys, string mat)[]
            {
                (new[] { "glass", "window", "glazing", "curtain" }, "Glass"),
                (new[] { "carpet" }, "Carpet"),
                (new[] { "roof" }, "PlasterLight"),
                (new[] { "ceiling" }, "PlasterLight"),
                (new[] { "floor", "slab", "tile" }, "FloorTileHall"),
                (new[] { "brick", "masonry" }, "Brick"),
                (new[] { "concrete", "foundation", "cement" }, "Concrete"),
                (new[] { "door", "wood", "timber", "oak", "walnut" }, "WoodDark"),
                (new[] { "metal", "steel", "rail", "alum", "chrome" }, "Metal"),
                (new[] { "wall", "plaster", "gypsum", "paint", "stucco" }, "Plaster"),
            };
            var cache = new Dictionary<string, Material>();
            Material Load(string n) => cache.TryGetValue(n, out var m) ? m
                : cache[n] = AssetDatabase.LoadAssetAtPath<Material>($"Assets/Materials/{n}.mat");
            int themed = 0;
            foreach (var r in building.GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    string name = ((mats[i] != null ? mats[i].name : "") + " " + r.gameObject.name).ToLowerInvariant();
                    string pick = "Plaster";
                    foreach (var (keys, mat) in map)
                        if (keys.Any(k => name.Contains(k))) { pick = mat; break; }
                    var loaded = Load(pick);
                    if (loaded != null) { mats[i] = loaded; themed++; }
                }
                r.sharedMaterials = mats;
            }
            Debug.Log($"[CaseClosed] colliders added: {colliders}, material slots themed: {themed}");

            // ---- theme atmosphere ----
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.10f, 0.105f, 0.12f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = new Color(0.015f, 0.015f, 0.02f);
            RenderSettings.fogDensity = 0.012f;

            // ---- interior light scatter + interior point collection ----
            var interior = ScatterLights(b);

            // ---- zone anchors spread across the interior (drag Anchor_* to refine!) ----
            string[] zones = { "MainHall", "CourtroomA", "EvidenceLocker", "Security", "Cafeteria",
                               "ParkingGarage", "Lab", "Maintenance", "HoldingCells", "BoilerRoom",
                               "ProsecutionOffice", "Archives", "RecordsRoom", "DefenseOffice",
                               "CourtroomB", "JudgeChambers", "StaffRoom", "PressRoom" };
            var anchorRoot = new GameObject("Anchors");
            var spread = SpreadPoints(interior, zones.Length, b);
            for (int i = 0; i < zones.Length; i++)
            {
                var a = new GameObject("Anchor_" + zones[i]);
                a.transform.SetParent(anchorRoot.transform);
                a.transform.position = spread[i];
                a.AddComponent<ZoneAnchor>().ZoneName = zones[i];
            }

            // ---- spawn point: the biggest ground-floor cluster ----
            var spawnPos = interior.Count > 0
                ? interior.Where(p => p.y < b.min.y + 2.5f).DefaultIfEmpty(interior[0]).First()
                : Vector3.zero;
            var spawn = new GameObject("SpawnPoint");
            spawn.transform.position = spawnPos + Vector3.up * 0.15f;

            // ---- systems (shared with the procedural scene) ----
            ProjectSetup.BuildPostFx();
            var playerPrefab = ProjectSetup.BuildPlayerPrefab();
            ProjectSetup.BuildNetwork(playerPrefab);
            ProjectSetup.BuildGameSystems(playerPrefab);

            const string scenePath = "Assets/Scenes/CourthouseRVT.unity";
            EditorSceneManager.SaveScene(scene, scenePath);
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(scenePath, true),
                new EditorBuildSettingsScene("Assets/Scenes/Courthouse.unity", true),
            };
            AssetDatabase.SaveAssets();
            Debug.Log("[CaseClosed] RVT building integrated -> " + scenePath +
                      " (now scene 0; the procedural courthouse remains as scene 1)");
        }

        private static Bounds WorldBounds(GameObject go)
        {
            var rends = go.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return new Bounds(go.transform.position, Vector3.one);
            Bounds b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);
            return b;
        }

        /// <summary>Grid-scan the volume; where a floor has a ceiling 2-6m above, it's a room -> light it.</summary>
        private static List<Vector3> ScatterLights(Bounds b)
        {
            var lightsRoot = new GameObject("ScatteredLights");
            var interior = new List<Vector3>();
            int placed = 0;
            for (float x = b.min.x + 3f; x < b.max.x - 2f && placed < 140; x += 7f)
                for (float z = b.min.z + 3f; z < b.max.z - 2f && placed < 140; z += 7f)
                {
                    var hits = Physics.RaycastAll(new Vector3(x, b.max.y + 2f, z), Vector3.down, b.size.y + 4f)
                        .OrderBy(hh => hh.point.y).ToArray();
                    // walk floor surfaces bottom-up; a hit with another surface 2-6m above = interior
                    for (int i = 0; i < hits.Length; i++)
                    {
                        float floorY = hits[i].point.y;
                        bool hasCeiling = hits.Any(hh => hh.point.y > floorY + 2.0f && hh.point.y < floorY + 6.0f);
                        if (!hasCeiling || hits[i].normal.y < 0.7f) continue;
                        interior.Add(new Vector3(x, floorY, z));
                        var l = new GameObject($"Lamp_{placed}").AddComponent<Light>();
                        l.type = LightType.Point;
                        l.range = 12f;
                        l.intensity = 3.6f;
                        l.color = new Color(1f, 0.92f, 0.74f);
                        l.transform.SetParent(lightsRoot.transform);
                        l.transform.position = new Vector3(x, floorY + 2.6f, z);
                        placed++;
                        i += 1; // skip the ceiling surface itself
                    }
                }
            Debug.Log($"[CaseClosed] interior lights placed: {placed}");
            return interior;
        }

        /// <summary>Pick n interior points far apart from each other (greedy max-min spread).</summary>
        private static List<Vector3> SpreadPoints(List<Vector3> pts, int n, Bounds b)
        {
            var result = new List<Vector3>();
            if (pts.Count == 0)
            {
                for (int i = 0; i < n; i++)
                    result.Add(b.center + new Vector3(Mathf.Cos(i * 2.4f), 0, Mathf.Sin(i * 2.4f)) * (2f + i));
                return result;
            }
            result.Add(pts[0]);
            while (result.Count < n)
            {
                Vector3 best = pts[0]; float bestD = -1f;
                foreach (var p in pts)
                {
                    float d = result.Min(q => (p - q).sqrMagnitude);
                    if (d > bestD) { bestD = d; best = p; }
                }
                result.Add(best);
            }
            return result;
        }
    }
}
