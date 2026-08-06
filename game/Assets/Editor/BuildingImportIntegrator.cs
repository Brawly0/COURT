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

            // ---- theme material mapping ----
            // Ordered, first-match-wins. Tuned against omarproject's real Revit
            // categories: "Basic Wall Generic - 140mm Masonry" (interior) vs
            // "- 300mm" (exterior shell), "Floor Standard Timber-Wood Finish",
            // "Assembled Stair"/"Non-Monolithic Run"/"Carriage", "Columns N",
            // "Railing 1100mm", "Top Rail Type Rectangular".
            var map = new (string[] keys, string mat)[]
            {
                (new[] { "glass", "window", "glazing", "curtain" }, "Glass"),
                (new[] { "carpet" }, "Carpet"),
                (new[] { "timber", "wood", "oak", "walnut", "door" }, "WoodDark"),   // before "floor"
                (new[] { "stair", "run", "landing", "carriage", "tread" }, "WoodDark"),
                (new[] { "railing", "top rail", "handrail", "balustrade" }, "WoodDark"),
                (new[] { "300mm", "exterior", "brick", "masonry wall" }, "Brick"),   // thick = outer shell
                (new[] { "roof", "ceiling" }, "PlasterLight"),
                (new[] { "column", "pilaster" }, "Plaster"),   // ceiling-tile grid on columns looked wrong
                (new[] { "floor", "slab" }, "FloorTileHall"),
                (new[] { "foundation", "concrete", "cement", "footing" }, "Concrete"),
                (new[] { "metal", "steel", "alum", "chrome" }, "Metal"),
                (new[] { "wall", "masonry", "plaster", "gypsum", "paint", "stucco", "partition" }, "Plaster"),
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

            // world-space texture density on every imported surface, so a 45m
            // wall and a stair tread read at the same texel scale
            foreach (var r in building.GetComponentsInChildren<Renderer>())
            {
                var s = r.bounds.size;
                float u, v;
                if (s.y <= s.x && s.y <= s.z) { u = s.x / 2f; v = s.z / 2f; }
                else if (s.z <= s.x && s.z <= s.y) { u = s.x / 2f; v = s.y / 2f; }
                else { u = s.z / 2f; v = s.y / 2f; }
                var mpb = new MaterialPropertyBlock();
                mpb.SetVector("_BaseMap_ST", new Vector4(Mathf.Clamp(u, 0.5f, 40f), Mathf.Clamp(v, 0.5f, 40f), 0f, 0f));
                r.SetPropertyBlock(mpb);
            }

            // ---- theme atmosphere ----
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.045f, 0.048f, 0.058f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = new Color(0.012f, 0.012f, 0.016f);
            RenderSettings.fogDensity = 0.030f;   // corridors fall into darkness

            // ---- detect the model's real floor levels from its slabs ----
            var levels = building.GetComponentsInChildren<Renderer>()
                .Where(r => r.gameObject.name.Contains("Floor"))
                .Select(r => Mathf.Round(r.bounds.max.y * 10f) / 10f)
                .Distinct().OrderBy(v => v).ToList();
            // omarproject is pilotis: open at grade, slabs only at 3.4m / 7.4m.
            // Give the ground level a slab so it is walkable (it becomes the garage).
            if (levels.Count == 0 || levels[0] > b.min.y + 1.5f)
            {
                var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
                ground.name = "GroundSlab";
                ground.transform.position = new Vector3(b.center.x, b.min.y - 0.15f, b.center.z);
                ground.transform.localScale = new Vector3(b.size.x + 1.0f, 0.3f, b.size.z + 1.0f);
                var cm = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Concrete.mat");
                if (cm != null) ground.GetComponent<Renderer>().sharedMaterial = cm;
                levels.Insert(0, b.min.y);
            }
            Debug.Log("[CaseClosed] floor levels: " + string.Join(", ", levels.Select(v => v.ToString("F1") + "m")));

            // ---- roof cap (Revit structural exports have no roof) ----
            var roof = GameObject.CreatePrimitive(PrimitiveType.Cube);
            roof.name = "RoofCap";
            roof.transform.position = new Vector3(b.center.x, b.max.y + 0.15f, b.center.z);
            roof.transform.localScale = new Vector3(b.size.x + 1.5f, 0.3f, b.size.z + 1.5f);
            var roofMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/PlasterLight.mat");
            if (roofMat != null) roof.GetComponent<Renderer>().sharedMaterial = roofMat;

            // ---- interior light scatter + interior point collection ----
            var interior = ScatterLights(b, levels);

            // ---- zone anchors by storey (drag Anchor_* in the editor to refine) ----
            // Storey 0 = service/vehicles, storey 1 = the public floor (hall +
            // courtroom, so the trial teleport lands where players already are),
            // storey 2+ = offices and archives.
            var byStorey = new[]
            {
                new[] { "ParkingGarage", "BoilerRoom", "Maintenance", "HoldingCells" },
                new[] { "MainHall", "CourtroomA", "EvidenceLocker", "Security", "Cafeteria", "Lab" },
                new[] { "ProsecutionOffice", "DefenseOffice", "Archives", "RecordsRoom",
                        "JudgeChambers", "StaffRoom", "PressRoom", "CourtroomB" },
            };
            var anchorRoot = new GameObject("Anchors");
            var floorGroups = interior.GroupBy(p => Mathf.Round(p.y * 2f) / 2f)
                                      .OrderBy(gr => gr.Key)
                                      .Select(gr => gr.ToList()).ToList();
            for (int s = 0; s < byStorey.Length; s++)
            {
                var zones = byStorey[s];
                var pts = floorGroups.Count > 0
                    ? floorGroups[Mathf.Min(s, floorGroups.Count - 1)]
                    : new List<Vector3> { b.center };
                var spread = SpreadPoints(pts, zones.Length, b);
                for (int i = 0; i < zones.Length; i++)
                {
                    var a = new GameObject("Anchor_" + zones[i]);
                    a.transform.SetParent(anchorRoot.transform);
                    a.transform.position = spread[i % spread.Count] + Vector3.up * 0.2f;
                    a.AddComponent<ZoneAnchor>().ZoneName = zones[i];
                }
            }

            // ---- spawn point: centre of the public floor (storey 1 if it exists) ----
            Vector3 spawnPos = b.center;
            if (interior.Count > 0)
            {
                var groups = interior.GroupBy(p => Mathf.Round(p.y * 2f) / 2f)
                                     .OrderBy(gr => gr.Key).Select(gr => gr.ToList()).ToList();
                var pub = groups[Mathf.Min(1, groups.Count - 1)];
                var mid = new Vector3(pub.Average(p => p.x), pub[0].y, pub.Average(p => p.z));
                spawnPos = pub.OrderBy(p => (p - mid).sqrMagnitude).First();
            }
            var spawn = new GameObject("SpawnPoint");
            spawn.transform.position = spawnPos + Vector3.up * 0.2f;

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

        /// <summary>
        /// Walk each detected floor level on a grid. A lamp exists ONLY where a
        /// raycast finds BOTH solid floor underfoot AND a real ceiling overhead,
        /// and the fixture mounts FLUSH to that ceiling. (v1 hung tubes at a
        /// fixed height with no ceiling check - they floated mid-air over the
        /// parking lot and open terraces.) Spacing is wide on purpose: the PSX
        /// look is pools of warm fluorescent light with dark stretches between,
        /// not an evenly lit office.
        /// </summary>
        private static List<Vector3> ScatterLights(Bounds b, List<float> levels)
        {
            var old = GameObject.Find("ScatteredLights");
            if (old != null) UnityEngine.Object.DestroyImmediate(old);
            var lightsRoot = new GameObject("ScatteredLights");
            var interior = new List<Vector3>();
            var accepted = new List<Vector3>();
            var tubeMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/TubeLight.mat");
            int placed = 0, openAir = 0;
            const float Spacing = 7.5f, MinSep = 6f;
            Physics.SyncTransforms();   // colliders created this frame are invisible to raycasts otherwise

            foreach (float level in levels)
            {
                for (float x = b.min.x + 2f; x < b.max.x - 1f; x += Spacing)
                    for (float z = b.min.z + 2f; z < b.max.z - 1f; z += Spacing)
                    {
                        // solid floor directly underfoot at this level?
                        if (!Physics.Raycast(new Vector3(x, level + 1.2f, z), Vector3.down, out var floorHit, 2.2f))
                            continue;
                        if (Mathf.Abs(floorHit.point.y - level) > 0.4f || floorHit.normal.y < 0.6f) continue;

                        // a real ceiling overhead? no ceiling, no lamp - and no
                        // anchor either (open terraces aren't interior space)
                        if (!Physics.Raycast(new Vector3(x, floorHit.point.y + 1.6f, z), Vector3.up, out var ceil, 8f)
                            || ceil.normal.y > -0.3f)
                        {
                            openAir++;
                            continue;
                        }

                        interior.Add(new Vector3(x, floorHit.point.y, z));

                        // keep lamps apart - pools, not floodlighting
                        bool tooClose = false;
                        foreach (var a in accepted)
                            if (Mathf.Abs(a.y - floorHit.point.y) < 1.5f &&
                                new Vector2(a.x - x, a.z - z).sqrMagnitude < MinSep * MinSep)
                            { tooClose = true; break; }
                        if (tooClose) continue;
                        accepted.Add(new Vector3(x, floorHit.point.y, z));

                        float ceilY = ceil.point.y;
                        var l = new GameObject($"Lamp_{placed}").AddComponent<Light>();
                        l.type = LightType.Point;
                        l.range = 11f;
                        l.intensity = 2.1f;
                        // aged fluorescents drift in colour - deterministic per position
                        float drift = Mathf.Abs((x * 7f + z * 13f) % 10f) / 10f;
                        l.color = Color.Lerp(new Color(1f, 0.93f, 0.78f), new Color(0.92f, 0.97f, 0.86f), drift);
                        l.transform.SetParent(lightsRoot.transform);
                        l.transform.position = new Vector3(x, ceilY - 0.45f, z);

                        var tube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        tube.name = "Tube";
                        tube.transform.SetParent(lightsRoot.transform);
                        tube.transform.position = new Vector3(x, ceilY - 0.05f, z);   // flush to the ceiling
                        tube.transform.localScale = new Vector3(2.2f, 0.07f, 0.18f);
                        UnityEngine.Object.DestroyImmediate(tube.GetComponent<Collider>());
                        if (tubeMat != null) tube.GetComponent<Renderer>().sharedMaterial = tubeMat;
                        placed++;
                    }
            }
            Debug.Log($"[CaseClosed] ceiling lights placed: {placed} (skipped {openAir} open-air points) across {levels.Count} levels");
            return interior;
        }

        /// <summary>Re-run just the light pass on the already-integrated scene.</summary>
        [MenuItem("Case Closed/Rebuild Ceiling Lights")]
        public static void RebuildLights()
        {
            var building = GameObject.Find("OmarBuilding");
            if (building == null) { Debug.LogError("[CaseClosed] OmarBuilding not found in the scene"); return; }
            var rends = building.GetComponentsInChildren<Renderer>();
            var b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);

            var levels = rends.Where(r => r.gameObject.name.Contains("Floor"))
                .Select(r => Mathf.Round(r.bounds.max.y * 10f) / 10f)
                .Distinct().OrderBy(v => v).ToList();
            if (levels.Count == 0 || levels[0] > b.min.y + 1.5f) levels.Insert(0, b.min.y);

            ScatterLights(b, levels);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
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
            var pool = new List<Vector3>(pts);
            result.Add(pool[0]);
            pool.RemoveAt(0);
            while (result.Count < n && pool.Count > 0)
            {
                int bestIdx = 0; float bestD = -1f;
                for (int i = 0; i < pool.Count; i++)
                {
                    float d = result.Min(q => (pool[i] - q).sqrMagnitude);
                    if (d > bestD) { bestD = d; bestIdx = i; }
                }
                result.Add(pool[bestIdx]);
                pool.RemoveAt(bestIdx);      // never hand out the same spot twice
            }
            // if the floor had fewer standable points than zones, jitter the reuse
            for (int i = result.Count; i < n; i++)
                result.Add(result[i % result.Count] + new Vector3((i % 3) * 1.6f - 1.6f, 0f, (i % 2) * 1.6f - 0.8f));
            return result;
        }
    }
}
