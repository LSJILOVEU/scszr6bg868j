using System.Collections;
using System.IO;
using UnityEngine;
using WarCore.Presentation;

namespace WarCore.Unity
{
    // Runs the M2b demo: the map file drives Sim; the scene's baked objects must come from the same file.
    [DefaultExecutionOrder(-100)]
    public sealed class M2Host : MonoBehaviour
    {
        public const string MapPath = "maps/M2Demo.wcmap";
        public const string ShotDirKey = "WarCore.M2.ShotDir";
        public const string PathModeKey = "WarCore.M2.PathMode";
        public const string CrowdModeKey = "WarCore.M2.CrowdMode";
        public const string CombatModeKey = "WarCore.M2.CombatMode";
        public const string MatchModeKey = "WarCore.M2.MatchMode";
        public const string M7ModeKey = "WarCore.M2.M7Mode";
        public const string M7MapPath = "maps/M7Demo.wcmap";
        public const string MatchMapPath = "maps/M6Demo.wcmap";
        private const long MatchLimit = 9000;
        private static readonly long[] ShotTicks = { 0, 150, 300, 600 };
        private static readonly long[] MatchShotTicks = { 0, 150, 300, 600, 1500, 3000 };
        private SimSession session;
        private EntityViews views;
        private UnitTrails trails;
        private ShotLines shots;
        private PointViews points;
        private bool matchMode;
        private bool m7Mode;
        private bool combatMode;
        private long endTick = -1;
        private bool pathMode;
        private bool crowdMode;
        private bool capturing;
        private GUIStyle style;

        private void Awake()
        {
#if UNITY_EDITOR
            if (UnityEditor.SessionState.GetBool(MpHost.ModeKey, false))
            {
                enabled = false;
                gameObject.AddComponent<MpHost>();
                return;
            }
            if (UnityEditor.SessionState.GetBool(P0Host.ModeKey, false)) // P0 runs the same scene with its own host
            {
                gameObject.AddComponent<P0Host>();
                enabled = false;
                return;
            }
            m7Mode = UnityEditor.SessionState.GetBool(M7ModeKey, false);
            UnityEditor.SessionState.EraseBool(M7ModeKey);
            matchMode = UnityEditor.SessionState.GetBool(MatchModeKey, false);
            UnityEditor.SessionState.EraseBool(MatchModeKey);
#endif
            matchMode |= m7Mode;
            byte[] bytes = File.ReadAllBytes(Path.Combine(Application.streamingAssetsPath, m7Mode ? M7MapPath : matchMode ? MatchMapPath : MapPath));
#if UNITY_EDITOR
            pathMode = UnityEditor.SessionState.GetBool(PathModeKey, false);
            UnityEditor.SessionState.EraseBool(PathModeKey);
            crowdMode = UnityEditor.SessionState.GetBool(CrowdModeKey, false);
            UnityEditor.SessionState.EraseBool(CrowdModeKey);
#endif
#if UNITY_EDITOR
            combatMode = UnityEditor.SessionState.GetBool(CombatModeKey, false);
            UnityEditor.SessionState.EraseBool(CombatModeKey);
#endif
            combatMode |= matchMode;
            crowdMode |= combatMode;
            pathMode |= crowdMode;
            string layer = "rwr-m6";
#if UNITY_EDITOR
            layer = UnityEditor.EditorPrefs.GetString("WarCore.Content.Layer", "");
            if (string.IsNullOrEmpty(layer)) layer = "rwr-m6";
#endif
            WorldRecipe recipe = m7Mode ? ContentLayers.M7(layer) : matchMode ? ContentLayers.M6(layer) : combatMode ? DemoRecipes.M5() : crowdMode ? DemoRecipes.M4() : pathMode ? DemoRecipes.M3() : DemoRecipes.M2b();
            session = SimSession.Create(recipe, bytes);
            MapBake baked = FindAnyObjectByType<MapBake>();
            if (baked == null || baked.fileHash != session.Map.FileHash)
            {
                Debug.LogError("Baked scene does not match the map file: run the matching WarCore/M2, WarCore/M6 or WarCore/M7 Bake Scene.");
                session = null;
                return;
            }
            var palette = new[] { new Color(0.2f, 0.5f, 1f), new Color(1f, 0.85f, 0.2f) };
            views = new EntityViews(transform, new Vector3(0.5f, 1f, 0.5f), 0.5f, palette);
            views.Apply(session.Previous, session.Current, 0);
            if (pathMode && !combatMode) // trails would bury the units and shots in a fight
            {
                trails = new UnitTrails(transform, palette, 1.2f);
                trails.Add(session.Current);
            }
            if (combatMode)
                shots = new ShotLines(transform, palette, 1.5f);
            if (matchMode)
            {
                points = new PointViews(transform);
                points.Apply(session.Current);
            }
            ViewMap map = session.Map;
            Camera camera = Camera.main;
            camera.orthographic = true;
            camera.transform.SetPositionAndRotation(
                new Vector3(map.MinX + map.Width / 2f, 40, map.MinZ + map.Depth / 2f), Quaternion.Euler(90, 0, 0));
            camera.orthographicSize = Mathf.Max(map.Depth / 2f, map.Width / 2f / camera.aspect) * 1.08f;
#if UNITY_EDITOR
            string dir = UnityEditor.SessionState.GetString(ShotDirKey, "");
            UnityEditor.SessionState.EraseString(ShotDirKey);
            if (dir != "")
            {
                if (m7Mode) session.EnableStepTiming((int)MatchLimit);
                capturing = true;
                StartCoroutine(Capture(dir));
            }
#endif
        }

        private void Update()
        {
            if (session == null || capturing || (!matchMode && session.Tick >= 600))
                return;
            session.Advance((long)(Time.unscaledDeltaTime * 1e6));
            points?.Apply(session.Current);
            views.Apply(session.Previous, session.Current, session.Alpha);
            trails?.Add(session.Current);
            shots?.Add(session.Current);
        }

        private static int Alive(FrameView frame, int team)
        {
            int count = 0;
            foreach (ViewEntity entity in frame.Entities)
                if (entity.Team == team && entity.Alive)
                    count++;
            return count;
        }

        // Step tick by tick (the trails need every tick), show exactly that tick (no interpolation), render, write the PNG.
        // Path mode also writes a frame every 10 ticks into frames/ for the video.
        private IEnumerator Capture(string dir)
        {
            string prefix = m7Mode ? "M7" : matchMode ? "M6" : combatMode ? "M5" : crowdMode ? "M4" : pathMode ? "M3" : "M2b";
            string frames = Path.Combine(dir, "frames");
            Directory.CreateDirectory(dir);
            if (pathMode)
                Directory.CreateDirectory(frames);
            for (int i = 0; i < 30; i++)
                yield return null; // let the editor finish compiling shaders before the first picture
            // Combat runs to max(600, the tick one side has no one left), capped at 1800. A match runs to its own end (Winner), capped at 9000.
            long[] shotTicks = matchMode ? MatchShotTicks : ShotTicks;
            for (long tick = 0; matchMode ? tick <= MatchLimit : tick <= (endTick >= 0 ? System.Math.Max(600, endTick) : 600) && tick <= 1800; tick++)
            {
                if (tick > 0)
                    session.StepOnce();
                trails?.Add(session.Current);
                shots?.Add(session.Current);
                points?.Apply(session.Current);
                bool ended = false;
                if (matchMode)
                {
                    if (endTick < 0 && session.IsFinished)
                        endTick = session.EndTick;
                }
                else if (combatMode && endTick < 0 && (Alive(session.Current, 1) == 0 || Alive(session.Current, 2) == 0))
                    endTick = tick;
                ended = combatMode && tick == endTick;
                if (m7Mode && (tick == 600 || ended))
                    Debug.Log($"M7 tick={session.Tick} hash={session.StateHash:X16} winner={session.Winner} reason={session.EndReason}");
                bool shot = System.Array.IndexOf(shotTicks, tick) >= 0;
                // The fight is short and fast: frame every 2 ticks until tick 200 so the video can show it.
                // A match is long: every 10 ticks to 900, then every 30 (the video says its playback speed).
                bool frame = pathMode && (matchMode ? tick % (tick <= 900 ? 10 : 30) == 0 : tick % (combatMode && tick <= 200 ? 2 : 10) == 0);
                if (!shot && !frame && !ended)
                    continue;
                views.Apply(session.Current, session.Current, 0);
                yield return null;
                yield return new WaitForEndOfFrame();
                byte[] png;
                Texture2D image = ScreenCapture.CaptureScreenshotAsTexture();
                png = image.EncodeToPNG();
                Destroy(image);
                if (shot)
                    File.WriteAllBytes(Path.Combine(dir, $"{prefix}-t{tick:0000}.png"), png);
                if (ended)
                    File.WriteAllBytes(Path.Combine(dir, $"{prefix}-end-t{tick:0000}.png"), png);
                if (frame || ended)
                    File.WriteAllBytes(Path.Combine(frames, $"f{tick:0000}.png"), png);
                if (ended && matchMode)
                    break;
            }
            if (m7Mode)
            {
                var timing = session.StepTiming;
                string report = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "Unity Editor (Mono) Sim.Step; includes cold start, excludes view capture/rendering\ncount={0}\nmean_ms={1:F6}\np95_ms={2:F6}\nmax_ms={3:F6}\nend_tick={4}\nend_hash={5:X16}\n",
                    timing.Count, timing.MeanMs, timing.P95Ms, timing.MaxMs, session.EndTick, session.StateHash);
                File.WriteAllText(Path.Combine(dir, "sim-step-timing.txt"), report);
                Debug.Log(report);
                session.DisableStepTiming();
            }
            capturing = false;
            Debug.Log(prefix + " shots written to " + dir + (matchMode ? $"; match end tick {endTick}" : ""));
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
            if (System.Environment.GetEnvironmentVariable("WARCORE_EXIT_AFTER_SHOTS") == "1")
                UnityEditor.EditorApplication.Exit(0); // unattended runs: close the editor this process started
#endif
        }

        // Display only (float, from view positions): the verified spacing numbers come from the headless tests.
        private static float MinSpacing(FrameView frame)
        {
            float best = float.MaxValue;
            var entities = frame.Entities;
            for (int a = 0; a < entities.Length; a++)
                for (int b = a + 1; b < entities.Length; b++)
                {
                    float dx = entities[a].X - entities[b].X, dz = entities[a].Z - entities[b].Z;
                    best = Mathf.Min(best, Mathf.Sqrt(dx * dx + dz * dz));
                }
            return best;
        }

        private void OnGUI()
        {
            if (session == null)
                return;
            style = style ?? new GUIStyle(GUI.skin.box) { fontSize = 22, alignment = TextAnchor.UpperLeft };
            if (matchMode)
            {
                ViewMatch m = session.Current.Match;
                string owners = "";
                foreach (ViewPoint p in session.Current.Points)
                    owners += $"  P{p.Id} {(p.Owner == 0 ? "-" : "T" + p.Owner)}";
                string result = !m.IsFinished ? "" : m.Winner < 0 ? "\nDRAW" : $"\nWINNER T{m.Winner} (end tick {m.EndTick}, reason {m.EndReason})";
                GUI.Box(new Rect(12, 12, 620, 172),
                    $"Tick {session.Tick}\nStateHash {session.StateHash:X16}\nMap {session.Map.FileHash.Substring(0, 16)}\nT1 on field {m.AliveOne} reserve {m.ReserveOne}   T2 on field {m.AliveTwo} reserve {m.ReserveTwo}\nPoints{owners}{result}", style);
                return;
            }
            string spacing = combatMode ? $"\nAlive  T1 {Alive(session.Current, 1)}  T2 {Alive(session.Current, 2)}"
                : crowdMode ? $"\nMin spacing {MinSpacing(session.Current):F3} m" : "";
            GUI.Box(new Rect(12, 12, 520, crowdMode ? 136 : 100),
                $"Tick {session.Tick} / 600\nStateHash {session.StateHash:X16}\nMap {session.Map.FileHash.Substring(0, 16)}{spacing}", style);
        }
    }
}
