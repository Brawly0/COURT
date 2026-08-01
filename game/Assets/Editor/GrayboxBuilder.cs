using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using CaseClosed.Game;

namespace CaseClosed.EditorTools
{
    /// <summary>
    /// Courthouse blockout v2 — real architecture, not floating boxes:
    ///   Basement (lab, holding, maintenance, boiler)
    ///   Ground   (atrium hall, COURTROOM A - sealed, locker, security, cafeteria, garage)
    ///   Floor 2  (prosecution office, archives, records, defense office)
    ///   Floor 3  (courtroom B - disused, judge's chambers, staff, press)
    /// Circulation: open staircases hug the atrium void, one flight per level,
    /// each landing on a real floor — everyone in the hall sees who changes
    /// floors (the sightline rule). Courtroom A's doors never open; the bell
    /// teleports players inside (CaseRuntime).
    /// Palette: URP Lit materials saved to Assets/Materials.
    /// </summary>
    public static class GrayboxBuilder
    {
        private const float WallH = 3.5f;
        private const float T = 0.3f;
        private const float DoorW = 2.2f;

        private static Material _brick, _plaster, _wood, _carpet, _tile, _concrete, _metal;

        private class Room
        {
            public string Name; public float X0, Z0, X1, Z1, Y;
            public char DoorSide; public float DoorAt;
            public Material WallMat, FloorMat;
            public bool SealedDoor;
            public Room(string n, float x0, float z0, float x1, float z1, float y, char side, float at,
                        Material wall = null, Material floor = null, bool sealedDoor = false)
            {
                Name = n; X0 = x0; Z0 = z0; X1 = x1; Z1 = z1; Y = y;
                DoorSide = side; DoorAt = at; WallMat = wall; FloorMat = floor; SealedDoor = sealedDoor;
            }
        }

        public static void Build()
        {
            BuildMaterials();
            var root = new GameObject("Courthouse");

            // ---------------- floor plates ----------------
            // Ground hall (atrium floor) with a stair well for the basement flight
            Slab(root, "Hall_Floor_W", -14, -4, 5.2f, 4, 0, _tile);
            Slab(root, "Hall_Floor_E", 5.2f, -3.1f, 14, 4, 0, _tile);        // east strip; open well south of it (basement stairs)
            Slab(root, "GarageLink_Floor", -24, -2, -14, 2, 0, _concrete);
            Slab(root, "Garage_Floor", -38, -7, -24, 7, 0, _concrete);

            // Basement full plate
            Slab(root, "Basement_Floor", -14, -12, 14, 12, -4, _concrete);

            // F2 ring: two wings + two end connectors around the atrium void
            Slab(root, "F2_NorthWing", -14, 4, 14, 12, 4, _tile);
            Slab(root, "F2_SouthWing", -14, -12, 14, -4, 4, _tile);
            Slab(root, "F2_WestLink", -14, -4, -11, 4, 4, _tile);
            Slab(root, "F2_EastLink", 11, -4, 14, 4, 4, _tile);

            // F3 ring, same shape
            Slab(root, "F3_NorthWing", -14, 4, 14, 12, 8, _tile);
            Slab(root, "F3_SouthWing", -14, -12, 14, -4, 8, _tile);
            Slab(root, "F3_WestLink", -14, -4, -11, 4, 8, _tile);
            Slab(root, "F3_EastLink", 11, -4, 14, 4, 8, _tile);

            // Roof + garage roof
            Slab(root, "Roof", -14.5f, -12.5f, 14.5f, 14.5f, 11.8f, _plaster);
            Slab(root, "Garage_Roof", -38, -7, -24, 7, 3.4f, _concrete);
            Slab(root, "GarageLink_Roof", -24, -2, -14, 2, 3.4f, _concrete);
            Slab(root, "Earth", -60, -40, 60, 40, -6, _concrete);

            // ---------------- rooms ----------------
            var rooms = new List<Room>
            {
                // ground
                new Room("CourtroomA",     -2,   4, 14, 14, 0, 'S', 3f, _wood, _carpet, sealedDoor: true),
                new Room("EvidenceLocker", -14,  4, -2, 12, 0, 'S', -8f),
                new Room("Security",       -14, -12, -4, -4, 0, 'N', -9f),
                new Room("Cafeteria",        4, -12, 14, -4, 0, 'N', 4.5f), // door west of the stair well
                new Room("ParkingGarage",  -38,  -7, -24, 7, 0, 'E', 0f, _concrete, _concrete),
                // basement
                new Room("Lab",            -14,   4, -2, 12, -4, 'S', -8f),
                new Room("Maintenance",     -2,   4, 14, 12, -4, 'S', 8f),
                new Room("HoldingCells",   -14, -12, -2, -4, -4, 'N', -8f, _concrete, _concrete),
                new Room("BoilerRoom",      -2, -12, 14, -4, -4, 'N', 8f, _concrete, _concrete),
                // floor 2
                new Room("ProsecutionOffice", -14, 4, -2, 12, 4, 'S', -8f),
                new Room("Archives",           -2, 4, 14, 12, 4, 'S', 8f),
                new Room("RecordsRoom",      -14, -12, -2, -4, 4, 'N', -8f),
                new Room("DefenseOffice",     -2, -12, 14, -4, 4, 'N', 8f),
                // floor 3
                new Room("CourtroomB",       -14, 4, -2, 12, 8, 'S', -8f, _wood, _carpet),
                new Room("JudgeChambers",     -2, 4, 14, 12, 8, 'S', 8f, _wood),
                new Room("StaffRoom",        -14, -12, -2, -4, 8, 'N', -8f),
                new Room("PressRoom",         -2, -12, 14, -4, 8, 'N', 8f),
            };
            foreach (var r in rooms) BuildRoom(root, r);

            // hall end walls (west has the garage doorway)
            WallSeg(root, 14, 14, -4, 4, 0, _plaster);
            WallSeg(root, -14, -14, -4, -1.2f, 0, _plaster);
            WallSeg(root, -14, -14, 1.2f, 4, 0, _plaster);
            // garage shell
            WallSeg(root, -38, -38, -7, 7, 0, _brick);
            WallSeg(root, -38, -24, -7, -7, 0, _brick);
            WallSeg(root, -38, -24, 7, 7, 0, _brick);
            WallSeg(root, -24, -24, -7, -2, 0, _brick);
            WallSeg(root, -24, -24, 2, 7, 0, _brick);
            // link corridor walls
            WallSeg(root, -24, -14, -2, -2, 0, _brick);
            WallSeg(root, -24, -14, 2, 2, 0, _brick);
            // exterior brick shell around the core (visual thickness)
            BuildShell(root);

            // ---------------- atrium staircases (every flight lands somewhere) ----------------
            // B -> G : along south void edge, base in the basement corridor east end,
            //          emerging through the hall's stair well
            Ramp(root, "Stairs_B_to_G", new Vector3(14, -4, -3.55f), new Vector3(6, 0, -3.55f), 2.3f, _concrete);
            // G -> F2 : along north void edge, west half
            Ramp(root, "Stairs_G_to_F2", new Vector3(-14, 0, 3.55f), new Vector3(-6, 4, 3.55f), 2.3f, _tile);
            Slab(root, "Landing_F2", -6, 3.1f, -5.2f, 4, 4, _tile);
            // F2 -> F3 : along north void edge, east half
            Slab(root, "Landing_F2_Base", 1.2f, 3.1f, 2, 4, 4, _tile);
            Ramp(root, "Stairs_F2_to_F3", new Vector3(2, 4, 3.55f), new Vector3(10, 8, 3.55f), 2.3f, _tile);
            Slab(root, "Landing_F3", 10, 3.1f, 10.8f, 4, 8, _tile);

            // rails: atrium void edges on F2/F3, with gaps where stair bridges land
            RailX(root, 4, -11, 11, new List<(float, float)> { (-5.6f, 1.4f), (1.6f, 1.4f) }, 4);  // F2 north (two bridges)
            RailX(root, -4, -11, 11, new List<(float, float)>(), 4);                                // F2 south
            RailSegZ(root, -11, -4, 4, 4); RailSegZ(root, 11, -4, 4, 4);
            RailX(root, 4, -11, 11, new List<(float, float)> { (10.4f, 1.4f) }, 8);                 // F3 north (bridge)
            RailX(root, -4, -11, 11, new List<(float, float)>(), 8);                                // F3 south
            RailSegZ(root, -11, -4, 4, 8); RailSegZ(root, 11, -4, 4, 8);
            // hall stair-well guard (open at the emergence end x<7.5)
            RailX(root, -3.1f, 7.5f, 14, new List<(float, float)>(), 0);

            // ---------------- zone anchors ----------------
            Anchor(root, "MainHall", 0, 0, 0);
            Anchor(root, "CourtroomA", 6, 0, 9);
            Anchor(root, "EvidenceLocker", -8, 0, 8);
            Anchor(root, "Security", -9, 0, -8);
            Anchor(root, "Cafeteria", 9, 0, -8);
            Anchor(root, "ParkingGarage", -31, 0, 0);
            Anchor(root, "Lab", -8, -4, 8);
            Anchor(root, "Maintenance", 6, -4, 8);
            Anchor(root, "HoldingCells", -8, -4, -8);
            Anchor(root, "BoilerRoom", 6, -4, -8);
            Anchor(root, "ProsecutionOffice", -8, 4, 8);
            Anchor(root, "Archives", 6, 4, 8);
            Anchor(root, "RecordsRoom", -8, 4, -8);
            Anchor(root, "DefenseOffice", 6, 4, -8);
            Anchor(root, "CourtroomB", -8, 8, 8);
            Anchor(root, "JudgeChambers", 6, 8, 8);
            Anchor(root, "StaffRoom", -8, 8, -8);
            Anchor(root, "PressRoom", 6, 8, -8);

            // ---------------- courtroom A furnishing ----------------
            FurnishCourtroom(root);

            // ---------------- light: municipal dread ----------------
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.055f, 0.06f, 0.075f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = new Color(0.015f, 0.015f, 0.02f);
            RenderSettings.fogDensity = 0.02f;

            foreach (var r in rooms)
                RoomLight(root, r.Name + "_Light",
                    (r.X0 + r.X1) / 2f, r.Y + 2.9f, (r.Z0 + r.Z1) / 2f,
                    r.Name.Contains("Garage") || r.Y < 0
                        ? new Color(0.72f, 0.95f, 0.78f) : new Color(1f, 0.92f, 0.74f));
            for (int i = 0; i < 3; i++)
            {
                RoomLight(root, $"Hall_Light_{i}", -10 + i * 10, 3.2f, 0, new Color(1f, 0.92f, 0.74f));
                RoomLight(root, $"Atrium_F2_{i}", -10 + i * 10, 7.2f, 0, new Color(1f, 0.92f, 0.74f));
                RoomLight(root, $"Atrium_F3_{i}", -10 + i * 10, 11.2f, 0, new Color(1f, 0.92f, 0.74f));
            }
            RoomLight(root, "Courtroom_Key", 6, 3.4f, 11, new Color(1f, 0.95f, 0.8f));
        }

        // ------------------------------------------------------------------
        private static void BuildMaterials()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Materials"))
                AssetDatabase.CreateFolder("Assets", "Materials");
            _brick = Mat("Brick", new Color(0.42f, 0.20f, 0.15f));
            _plaster = Mat("Plaster", new Color(0.52f, 0.48f, 0.38f));
            _wood = Mat("WoodDark", new Color(0.24f, 0.15f, 0.09f));
            _carpet = Mat("Carpet", new Color(0.10f, 0.20f, 0.15f));
            _tile = Mat("FloorTile", new Color(0.34f, 0.36f, 0.32f));
            _concrete = Mat("Concrete", new Color(0.30f, 0.30f, 0.31f));
            _metal = Mat("Metal", new Color(0.35f, 0.36f, 0.40f));
        }

        private static Material Mat(string name, Color c)
        {
            string path = $"Assets/Materials/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.color = c;
            m.SetFloat("_Smoothness", 0.08f);
            return m;
        }

        private static GameObject Box(GameObject parent, string name, Vector3 center, Vector3 size, Material mat)
        {
            var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
            b.name = name;
            b.transform.SetParent(parent.transform);
            b.transform.position = center;
            b.transform.localScale = size;
            if (mat != null) b.GetComponent<Renderer>().sharedMaterial = mat;
            return b;
        }

        private static void Slab(GameObject root, string name, float x0, float z0, float x1, float z1, float yTop, Material mat)
            => Box(root, name, new Vector3((x0 + x1) / 2f, yTop - T / 2f, (z0 + z1) / 2f),
                   new Vector3(x1 - x0, T, z1 - z0), mat);

        private static void BuildRoom(GameObject root, Room r)
        {
            var g = new GameObject("Room_" + r.Name);
            g.transform.SetParent(root.transform);
            var wall = r.WallMat != null ? r.WallMat : _plaster;
            var floor = r.FloorMat != null ? r.FloorMat : _tile;
            Slab(g, r.Name + "_Floor", r.X0, r.Z0, r.X1, r.Z1, r.Y, floor);

            WallX(g, r, r.Z1, r.DoorSide == 'N', wall);
            WallX(g, r, r.Z0, r.DoorSide == 'S', wall);
            WallZ(g, r, r.X1, r.DoorSide == 'E', wall);
            WallZ(g, r, r.X0, r.DoorSide == 'W', wall);

            // door frame (+ sealed slab for Courtroom A)
            BuildDoorway(g, r);
            DoorSign(g, r);
        }

        private static void BuildDoorway(GameObject g, Room r)
        {
            float y = r.Y;
            Vector3 center; Vector3 lintelSize; Vector3 jambSize; Vector3 jambOff;
            bool alongX = r.DoorSide == 'N' || r.DoorSide == 'S';
            float wallPos = r.DoorSide == 'N' ? r.Z1 : r.DoorSide == 'S' ? r.Z0
                          : r.DoorSide == 'E' ? r.X1 : r.X0;
            if (alongX)
            {
                center = new Vector3(r.DoorAt, 0, wallPos);
                lintelSize = new Vector3(DoorW + 0.6f, WallH - 2.6f, T + 0.1f);
                jambSize = new Vector3(0.25f, 2.6f, T + 0.1f);
                jambOff = new Vector3(DoorW / 2f + 0.1f, 0, 0);
            }
            else
            {
                center = new Vector3(wallPos, 0, r.DoorAt);
                lintelSize = new Vector3(T + 0.1f, WallH - 2.6f, DoorW + 0.6f);
                jambSize = new Vector3(T + 0.1f, 2.6f, 0.25f);
                jambOff = new Vector3(0, 0, DoorW / 2f + 0.1f);
            }
            Box(g, "Lintel", center + Vector3.up * (y + 2.6f + lintelSize.y / 2f), lintelSize, _wood);
            Box(g, "Jamb_L", center + Vector3.up * (y + 1.3f) - jambOff, jambSize, _wood);
            Box(g, "Jamb_R", center + Vector3.up * (y + 1.3f) + jambOff, jambSize, _wood);

            if (r.SealedDoor)
            {
                var size = alongX ? new Vector3(DoorW, 2.6f, T) : new Vector3(T, 2.6f, DoorW);
                Box(g, "SealedDoor", center + Vector3.up * (y + 1.3f), size, _wood);
            }
        }

        private static void DoorSign(GameObject g, Room r)
        {
            bool alongX = r.DoorSide == 'N' || r.DoorSide == 'S';
            float wallPos = r.DoorSide == 'N' ? r.Z1 : r.DoorSide == 'S' ? r.Z0
                          : r.DoorSide == 'E' ? r.X1 : r.X0;
            float outward = r.DoorSide == 'N' ? 0.35f : r.DoorSide == 'S' ? -0.35f : 0f;
            float outwardX = r.DoorSide == 'E' ? 0.35f : r.DoorSide == 'W' ? -0.35f : 0f;
            Vector3 pos = alongX
                ? new Vector3(r.DoorAt, r.Y + 2.85f, wallPos + outward)
                : new Vector3(wallPos + outwardX, r.Y + 2.85f, r.DoorAt);

            var go = new GameObject("Sign_" + r.Name);
            go.transform.SetParent(g.transform);
            go.transform.position = pos;
            float yaw = r.DoorSide == 'N' ? 0f : r.DoorSide == 'S' ? 180f : r.DoorSide == 'E' ? 90f : -90f;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            var tm = go.AddComponent<TextMesh>();
            tm.text = r.SealedDoor ? "COURTROOM A — COURT IS IN SESSION" : Pretty(r.Name);
            tm.characterSize = 0.055f;
            tm.fontSize = 60;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = new Color(1f, 0.97f, 0.85f);
        }

        private static string Pretty(string n)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in n)
            {
                if (char.IsUpper(c) && sb.Length > 0) sb.Append(' ');
                sb.Append(char.ToUpperInvariant(c));
            }
            return sb.ToString();
        }

        private static void WallX(GameObject parent, Room r, float z, bool hasDoor, Material mat)
        {
            if (!hasDoor) { WallSeg(parent, r.X0, r.X1, z, z, r.Y, mat); return; }
            float a = r.DoorAt - DoorW / 2f, b = r.DoorAt + DoorW / 2f;
            if (a > r.X0) WallSeg(parent, r.X0, a, z, z, r.Y, mat);
            if (b < r.X1) WallSeg(parent, b, r.X1, z, z, r.Y, mat);
        }

        private static void WallZ(GameObject parent, Room r, float x, bool hasDoor, Material mat)
        {
            if (!hasDoor) { WallSeg(parent, x, x, r.Z0, r.Z1, r.Y, mat); return; }
            float a = r.DoorAt - DoorW / 2f, b = r.DoorAt + DoorW / 2f;
            if (a > r.Z0) WallSeg(parent, x, x, r.Z0, a, r.Y, mat);
            if (b < r.Z1) WallSeg(parent, x, x, b, r.Z1, r.Y, mat);
        }

        private static void WallSeg(GameObject parent, float x0, float x1, float z0, float z1, float y, Material mat = null)
        {
            bool alongX = Mathf.Abs(x1 - x0) > Mathf.Abs(z1 - z0);
            Box(parent, "Wall",
                new Vector3((x0 + x1) / 2f, y + WallH / 2f, (z0 + z1) / 2f),
                alongX ? new Vector3(x1 - x0, WallH, T) : new Vector3(T, WallH, z1 - z0),
                mat != null ? mat : _plaster);
        }

        private static void RailX(GameObject root, float z, float x0, float x1, List<(float center, float width)> gaps, float y)
        {
            gaps.Sort((p, q) => p.center.CompareTo(q.center));
            float cur = x0;
            foreach (var (center, width) in gaps)
            {
                float a = center - width / 2f;
                if (a > cur && width > 0f) { RailSegX(root, cur, a, z, y); cur = center + width / 2f; }
            }
            if (cur < x1) RailSegX(root, cur, x1, z, y);
        }

        private static void RailSegX(GameObject root, float x0, float x1, float z, float y)
            => Box(root, "Rail", new Vector3((x0 + x1) / 2f, y + 0.55f, z), new Vector3(x1 - x0, 1.1f, 0.12f), _wood);

        private static void RailSegZ(GameObject root, float x, float z0, float z1, float y)
            => Box(root, "Rail", new Vector3(x, y + 0.55f, (z0 + z1) / 2f), new Vector3(0.12f, 1.1f, z1 - z0), _wood);

        private static void Ramp(GameObject root, string name, Vector3 bottom, Vector3 top, float width, Material mat)
        {
            Vector3 mid = (bottom + top) / 2f;
            Vector3 dir = top - bottom;
            float len = dir.magnitude;
            var r = Box(root, name, mid - Vector3.up * (T / 2f), new Vector3(width, T, len), mat);
            r.transform.rotation = Quaternion.LookRotation(new Vector3(dir.x, 0, dir.z).normalized) *
                                   Quaternion.Euler(-Mathf.Atan2(dir.y, new Vector2(dir.x, dir.z).magnitude) * Mathf.Rad2Deg, 0, 0);
        }

        private static void Anchor(GameObject root, string zone, float x, float y, float z)
        {
            var a = new GameObject("Anchor_" + zone);
            a.transform.SetParent(root.transform);
            a.transform.position = new Vector3(x, y, z);
            a.AddComponent<ZoneAnchor>().ZoneName = zone;
        }

        private static void FurnishCourtroom(GameObject root)
        {
            var g = new GameObject("Courtroom_Furniture");
            g.transform.SetParent(root.transform);

            // judge's bench on a raised platform at the north wall
            Box(g, "Bench_Platform", new Vector3(6, 0.2f, 12.6f), new Vector3(6f, 0.4f, 2.4f), _wood);
            Box(g, "Judge_Bench", new Vector3(6, 1.0f, 12.9f), new Vector3(4.4f, 1.3f, 1.2f), _wood);
            Box(g, "Bench_Back", new Vector3(6, 2.2f, 13.7f), new Vector3(5f, 3.4f, 0.25f), _wood);
            // witness stand + clerk desk flank the bench
            Box(g, "Witness_Stand", new Vector3(2.4f, 0.65f, 11.9f), new Vector3(1.5f, 1.3f, 1.5f), _wood);
            Box(g, "Clerk_Desk", new Vector3(9.6f, 0.55f, 11.9f), new Vector3(1.8f, 1.1f, 1.2f), _wood);
            // counsel tables + chairs
            for (int t = 0; t < 2; t++)
            {
                float x = t == 0 ? 3.4f : 8.6f;
                Box(g, "Counsel_Table", new Vector3(x, 0.45f, 9.2f), new Vector3(2.4f, 0.12f, 1.1f), _wood);
                Box(g, "Table_Leg", new Vector3(x, 0.22f, 9.2f), new Vector3(2.1f, 0.44f, 0.8f), _wood);
                for (int c = 0; c < 2; c++)
                    Box(g, "Chair", new Vector3(x - 0.6f + c * 1.2f, 0.3f, 8.3f), new Vector3(0.5f, 0.6f, 0.5f), _wood);
            }
            // the bar (rail separating gallery), with a gate gap
            Box(g, "Bar_W", new Vector3(2.6f, 0.55f, 7.6f), new Vector3(9.2f - 4.6f, 1.1f, 0.12f), _wood);
            Box(g, "Bar_E", new Vector3(11.4f, 0.55f, 7.6f), new Vector3(14f - 8.8f, 1.1f, 0.12f), _wood);
            // gallery benches (where the bell drops everyone)
            for (int row = 0; row < 2; row++)
                for (int col = 0; col < 2; col++)
                    Box(g, "Gallery_Bench",
                        new Vector3(3.4f + col * 5.2f, 0.28f, 5.2f + row * 1.2f),
                        new Vector3(3.6f, 0.55f, 0.5f), _wood);
        }

        private static void RoomLight(GameObject root, string name, float x, float y, float z, Color c)
        {
            var l = new GameObject(name).AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 15f;
            l.intensity = 3.4f;
            l.color = c;
            l.transform.SetParent(root.transform);
            l.transform.position = new Vector3(x, y, z);
        }

        private static void BuildShell(GameObject root)
        {
            // brick skin just outside the core outer walls (reads as exterior mass)
            Box(root, "Shell_N", new Vector3(0, 4f, 14.75f), new Vector3(29.6f, 16f, 0.2f), _brick);
            Box(root, "Shell_S", new Vector3(0, 4f, -12.75f), new Vector3(29.6f, 16f, 0.2f), _brick);
            Box(root, "Shell_E", new Vector3(14.75f, 4f, 1f), new Vector3(0.2f, 16f, 27.7f), _brick);
            Box(root, "Shell_W_N", new Vector3(-14.75f, 4f, 8.25f), new Vector3(0.2f, 16f, 13.1f), _brick);
            Box(root, "Shell_W_S", new Vector3(-14.75f, 4f, -7.25f), new Vector3(0.2f, 16f, 11.1f), _brick);
            Box(root, "Shell_W_TopBand", new Vector3(-14.75f, 8f, 0f), new Vector3(0.2f, 8f, 4.6f), _brick);
        }
    }
}
