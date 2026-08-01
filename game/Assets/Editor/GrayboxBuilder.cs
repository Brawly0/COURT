using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using CaseClosed.Game;

namespace CaseClosed.EditorTools
{
    /// <summary>
    /// Courthouse blockout v2.3 - BIG. 48m core, 12m-wide atrium hall, four
    /// levels, a grand central staircase stack (12m flights, 4.5m wide, railed),
    /// rooms roughly doubled. Circulation rule unchanged: every flight lands on
    /// a real floor, no stair touches a door wall, the atrium sees everything.
    /// </summary>
    public static class GrayboxBuilder
    {
        private const float WallH = 3.5f;
        private const float T = 0.3f;
        private const float DoorW = 3.0f;

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

            // ---------------- hall floor with the CENTERED stair well ----------------
            // well: x[-14.5,-1.5], z[-2.5,2.5]
            Slab(root, "Hall_Floor_W", -24, -6, -14.5f, 6, 0, _tileHall);
            Slab(root, "Hall_Floor_E", -1.5f, -6, 24, 6, 0, _tileHall);
            Slab(root, "Hall_Floor_N", -14.5f, 2.5f, -1.5f, 6, 0, _tileHall);
            Slab(root, "Hall_Floor_S", -14.5f, -6, -1.5f, -2.5f, 0, _tileHall);
            Slab(root, "GarageLink_Floor", -38, -3, -24, 3, 0, _concrete);
            Slab(root, "Garage_Floor", -56, -10, -38, 10, 0, _concrete);

            // basement plate
            Slab(root, "Basement_Floor", -24, -20, 24, 20, -4, _concrete);

            // F2 / F3 rings: wings + end links + BALCONY CORRIDORS in front of the room
            // doors (doors must open onto a walkway, never onto the void).
            // Atrium void slot shrinks to x[-18,18], z[-3,3].
            foreach (float fy in new[] { 4f, 8f })
            {
                string p = fy < 6 ? "F2" : "F3";
                Slab(root, p + "_NorthWing", -24, 6, 24, 20, fy, _tile);
                Slab(root, p + "_SouthWing", -24, -20, 24, -6, fy, _tile);
                Slab(root, p + "_WestLink", -24, -6, -18, 6, fy, _tile);
                Slab(root, p + "_EastLink", 18, -6, 24, 6, fy, _tile);
                Slab(root, p + "_NorthBalcony", -18, 3, 18, 6, fy, _tileHall);
                Slab(root, p + "_SouthBalcony", -18, -6, 18, -3, fy, _tileHall);
                // ceiling boards under every overhang - undersides must read as
                // ceiling tiles, not as upside-down checkerboard
                Box(root, p + "_NBalcCeil", new Vector3(0, fy - 0.34f, 4.5f), new Vector3(36f, 0.06f, 3f), _plasterLight);
                Box(root, p + "_SBalcCeil", new Vector3(0, fy - 0.34f, -4.5f), new Vector3(36f, 0.06f, 3f), _plasterLight);
                Box(root, p + "_WLinkCeil", new Vector3(-21f, fy - 0.34f, 0f), new Vector3(6f, 0.06f, 11.9f), _plasterLight);
                Box(root, p + "_ELinkCeil", new Vector3(21f, fy - 0.34f, 0f), new Vector3(6f, 0.06f, 11.9f), _plasterLight);
            }

            // roof + garage roofs + earth
            Slab(root, "Roof", -24.5f, -20.5f, 24.5f, 22.5f, 11.8f, _plaster);
            Slab(root, "Garage_Roof", -56, -10, -38, 10, 3.4f, _concrete);
            Slab(root, "GarageLink_Roof", -38, -3, -24, 3, 3.4f, _concrete);
            Slab(root, "Earth", -80, -50, 80, 50, -6, _concrete);

            // ---------------- rooms ----------------
            var rooms = new List<Room>
            {
                // ground
                new Room("CourtroomA",     -2,   6, 24, 22, 0, 'S', 6f, _wood, _carpet, sealedDoor: true),
                new Room("EvidenceLocker", -24,  6, -2, 18, 0, 'S', -13f),
                new Room("Security",       -24, -20, -6, -6, 0, 'N', -15f),
                new Room("Cafeteria",        6, -20, 24, -6, 0, 'N', 15f),
                new Room("ParkingGarage",  -56, -10, -38, 10, 0, 'E', 0f, _concrete, _concrete),
                // basement (split at x=0)
                new Room("Lab",            -24,   6, 0, 20, -4, 'S', -12f),
                new Room("Maintenance",      0,   6, 24, 20, -4, 'S', 12f),
                new Room("HoldingCells",   -24, -20, 0, -6, -4, 'N', -12f, _concrete, _concrete),
                new Room("BoilerRoom",       0, -20, 24, -6, -4, 'N', 12f, _concrete, _concrete),
                // floor 2
                new Room("ProsecutionOffice", -24, 6, 0, 20, 4, 'S', -12f, null, _carpetOffice),
                new Room("Archives",            0, 6, 24, 20, 4, 'S', 12f),
                new Room("RecordsRoom",       -24, -20, 0, -6, 4, 'N', -12f),
                new Room("DefenseOffice",       0, -20, 24, -6, 4, 'N', 12f, null, _carpetOffice),
                // floor 3
                new Room("CourtroomB",        -24, 6, 0, 20, 8, 'S', -12f, _wood, _carpet),
                new Room("JudgeChambers",       0, 6, 24, 20, 8, 'S', 12f, _wood),
                new Room("StaffRoom",         -24, -20, 0, -6, 8, 'N', -12f),
                new Room("PressRoom",           0, -20, 24, -6, 8, 'N', 12f),
            };
            foreach (var r in rooms) BuildRoom(root, r);

            // hall end walls (west has the garage doorway)
            WallSeg(root, 24, 24, -6, 6, 0, _plaster);
            WallSeg(root, -24, -24, -6, -3, 0, _plaster);
            WallSeg(root, -24, -24, 3, 6, 0, _plaster);
            // garage shell + link corridor
            WallSeg(root, -56, -56, -10, 10, 0, _brick);
            WallSeg(root, -56, -38, -10, -10, 0, _brick);
            WallSeg(root, -56, -38, 10, 10, 0, _brick);
            WallSeg(root, -38, -38, -10, -3, 0, _brick);
            WallSeg(root, -38, -38, 3, 10, 0, _brick);
            WallSeg(root, -38, -24, -3, -3, 0, _brick);
            WallSeg(root, -38, -24, 3, 3, 0, _brick);
            BuildShell(root);

            // ---------------- THE GRAND STAIRCASE (stacked, mid-atrium) ----------------
            RampRailed(root, "Stairs_B_to_G", new Vector3(-14, -4, 0), new Vector3(-2, 0, 0), 4.5f, _wood);
            Slab(root, "Stairs_G_Base", -15, -2.5f, -14, 2.5f, 0, _tileHall);       // seals the well's west edge
            RampRailed(root, "Stairs_G_to_F2", new Vector3(-14, 0, 0), new Vector3(-2, 4, 0), 4.5f, _wood);
            Slab(root, "Landing_F2", -2, -2.5f, 3, 2.5f, 4, _tileHall);             // mid-air landing over the hall
            Slab(root, "Bridge_F2_North", -1.5f, 2.5f, 1.5f, 3, 4, _tileHall);      // landing -> F2 north balcony
            RampRailed(root, "Stairs_F2_to_F3", new Vector3(3, 4, 0), new Vector3(15, 8, 0), 4.5f, _wood);
            Slab(root, "Bridge_F3_East", 15, -2, 18, 2, 8, _tileHall);              // flight top -> F3 east link

            // landing edge rails (open west = arrival, open east = departure)
            RailX(root, -2.5f, -2, 3, new List<(float, float)>(), 4);
            RailX(root, 2.5f, -2, 3, new List<(float, float)> { (0f, 3.2f) }, 4);

            // hall well guards (east edge open - the basement flight emerges there)
            RailX(root, 2.5f, -14.5f, -1.5f, new List<(float, float)>(), 0);
            RailX(root, -2.5f, -14.5f, -1.5f, new List<(float, float)>(), 0);

            // atrium void edge rails, on the BALCONY edges (z = +-3)
            RailX(root, 3, -18, 18, new List<(float, float)> { (0f, 3.4f) }, 4);    // F2 north: bridge gap
            RailX(root, -3, -18, 18, new List<(float, float)>(), 4);
            RailSegZ(root, -18, -3, 3, 4); RailSegZ(root, 18, -3, 3, 4);
            RailX(root, 3, -18, 18, new List<(float, float)>(), 8);
            RailX(root, -3, -18, 18, new List<(float, float)>(), 8);
            RailSegZ(root, -18, -3, 3, 8);
            RailSegZ(root, 18, -3, -2, 8); RailSegZ(root, 18, 2, 3, 8);             // F3 east: bridge gap

            // ---------------- zone anchors ----------------
            Anchor(root, "MainHall", 10, 0, 0);
            Anchor(root, "CourtroomA", 11, 0, 14);
            Anchor(root, "EvidenceLocker", -13, 0, 12);
            Anchor(root, "Security", -15, 0, -13);
            Anchor(root, "Cafeteria", 15, 0, -13);
            Anchor(root, "ParkingGarage", -47, 0, 0);
            Anchor(root, "Lab", -12, -4, 13);
            Anchor(root, "Maintenance", 12, -4, 13);
            Anchor(root, "HoldingCells", -12, -4, -13);
            Anchor(root, "BoilerRoom", 12, -4, -13);
            Anchor(root, "ProsecutionOffice", -12, 4, 13);
            Anchor(root, "Archives", 12, 4, 13);
            Anchor(root, "RecordsRoom", -12, 4, -13);
            Anchor(root, "DefenseOffice", 12, 4, -13);
            Anchor(root, "CourtroomB", -12, 8, 13);
            Anchor(root, "JudgeChambers", 12, 8, 13);
            Anchor(root, "StaffRoom", -12, 8, -13);
            Anchor(root, "PressRoom", 12, 8, -13);

            FurnishCourtroom(root);
            DetailPass(root);

            // ---------------- light: municipal dread ----------------
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.10f, 0.105f, 0.12f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = new Color(0.015f, 0.015f, 0.02f);
            RenderSettings.fogDensity = 0.012f;

            foreach (var r in rooms)
            {
                float cx = (r.X0 + r.X1) / 2f, cz = (r.Z0 + r.Z1) / 2f;
                Color c = r.Name.Contains("Garage") || r.Y < 0
                    ? new Color(0.72f, 0.95f, 0.78f) : new Color(1f, 0.92f, 0.74f);
                // big rooms get two lamps
                RoomLight(root, r.Name + "_LightW", cx - (r.X1 - r.X0) / 4f, r.Y + 2.9f, cz, c);
                RoomLight(root, r.Name + "_LightE", cx + (r.X1 - r.X0) / 4f, r.Y + 2.9f, cz, c);
            }
            foreach (float lx in new[] { -20f, -8f, 4f, 14f, 21f })
            {
                RoomLight(root, $"Hall_{lx}", lx, 3.2f, 0, new Color(1f, 0.92f, 0.74f));
                RoomLight(root, $"AtriumF2_{lx}", lx, 7.2f, 0, new Color(1f, 0.92f, 0.74f));
                RoomLight(root, $"AtriumF3_{lx}", lx, 11.2f, 0, new Color(1f, 0.92f, 0.74f));
            }
            RoomLight(root, "Basement_HallW", -8, -1.2f, 0, new Color(0.78f, 0.92f, 0.72f));
            RoomLight(root, "Basement_HallE", 8, -1.2f, 0, new Color(0.78f, 0.92f, 0.72f));
            RoomLight(root, "Courtroom_Key", 11, 3.2f, 14, new Color(1f, 0.95f, 0.8f));
            RoomLight(root, "Courtroom_Bench", 11, 3.0f, 19.5f, new Color(1f, 0.93f, 0.75f)); // the bench is the stage

            // hall fluorescent strips, wall-mounted above the wainscot
            foreach (float tx in new[] { -20f, -12f, -4f, 4f, 12f, 20f })
            {
                Box(root, "HallTube", new Vector3(tx, 3.05f, 5.80f), new Vector3(3.6f, 0.07f, 0.12f), _tube);
                Box(root, "HallTube", new Vector3(tx, 3.05f, -5.80f), new Vector3(3.6f, 0.07f, 0.12f), _tube);
            }
        }

        // ------------------------------------------------------------------
        private static Material _glass, _plant, _paper, _flagBlue, _flagRed, _gold;

        private static void BuildMaterials()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Materials"))
                AssetDatabase.CreateFolder("Assets", "Materials");

            // textures carry the color; tints stay near-white unless deliberate
            var wood = TextureFactory.WoodPlanks();
            var checker = TextureFactory.CheckerFloor();
            var tile = TextureFactory.TileFloor();
            var brick = TextureFactory.Brick();
            var carpet = TextureFactory.Carpet();
            var plaster = TextureFactory.Plaster();
            var ceiling = TextureFactory.CeilingTiles();
            var concrete = TextureFactory.Concrete();

            _brick = Mat("Brick", Color.white, brick);
            _plaster = Mat("Plaster", Color.white, plaster);
            _plasterLight = Mat("PlasterLight", Color.white, ceiling);
            _wood = Mat("WoodDark", new Color(0.95f, 0.88f, 0.82f), wood);
            _carpet = Mat("Carpet", new Color(0.22f, 0.42f, 0.30f), carpet);
            _carpetOffice = Mat("CarpetOffice", new Color(0.60f, 0.26f, 0.22f), carpet);
            _tile = Mat("FloorTile", Color.white, tile);
            _tileHall = Mat("FloorTileHall", Color.white, checker);
            _concrete = Mat("Concrete", Color.white, concrete);
            _metal = Mat("Metal", new Color(0.35f, 0.36f, 0.40f));
            _tube = MatEmissive("TubeLight", new Color(0.9f, 0.92f, 0.85f), new Color(1.6f, 1.55f, 1.35f));
            _screen = MatEmissive("Screen", new Color(0.1f, 0.2f, 0.12f), new Color(0.25f, 0.9f, 0.35f));
            _redAccent = MatEmissive("RedAccent", new Color(0.45f, 0.07f, 0.05f), new Color(0.55f, 0.06f, 0.04f));
            _yellow = Mat("CautionYellow", new Color(0.75f, 0.62f, 0.10f));
            _glass = Mat("Glass", new Color(0.06f, 0.09f, 0.13f));
            _glass.SetFloat("_Smoothness", 0.85f);
            _plant = Mat("Plant", new Color(0.16f, 0.30f, 0.13f));
            _paper = Mat("Paper", new Color(0.86f, 0.85f, 0.78f));
            _flagBlue = Mat("FlagBlue", new Color(0.14f, 0.18f, 0.38f));
            _flagRed = Mat("FlagRed", new Color(0.48f, 0.12f, 0.12f));
            _gold = MatEmissive("Gold", new Color(0.62f, 0.50f, 0.20f), new Color(0.18f, 0.14f, 0.05f));
            // runtime-spawn materials (assigned to CaseRuntime by ProjectSetup —
            // unassigned primitives go magenta in builds)
            Mat("Manila", new Color(0.78f, 0.68f, 0.45f));
            Mat("EvidenceYellow", new Color(0.88f, 0.72f, 0.12f));
            Mat("Suit", new Color(0.13f, 0.13f, 0.16f));
            Mat("Skin", new Color(0.80f, 0.62f, 0.48f));
        }

        private static Material MatEmissive(string name, Color baseCol, Color emission)
        {
            var m = Mat(name, baseCol);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            m.SetColor("_EmissionColor", emission);
            return m;
        }

        private static Material Mat(string name, Color c, Texture2D tex = null)
        {
            string path = $"Assets/Materials/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.color = c;
            m.mainTexture = tex;
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
            if (mat != null)
            {
                var r = b.GetComponent<Renderer>();
                r.sharedMaterial = mat;
                // world-space texture density: ~1 repeat per 2m, per-renderer,
                // so a 48m floor and a 2m shelf read at the same scale
                if (mat.mainTexture != null)
                {
                    float sx, sy;
                    if (size.y <= size.x && size.y <= size.z) { sx = size.x / 2f; sy = size.z / 2f; }       // slab
                    else if (size.z <= size.x && size.z <= size.y) { sx = size.x / 2f; sy = size.y / 2f; }  // wall along X
                    else { sx = size.z / 2f; sy = size.y / 2f; }                                            // wall along Z
                    var mpb = new MaterialPropertyBlock();
                    mpb.SetVector("_BaseMap_ST", new Vector4(Mathf.Max(0.5f, sx), Mathf.Max(0.5f, sy), 0f, 0f));
                    r.SetPropertyBlock(mpb);
                }
            }
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

            if (r.Name != "ParkingGarage")
            {
                float cy = r.Y + 3.42f;   // uniform: taller ceilings exposed the shell brick above the walls
                Slab(g, r.Name + "_Ceiling", r.X0 + 0.15f, r.Z0 + 0.15f, r.X1 - 0.15f, r.Z1 - 0.15f,
                     cy + T, _plasterLight);
                float cx = (r.X0 + r.X1) / 2f, cz = (r.Z0 + r.Z1) / 2f, q = (r.X1 - r.X0) / 4f;
                Box(g, "Tube", new Vector3(cx - q, cy - 0.05f, cz), new Vector3(3.0f, 0.07f, 0.2f), _tube);
                Box(g, "Tube", new Vector3(cx + q, cy - 0.05f, cz), new Vector3(3.0f, 0.07f, 0.2f), _tube);
            }

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
            // TextMesh reads correctly when its +Z points AWAY from the viewer;
            // the viewer stands on the corridor side, so +Z points INTO the room.
            float yaw = r.DoorSide == 'N' ? 180f : r.DoorSide == 'S' ? 0f : r.DoorSide == 'E' ? -90f : 90f;
            sign.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            string label = r.SealedDoor ? "COURTROOM A - COURT IS IN SESSION" : Pretty(r.Name);
            var plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plate.name = "Plate";
            plate.transform.SetParent(sign.transform, false);
            plate.transform.localPosition = new Vector3(0f, 0f, 0.06f);
            plate.transform.localScale = new Vector3(Mathf.Min(label.Length * 0.14f + 0.4f, 3.4f), 0.5f, 0.06f);
            plate.GetComponent<Renderer>().sharedMaterial = _wood;

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

        private static void RampRailed(GameObject root, string name, Vector3 bottom, Vector3 top, float width, Material mat)
        {
            Ramp(root, name, bottom, top, width, mat);
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

        private static void AddProps(GameObject g, Room r)
        {
            float cx = (r.X0 + r.X1) / 2f, cz = (r.Z0 + r.Z1) / 2f, y = r.Y;
            float w = r.X1 - r.X0;
            switch (r.Name)
            {
                case "CourtroomA": return;
                case "EvidenceLocker":
                case "Archives":
                case "RecordsRoom":
                    for (int row = 0; row < 4; row++)
                        Box(g, "Shelf", new Vector3(cx, y + 1.1f, r.Z0 + 2.4f + row * 3.0f),
                            new Vector3(w - 4f, 2.2f, 0.7f), _metal);
                    Box(g, "Desk", new Vector3(r.X0 + 2.0f, y + 0.5f, r.Z0 + 1.3f), new Vector3(2.0f, 1.0f, 0.9f), _wood);
                    break;
                case "Lab":
                    Box(g, "Bench_N", new Vector3(cx, y + 0.5f, r.Z1 - 1.1f), new Vector3(w - 4f, 1.0f, 1.1f), _metal);
                    Box(g, "Bench_S", new Vector3(cx, y + 0.5f, r.Z0 + 1.1f), new Vector3(w - 4f, 1.0f, 1.1f), _metal);
                    for (int m = 0; m < 4; m++)
                    {
                        float mx = r.X0 + 3f + m * ((w - 6f) / 3f);
                        Box(g, "Machine", new Vector3(mx, y + 1.35f, r.Z1 - 1.1f), new Vector3(1.2f, 0.7f, 0.8f), _metal);
                        Box(g, "MachineScreen", new Vector3(mx, y + 1.4f, r.Z1 - 0.62f), new Vector3(0.8f, 0.45f, 0.06f), _screen);
                    }
                    break;
                case "ProsecutionOffice":
                case "DefenseOffice":
                case "JudgeChambers":
                case "StaffRoom":
                    for (int d = 0; d < 3; d++)
                    {
                        float dx = cx - 6f + d * 6f;
                        Box(g, "Desk", new Vector3(dx, y + 0.42f, cz), new Vector3(2.4f, 0.84f, 1.2f), _wood);
                        Box(g, "Chair", new Vector3(dx, y + 0.32f, cz - 1.3f), new Vector3(0.55f, 0.64f, 0.55f), _wood);
                        // CRT + paperwork (nobody's desk is clean in this courthouse)
                        Box(g, "Crt", new Vector3(dx - 0.6f, y + 1.05f, cz + 0.2f), new Vector3(0.42f, 0.38f, 0.4f), _metal);
                        Box(g, "CrtScreen", new Vector3(dx - 0.6f, y + 1.07f, cz - 0.01f), new Vector3(0.32f, 0.26f, 0.02f), _screen);
                        var stack = Box(g, "Papers", new Vector3(dx + 0.55f, y + 0.88f, cz - 0.1f), new Vector3(0.3f, 0.08f, 0.4f), _paper);
                        stack.transform.rotation = Quaternion.Euler(0f, 8f + d * 23f, 0f);
                    }
                    Box(g, "Cabinet", new Vector3(r.X1 - 1.0f, y + 1.0f, r.Z1 - 1.0f), new Vector3(1.2f, 2.0f, 0.7f), _metal);
                    break;
                case "Cafeteria":
                    for (int t2 = 0; t2 < 5; t2++)
                        Box(g, "Table", new Vector3(r.X0 + 2.8f + t2 * ((w - 6f) / 4f), y + 0.42f, cz),
                            new Vector3(1.7f, 0.84f, 1.7f), _wood);
                    Box(g, "Counter", new Vector3(cx, y + 0.55f, r.Z0 + 1.2f), new Vector3(w - 5f, 1.1f, 1.1f), _metal);
                    Box(g, "Vending", new Vector3(r.X1 - 1.1f, y + 1.0f, r.Z1 - 1.1f), new Vector3(1.1f, 2.0f, 1.0f), _redAccent);
                    Box(g, "VendingGlow", new Vector3(r.X1 - 1.1f, y + 1.2f, r.Z1 - 1.62f), new Vector3(0.7f, 1.2f, 0.06f), _screen);
                    break;
                case "Security":
                    // console lives on the SOUTH wall - the door is on the north wall
                    Box(g, "Console", new Vector3(cx, y + 0.5f, r.Z0 + 1.1f), new Vector3(6.5f, 1.0f, 1.0f), _metal);
                    for (int s = 0; s < 6; s++)
                        Box(g, "Monitor", new Vector3(cx - 2.9f + s * 1.15f, y + 1.5f, r.Z0 + 0.8f),
                            new Vector3(0.95f, 0.7f, 0.08f), _screen);
                    Box(g, "Chair", new Vector3(cx, y + 0.32f, r.Z0 + 2.4f), new Vector3(0.55f, 0.64f, 0.55f), _wood);
                    break;
                case "HoldingCells":
                    for (int cell = 0; cell < 2; cell++)
                    {
                        float bx = r.X0 + 2f + cell * ((w - 4f) / 2f + 0.5f);
                        for (int bar = 0; bar < 16; bar++)
                        {
                            var b = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                            b.name = "Bar";
                            b.transform.SetParent(g.transform);
                            b.transform.position = new Vector3(bx + bar * 0.5f, y + 1.3f, cz);
                            b.transform.localScale = new Vector3(0.07f, 1.3f, 0.07f);
                            b.GetComponent<Renderer>().sharedMaterial = _metal;
                        }
                        Box(g, "Bunk", new Vector3(bx + 2f, y + 0.3f, r.Z1 - 1.0f), new Vector3(2.2f, 0.25f, 1.0f), _metal);
                    }
                    break;
                case "BoilerRoom":
                    for (int b2 = 0; b2 < 3; b2++)
                    {
                        var tank = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                        tank.name = "Boiler";
                        tank.transform.SetParent(g.transform);
                        tank.transform.position = new Vector3(cx - 6f + b2 * 6f, y + 1.5f, cz);
                        tank.transform.localScale = new Vector3(2.0f, 1.5f, 2.0f);
                        tank.GetComponent<Renderer>().sharedMaterial = _metal;
                    }
                    break;
                case "Maintenance":
                    Box(g, "Shelf", new Vector3(cx, y + 1.1f, r.Z1 - 0.7f), new Vector3(w - 4f, 2.2f, 0.7f), _metal);
                    var bucket = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    bucket.name = "MopBucket";
                    bucket.transform.SetParent(g.transform);
                    bucket.transform.position = new Vector3(cx, y + 0.25f, cz);
                    bucket.transform.localScale = new Vector3(0.45f, 0.25f, 0.45f);
                    bucket.GetComponent<Renderer>().sharedMaterial = _yellow;
                    break;
                case "PressRoom":
                    // podium on the SOUTH wall - the door is on the north wall
                    Box(g, "Podium", new Vector3(cx, y + 0.6f, r.Z0 + 1.4f), new Vector3(0.9f, 1.2f, 0.7f), _wood);
                    for (int row = 0; row < 3; row++)
                        for (int c2 = 0; c2 < 4; c2++)
                            Box(g, "Chair", new Vector3(cx - 4.5f + c2 * 3f, y + 0.32f, cz - 2f + row * 1.6f),
                                new Vector3(0.55f, 0.64f, 0.55f), _wood);
                    break;
                case "CourtroomB":
                    for (int crate = 0; crate < 8; crate++)
                        Box(g, "Crate", new Vector3(r.X0 + 2f + (crate % 4) * 1.8f, y + 0.45f + (crate / 4) * 0.9f,
                            r.Z1 - 1.6f), new Vector3(1.3f, 0.9f, 1.3f), _wood);
                    Box(g, "DustyBench", new Vector3(cx, y + 0.6f, r.Z0 + 1.6f), new Vector3(4.5f, 1.2f, 1.2f), _wood);
                    break;
                case "ParkingGarage":
                    for (int car = 0; car < 4; car++)
                    {
                        float carX = r.X0 + 3.5f + car * 4.5f;
                        Box(g, "CarBody", new Vector3(carX, y + 0.75f, cz - 4f), new Vector3(2.0f, 0.9f, 4.4f),
                            car == 1 ? _redAccent : _metal);
                        Box(g, "CarCabin", new Vector3(carX, y + 1.45f, cz - 4.5f), new Vector3(1.8f, 0.6f, 2.1f), _metal);
                    }
                    break;
            }
        }

        private static void FurnishCourtroom(GameObject root)
        {
            var g = new GameObject("Courtroom_Furniture");
            g.transform.SetParent(root.transform);

            Box(g, "Bench_Platform", new Vector3(11, 0.2f, 20.5f), new Vector3(9f, 0.4f, 2.8f), _wood);
            Box(g, "Judge_Bench", new Vector3(11, 1.05f, 20.8f), new Vector3(6f, 1.3f, 1.3f), _wood);
            Box(g, "Bench_Back", new Vector3(11, 2.3f, 21.6f), new Vector3(7f, 3.6f, 0.25f), _wood);
            Box(g, "Witness_Stand", new Vector3(5.6f, 0.65f, 19.6f), new Vector3(1.7f, 1.3f, 1.7f), _wood);
            Box(g, "Clerk_Desk", new Vector3(16.4f, 0.55f, 19.6f), new Vector3(2.2f, 1.1f, 1.3f), _wood);

            for (int t = 0; t < 2; t++)
            {
                float x = t == 0 ? 7f : 15f;
                Box(g, "Counsel_Table", new Vector3(x, 0.45f, 15.8f), new Vector3(2.8f, 0.12f, 1.3f), _wood);
                Box(g, "Table_Leg", new Vector3(x, 0.22f, 15.8f), new Vector3(2.5f, 0.44f, 1.0f), _wood);
                for (int c = 0; c < 2; c++)
                    Box(g, "Chair", new Vector3(x - 0.7f + c * 1.4f, 0.3f, 14.6f), new Vector3(0.55f, 0.6f, 0.55f), _wood);
            }

            Box(g, "Bar_W", new Vector3(3.9f, 0.55f, 12.6f), new Vector3(11.4f, 1.1f, 0.12f), _wood);
            Box(g, "Bar_E", new Vector3(18.1f, 0.55f, 12.6f), new Vector3(11.4f, 1.1f, 0.12f), _wood);

            for (int row = 0; row < 3; row++)
                for (int col = 0; col < 2; col++)
                    Box(g, "Gallery_Bench",
                        new Vector3(5f + col * 12f, 0.28f, 8.4f + row * 1.5f),
                        new Vector3(7f, 0.55f, 0.55f), _wood);
        }

        /// <summary>Architectural dressing: the pass that separates a blockout from a build.</summary>
        private static void DetailPass(GameObject root)
        {
            var g = new GameObject("Details");
            g.transform.SetParent(root.transform);

            // atrium columns, ground to roof (they carry the balconies)
            foreach (float cxp in new[] { -18f, -12f, -6f, 0f, 12f, 18f })   // north (skip x=6: courtroom door)
                Column(g, cxp, 5.6f);
            foreach (float cxp in new[] { -18f, -12f, -6f, 0f, 6f, 12f, 18f }) // south
                Column(g, cxp, -5.6f);

            // hall benches, plants, bulletin board, clock
            foreach (var bx in new[] { (-7f, 5.2f), (16f, 5.2f), (-9f, -5.2f), (8f, -5.2f) })
            {
                Box(g, "HallBench", new Vector3(bx.Item1, 0.45f, bx.Item2), new Vector3(3.0f, 0.5f, 0.6f), _wood);
                Box(g, "HallBenchBack", new Vector3(bx.Item1, 0.95f, bx.Item2 + Mathf.Sign(bx.Item2) * 0.25f),
                    new Vector3(3.0f, 0.9f, 0.1f), _wood);
            }
            foreach (var pp in new[] { (-23f, 5f), (23f, 5f), (-23f, -5f), (23f, -5f), (-3f, 5f), (9f, -5f) })
                Plant(g, pp.Item1, 0f, pp.Item2);

            Box(g, "BulletinFrame", new Vector3(-19f, 1.9f, 5.78f), new Vector3(2.0f, 1.3f, 0.08f), _wood);
            Box(g, "BulletinCork", new Vector3(-19f, 1.9f, 5.72f), new Vector3(1.8f, 1.1f, 0.05f),
                AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Manila.mat"));
            for (int p = 0; p < 5; p++)
            {
                var paper = Box(g, "Notice", new Vector3(-19.7f + p * 0.35f, 1.95f + (p % 2) * 0.25f - 0.12f, 5.68f),
                    new Vector3(0.24f, 0.32f, 0.01f), _paper);
                paper.transform.rotation = Quaternion.Euler(0f, 0f, (p % 3 - 1) * 6f);
            }

            var clock = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            clock.name = "WallClock";
            clock.transform.SetParent(g.transform);
            clock.transform.position = new Vector3(23.8f, 3.0f, 0f);
            clock.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
            clock.transform.localScale = new Vector3(0.7f, 0.03f, 0.7f);
            clock.GetComponent<Renderer>().sharedMaterial = _paper;

            // EXIT signs: garage link mouth + east hall fire door
            // (TextMesh reads correctly with +Z pointing AWAY from the viewer)
            ExitSign(g, new Vector3(-23.6f, 3.0f, 0f), -90f);
            Box(g, "FireDoor", new Vector3(23.85f, 1.3f, -4.0f), new Vector3(0.2f, 2.6f, 1.6f), _metal);
            ExitSign(g, new Vector3(23.6f, 3.0f, -4.0f), 90f);

            // fake windows on outer walls of the big ground rooms + courtroom
            foreach (var wpos in new[] { (5f, 21.8f, 0f), (17f, 21.8f, 0f),        // courtroom north
                                         (-19f, 17.8f, 0f), (-8f, 17.8f, 0f),      // locker north
                                         (-19f, -19.8f, 0f), (-10f, -19.8f, 0f),   // security south
                                         (10f, -19.8f, 0f), (19f, -19.8f, 0f) })   // cafeteria south
            {
                Box(g, "WindowFrame", new Vector3(wpos.Item1, 2.0f, wpos.Item2), new Vector3(2.3f, 1.7f, 0.1f), _wood);
                Box(g, "WindowGlass", new Vector3(wpos.Item1, 2.0f, wpos.Item2 + (wpos.Item2 > 0 ? -0.03f : 0.03f)),
                    new Vector3(2.05f, 1.45f, 0.05f), _glass);
            }

            // parking stall lines
            for (int line = 0; line < 5; line++)
                Box(g, "ParkLine", new Vector3(-52.5f + line * 4.5f, 0.02f, -4f), new Vector3(0.14f, 0.02f, 4.6f), _paper);

            // courtroom dignity: flags flanking the bench, gold emblem, jury box
            FlagPole(g, new Vector3(7.2f, 0f, 21.0f), _flagBlue);
            FlagPole(g, new Vector3(14.8f, 0f, 21.0f), _flagRed);
            var emblem = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            emblem.name = "Emblem";
            emblem.transform.SetParent(g.transform);
            emblem.transform.position = new Vector3(11f, 2.9f, 21.42f);   // proud of the bench back, not buried in it
            emblem.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            emblem.transform.localScale = new Vector3(1.1f, 0.04f, 1.1f);
            emblem.GetComponent<Renderer>().sharedMaterial = _gold;

            Box(g, "JuryRail", new Vector3(21.4f, 0.55f, 15.5f), new Vector3(0.12f, 1.1f, 5.5f), _wood);
            Box(g, "JuryRail2", new Vector3(22.6f, 0.55f, 12.8f), new Vector3(2.6f, 1.1f, 0.12f), _wood);
            for (int jr = 0; jr < 2; jr++)
                for (int jc = 0; jc < 3; jc++)
                    Box(g, "JuryChair", new Vector3(22.2f + jr * 1.0f, 0.32f + jr * 0.25f, 13.8f + jc * 1.5f),
                        new Vector3(0.55f, 0.64f, 0.55f), _wood);
        }

        private static void Column(GameObject g, float x, float z)
        {
            var c = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            c.name = "Column";
            c.transform.SetParent(g.transform);
            c.transform.position = new Vector3(x, 5.8f, z);
            c.transform.localScale = new Vector3(0.75f, 6.0f, 0.75f);
            c.GetComponent<Renderer>().sharedMaterial = _plasterLight;
            Box(g, "ColumnBase", new Vector3(x, 0.25f, z), new Vector3(1.1f, 0.5f, 1.1f), _wood);
        }

        private static void Plant(GameObject g, float x, float y, float z)
        {
            var pot = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pot.name = "PlantPot";
            pot.transform.SetParent(g.transform);
            pot.transform.position = new Vector3(x, y + 0.25f, z);
            pot.transform.localScale = new Vector3(0.55f, 0.25f, 0.55f);
            pot.GetComponent<Renderer>().sharedMaterial = _concrete;
            var bush = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bush.name = "PlantBush";
            bush.transform.SetParent(g.transform);
            bush.transform.position = new Vector3(x, y + 0.95f, z);
            bush.transform.localScale = new Vector3(0.85f, 1.05f, 0.85f);
            UnityEngine.Object.DestroyImmediate(bush.GetComponent<Collider>());
            bush.GetComponent<Renderer>().sharedMaterial = _plant;
        }

        private static void ExitSign(GameObject g, Vector3 pos, float yaw)
        {
            var sign = Box(g, "ExitSign", pos, new Vector3(0.08f, 0.28f, 0.75f), _redAccent);
            sign.transform.rotation = Quaternion.Euler(0f, 0f, 0f);
            var t = new GameObject("ExitText").AddComponent<TextMesh>();
            t.transform.SetParent(g.transform, false);
            t.transform.position = pos + new Vector3(yaw > 0 ? 0.06f : -0.06f, 0f, 0f);
            t.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            t.text = "EXIT";
            t.characterSize = 0.09f;
            t.fontSize = 40;
            t.anchor = TextAnchor.MiddleCenter;
            t.color = new Color(1f, 0.9f, 0.9f);
        }

        private static void FlagPole(GameObject g, Vector3 basePos, Material flagMat)
        {
            var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pole.name = "FlagPole";
            pole.transform.SetParent(g.transform);
            pole.transform.position = basePos + Vector3.up * 1.5f;
            pole.transform.localScale = new Vector3(0.07f, 1.5f, 0.07f);
            pole.GetComponent<Renderer>().sharedMaterial = _gold;
            Box(g, "Flag", basePos + new Vector3(0.28f, 2.4f, 0f), new Vector3(0.5f, 0.85f, 0.03f), flagMat);
        }

        private static void RoomLight(GameObject root, string name, float x, float y, float z, Color c)
        {
            var l = new GameObject(name).AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 17f;
            l.intensity = 4.4f;
            l.color = c;
            l.transform.SetParent(root.transform);
            l.transform.position = new Vector3(x, y, z);
        }

        private static void BuildShell(GameObject root)
        {
            Box(root, "Shell_N", new Vector3(0, 4f, 22.75f), new Vector3(49.6f, 16f, 0.2f), _brick);
            Box(root, "Shell_S", new Vector3(0, 4f, -20.75f), new Vector3(49.6f, 16f, 0.2f), _brick);
            Box(root, "Shell_E", new Vector3(24.75f, 4f, 1f), new Vector3(0.2f, 16f, 43.7f), _brick);
            Box(root, "Shell_W_N", new Vector3(-24.75f, 4f, 12.9f), new Vector3(0.2f, 16f, 19.4f), _brick);
            Box(root, "Shell_W_S", new Vector3(-24.75f, 4f, -11.9f), new Vector3(0.2f, 16f, 17.4f), _brick);
            Box(root, "Shell_W_TopBand", new Vector3(-24.75f, 8f, 0f), new Vector3(0.2f, 8f, 6.4f), _brick);
        }
    }
}
