using System.Collections.Generic;
using UnityEngine;
using CaseClosed.Game;

namespace CaseClosed.EditorTools
{
    /// <summary>
    /// Builds the graybox courthouse from the GDD 04 floor plan: ground floor
    /// around a main hall, floor 2 with a balcony bridge OVER the hall (the
    /// sightline rule), a basement, and the parking garage. All primitives.
    /// Dimensions are calibrated to the walk-time targets (walk 3.5 m/s).
    /// </summary>
    public static class GrayboxBuilder
    {
        private const float WallH = 3.5f;
        private const float T = 0.3f;      // slab/wall thickness
        private const float DoorW = 2.2f;

        private class Room
        {
            public string Name; public float X0, Z0, X1, Z1, Y;
            public char DoorSide; public float DoorAt; // N/S/E/W wall, door center along it
            public Room(string n, float x0, float z0, float x1, float z1, float y, char side, float at)
            { Name = n; X0 = x0; Z0 = z0; X1 = x1; Z1 = z1; Y = y; DoorSide = side; DoorAt = at; }
        }

        public static void Build()
        {
            var root = new GameObject("Courthouse");

            // ---------------- floors & open slabs ----------------
            Slab(root, "Floor_MainHall", -20, -5, 20, 5, 0);
            Slab(root, "Floor_GarageLink", -22, -2, -20, 2, 0);
            Slab(root, "Floor_Basement", -22, -16, 2, 16, -4);
            Slab(root, "Floor2_NorthBalcony", -8, 5, 4, 17, 4);      // ramp landing + office/archive access
            Slab(root, "Floor2_HallBridge", -8, -5, -4, 5, 4);       // the bridge over the hall
            Slab(root, "Earth", -60, -40, 60, 40, -6);               // catch-all under everything

            // ---------------- rooms ----------------
            var rooms = new List<Room>
            {
                new Room("CourtroomA",        4,   5, 20, 17, 0, 'S', 12f),
                new Room("EvidenceLocker",  -20,   5, -8, 15, 0, 'S', -14f),
                new Room("Security",        -20, -13,-10, -5, 0, 'N', -15f),
                new Room("Cafeteria",         8, -13, 20, -5, 0, 'N', 14f),
                new Room("ParkingGarage",   -38,  -8,-22,  8, 0, 'E', 0f),
                new Room("Lab",             -20,   5, -8, 15,-4, 'S', -14f),
                new Room("HoldingCells",    -20, -13, -8, -5,-4, 'N', -14f),
                new Room("Archives",          4,   5, 20, 17, 4, 'W', 11f),
                new Room("ProsecutionOffice",-20,  5, -8, 15, 4, 'E', 10f),
                new Room("DefenseOffice",   -20, -13, -8, -5, 4, 'E', -9f),
            };
            foreach (var r in rooms) BuildRoom(root, r);

            // DefenseOffice reachable via a south bridge from the hall bridge
            Slab(root, "Floor2_SouthLink", -8, -13, -4, -5, 4);
            // (its door faces east onto x=-8; give the link slab reach)
            Slab(root, "Floor2_SouthLandng", -10, -13, -8, -5, 4);

            // ---------------- ramps ----------------
            Ramp(root, "Ramp_UpToFloor2", new Vector3(-4, 0, 5), new Vector3(-4, 4, 13), 4f);
            Ramp(root, "Ramp_DownToBasement", new Vector3(-2, 0, -5), new Vector3(-2, -4, -13), 4f);

            // hall edge rails with gaps at doors + ramps (falls are funny once, annoying twice)
            RailX(root, 5f, -20, 20, new List<(float, float)> { (12f, DoorW + 1f), (-14f, DoorW + 1f), (-4f, 4.6f) });
            RailX(root, -5f, -20, 20, new List<(float, float)> { (-15f, DoorW + 1f), (14f, DoorW + 1f), (-2f, 4.6f), (-19f, 2f) });

            // ---------------- zone anchors ----------------
            Anchor(root, "MainHall", 0, 0, 0);
            Anchor(root, "CourtroomA", 12, 0, 11);
            Anchor(root, "EvidenceLocker", -14, 0, 10);
            Anchor(root, "Security", -15, 0, -9);
            Anchor(root, "Cafeteria", 14, 0, -9);
            Anchor(root, "ParkingGarage", -30, 0, 0);
            Anchor(root, "Lab", -14, -4, 10);
            Anchor(root, "HoldingCells", -14, -4, -9);
            Anchor(root, "Archives", 12, 4, 11);
            Anchor(root, "ProsecutionOffice", -14, 4, 10);
            Anchor(root, "DefenseOffice", -14, 4, -9);

            // ---------------- light ----------------
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.16f, 0.17f, 0.20f);
            var sun = new GameObject("Dim Directional").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 0.30f;
            sun.color = new Color(1.0f, 0.95f, 0.85f);
            sun.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
            sun.transform.SetParent(root.transform);

            foreach (var r in rooms)
                RoomLight(root, r.Name + "_Light",
                    (r.X0 + r.X1) / 2f, r.Y + 2.9f, (r.Z0 + r.Z1) / 2f,
                    r.Name == "ParkingGarage" ? new Color(0.75f, 0.95f, 0.8f) : new Color(1f, 0.93f, 0.78f));
            RoomLight(root, "Hall_Light_E", 10, 2.9f, 0, new Color(1f, 0.93f, 0.78f));
            RoomLight(root, "Hall_Light_W", -10, 2.9f, 0, new Color(1f, 0.93f, 0.78f));
            RoomLight(root, "Basement_Light", -10, -1.2f, 0, new Color(0.8f, 0.9f, 0.75f));
        }

        // ------------------------------------------------------------------
        private static void Slab(GameObject root, string name, float x0, float z0, float x1, float z1, float yTop)
        {
            var s = GameObject.CreatePrimitive(PrimitiveType.Cube);
            s.name = name;
            s.transform.SetParent(root.transform);
            s.transform.position = new Vector3((x0 + x1) / 2f, yTop - T / 2f, (z0 + z1) / 2f);
            s.transform.localScale = new Vector3(x1 - x0, T, z1 - z0);
        }

        private static void BuildRoom(GameObject root, Room r)
        {
            var g = new GameObject("Room_" + r.Name);
            g.transform.SetParent(root.transform);
            Slab(g, r.Name + "_Floor", r.X0, r.Z0, r.X1, r.Z1, r.Y);

            // four walls; the door side gets two segments with a gap
            WallX(g, r, r.Z1, r.DoorSide == 'N');
            WallX(g, r, r.Z0, r.DoorSide == 'S');
            WallZ(g, r, r.X1, r.DoorSide == 'E');
            WallZ(g, r, r.X0, r.DoorSide == 'W');

            Sign(g, r.Name, (r.X0 + r.X1) / 2f, r.Y + 2.6f, (r.Z0 + r.Z1) / 2f);
        }

        private static void WallX(GameObject parent, Room r, float z, bool hasDoor)
        {
            if (!hasDoor) { WallSeg(parent, r.X0, r.X1, z, z, r.Y); return; }
            float a = r.DoorAt - DoorW / 2f, b = r.DoorAt + DoorW / 2f;
            if (a > r.X0) WallSeg(parent, r.X0, a, z, z, r.Y);
            if (b < r.X1) WallSeg(parent, b, r.X1, z, z, r.Y);
        }

        private static void WallZ(GameObject parent, Room r, float x, bool hasDoor)
        {
            if (!hasDoor) { WallSeg(parent, x, x, r.Z0, r.Z1, r.Y); return; }
            float a = r.DoorAt - DoorW / 2f, b = r.DoorAt + DoorW / 2f;
            if (a > r.Z0) WallSeg(parent, x, x, r.Z0, a, r.Y);
            if (b < r.Z1) WallSeg(parent, x, x, b, r.Z1, r.Y);
        }

        private static void WallSeg(GameObject parent, float x0, float x1, float z0, float z1, float y)
        {
            var w = GameObject.CreatePrimitive(PrimitiveType.Cube);
            w.name = "Wall";
            w.transform.SetParent(parent.transform);
            bool alongX = Mathf.Abs(x1 - x0) > Mathf.Abs(z1 - z0);
            w.transform.position = new Vector3((x0 + x1) / 2f, y + WallH / 2f, (z0 + z1) / 2f);
            w.transform.localScale = alongX
                ? new Vector3(x1 - x0, WallH, T)
                : new Vector3(T, WallH, z1 - z0);
        }

        private static void RailX(GameObject root, float z, float x0, float x1, List<(float center, float width)> gaps)
        {
            gaps.Sort((p, q) => p.center.CompareTo(q.center));
            float cur = x0;
            foreach (var (center, width) in gaps)
            {
                float a = center - width / 2f;
                if (a > cur) RailSeg(root, cur, a, z);
                cur = center + width / 2f;
            }
            if (cur < x1) RailSeg(root, cur, x1, z);
        }

        private static void RailSeg(GameObject root, float x0, float x1, float z)
        {
            var w = GameObject.CreatePrimitive(PrimitiveType.Cube);
            w.name = "Rail";
            w.transform.SetParent(root.transform);
            w.transform.position = new Vector3((x0 + x1) / 2f, 0.55f, z);
            w.transform.localScale = new Vector3(x1 - x0, 1.1f, 0.15f);
        }

        private static void Ramp(GameObject root, string name, Vector3 top0, Vector3 top1, float width)
        {
            var r = GameObject.CreatePrimitive(PrimitiveType.Cube);
            r.name = name;
            r.transform.SetParent(root.transform);
            Vector3 mid = (top0 + top1) / 2f;
            Vector3 dir = top1 - top0;
            float len = dir.magnitude;
            r.transform.position = mid - Vector3.up * (T / 2f);
            r.transform.rotation = Quaternion.LookRotation(new Vector3(dir.x, 0, dir.z).normalized) *
                                   Quaternion.Euler(-Mathf.Atan2(dir.y, new Vector2(dir.x, dir.z).magnitude) * Mathf.Rad2Deg, 0, 0);
            r.transform.localScale = new Vector3(width, T, len);
        }

        private static void Anchor(GameObject root, string zone, float x, float y, float z)
        {
            var a = new GameObject("Anchor_" + zone);
            a.transform.SetParent(root.transform);
            a.transform.position = new Vector3(x, y, z);
            a.AddComponent<ZoneAnchor>().ZoneName = zone;
        }

        private static void Sign(GameObject parent, string text, float x, float y, float z)
        {
            var go = new GameObject("Sign_" + text);
            go.transform.SetParent(parent.transform);
            go.transform.position = new Vector3(x, y, z);
            var tm = go.AddComponent<TextMesh>();
            tm.text = text.ToUpperInvariant();
            tm.characterSize = 0.12f;
            tm.fontSize = 60;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = new Color(1f, 1f, 0.9f, 0.9f);
            go.AddComponent<FaceCamera>();
        }

        private static void RoomLight(GameObject root, string name, float x, float y, float z, Color c)
        {
            var l = new GameObject(name).AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 13f;
            l.intensity = 2.1f;
            l.color = c;
            l.transform.SetParent(root.transform);
            l.transform.position = new Vector3(x, y, z);
        }
    }
}
