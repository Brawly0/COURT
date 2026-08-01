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
        private const float DoorW = 2.6f;

        private static Material _brick, _plaster, _plasterLight, _wood, _carpet, _carpetOffice,
                                _tile, _tileHall, _concrete, _metal, _tube, _screen, _redAccent, _yellow;

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
            // hall floor with a CENTERED stair well (the grand staircase lives mid-atrium,
            // far from every door wall)
            Slab(root, "Hall_Floor_W", -14, -4, -10.4f, 4, 0, _tileHall);
            Slab(root, "Hall_Floor_E", -1.6f, -4, 14, 4, 0, _tileHall);
            Slab(root, "Hall_Floor_N", -10.4f, 1.8f, -1.6f, 4, 0, _tileHall);
            Slab(root, "Hall_Floor_S", -10.4f, -4, -1.6f, -1.8f, 0, _tileHall);
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
                new Room("ProsecutionOffice", -14, 4, -2, 12, 4, 'S', -8f, null, _carpetOffice),
                new Room("Archives",           -2, 4, 14, 12, 4, 'S', 8f),
                new Room("RecordsRoom",      -14, -12, -2, -4, 4, 'N', -8f),
                new Room("DefenseOffice",     -2, -12, 14, -4, 4, 'N', 8f, null, _carpetOffice),
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

            // ---------------- THE GRAND STAIRCASE (stacked flights, mid-atrium) ----------------
            // Basement flight, under the ground flight, emerging east out of the well:
            RampRailed(root, "Stairs_B_to_G", new Vector3(-10, -4, 0), new Vector3(-2, 0, 0), 3.2f, _concrete);
            // Ground -> F2, directly above it (4m headroom), base platform seals the well's west edge:
            Slab(root, "Stairs_G_Base", -10.7f, -1.8f, -10, 1.8f, 0, _tileHall);
            RampRailed(root, "Stairs_G_to_F2", new Vector3(-10, 0, 0), new Vector3(-2, 4, 0), 3.2f, _tileHall);
            // Mid-air landing over the hall + bridge to the F2 north wing:
            Slab(root, "Landing_F2", -2, -1.75f, 2, 1.75f, 4, _tileHall);
            Slab(root, "Bridge_F2_North", -1.5f, 1.75f, 1.5f, 4, 4, _tileHall);
            // F2 -> F3 continues east off the landing, bridging to the F3 east link:
            RampRailed(root, "Stairs_F2_to_F3", new Vector3(2, 4, 0), new Vector3(10, 8, 0), 3.2f, _tileHall);
            Slab(root, "Bridge_F3_East", 10, -1.6f, 11, 1.6f, 8, _tileHall);

            // landing edge rails (open where flights arrive/depart, gap at the north bridge)
            RailX(root, -1.75f, -2, 2, new List<(float, float)>(), 4);
            RailX(root, 1.75f, -2, 2, new List<(float, float)> { (0f, 3.2f) }, 4);

            // hall stair-well guards (east edge open - that's where the basement flight emerges)
            RailX(root, 1.8f, -10.4f, -1.6f, new List<(float, float)>(), 0);
            RailX(root, -1.8f, -10.4f, -1.6f, new List<(float, float)>(), 0);

            // atrium void edge rails on F2/F3
            RailX(root, 4, -11, 11, new List<(float, float)> { (0f, 3.4f) }, 4);   // F2 north: gap at bridge
            RailX(root, -4, -11, 11, new List<(float, float)>(), 4);
            RailSegZ(root, -11, -4, 4, 4); RailSegZ(root, 11, -4, 4, 4);
            RailX(root, 4, -11, 11, new List<(float, float)>(), 8);
            RailX(root, -4, -11, 11, new List<(float, float)>(), 8);
            RailSegZ(root, -11, -4, 4, 8);
            RailSegZ(root, 11, -4, -1.6f, 8); RailSegZ(root, 11, 1.6f, 4, 8);      // F3 east: gap at bridge

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
            RenderSettings.ambientLight = new Color(0.10f, 0.105f, 0.12f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = new Color(0.015f, 0.015f, 0.02f);
            RenderSettings.fogDensity = 0.015f;

            // hall fluorescent strips, wall-mounted above the wainscot on both sides
            for (int i = 0; i < 3; i++)
            {
                Box(root, "HallTube", new Vector3(-8f + i * 8f, 3.05f, 3.80f), new Vector3(3.2f, 0.07f, 0.12f), _tube);
                Box(root, "HallTube", new Vector3(-8f + i * 8f, 3.05f, -3.80f), new Vector3(3.2f, 0.07f, 0.12f), _tube);
            }

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
            _plaster = Mat("Plaster", new Color(0.58f, 0.54f, 0.44f));
            _plasterLight = Mat("PlasterLight", new Color(0.72f, 0.70f, 0.63f));
            _wood = Mat("WoodDark", new Color(0.26f, 0.17f, 0.10f));
            _carpet = Mat("Carpet", new Color(0.10f, 0.20f, 0.15f));
            _carpetOffice = Mat("CarpetOffice", new Color(0.30f, 0.13f, 0.11f));
            _tile = Mat("FloorTile", new Color(0.38f, 0.40f, 0.36f));
            _tileHall = Mat("FloorTileHall", new Color(0.47f, 0.48f, 0.43f));
            _concrete = Mat("Concrete", new Color(0.33f, 0.33f, 0.34f));
            _metal = Mat("Metal", new Color(0.35f, 0.36f, 0.40f));
            _tube = MatEmissive("TubeLight", new Color(0.9f, 0.92f, 0.85f), new Color(1.6f, 1.55f, 1.35f));
            _screen = MatEmissive("Screen", new Color(0.1f, 0.2f, 0.12f), new Color(0.25f, 0.9f, 0.35f));
            _redAccent = MatEmissive("RedAccent", new Color(0.45f, 0.07f, 0.05f), new Color(0.55f, 0.06f, 0.04f));
            _yellow = Mat("CautionYellow", new Color(0.75f, 0.62f, 0.10f));
        }

        private static Material MatEmissive(string name, Color baseCol, Color emission)
        {
            var m = Mat(name, baseCol);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            m.SetColor("_EmissionColor", emission);
            return m;
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

            // ceiling board + fluorescent tubes (except the open garage volume)
            if (r.Name != "ParkingGarage")
            {
                float cy = r.Y + (r.Name == "CourtroomA" ? 3.9f : 3.42f);
                Slab(g, r.Name + "_Ceiling", r.X0 + 0.15f, r.Z0 + 0.15f, r.X1 - 0.15f, r.Z1 - 0.15f,
                     cy + T, _plasterLight);
                float cx = (r.X0 + r.X1) / 2f, cz = (r.Z0 + r.Z1) / 2f;
                Box(g, "Tube", new Vector3(cx - 2.5f, cy - 0.05f, cz), new Vector3(2.4f, 0.07f, 0.2f), _tube);
                Box(g, "Tube", new Vector3(cx + 2.5f, cy - 0.05f, cz), new Vector3(2.4f, 0.07f, 0.2f), _tube);
            }

            // door frame (+ sealed slab for Courtroom A)
            BuildDoorway(g, r);
            DoorSign(g, r);
            AddProps(g, r);
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

            var sign = new GameObject("Sign_" + r.Name);
            sign.transform.SetParent(g.transform);
            sign.transform.position = pos;
            // TextMesh reads correctly when its +Z points AWAY from the viewer.
            // The viewer stands on the corridor side; +Z must point INTO the room.
            float yaw = r.DoorSide == 'N' ? 180f : r.DoorSide == 'S' ? 0f : r.DoorSide == 'E' ? -90f : 90f;
            sign.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            string label = r.SealedDoor ? "COURTROOM A - COURT IS IN SESSION" : Pretty(r.Name);
            // wooden plate sits BEHIND the text (toward the wall), single readable face
            var plate = new GameObject("Plate");
            plate.transform.SetParent(sign.transform, false);
            plate.transform.localPosition = new Vector3(0f, 0f, 0.06f);
            var plateBox = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plateBox.transform.SetParent(plate.transform, false);
            plateBox.transform.localScale = new Vector3(Mathf.Min(label.Length * 0.14f + 0.4f, 3.2f), 0.5f, 0.06f);
            plateBox.GetComponent<Renderer>().sharedMaterial = _wood;

            var tgo = new GameObject("Text");
            tgo.transform.SetParent(sign.transform, false);
            var tm = tgo.AddComponent<TextMesh>();
            tm.text = label;
            tm.characterSize = 0.05f;
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
            var m = mat != null ? mat : _plaster;
            Vector3 mid = new Vector3((x0 + x1) / 2f, 0, (z0 + z1) / 2f);
            if (m == _plaster)
            {
                // courthouse two-tone: dark wood wainscot under plaster
                const float wainH = 1.15f;
                Box(parent, "Wainscot", mid + Vector3.up * (y + wainH / 2f),
                    alongX ? new Vector3(x1 - x0, wainH, T + 0.04f) : new Vector3(T + 0.04f, wainH, z1 - z0), _wood);
                Box(parent, "Wall", mid + Vector3.up * (y + wainH + (WallH - wainH) / 2f),
                    alongX ? new Vector3(x1 - x0, WallH - wainH, T) : new Vector3(T, WallH - wainH, z1 - z0), _plaster);
                return;
            }
            Box(parent, "Wall", mid + Vector3.up * (y + WallH / 2f),
                alongX ? new Vector3(x1 - x0, WallH, T) : new Vector3(T, WallH, z1 - z0), m);
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

        /// <summary>A staircase flight with wooden side rails riding the slope.</summary>
        private static void RampRailed(GameObject root, string name, Vector3 bottom, Vector3 top, float width, Material mat)
        {
            Ramp(root, name, bottom, top, width, mat);
            // side rails: thin sloped boxes lifted above the surface, offset to the ramp's edges
            Vector3 side = Vector3.Cross(Vector3.up, (top - bottom).normalized).normalized;
            Vector3 lift = Vector3.up * 0.62f;
            Vector3 off = side * (width / 2f + 0.08f);
            Ramp(root, name + "_RailL", bottom + lift - off, top + lift - off, 0.12f, _wood);
            Ramp(root, name + "_RailR", bottom + lift + off, top + lift + off, 0.12f, _wood);
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

        /// <summary>Per-room furniture blockout: rooms should read at a glance.</summary>
        private static void AddProps(GameObject g, Room r)
        {
            float cx = (r.X0 + r.X1) / 2f, cz = (r.Z0 + r.Z1) / 2f, y = r.Y;
            switch (r.Name)
            {
                case "CourtroomA": return; // furnished separately
                case "EvidenceLocker":
                case "Archives":
                case "RecordsRoom":
                    for (int row = 0; row < 3; row++)
                        Box(g, "Shelf", new Vector3(cx, y + 1.1f, r.Z0 + 2.2f + row * 2.6f),
                            new Vector3((r.X1 - r.X0) - 3.5f, 2.2f, 0.6f), _metal);
                    Box(g, "Desk", new Vector3(r.X0 + 1.6f, y + 0.5f, r.Z0 + 1.2f), new Vector3(1.8f, 1.0f, 0.8f), _wood);
                    break;
                case "Lab":
                    Box(g, "Bench_N", new Vector3(cx, y + 0.5f, r.Z1 - 1.0f), new Vector3((r.X1 - r.X0) - 3f, 1.0f, 1.0f), _metal);
                    Box(g, "Bench_S", new Vector3(cx, y + 0.5f, r.Z0 + 1.0f), new Vector3((r.X1 - r.X0) - 3f, 1.0f, 1.0f), _metal);
                    for (int m = 0; m < 3; m++)
                    {
                        Box(g, "Machine", new Vector3(r.X0 + 2.5f + m * 3.4f, y + 1.35f, r.Z1 - 1.0f),
                            new Vector3(1.1f, 0.7f, 0.7f), _metal);
                        Box(g, "MachineScreen", new Vector3(r.X0 + 2.5f + m * 3.4f, y + 1.4f, r.Z1 - 0.6f),
                            new Vector3(0.7f, 0.4f, 0.06f), _screen);
                    }
                    break;
                case "ProsecutionOffice":
                case "DefenseOffice":
                case "JudgeChambers":
                case "StaffRoom":
                    for (int d = 0; d < 2; d++)
                    {
                        Box(g, "Desk", new Vector3(cx - 2.5f + d * 5f, y + 0.42f, cz), new Vector3(2.2f, 0.84f, 1.1f), _wood);
                        Box(g, "Chair", new Vector3(cx - 2.5f + d * 5f, y + 0.32f, cz - 1.2f), new Vector3(0.55f, 0.64f, 0.55f), _wood);
                    }
                    Box(g, "Cabinet", new Vector3(r.X1 - 0.8f, y + 1.0f, r.Z1 - 0.8f), new Vector3(1.0f, 2.0f, 0.6f), _metal);
                    break;
                case "Cafeteria":
                    for (int t2 = 0; t2 < 3; t2++)
                        Box(g, "Table", new Vector3(r.X0 + 2.5f + t2 * 3.4f, y + 0.42f, cz), new Vector3(1.6f, 0.84f, 1.6f), _wood);
                    Box(g, "Counter", new Vector3(cx, y + 0.55f, r.Z0 + 1.0f), new Vector3((r.X1 - r.X0) - 4f, 1.1f, 1.0f), _metal);
                    // the vending machine: one red accent per room (GDD 11)
                    Box(g, "Vending", new Vector3(r.X1 - 0.9f, y + 1.0f, r.Z1 - 0.9f), new Vector3(1.0f, 2.0f, 0.9f), _redAccent);
                    Box(g, "VendingGlow", new Vector3(r.X1 - 0.9f, y + 1.2f, r.Z1 - 1.38f), new Vector3(0.6f, 1.2f, 0.06f), _screen);
                    break;
                case "Security":
                    Box(g, "Console", new Vector3(cx, y + 0.5f, r.Z1 - 1.0f), new Vector3(4.5f, 1.0f, 0.9f), _metal);
                    for (int s = 0; s < 4; s++)
                        Box(g, "Monitor", new Vector3(cx - 1.7f + s * 1.15f, y + 1.45f, r.Z1 - 0.75f),
                            new Vector3(0.9f, 0.65f, 0.08f), _screen);
                    Box(g, "Chair", new Vector3(cx, y + 0.32f, r.Z1 - 2.2f), new Vector3(0.55f, 0.64f, 0.55f), _wood);
                    break;
                case "HoldingCells":
                    for (int cell = 0; cell < 2; cell++)
                    {
                        float bx = r.X0 + 1.5f + cell * ((r.X1 - r.X0 - 3f) / 2f + 0.8f);
                        for (int bar = 0; bar < 9; bar++)
                        {
                            var b = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                            b.name = "Bar";
                            b.transform.SetParent(g.transform);
                            b.transform.position = new Vector3(bx + bar * 0.42f, y + 1.3f, cz);
                            b.transform.localScale = new Vector3(0.07f, 1.3f, 0.07f);
                            b.GetComponent<Renderer>().sharedMaterial = _metal;
                        }
                        Box(g, "Bunk", new Vector3(bx + 1.7f, y + 0.3f, r.Z1 - 0.8f), new Vector3(2.0f, 0.25f, 0.9f), _metal);
                    }
                    break;
                case "BoilerRoom":
                    for (int b2 = 0; b2 < 2; b2++)
                    {
                        var tank = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                        tank.name = "Boiler";
                        tank.transform.SetParent(g.transform);
                        tank.transform.position = new Vector3(cx - 2.5f + b2 * 5f, y + 1.5f, cz);
                        tank.transform.localScale = new Vector3(1.8f, 1.5f, 1.8f);
                        tank.GetComponent<Renderer>().sharedMaterial = _metal;
                    }
                    break;
                case "Maintenance":
                    Box(g, "Shelf", new Vector3(cx, y + 1.1f, r.Z1 - 0.6f), new Vector3((r.X1 - r.X0) - 3f, 2.2f, 0.6f), _metal);
                    var bucket = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    bucket.name = "MopBucket";
                    bucket.transform.SetParent(g.transform);
                    bucket.transform.position = new Vector3(cx, y + 0.25f, cz);
                    bucket.transform.localScale = new Vector3(0.45f, 0.25f, 0.45f);
                    bucket.GetComponent<Renderer>().sharedMaterial = _yellow;
                    break;
                case "PressRoom":
                    Box(g, "Podium", new Vector3(cx, y + 0.6f, r.Z1 - 1.2f), new Vector3(0.8f, 1.2f, 0.6f), _wood);
                    for (int row = 0; row < 2; row++)
                        for (int c2 = 0; c2 < 3; c2++)
                            Box(g, "Chair", new Vector3(cx - 2f + c2 * 2f, y + 0.32f, cz - 1f + row * 1.3f),
                                new Vector3(0.55f, 0.64f, 0.55f), _wood);
                    break;
                case "CourtroomB":
                    for (int crate = 0; crate < 6; crate++)
                        Box(g, "Crate", new Vector3(r.X0 + 1.5f + (crate % 3) * 1.6f, y + 0.45f + (crate / 3) * 0.9f,
                            r.Z1 - 1.4f), new Vector3(1.2f, 0.9f, 1.2f), _wood);
                    Box(g, "DustyBench", new Vector3(cx, y + 0.6f, r.Z0 + 1.4f), new Vector3(3.5f, 1.2f, 1.0f), _wood);
                    break;
                case "ParkingGarage":
                    for (int car = 0; car < 3; car++)
                    {
                        float carX = r.X0 + 3f + car * 4.5f;
                        Box(g, "CarBody", new Vector3(carX, y + 0.75f, cz - 2f), new Vector3(2.0f, 0.9f, 4.2f),
                            car == 1 ? _redAccent : _metal);
                        Box(g, "CarCabin", new Vector3(carX, y + 1.45f, cz - 2.4f), new Vector3(1.8f, 0.6f, 2.0f), _metal);
                    }
                    break;
            }
        }

        private static void RoomLight(GameObject root, string name, float x, float y, float z, Color c)
        {
            var l = new GameObject(name).AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 16f;
            l.intensity = 4.4f;
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
