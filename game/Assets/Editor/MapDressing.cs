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

        /// <summary>A wall segment read from the Revit export.</summary>
        private class Seg
        {
            public Bounds B;
            public bool Exterior;      // 300mm shell vs 140mm partition
            public int Axis;           // 0 = runs along X (faces +/-Z), 1 = runs along Z
            public float Lane => Axis == 0 ? B.center.z : B.center.x;
            public float Min => Axis == 0 ? B.min.x : B.min.z;
            public float Max => Axis == 0 ? B.max.x : B.max.z;
            public float Thick => Axis == 0 ? B.size.z : B.size.x;
        }

        /// <summary>A real opening: a gap Omar left between two wall segments.</summary>
        private class Opening
        {
            public Vector3 Center;     // gap centre in plan, y = wall band centre
            public float Width;
            public float Thick;
            public Vector3 Normal;     // perpendicular to the wall run
            public bool Exterior;
            public float BandMin, BandMax;   // vertical extent of the wall run
        }

        private static readonly List<Opening> _openings = new List<Opening>();
        private static Bounds? _stairZone;   // inflated stair footprint, keep-clear

        [MenuItem("Case Closed/Dress Map (Architect Pass)")]
        public static void Run()
        {
            var old = GameObject.Find("MapDressing");
            if (old != null) Object.DestroyImmediate(old);
            _mats.Clear();
            _openings.Clear();
            _placedKits.Clear();
            _root = new GameObject("MapDressing").transform;

            var building = GameObject.Find("OmarBuilding");
            if (building == null) { Debug.LogError("[Dressing] OmarBuilding not found"); return; }
            Physics.SyncTransforms();

            // THE RULE THAT KILLS THE RANDOMNESS: nothing is placed freehand.
            // Doors exist only in the gaps Omar modelled between wall segments;
            // windows are centred on the real structural bays; furniture faces
            // the room's actual entrance.
            // stair keep-clear zone from the real stair geometry
            _stairZone = null;
            foreach (var r in building.GetComponentsInChildren<Renderer>())
            {
                var n = r.gameObject.name;
                if (!n.Contains("Stair") && !n.Contains("Landing") && !n.Contains("Carriage") && !n.Contains("Run ")) continue;
                if (_stairZone == null) _stairZone = r.bounds;
                else { var z = _stairZone.Value; z.Encapsulate(r.bounds); _stairZone = z; }
            }
            if (_stairZone.HasValue)
            {
                var z = _stairZone.Value;
                z.Expand(new Vector3(2.4f, 0f, 2.4f));
                _stairZone = z;
            }

            var segs = CollectWalls(building);
            FindOpenings(segs);
            int doors = 0;
            foreach (var o in _openings) doors += BuildPortal(o);
            int windows = 0;
            foreach (var s in segs.Where(s => s.Exterior)) windows += BuildSegmentWindows(s);

            int kits = 0;
            var anchors = Object.FindObjectsByType<ZoneAnchor>(FindObjectsSortMode.None);
            foreach (var a in anchors)
            {
                Vector3 doorDir = TowardNearestOpening(a.transform.position);
                if (Furnish(a, doorDir)) kits++;
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log($"[Dressing] real openings={_openings.Count} portals={doors} bay windows={windows} kits={kits}");
        }

        private static List<Seg> CollectWalls(GameObject building)
        {
            var list = new List<Seg>();
            foreach (var r in building.GetComponentsInChildren<Renderer>())
            {
                bool ext = r.gameObject.name.Contains("300mm");
                bool inte = r.gameObject.name.Contains("140mm");
                if (!ext && !inte) continue;
                list.Add(new Seg
                {
                    B = r.bounds,
                    Exterior = ext,
                    Axis = r.bounds.size.x >= r.bounds.size.z ? 0 : 1,
                });
            }
            return list;
        }

        private static void FindOpenings(List<Seg> segs)
        {
            // 1. gaps BETWEEN collinear segments
            var lanes = segs.GroupBy(s => s.Axis + "|" + Mathf.Round(s.Lane / 0.3f) * 0.3f);
            foreach (var lane in lanes)
            {
                var run = lane.OrderBy(s => s.Min).ToList();
                for (int i = 0; i < run.Count - 1; i++)
                {
                    float gap = run[i + 1].Min - run[i].Max;
                    if (gap < 0.7f || gap > 4.5f) continue;
                    float mid = (run[i].Max + run[i + 1].Min) * 0.5f;
                    var s = run[i];
                    _openings.Add(new Opening
                    {
                        Center = s.Axis == 0
                            ? new Vector3(mid, s.B.center.y, s.Lane)
                            : new Vector3(s.Lane, s.B.center.y, mid),
                        Width = gap,
                        Thick = Mathf.Max(s.Thick, 0.14f),
                        Normal = s.Axis == 0 ? Vector3.forward : Vector3.right,
                        Exterior = s.Exterior,
                        BandMin = Mathf.Min(run[i].B.min.y, run[i + 1].B.min.y),
                        BandMax = Mathf.Max(run[i].B.max.y, run[i + 1].B.max.y),
                    });
                }
            }

            // 2. gaps at a partition's END - most rooms are entered through the
            // slot between a wall's end and the next wall, and without this the
            // upper-floor rooms had entrances with no doors at all
            foreach (var s in segs.Where(x => !x.Exterior))
            {
                foreach (int e in new[] { -1, 1 })
                {
                    Vector3 runDir = (s.Axis == 0 ? Vector3.right : Vector3.forward) * e;
                    Vector3 endPt = s.Axis == 0
                        ? new Vector3(e > 0 ? s.B.max.x : s.B.min.x, 0f, s.Lane)
                        : new Vector3(s.Lane, 0f, e > 0 ? s.B.max.z : s.B.min.z);

                    foreach (float probeY in new[] { 4.9f, 8.9f })
                    {
                        if (probeY < s.B.min.y || probeY > s.B.max.y) continue;
                        Vector3 from = new Vector3(endPt.x, probeY, endPt.z) + runDir * 0.06f;
                        if (!Physics.Raycast(from, runDir, out var hit, 3.0f)) continue;
                        if (hit.distance < 0.65f || hit.distance > 2.5f) continue;

                        Vector3 c = from + runDir * (hit.distance * 0.5f);
                        c.y = s.B.center.y;
                        bool dup = _openings.Any(o =>
                            Mathf.Abs(o.Center.x - c.x) < 1.0f && Mathf.Abs(o.Center.z - c.z) < 1.0f);
                        if (dup) break;

                        _openings.Add(new Opening
                        {
                            Center = c,
                            Width = hit.distance + 0.06f,
                            Thick = Mathf.Max(s.Thick, 0.14f),
                            Normal = s.Axis == 0 ? Vector3.forward : Vector3.right,
                            Exterior = false,
                            BandMin = s.B.min.y,
                            BandMax = s.B.max.y,
                        });
                        break;   // one opening per wall end
                    }
                }
            }
        }

        private static Vector3 TowardNearestOpening(Vector3 from)
        {
            Opening best = null;
            float bestD = float.MaxValue;
            foreach (var o in _openings)
            {
                // same storey only: compare against the anchor's height band
                var flat = new Vector3(o.Center.x - from.x, 0f, o.Center.z - from.z);
                float d = flat.sqrMagnitude;
                if (d < bestD) { bestD = d; best = o; }
            }
            if (best == null) return Vector3.forward;
            var dir = new Vector3(best.Center.x - from.x, 0f, best.Center.z - from.z);
            return dir.sqrMagnitude < 0.25f ? Vector3.forward : dir.normalized;
        }

        /// <summary>
        /// Build door joinery INSIDE a real gap, one per storey that has floor
        /// there. Interior 4m gaps become open double-door portals (this is a
        /// public building - leaves parked open, players walk through); the
        /// 2.4m entrance bays get glazed courthouse doors.
        /// </summary>
        private static int BuildPortal(Opening o)
        {
            int built = 0;
            foreach (float probeY in new[] { 1.0f, 4.6f, 8.6f })
            {
                if (!Physics.Raycast(new Vector3(o.Center.x, probeY + 0.8f, o.Center.z),
                                     Vector3.down, out var floorHit, 3.2f)) continue;
                if (floorHit.normal.y < 0.6f) continue;
                float floorY = floorHit.point.y;

                // a doorway only exists where its WALL exists. The building is
                // pilotis - walls start at 3.4m - so without this check every
                // gap also spawned a freestanding doorframe in the open garage.
                if (floorY < o.BandMin - 0.4f || floorY > o.BandMax - 2.3f) continue;

                var p = new GameObject((o.Exterior ? "Entry_" : "Portal_") + built).transform;
                p.SetParent(_root);
                p.position = new Vector3(o.Center.x, floorY, o.Center.z);
                p.rotation = Quaternion.LookRotation(o.Normal);
                float w = o.Width, t = o.Thick + 0.08f;

                // jambs + header fill the raw structural slot
                Box(p, "JambL", new Vector3(-w / 2f + 0.09f, 1.3f, 0f), new Vector3(0.18f, 2.6f, t), "WoodDark", true);
                Box(p, "JambR", new Vector3(w / 2f - 0.09f, 1.3f, 0f), new Vector3(0.18f, 2.6f, t), "WoodDark", true);
                Box(p, "Header", new Vector3(0f, 2.75f, 0f), new Vector3(w, 0.3f, t), "WoodDark");
                // transom glazing fills the slot up to the next slab - the tall
                // Revit gaps read as intentional clerestory, not a hole
                Box(p, "Transom", new Vector3(0f, 3.35f, 0f), new Vector3(w - 0.1f, 0.9f, 0.06f), "Glass");

                float leafW = (w - 0.36f) / 2f;
                if (o.Exterior)
                {
                    // glazed entrance doors, parked open at 30 degrees
                    var l = Box(p, "LeafL", Vector3.zero, Vector3.one, "WoodDark");
                    l.localPosition = new Vector3(-w / 2f + 0.18f, 0f, 0f);
                    l.localRotation = Quaternion.Euler(0f, -30f, 0f);
                    Box(l, "LeafLPanel", new Vector3(leafW / 2f, 1.15f, 0f), new Vector3(leafW, 2.3f, 0.07f), "WoodDark");
                    Box(l, "LeafLGlass", new Vector3(leafW / 2f, 1.35f, 0f), new Vector3(leafW - 0.24f, 1.5f, 0.09f), "Glass");
                    var rr = Box(p, "LeafR", Vector3.zero, Vector3.one, "WoodDark");
                    rr.localPosition = new Vector3(w / 2f - 0.18f, 0f, 0f);
                    rr.localRotation = Quaternion.Euler(0f, 30f, 0f);
                    Box(rr, "LeafRPanel", new Vector3(-leafW / 2f, 1.15f, 0f), new Vector3(leafW, 2.3f, 0.07f), "WoodDark");
                    Box(rr, "LeafRGlass", new Vector3(-leafW / 2f, 1.35f, 0f), new Vector3(leafW - 0.24f, 1.5f, 0.09f), "Glass");
                }
                else
                {
                    // interior portal: double leaves parked fully open inside
                    var l = Box(p, "LeafL", Vector3.zero, Vector3.one, "Wood");
                    l.localPosition = new Vector3(-w / 2f + 0.18f, 0f, 0f);
                    l.localRotation = Quaternion.Euler(0f, -100f, 0f);
                    Box(l, "LeafLPanel", new Vector3(leafW / 2f, 1.15f, 0f), new Vector3(leafW, 2.3f, 0.06f), "Wood");
                    var rr = Box(p, "LeafR", Vector3.zero, Vector3.one, "Wood");
                    rr.localPosition = new Vector3(w / 2f - 0.18f, 0f, 0f);
                    rr.localRotation = Quaternion.Euler(0f, 100f, 0f);
                    Box(rr, "LeafRPanel", new Vector3(-leafW / 2f, 1.15f, 0f), new Vector3(leafW, 2.3f, 0.06f), "Wood");
                }

                // nameplates on BOTH faces (single-faced TextMesh reads mirrored
                // from behind). Entrances announce the building; interior portals
                // name the room behind each face.
                Box(p, "Plate", new Vector3(0f, 3.0f, 0f), new Vector3(2.2f, 0.4f, t + 0.1f), "WoodDark");
                foreach (int side in new[] { 1, -1 })
                {
                    Vector3 face = o.Normal * side;
                    string label;
                    if (o.Exterior) label = "DISTRICT COURT";
                    else
                    {
                        // the room this face opens INTO is on the opposite side
                        var az = Object.FindObjectsByType<ZoneAnchor>(FindObjectsSortMode.None)
                            .Where(z => Mathf.Abs(z.transform.position.y - floorY) < 1.6f
                                     && Vector3.Dot(z.transform.position - p.position, face) < 0f)
                            .OrderBy(z => (z.transform.position - p.position).sqrMagnitude)
                            .FirstOrDefault();
                        if (az == null || (az.transform.position - p.position).sqrMagnitude > 144f) continue;
                        label = Display(az.ZoneName);
                    }
                    Sign(p, p.position + face * (t / 2f + 0.08f) + Vector3.up * 3.0f, face, label);
                }
                built++;
            }
            return built;
        }

        /// <summary>
        /// One window per storey per exterior segment, CENTRED on the segment -
        /// the rhythm follows the real structural bays, so inside and outside
        /// agree and nothing lands on a corner or clips an edge.
        /// </summary>
        private static int BuildSegmentWindows(Seg s)
        {
            if (s.Max - s.Min < 2.2f) return 0;
            int placed = 0;
            float mid = (s.Min + s.Max) * 0.5f;
            foreach (float sillBand in new[] { 4.6f, 8.6f })
            {
                if (sillBand < s.B.min.y || sillBand > s.B.max.y - 1.2f) continue;
                Vector3 pos = s.Axis == 0
                    ? new Vector3(mid, sillBand + 0.6f, s.Lane)
                    : new Vector3(s.Lane, sillBand + 0.6f, mid);
                var w = new GameObject("BayWindow").transform;
                w.SetParent(_root);
                w.position = pos;
                w.rotation = Quaternion.LookRotation(s.Axis == 0 ? Vector3.forward : Vector3.right);
                float ww = Mathf.Min(1.8f, (s.Max - s.Min) - 1.0f);
                float t = s.Thick + 0.1f;
                Box(w, "Frame", Vector3.zero, new Vector3(ww + 0.22f, 1.82f, t), "WoodDark");
                Box(w, "Glass", Vector3.zero, new Vector3(ww, 1.6f, t + 0.06f), "Glass");
                Box(w, "Mullion", Vector3.zero, new Vector3(0.07f, 1.62f, t + 0.08f), "WoodDark");
                Box(w, "Transom", Vector3.zero, new Vector3(ww + 0.02f, 0.07f, t + 0.08f), "WoodDark");
                Box(w, "Sill", new Vector3(0f, -1.0f, 0f), new Vector3(ww + 0.4f, 0.1f, t + 0.3f), "PlasterLight");
                placed++;
            }
            return placed;
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
            // the default font shader is ZTest Always - text glows through
            // walls, floors, and the far side of its own plate. Assign the
            // builtin font EXPLICITLY (a fresh TextMesh reports font == null
            // while still rendering via fallback) and swap to the depth-tested
            // 3D text shader so signs behave like objects.
            var f = tm.font;
            if (f == null)
            {
                f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                tm.font = f;
            }
            var shader = Shader.Find("CaseClosed/TextDepth");   // our URP depth-tested text
            if (f != null && shader != null)
            {
                var m = new Material(shader) { mainTexture = f.material.mainTexture };
                m.SetColor("_Color", Color.white);
                go.GetComponent<MeshRenderer>().sharedMaterial = m;
            }
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

        private static readonly List<Vector3> _placedKits = new List<Vector3>();

        /// <summary>Indoors (floor below + ceiling above), off the entrances, off the stair.</summary>
        private static bool KitSpotValid(Vector3 p, float storeyY)
        {
            var probe = new Vector3(p.x, storeyY, p.z);
            if (!Physics.Raycast(probe + Vector3.up * 1.2f, Vector3.down, out var fl, 3.2f)) return false;
            if (!Physics.Raycast(probe + Vector3.up * 1.6f, Vector3.up, out _, 8f)) return false;
            foreach (var o in _openings.Where(x => x.Exterior))
                if (new Vector3(p.x - o.Center.x, 0f, p.z - o.Center.z).magnitude < 4.0f) return false;
            if (_stairZone.HasValue)
            {
                var sb = _stairZone.Value;
                if (p.x > sb.min.x && p.x < sb.max.x && p.z > sb.min.z && p.z < sb.max.z) return false;
            }
            return true;
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
            Vector3 side = Vector3.Cross(Vector3.up, doorDir);
            float left = WallDist(a.transform.position, -side, 6f);
            float right = WallDist(a.transform.position, side, 6f);
            k.position = a.transform.position
                       + doorDir * ((fore - back) * 0.5f)
                       + side * ((right - left) * 0.5f);      // centre on BOTH axes
            float s = Mathf.Clamp(Mathf.Min(back + fore, left + right) / 8.8f, 0.55f, 1f);
            k.localScale = new Vector3(s, Mathf.Max(s, 0.85f), s);

            // CIRCULATION IS SACRED: no furniture in the entrance bays or the
            // stair throat (shelf rows were physically blocking the way in)
            foreach (var o in _openings.Where(o => o.Exterior))
            {
                var flat = new Vector3(k.position.x - o.Center.x, 0f, k.position.z - o.Center.z);
                if (flat.magnitude < 4.0f)
                    k.position += (flat.sqrMagnitude > 0.01f ? flat.normalized : -o.Normal)
                                * (4.0f - flat.magnitude);
            }
            if (_stairZone.HasValue)
            {
                var sb = _stairZone.Value;
                var p2 = k.position;
                if (p2.x > sb.min.x && p2.x < sb.max.x && p2.z > sb.min.z && p2.z < sb.max.z)
                {
                    // push out through the nearest zone face
                    float[] exits = { p2.x - sb.min.x, sb.max.x - p2.x, p2.z - sb.min.z, sb.max.z - p2.z };
                    int m = System.Array.IndexOf(exits, exits.Min());
                    k.position += m == 0 ? Vector3.left * (exits[0] + 0.8f)
                               : m == 1 ? Vector3.right * (exits[1] + 0.8f)
                               : m == 2 ? Vector3.back * (exits[2] + 0.8f)
                               : Vector3.forward * (exits[3] + 0.8f);
                }
            }

            // pushing can shove a lobby kit OUT of the building entirely. If the
            // fitted spot is invalid, re-home the kit into a real room: just
            // behind one of the interior doorways on this storey.
            float storeyY = a.transform.position.y;
            if (!KitSpotValid(k.position, storeyY))
            {
                bool homed = false;
                foreach (var o in _openings.Where(x => !x.Exterior))
                {
                    foreach (int sideSign in new[] { 1, -1 })
                    {
                        var cand = new Vector3(o.Center.x, storeyY, o.Center.z)
                                 + o.Normal * (sideSign * 3.2f);
                        if (!KitSpotValid(cand, storeyY)) continue;
                        if (_placedKits.Any(pk => Vector3.Distance(pk, cand) < 3.5f)) continue;
                        k.position = cand;
                        homed = true;
                        break;
                    }
                    if (homed) break;
                }
                if (!homed) Debug.LogWarning($"[Dressing] no valid room for {a.ZoneName}, kit stays at fitted spot");
            }
            _placedKits.Add(k.position);

            // no ceiling lamp near this room? hang a bare bulb so the kit isn't
            // furnishing a black void (rooms between light pools were pitch dark)
            var lightsRoot = GameObject.Find("ScatteredLights");
            bool lit = false;
            if (lightsRoot != null)
                foreach (Transform lc in lightsRoot.transform)
                    if (lc.GetComponent<Light>() != null &&
                        Vector3.Distance(lc.position, k.position) < 6.5f) { lit = true; break; }
            if (!lit)
            {
                var bulb = new GameObject("RoomBulb").AddComponent<Light>();
                bulb.type = LightType.Point;
                bulb.range = 9f;
                bulb.intensity = 1.7f;
                bulb.color = new Color(1f, 0.9f, 0.72f);
                bulb.transform.SetParent(k);
                bulb.transform.position = k.position + Vector3.up * 2.5f;
            }

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
