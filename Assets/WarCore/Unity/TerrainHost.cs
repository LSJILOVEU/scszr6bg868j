using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using WarCore.Presentation;

namespace WarCore.Unity
{
    // L8b: a whole terrain match (or the A3a search demo) built at run time in an empty scene, seen through a fixed camera
    // that frames the whole map, with a HUD and anchor screenshots. Before the first tick it checks the chain
    // scene objects -> view data -> Sim -> control points (SelfCheck) and refuses to run when any link disagrees.
    // Read-only: it never enqueues a command. The L8b menu sets the keys below and enters Play mode.
    public sealed class TerrainHost : MonoBehaviour
    {
        public const string DemoKey = "WarCore.L8b.Demo";
        public const string LayerKey = "WarCore.L8b.Layer";
        public const string ShotDirKey = "WarCore.L8b.ShotDir";
        public const string PrefixKey = "WarCore.L8b.Prefix";
        public const string TerrainDemoName = "terrain", A3aDemoName = "a3a";
        public const string DefaultLayer = "rwr-terrain-demo", DefaultPrefix = "L8b";
        public const string HoldLine = "HOLD 3 s (end frame)";
        private const long MatchLimit = 9000, A3aLastTick = 900;
        private const int HoldFrames = 90;
        private static readonly long[] A3aTicks = { 0, 36, 46, 146, 206, 336, 900 };

        private SimSession session;
        private EntityViews views;
        private PointViews points;
        private ShotLines shots;
        private GameObject mapRoot;
        private bool capturing, a3a;
        private string prefix = DefaultPrefix;
        private string hud = "";
        private GUIStyle style;
        private bool firstShotLogged;

        private void Awake()
        {
            string demo = TerrainDemoName, layer = DefaultLayer, dir = "";
#if UNITY_EDITOR
            demo = UnityEditor.SessionState.GetString(DemoKey, TerrainDemoName);
            layer = UnityEditor.SessionState.GetString(LayerKey, DefaultLayer);
            dir = UnityEditor.SessionState.GetString(ShotDirKey, "");
            prefix = UnityEditor.SessionState.GetString(PrefixKey, DefaultPrefix);
            UnityEditor.SessionState.EraseString(DemoKey);
            UnityEditor.SessionState.EraseString(LayerKey);
            UnityEditor.SessionState.EraseString(ShotDirKey);
            UnityEditor.SessionState.EraseString(PrefixKey);
#endif
            a3a = demo == A3aDemoName;
            session = a3a ? SimSession.Create(A3aDemo.Recipe(), A3aDemo.MapBytes())
                : SimSession.Create(ContentLayers.Terrain(layer), TerrainBake.DemoMapBytes());
            mapRoot = MapView.Build(session.Map, NewMaterial(new Color(0.62f, 0.66f, 0.48f)), NewMaterial(new Color(0.45f, 0.42f, 0.4f)));
            var palette = new[] { new Color(0.2f, 0.5f, 1f), new Color(1f, 0.85f, 0.2f) };
            views = new EntityViews(transform, new Vector3(0.5f, 1f, 0.5f), 0.5f, palette);
            points = new PointViews(transform, ViewGround.For(session.Map));
            shots = new ShotLines(transform, palette, 1.5f, ViewGround.For(session.Map));
            Camera camera = Camera.main;
            TerrainBake.Frame(camera, mapRoot.transform, camera.aspect);
            Show(session.Previous, session.Current, 0);

            string problems = SelfCheck(session.Map, session, session.Current, mapRoot.transform, transform);
            if (problems != "")
            {
                Debug.LogError("L8b self-check failed:\n" + problems);
                session = null;
                if (dir != "")
                {
                    File.WriteAllText(Path.Combine(dir, prefix + "-selfcheck-failed.txt"), problems);
#if UNITY_EDITOR
                    if (Environment.GetEnvironmentVariable("WARCORE_EXIT_AFTER_SHOTS") == "1")
                        UnityEditor.EditorApplication.Exit(1);
#endif
                }
                return;
            }
            Debug.Log("L8b self-check clean: positions " + PointCheck.Run(session.Current, session).Positions);
            if (dir != "")
            {
                capturing = true;
                StartCoroutine(Capture(dir));
            }
        }

        private static Material NewMaterial(Color color)
        {
            // Built-in render pipeline: the lit shader is Standard (as L7Terrain). Made at run time, never saved.
            var material = new Material(Shader.Find("Standard"));
            material.color = color;
            return material;
        }

        // Empty string = every link of the chain agrees; else one line per problem, each starting with its rule:
        //   root    the map root is not the identity transform (VerifyGround only looks at the Ground's local transform);
        //   ground  height field: VerifyGround; flat: the Ground slab bounds (top at FloorY, X and Z start, width, depth);
        //   wall    a ViewWall without its box, a box off its centre, size or rotation (local, 1e-3), or more "Wall " boxes than walls;
        //   terrain TerrainCheck (view data against Sim) is not clean;
        //   points  PointCheck (Sim and drawn points against the match) is not clean;
        //   disc    a drawn point without its disc, or a disc not exactly at (X, ground + 0.02, Z).
        // It never stops the editor; the caller decides.
        public static string SelfCheck(ViewMap drawn, SimSession session, FrameView frame, Transform mapRoot, Transform pointsParent)
        {
            if (drawn == null) throw new ArgumentNullException(nameof(drawn));
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            if (mapRoot == null) throw new ArgumentNullException(nameof(mapRoot));
            if (pointsParent == null) throw new ArgumentNullException(nameof(pointsParent));
            var lines = new StringBuilder();

            Matrix4x4 m = mapRoot.localToWorldMatrix, identity = Matrix4x4.identity;
            for (int i = 0; i < 16; i++)
                if (m[i] != identity[i])
                {
                    lines.Append("root localToWorldMatrix is not identity\n");
                    break;
                }

            if (drawn.HasHeightField)
            {
                string ground = MapView.VerifyGround(drawn, mapRoot);
                if (ground != "")
                    lines.Append("ground ").Append(OneLine(ground)).Append('\n');
            }
            else
            {
                Transform g = mapRoot.Find("Ground");
                Renderer r = g == null ? null : g.GetComponent<Renderer>();
                if (r == null)
                    lines.Append("ground slab missing\n");
                else
                {
                    Bounds b = r.bounds;
                    if (Off(b.max.y, drawn.FloorY) || Off(b.min.x, drawn.MinX) || Off(b.size.x, drawn.Width)
                        || Off(b.size.z, drawn.Depth) || Off(b.min.z, drawn.MinZ))
                        lines.Append("ground slab does not match the map extent or floor height\n");
                }
            }

            int boxes = 0;
            foreach (Transform child in mapRoot)
                if (child.name.StartsWith("Wall ", StringComparison.Ordinal)) boxes++;
            foreach (ViewWall w in drawn.Walls)
            {
                string name = $"Wall {w.Chunk}:{w.Placement}";
                Transform t = mapRoot.Find(name);
                if (t == null)
                    lines.Append("wall ").Append(name).Append(" missing\n");
                else if (Vector3.Distance(t.localPosition, new Vector3(w.CenterX, w.CenterY, w.CenterZ)) > 1e-3f
                    || Vector3.Distance(t.localScale, new Vector3(w.SizeX, w.SizeY, w.SizeZ)) > 1e-3f
                    || Quaternion.Angle(t.localRotation, Quaternion.identity) > 1e-3f)
                    lines.Append("wall ").Append(name).Append(" centre, size or rotation differs\n");
            }
            if (boxes > drawn.Walls.Length)
                lines.Append("wall ").Append(boxes - drawn.Walls.Length).Append(" boxes more than walls\n");

            TerrainCheckResult terrain = TerrainCheck.Run(drawn, session);
            if (!terrain.IsClean)
                lines.Append("terrain ").Append(OneLine(terrain.ToString())).Append('\n');

            PointCheckResult check = PointCheck.Run(frame, session);
            if (!check.IsClean)
                lines.Append("points ").Append(OneLine(check.ToString())).Append('\n');

            ViewGround field = ViewGround.For(drawn);
            foreach (ViewPoint p in frame.Points)
            {
                Transform disc = pointsParent.Find("Point " + p.Id);
                if (disc == null)
                {
                    lines.Append("disc ").Append(p.Id).Append(" missing\n");
                    continue;
                }
                float floor = field == null ? 0f : field.HeightAt(p.X, p.Z, 0f);
                Vector3 at = disc.position, expected = new Vector3(p.X, floor + 0.02f, p.Z);
                if (Bits(at.x) != Bits(expected.x) || Bits(at.y) != Bits(expected.y) || Bits(at.z) != Bits(expected.z))
                    lines.Append("disc ").Append(p.Id).Append(" not at its point\n");
            }
            return lines.ToString();
        }

        private static bool Off(float a, float b) => Mathf.Abs(a - b) > 1e-3f;
        private static int Bits(float f) => BitConverter.SingleToInt32Bits(f);
        private static string OneLine(string text) => text.Trim().Replace('\n', ';');

        // The HUD: HudLines first (tick, state hash, points), then the map hash, both teams on the field and in reserve, and the result.
        public static string Hud(FrameView frame, ulong stateHash, ViewMap map)
        {
            var text = new StringBuilder(HudLines.Build(frame, stateHash, null, null, null));
            ViewMatch m = frame.Match;
            text.Append("\nMap ").Append(map.FileHash.Substring(0, 16));
            text.Append(FormattableString.Invariant($"\nT1 on field {m.AliveOne} reserve {m.ReserveOne}   T2 on field {m.AliveTwo} reserve {m.ReserveTwo}"));
            if (m.IsFinished)
                text.Append(m.Winner < 0 ? "\nDRAW" : FormattableString.Invariant($"\nWINNER T{m.Winner} (end tick {m.EndTick}, reason {m.EndReason})"));
            return text.ToString();
        }

        // One picture of the world: units, points, shots and the HUD; the camera never moves.
        private void Show(FrameView previous, FrameView current, float alpha, bool hold = false)
        {
            points.Apply(current);
            views.Apply(previous, current, alpha);
            hud = Hud(current, session.StateHash, session.Map) + (hold ? "\n" + HoldLine : "");
        }

        private void Update()
        {
            if (session == null || capturing)
                return;
            session.Advance((long)(Time.unscaledDeltaTime * 1e6));
            shots.Add(session.Current);
            Show(session.Previous, session.Current, session.Alpha);
        }

        // Step tick by tick, show exactly that tick, write every anchor picture and one video frame per tick (flat, in dir).
        // terrain: ShotPlan anchors until the match ends (or MatchLimit), then 90 more frames of the end picture marked HOLD.
        // a3a: the fixed anchor ticks, up to tick 900 (the rule set never ends the match).
        private IEnumerator Capture(string dir)
        {
            for (int i = 0; i < 30; i++)
                yield return null; // let the editor finish compiling shaders before the first picture
            string csv = Path.Combine(dir, prefix + "-shots.csv");
            File.WriteAllText(csv, "file,tick,hash\n");
            var plan = new ShotPlan();
            var anchors = new List<ShotAnchor>();
            long last = a3a ? A3aLastTick : MatchLimit, frame = 0;
            for (long tick = 0; tick <= last; tick++)
            {
                if (tick > 0)
                    session.StepOnce();
                shots.Add(session.Current);
                var names = new List<string>();
                if (a3a)
                {
                    if (Array.IndexOf(A3aTicks, tick) >= 0)
                        names.Add(FormattableString.Invariant($"{prefix}-t{tick:0000}.png"));
                }
                else
                {
                    plan.Observe(session.Current, anchors);
                    foreach (ShotAnchor anchor in anchors)
                        names.Add(anchor.FileName(prefix));
                }
                foreach (string name in names)
                {
                    yield return Write(Path.Combine(dir, name), false);
                    File.AppendAllText(csv, FormattableString.Invariant($"{name},{session.Tick},{session.StateHash:X16}\n"));
                }
                yield return Write(Path.Combine(dir, FormattableString.Invariant($"{prefix}-frame-{frame++:0000}.png")), false);
                if (!a3a && session.IsFinished)
                    break;
            }
            if (!a3a)
                for (int i = 0; i < HoldFrames; i++)
                    yield return Write(Path.Combine(dir, FormattableString.Invariant($"{prefix}-frame-{frame++:0000}.png")), true);
            capturing = false;
            Debug.Log(FormattableString.Invariant($"L8b shots written to {dir}: {frame} frames; last tick {session.Tick}, hash {session.StateHash:X16}"));
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
            if (Environment.GetEnvironmentVariable("WARCORE_EXIT_AFTER_SHOTS") == "1")
                UnityEditor.EditorApplication.Exit(0);
#endif
        }

        private IEnumerator Write(string path, bool hold)
        {
            Show(session.Current, session.Current, 0, hold);
            yield return null;
            yield return new WaitForEndOfFrame();
            Texture2D image = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(path, image.EncodeToPNG());
            Destroy(image);
            if (!firstShotLogged)
            {
                firstShotLogged = true;
                Debug.Log("L8b first shot written: " + path);
            }
        }

        private void OnGUI()
        {
            if (session == null)
                return;
            style = style ?? new GUIStyle(GUI.skin.box) { fontSize = 22, alignment = TextAnchor.UpperLeft };
            GUI.Box(new Rect(12, 12, 760, 236), hud, style);
        }
    }
}
