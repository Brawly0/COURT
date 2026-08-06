using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using CaseClosed.Game;

namespace CaseClosed.EditorTools
{
    /// <summary>
    /// The architecture/dressing pass for the Revit courthouse. Everything is
    /// raycast-fit to the REAL building geometry, PSX-chunky, deterministic,
    /// and idempotent (delete the MapDressing root, run again).
    ///
    ///  - WINDOWS: embedded through actual exterior walls. Two opposing
    ///    raycasts find both faces of a wall; the frame+glass box spans the
    ///    measured thickness, so windows read from inside AND outside.
    ///  - DOORS: each zone anchor raycasts toward the atrium; the first wall
    ///    it hits gets a doorframe + leaf + nameplate (PS1 games painted doors
    ///    onto walls - same trick, one mesh deeper).
    ///  - FURNITURE: a themed kit per room type, oriented toward its door.
    ///  - The aventador parks in the garage.
    /// Menu: Case Closed > Dress Map (Architect Pass).
    /// </summary>
    public static class MapDressing
    {
        private static readonly Dictionary<string, Material> _mats = new Dictionary<string, Material>();
        private static Transform _root;

        [MenuItem("Case Closed/Dress Map (Architect Pass)")]
        public static void Run()
        {
            var old = GameObject.Find("MapDressing");
            if (old != null) Object.DestroyImmediate(old);
            _mats.Clear();
            _root = new GameObject("MapDressing").transform;

            var building = GameObject.Find("OmarBuilding");
            if (building == null) { Debug.LogError("[Dressing] OmarBuilding not found"); return; }
            var rends = building.GetComponentsInChildren<Renderer>();
            var b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);
            Physics.SyncTransforms();

            int windows = PlaceWindows(b);
            int doors = 0, kits = 0;
            var anchors = Object.FindObjectsByType<ZoneAnchor>(FindObjectsSortMode.None);
            foreach (var a in anchors)
            {
                Vector3 doorDir = DoorDirection(a.transform.position);
                if (PlaceDoor(a, doorDir)) doors++;
                if (Furnish(a, doorDir)) kits++;
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log($"[Dressing] windows={windows} doors={doors} kits={kits} anchors={anchors.Length}");
        }

        // ---------------------------------------------------------------- helpers
        private static Material Mat(string name)
        {
            if (_mats.TryGetValue(name, out var m) && m != null) return m;
            m = AssetDatabase.LoadAssetAtPath<Material>($"Assets/Materials/{name}.mat");
            if (m == null) m = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Plaster.mat");
            _mats[name] = m;
            return m;
        }

        private static Transform Box(Transform parent, string name, Vector3 localPos, Vector3 scale,
                                     string mat, bool collider = false, float yaw = 0f)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            go.GetComponent<Renderer>().sharedMaterial = Mat(mat);
            return go.transform;
        }

        private static Transform Cyl(Transform parent, string name, Vector3 localPos, Vector3 scale,
                                     string mat, bool collider = false)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = Mat(mat);
            return go.transform;
        }

        private static void Sign(Transform parent, Vector3 worldPos, Vector3 facing, string text)
        {
            var go = new GameObject("Sign_" + text);
            go.transform.SetParent(parent);
            go.transform.position = worldPos;
            // verified in-scene: text reads correctly with +Z pointing TOWARD
            // the viewer standing on the `facing` side
            go.transform.rotation = Quaternion.LookRotation(facing);
            var tm = go.AddComponent<TextMesh>();
            tm.text = text;
            tm.fontSize = 48;
            tm.characterSize = 0.055f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.color = new Color(0.92f, 0.90f, 0.82f);
        }

        // ---------------------------------------------------------------- windows
        private static int PlaceWindows(Bounds b)
        {
            int placed = 0;
            float[] sillLevels = { 3.6f, 7.6f };   // upper storeys; grade is pilotis
            foreach (float level in sillLevels)
            {
                float y = level + 1.7f;
                // long facades (normal = +/-Z), then short facades (+/-X)
                for (float x = b.min.x + 3f; x < b.max.x - 3f; x += 4.5f)
                {
                    placed += TryWindow(new Vector3(x, y, b.max.z + 2f), Vector3.back);
                    placed += TryWindow(new Vector3(x, y, b.min.z - 2f), Vector3.forward);
                }
                for (float z = b.min.z + 3f; z < b.max.z - 3f; z += 4.5f)
                {
                    placed += TryWindow(new Vector3(b.max.x + 2f, y, z), Vector3.left);
                    placed += TryWindow(new Vector3(b.min.x - 2f, y, z), Vector3.right);
                }
            }
            return placed;
        }

        private static int TryWindow(Vector3 origin, Vector3 dirIn)
        {
            // outer face
            if (!Physics.Raycast(origin, dirIn, out var outer, 6f)) return 0;
            if (Vector3.Dot(outer.normal, -dirIn) < 0.8f) return 0;          // not a facade plane
            // inner face: cast back from inside the wall
            var innerOrigin = outer.point + dirIn * 1.5f;
            if (!Physics.Raycast(innerOrigin, -dirIn, out var inner, 1.6f)) return 0;
            float thickness = Vector3.Dot(inner.point - outer.point, dirIn);
            if (thickness < 0.08f || thickness > 0.9f) return 0;             // opening or double wall

            Vector3 mid = (outer.point + inner.point) * 0.5f;
            var w = new GameObject("Window").transform;
            w.SetParent(_root);
            w.position = mid;
            w.rotation = Quaternion.LookRotation(dirIn);
            // frame pokes past both faces; glass sits proud of the frame
            Box(w, "Frame", Vector3.zero, new Vector3(1.9f, 1.7f, thickness + 0.05f), "WoodDark");
            Box(w, "Glass", Vector3.zero, new Vector3(1.6f, 1.4f, thickness + 0.12f), "Glass");
            Box(w, "Mullion", Vector3.zero, new Vector3(0.07f, 1.42f, thickness + 0.14f), "WoodDark");
            Box(w, "Transom", Vector3.zero, new Vector3(1.62f, 0.07f, thickness + 0.14f), "WoodDark");
            Box(w, "Sill", new Vector3(0f, -0.92f, 0f), new Vector3(2.05f, 0.09f, thickness + 0.3f), "PlasterLight");
            return 1;
        }

        // ---------------------------------------------------------------- doors
        private static Vector3 DoorDirection(Vector3 anchorPos)
        {
            var toCenter = new Vector3(-anchorPos.x, 0f, -anchorPos.z);
            return toCenter.sqrMagnitude < 0.5f ? Vector3.forward : toCenter.normalized;
        }

        private static bool PlaceDoor(ZoneAnchor a, Vector3 dir)
        {
            var from = a.transform.position + Vector3.up * 1.2f;
            if (!Physics.Raycast(from, dir, out var hit, 9f)) return false;
            if (Mathf.Abs(hit.normal.y) > 0.4f) return false;                 // floor/ceiling, not a wall

            var d = new GameObject("Door_" + a.ZoneName).transform;
            d.SetParent(_root);
            d.position = new Vector3(hit.point.x, a.transform.position.y, hit.point.z) + hit.normal * 0.02f;
            d.rotation = Quaternion.LookRotation(hit.normal);

            Box(d, "Frame", new Vector3(0f, 1.15f, 0f), new Vector3(1.45f, 2.35f, 0.10f), "WoodDark");
            Box(d, "Leaf", new Vector3(0f, 1.1f, 0.045f), new Vector3(1.15f, 2.2f, 0.06f), "Wood");
            Box(d, "Kick", new Vector3(0f, 0.18f, 0.085f), new Vector3(1.1f, 0.3f, 0.02f), "Metal");
            Cyl(d, "Knob", new Vector3(0.42f, 1.05f, 0.10f), new Vector3(0.07f, 0.02f, 0.07f), "Gold");
            Box(d, "Plate", new Vector3(0f, 2.55f, 0.02f), new Vector3(1.7f, 0.34f, 0.05f), "WoodDark");
            Sign(d, d.position + Vector3.up * 2.55f + hit.normal * 0.09f, hit.normal, Display(a.ZoneName));
            return true;
        }

        private static string Display(string zone)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in zone)
            {
                if (char.IsUpper(c) && sb.Length > 0) sb.Append(' ');
                sb.Append(char.ToUpper(c));
            }
            return sb.ToString();
        }

        // ---------------------------------------------------------------- furniture
        private static float WallDist(Vector3 pos, Vector3 dir, float max)
        {
            return Physics.Raycast(pos + Vector3.up * 1.2f, dir, out var hit, max) ? hit.distance : max;
        }

        private static bool Furnish(ZoneAnchor a, Vector3 doorDir)
        {
            var k = new GameObject("Kit_" + a.ZoneName).transform;
            k.SetParent(_root);
            k.rotation = Quaternion.LookRotation(doorDir);   // +Z faces the door

            // FIT THE ROOM: anchors can sit near walls, and a kit placed blindly
            // pushes furniture through the facade (the courtroom's judge bench
            // ended up outside the building). Measure clearance both ways,
            // center the kit between the walls, shrink it if the room is tight.
            float back = WallDist(a.transform.position, -doorDir, 6f);
            float fore = WallDist(a.transform.position, doorDir, 6f);
            k.position = a.transform.position + doorDir * ((fore - back) * 0.5f);
            float s = Mathf.Clamp((back + fore) / 8.8f, 0.55f, 1f);
            k.localScale = new Vector3(s, Mathf.Max(s, 0.85f), s);

            switch (a.ZoneName)
            {
                case "MainHall": MainHall(k); break;
                case "CourtroomA": Courtroom(k); break;
                case "EvidenceLocker": ShelfRoom(k, "Manila", 3); break;
                case "Archives": ShelfRoom(k, "WoodDark", 4); break;
                case "RecordsRoom": ShelfRoom(k, "Metal", 4); break;
                case "Security": Security(k); break;
                case "Cafeteria": Cafeteria(k); break;
                case "Lab": Lab(k); break;
                case "ProsecutionOffice": Office(k, "CarpetOffice"); break;
                case "DefenseOffice": Office(k, "Carpet"); break;
                case "PressRoom": PressRoom(k); break;
                case "ParkingGarage": Garage(k); break;
                case "BoilerRoom": Boiler(k); break;
                case "Maintenance": Maintenance(k); break;
                case "HoldingCells": HoldingCells(k); break;
                default: Object.DestroyImmediate(k.gameObject); return false;
            }
            return true;
        }

        private static void Desk(Transform p, Vector3 at, float yaw, string top = "WoodDark")
        {
            var d = new GameObject("Desk").transform;
            d.SetParent(p, false);
            d.localPosition = at;
            d.localRotation = Quaternion.Euler(0f, yaw, 0f);
            Box(d, "Top", new Vector3(0f, 0.76f, 0f), new Vector3(1.6f, 0.07f, 0.8f), top, true);
            Box(d, "LegL", new Vector3(-0.7f, 0.38f, 0f), new Vector3(0.08f, 0.76f, 0.7f), top);
            Box(d, "LegR", new Vector3(0.7f, 0.38f, 0f), new Vector3(0.08f, 0.76f, 0.7f), top);
            Box(d, "Papers", new Vector3(0.3f, 0.815f, 0.1f), new Vector3(0.32f, 0.03f, 0.24f), "Manila");
        }

        private static void Chair(Transform p, Vector3 at, float yaw)
        {
            var c = new GameObject("Chair").transform;
            c.SetParent(p, false);
            c.localPosition = at;
            c.localRotation = Quaternion.Euler(0f, yaw, 0f);
            Box(c, "Seat", new Vector3(0f, 0.45f, 0f), new Vector3(0.48f, 0.07f, 0.48f), "WoodDark", true);
            Box(c, "Back", new Vector3(0f, 0.82f, 0.21f), new Vector3(0.48f, 0.75f, 0.06f), "WoodDark");
            Box(c, "Legs", new Vector3(0f, 0.22f, 0f), new Vector3(0.4f, 0.44f, 0.4f), "Metal");
        }

        private static void Bench(Transform p, Vector3 at, float yaw)
        {
            var c = new GameObject("Bench").transform;
            c.SetParent(p, false);
            c.localPosition = at;
            c.localRotation = Quaternion.Euler(0f, yaw, 0f);
            Box(c, "Seat", new Vector3(0f, 0.45f, 0f), new Vector3(2.2f, 0.08f, 0.55f), "Wood", true);
            Box(c, "Back", new Vector3(0f, 0.85f, 0.25f), new Vector3(2.2f, 0.7f, 0.07f), "Wood");
            Box(c, "LegA", new Vector3(-0.9f, 0.22f, 0f), new Vector3(0.1f, 0.44f, 0.5f), "WoodDark");
            Box(c, "LegB", new Vector3(0.9f, 0.22f, 0f), new Vector3(0.1f, 0.44f, 0.5f), "WoodDark");
        }

        private static void Plant(Transform p, Vector3 at)
        {
            var c = new GameObject("Plant").transform;
            c.SetParent(p, false);
            c.localPosition = at;
            Cyl(c, "Pot", new Vector3(0f, 0.22f, 0f), new Vector3(0.42f, 0.22f, 0.42f), "RedAccent", true);
            Box(c, "Leaves", new Vector3(0f, 0.85f, 0f), new Vector3(0.55f, 0.9f, 0.55f), "Plant");
        }

        private static void Shelf(Transform p, Vector3 at, float yaw, string boxMat)
        {
            var s = new GameObject("Shelf").transform;
            s.SetParent(p, false);
            s.localPosition = at;
            s.localRotation = Quaternion.Euler(0f, yaw, 0f);
            Box(s, "Body", new Vector3(0f, 1.1f, 0f), new Vector3(2.4f, 2.2f, 0.5f), "WoodDark", true);
            for (int i = 0; i < 4; i++)
            {
                Box(s, "Row" + i, new Vector3(0f, 0.42f + i * 0.52f, -0.06f), new Vector3(2.2f, 0.06f, 0.45f), "Wood");
                // staggered box/file fills - deterministic, no two rows alike
                for (int jx = 0; jx < 4; jx++)
                    if ((i * 7 + jx * 3) % 5 != 0)
                        Box(s, $"File{i}_{jx}", new Vector3(-0.82f + jx * 0.55f, 0.62f + i * 0.52f, -0.05f),
                            new Vector3(0.42f, 0.34f, 0.36f), boxMat);
            }
        }

        private static void MainHall(Transform k)
        {
            Desk(k, new Vector3(0f, 0f, 1.2f), 180f);       // reception faces the door
            Chair(k, new Vector3(0f, 0f, 2.2f), 180f);
            Bench(k, new Vector3(-2.6f, 0f, -1.2f), 0f);
            Bench(k, new Vector3(2.6f, 0f, -1.2f), 0f);
            Bench(k, new Vector3(0f, 0f, -2.6f), 180f);
            Plant(k, new Vector3(-3.6f, 0f, 1.5f));
            Plant(k, new Vector3(3.6f, 0f, 1.5f));
            Box(k, "Rug", new Vector3(0f, 0.012f, 0f), new Vector3(4.5f, 0.02f, 3.2f), "Carpet");
        }

        private static void Courtroom(Transform k)
        {
            // judge bench opposite the door, gallery near it - courtroom logic
            var bench = new GameObject("JudgeBench").transform;
            bench.SetParent(k, false);
            bench.localPosition = new Vector3(0f, 0f, -3.4f);
            Box(bench, "Riser", new Vector3(0f, 0.25f, 0f), new Vector3(4.2f, 0.5f, 1.6f), "WoodDark", true);
            Box(bench, "Front", new Vector3(0f, 1.05f, 0.75f), new Vector3(4.2f, 1.15f, 0.14f), "WoodDark", true);
            Box(bench, "Top", new Vector3(0f, 1.66f, 0.68f), new Vector3(4.3f, 0.09f, 0.4f), "Wood");
            Cyl(bench, "Emblem", new Vector3(0f, 1.15f, 0.83f), new Vector3(0.7f, 0.02f, 0.7f), "Gold");
            Chair(k, new Vector3(0f, 0.5f, -3.7f), 0f);
            Box(k, "FlagL", new Vector3(-2.4f, 1.5f, -3.9f), new Vector3(0.08f, 3f, 0.08f), "WoodDark");
            Box(k, "FlagClothL", new Vector3(-2.25f, 2.4f, -3.9f), new Vector3(0.5f, 0.9f, 0.05f), "FlagRed");
            Box(k, "FlagR", new Vector3(2.4f, 1.5f, -3.9f), new Vector3(0.08f, 3f, 0.08f), "WoodDark");
            Box(k, "FlagClothR", new Vector3(2.25f, 2.4f, -3.9f), new Vector3(0.5f, 0.9f, 0.05f), "FlagBlue");
            // witness stand + counsel tables
            Box(k, "WitnessBox", new Vector3(-2.6f, 0.55f, -2.2f), new Vector3(1.2f, 1.1f, 1.2f), "WoodDark", true);
            Desk(k, new Vector3(-1.5f, 0f, -0.2f), 0f);
            Chair(k, new Vector3(-1.5f, 0f, 0.7f), 0f);
            Desk(k, new Vector3(1.5f, 0f, -0.2f), 0f);
            Chair(k, new Vector3(1.5f, 0f, 0.7f), 0f);
            // bar rail + gallery pews toward the door
            Box(k, "BarRail", new Vector3(0f, 0.55f, 1.4f), new Vector3(6.5f, 0.06f, 0.08f), "Wood");
            Bench(k, new Vector3(-1.8f, 0f, 2.4f), 0f);
            Bench(k, new Vector3(1.8f, 0f, 2.4f), 0f);
            Bench(k, new Vector3(-1.8f, 0f, 3.5f), 0f);
            Bench(k, new Vector3(1.8f, 0f, 3.5f), 0f);
        }

        private static void ShelfRoom(Transform k, string boxMat, int rows)
        {
            for (int i = 0; i < rows; i++)
            {
                float z = -2.4f + i * 1.6f;
                Shelf(k, new Vector3(-1.4f, 0f, z), 0f, boxMat);
                if (i < rows - 1) Shelf(k, new Vector3(1.4f, 0f, z + 0.8f), 180f, boxMat);
            }
        }

        private static void Security(Transform k)
        {
            Desk(k, new Vector3(0f, 0f, 0.5f), 180f, "Metal");
            Chair(k, new Vector3(0f, 0f, 1.5f), 180f);
            var wall = new GameObject("MonitorWall").transform;
            wall.SetParent(k, false);
            wall.localPosition = new Vector3(0f, 0f, -1.8f);
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 2; j++)
                    Box(wall, $"Mon{i}{j}", new Vector3(-0.8f + i * 0.8f, 1.3f + j * 0.65f, 0f),
                        new Vector3(0.7f, 0.55f, 0.18f), "Screen");
            Box(k, "Rack", new Vector3(2.0f, 0.9f, -1.6f), new Vector3(0.6f, 1.8f, 0.6f), "Metal", true);
        }

        private static void Cafeteria(Transform k)
        {
            for (int i = 0; i < 3; i++)
            {
                float x = -2.4f + i * 2.4f;
                Cyl(k, "Table" + i, new Vector3(x, 0.72f, 0f), new Vector3(1.3f, 0.04f, 1.3f), "PlasterLight", true);
                Cyl(k, "TableLeg" + i, new Vector3(x, 0.36f, 0f), new Vector3(0.12f, 0.36f, 0.12f), "Metal");
                Chair(k, new Vector3(x, 0f, 0.95f), 180f);
                Chair(k, new Vector3(x, 0f, -0.95f), 0f);
            }
            Box(k, "Counter", new Vector3(0f, 0.5f, -2.6f), new Vector3(4.5f, 1.0f, 0.7f), "Metal", true);
            Box(k, "Vending", new Vector3(2.9f, 1.0f, -2.5f), new Vector3(1.0f, 2.0f, 0.8f), "RedAccent", true);
            Box(k, "VendGlass", new Vector3(2.9f, 1.25f, -2.08f), new Vector3(0.7f, 1.1f, 0.05f), "Screen");
        }

        private static void Lab(Transform k)
        {
            for (int i = 0; i < 2; i++)
            {
                float z = -1f + i * 2f;
                Box(k, "Bench" + i, new Vector3(0f, 0.45f, z), new Vector3(4f, 0.9f, 0.9f), "Metal", true);
                for (int j = 0; j < 3; j++)
                    Cyl(k, $"Beaker{i}{j}", new Vector3(-1.2f + j * 1.2f, 1.05f, z), new Vector3(0.16f, 0.18f, 0.16f), "Glass");
            }
            Box(k, "FumeHood", new Vector3(0f, 1.2f, -2.8f), new Vector3(1.6f, 2.4f, 0.9f), "PlasterLight", true);
        }

        private static void Office(Transform k, string rug)
        {
            Box(k, "Rug", new Vector3(0f, 0.012f, 0f), new Vector3(3.6f, 0.02f, 2.8f), rug);
            Desk(k, new Vector3(0f, 0f, -0.6f), 0f);
            Chair(k, new Vector3(0f, 0f, -1.6f), 0f);
            Chair(k, new Vector3(-0.7f, 0f, 0.6f), 180f);
            Chair(k, new Vector3(0.7f, 0f, 0.6f), 180f);
            Shelf(k, new Vector3(-2.2f, 0f, -1.4f), 90f, "WoodDark");
            Box(k, "Cabinet", new Vector3(2.2f, 0.7f, -1.4f), new Vector3(0.6f, 1.4f, 0.7f), "Metal", true);
            Plant(k, new Vector3(2.3f, 0f, 1.4f));
        }

        private static void PressRoom(Transform k)
        {
            Box(k, "Podium", new Vector3(0f, 0.6f, -2.2f), new Vector3(0.9f, 1.2f, 0.7f), "WoodDark", true);
            Cyl(k, "Mic", new Vector3(0f, 1.45f, -2.2f), new Vector3(0.03f, 0.22f, 0.03f), "Metal");
            Box(k, "Backdrop", new Vector3(0f, 1.6f, -3.2f), new Vector3(4.2f, 2.6f, 0.1f), "FlagBlue");
            for (int r = 0; r < 2; r++)
                for (int c = 0; c < 3; c++)
                    Chair(k, new Vector3(-1.4f + c * 1.4f, 0f, 0.2f + r * 1.1f), 0f);
        }

        private static void Garage(Transform k)
        {
            // painted bays
            for (int i = 0; i < 4; i++)
            {
                float x = -4.5f + i * 3f;
                Box(k, "BayLine" + i, new Vector3(x, 0.015f, 0f), new Vector3(0.12f, 0.01f, 5.5f), "CautionYellow");
            }
            Cyl(k, "OilStain", new Vector3(-3f, 0.008f, 1f), new Vector3(1.4f, 0.005f, 1.8f), "WoodDark");
            Box(k, "Crates", new Vector3(4.8f, 0.5f, -2f), new Vector3(1.0f, 1.0f, 1.0f), "Wood", true);
            Box(k, "Crates2", new Vector3(4.8f, 1.25f, -2f), new Vector3(0.7f, 0.5f, 0.7f), "Wood", true);

            // the aventador, if Omar's model is importable
            var guid = AssetDatabase.FindAssets("aventador t:Model").FirstOrDefault()
                    ?? AssetDatabase.FindAssets("aventador").FirstOrDefault();
            if (guid == null) { Debug.Log("[Dressing] no aventador model found - garage stays empty"); return; }
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) return;

            var car = (GameObject)PrefabUtility.InstantiatePrefab(prefab, k.gameObject.scene);
            car.name = "Aventador";
            car.transform.SetParent(k);
            var carRends = car.GetComponentsInChildren<Renderer>();
            if (carRends.Length > 0)
            {
                var cb = carRends[0].bounds;
                foreach (var r in carRends) cb.Encapsulate(r.bounds);
                float len = Mathf.Max(cb.size.x, cb.size.z);
                if (len > 0.01f) car.transform.localScale *= 4.6f / len;   // real aventador ~4.8m
                // recompute after scale to sit it on the floor
                carRends = car.GetComponentsInChildren<Renderer>();
                cb = carRends[0].bounds;
                foreach (var r in carRends) cb.Encapsulate(r.bounds);
                car.transform.position = k.position + k.rotation * new Vector3(-1.5f, 0f, 0f)
                                       + Vector3.up * (k.position.y - cb.min.y + 0.02f);
            }
            car.transform.rotation = k.rotation * Quaternion.Euler(0f, 65f, 0f);
            // Unity fake-null: GetComponent's "null" isn't C# null, ?? misfires
            var bc = car.GetComponent<BoxCollider>();
            if (bc == null) bc = car.AddComponent<BoxCollider>();
            bc.center = new Vector3(0f, 0.6f, 0f);
            bc.size = new Vector3(2.2f, 1.2f, 4.6f);
        }

        private static void Boiler(Transform k)
        {
            Cyl(k, "Tank", new Vector3(0f, 1.3f, -1.5f), new Vector3(1.8f, 1.3f, 1.8f), "Metal", true);
            Cyl(k, "TankTop", new Vector3(0f, 2.75f, -1.5f), new Vector3(0.5f, 0.2f, 0.5f), "Metal");
            for (int i = 0; i < 3; i++)
                Cyl(k, "Pipe" + i, new Vector3(-2f + i * 0.4f, 1.5f, -2.6f), new Vector3(0.15f, 1.5f, 0.15f), "Metal");
            Cyl(k, "Valve", new Vector3(0.9f, 1.3f, -0.6f), new Vector3(0.4f, 0.04f, 0.4f), "RedAccent");
            Box(k, "Gauge", new Vector3(-0.9f, 1.6f, -0.55f), new Vector3(0.3f, 0.3f, 0.1f), "Screen");
        }

        private static void Maintenance(Transform k)
        {
            Shelf(k, new Vector3(0f, 0f, -1.6f), 0f, "Metal");
            Cyl(k, "Bucket", new Vector3(1.6f, 0.25f, 0.5f), new Vector3(0.4f, 0.25f, 0.4f), "CautionYellow");
            Cyl(k, "MopHandle", new Vector3(1.75f, 0.8f, 0.55f), new Vector3(0.03f, 0.7f, 0.03f), "WoodDark");
            Box(k, "WetSign", new Vector3(0.8f, 0.35f, 1.2f), new Vector3(0.5f, 0.7f, 0.04f), "CautionYellow", false, 25f);
        }

        private static void HoldingCells(Transform k)
        {
            // a barred cell front: uprights + rails
            for (int i = 0; i < 9; i++)
                Cyl(k, "Bar" + i, new Vector3(-1.6f + i * 0.4f, 1.25f, -1.2f), new Vector3(0.06f, 1.25f, 0.06f), "Metal");
            Box(k, "RailTop", new Vector3(0f, 2.5f, -1.2f), new Vector3(3.6f, 0.1f, 0.1f), "Metal");
            Box(k, "RailBot", new Vector3(0f, 0.05f, -1.2f), new Vector3(3.6f, 0.1f, 0.1f), "Metal");
            // cot inside
            Box(k, "CotFrame", new Vector3(0f, 0.25f, -2.4f), new Vector3(2.0f, 0.15f, 0.9f), "Metal", true);
            Box(k, "Mattress", new Vector3(0f, 0.38f, -2.4f), new Vector3(1.9f, 0.12f, 0.85f), "PlasterLight");
            Bench(k, new Vector3(0f, 0f, 1.6f), 180f);
        }
    }
}
