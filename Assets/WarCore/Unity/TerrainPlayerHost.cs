using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using WarCore.Presentation;

namespace WarCore.Unity
{
    // L10: the demo terrain match with a scripted player (TerrainPlayerDemo), built at run time in an empty scene. The camera
    // follows the controlled unit. The script is queued once in Awake; keyboard and mouse are never read. Before the first
    // tick it runs TerrainHost.SelfCheck and refuses to run when any link disagrees. The L10 menu sets the keys below.
    public sealed class TerrainPlayerHost : MonoBehaviour
    {
        public const string ShotDirKey = "WarCore.L10.ShotDir";
        public const string PrefixKey = "WarCore.L10.Prefix";
        public const string DefaultPrefix = "L10";
        public const string HoldLine = "HOLD (end frame)";
        private const long MatchLimit = 9000;
        private const int VideoFrames = 900; // 30 s at 30 fps: the end picture is held until then
        private const int ShotWidth = 1920, ShotHeight = 1080, LabelScale = 4;
        public const double StallSeconds = 60; // no new picture for this long while capturing: log, report and exit 1

        private SimSession session;
        private EntityViews views;
        private PointViews points;
        private ShotLines shots;
        private IsoCamera isoCamera;
        private GameObject mapRoot;
        private bool capturing, auto;
        private string prefix = DefaultPrefix, dir = "";
        private string hud = "";
        private GUIStyle style;
        private string writeError;
        private RenderTexture target;
        private Texture2D shot;
        private readonly System.Diagnostics.Stopwatch clock = new System.Diagnostics.Stopwatch();
        private double lastPicture;
        private long pictures;

        private void Awake()
        {
#if UNITY_EDITOR
            dir = UnityEditor.SessionState.GetString(ShotDirKey, "");
            prefix = UnityEditor.SessionState.GetString(PrefixKey, DefaultPrefix);
            UnityEditor.SessionState.EraseString(ShotDirKey);
            UnityEditor.SessionState.EraseString(PrefixKey);
#endif
            auto = Environment.GetEnvironmentVariable("WARCORE_EXIT_AFTER_SHOTS") == "1";
            // Keep stepping when the editor loses focus; pictures are rendered off screen, never read back from the Game view.
            Application.runInBackground = true;
            session = SimSession.Create(TerrainPlayerDemo.Recipe(), TerrainBake.DemoMapBytes());
            foreach (var item in TerrainPlayerDemo.Script())
                session.SetPlayerInput(item.AtTick, item.Input);
            mapRoot = MapView.Build(session.Map, NewMaterial(new Color(0.62f, 0.66f, 0.48f)), NewMaterial(new Color(0.45f, 0.42f, 0.4f)));
            var palette = new[] { new Color(0.2f, 0.5f, 1f), new Color(1f, 0.85f, 0.2f) };
            views = new EntityViews(transform, new Vector3(0.5f, 1f, 0.5f), 0.5f, palette);
            points = new PointViews(transform, ViewGround.For(session.Map));
            shots = new ShotLines(transform, palette, 1.5f, ViewGround.For(session.Map));
            isoCamera = new IsoCamera(20f, session.Map.FloorY);
            Show(session.Previous, session.Current, 0);

            string problems = TerrainHost.SelfCheck(session.Map, session, session.Current, mapRoot.transform, transform);
            if (problems != "")
            {
                Debug.LogError("L10 self-check failed:\n" + problems);
                session = null;
                if (dir != "")
                {
                    string error = IsShotPrefix(prefix) ? TryWrite(() => File.WriteAllText(Path.Combine(dir, prefix + "-selfcheck-failed.txt"), problems))
                        : "bad shot prefix";
                    if (error != null)
                        Debug.LogError("L10 self-check report not written: " + error);
                    Quit(1);
                }
                return;
            }
            Debug.Log("L10 self-check clean");
            if (dir != "")
            {
                capturing = true;
                clock.Start();
#if UNITY_EDITOR
                UnityEditor.EditorApplication.update += Watchdog;
#endif
                StartCoroutine(Capture());
            }
        }

        // Runs on the editor loop, outside the player loop: if no picture was written for StallSeconds while capturing, the
        // capture is reported as stalled (log and <prefix>-stalled.txt) and the editor exits with 1 in automatic mode.
        private void Watchdog()
        {
            if (!capturing || clock.Elapsed.TotalSeconds - lastPicture < StallSeconds)
                return;
            string text = FormattableString.Invariant($"no picture for {StallSeconds} s after {pictures} pictures (tick {session?.Tick})");
            Debug.LogError("L10 capture stalled: " + text);
            if (IsShotPrefix(prefix))
                TryWrite(() => File.WriteAllText(Path.Combine(dir, prefix + "-stalled.txt"), text + "\n"));
            capturing = false;
            session = null;
            Quit(1);
        }

        private void OnDestroy()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update -= Watchdog;
#endif
            if (target != null) Destroy(target);
            if (shot != null) Destroy(shot);
        }

        // A shot prefix is a plain file name starting with L10: letters, digits, '-' or '_' only, so every output stays flat.
        public static bool IsShotPrefix(string prefix)
        {
            if (string.IsNullOrEmpty(prefix) || !prefix.StartsWith(DefaultPrefix, StringComparison.Ordinal))
                return false;
            foreach (char c in prefix)
                if (!(c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '-' || c == '_'))
                    return false;
            return true;
        }

        // TerrainHost's HUD, then the controlled key and its height in metres (three decimals); "-" when absent or dead.
        public static string Hud(FrameView frame, ulong stateHash, ViewMap map, ulong? player)
        {
            string text = TerrainHost.Hud(frame, stateHash, map);
            text += "\nPlayer " + (player.HasValue ? player.Value.ToString() : "-");
            if (player.HasValue && frame.TryGet(player.Value, out ViewEntity unit) && unit.Alive)
                text += FormattableString.Invariant($"\nPlayerY {unit.Y:0.000}");
            else
                text += "\nPlayerY -";
            return text;
        }

        private static string TryWrite(Action write)
        {
            try { write(); return null; }
            catch (Exception e) { return e.GetType().Name + ": " + e.Message; }
        }

        private static Material NewMaterial(Color color)
        {
            var material = new Material(Shader.Find("Standard"));
            material.color = color;
            return material;
        }

        private void Quit(int code)
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
            if (auto)
                UnityEditor.EditorApplication.Exit(code);
#endif
        }

        private void Fail(string what)
        {
            capturing = false;
            session = null;
            Debug.LogError("L10 capture failed: " + what);
            Quit(1);
        }

        // A step threw: the session is faulted for good. Report it, write <prefix>-step-failed.txt when capturing, exit 1.
        private void StepFailed(Exception e)
        {
            string text = e.GetType().Name + ": " + e.Message;
            Debug.LogError("L10 step failed: " + text);
            if (dir != "" && IsShotPrefix(prefix))
            {
                string error = TryWrite(() => File.WriteAllText(Path.Combine(dir, prefix + "-step-failed.txt"), text + "\n"));
                if (error != null)
                    Debug.LogError("L10 step report not written: " + error);
            }
            capturing = false;
            session = null;
            Quit(1);
        }

        private void Show(FrameView previous, FrameView current, float alpha, bool hold = false)
        {
            points.Apply(current);
            views.Apply(previous, current, alpha);
            FrameView shown = FrameView.Interpolate(previous, current, alpha);
            ulong? key = session.ControlledKey;
            if (key.HasValue && shown.TryGet(key.Value, out ViewEntity followed))
                isoCamera.Follow(followed.X, followed.Z);
            IsoCameraInput.Apply(isoCamera, Camera.main);
            hud = Hud(current, session.StateHash, session.Map, key) + (hold ? "\n" + HoldLine : "");
        }

        private void Update()
        {
            if (session == null || capturing)
                return;
            try { session.Advance((long)(Time.unscaledDeltaTime * 1e6)); }
            catch (Exception e) { StepFailed(e); return; }
            shots.Add(session.Current);
            Show(session.Previous, session.Current, session.Alpha);
        }

        // Step tick by tick: ShotPlan anchors (with the csv) and one video frame per tick until the match ends, then the end
        // picture marked HOLD until VideoFrames frames exist ("-hold-" in the file name).
        private IEnumerator Capture()
        {
            for (int i = 0; i < 30; i++)
                yield return null; // let the editor finish compiling shaders before the first picture
            lastPicture = clock.Elapsed.TotalSeconds;
            if (!IsShotPrefix(prefix))
            {
                Fail("shot prefix '" + prefix + "' is not a plain file name starting with " + DefaultPrefix);
                yield break;
            }
            string csv = Path.Combine(dir, prefix + "-shots.csv");
            string error = TryWrite(() => File.WriteAllText(csv, "name,tick,hash\n"));
            if (error != null)
            {
                Fail(csv + ": " + error);
                yield break;
            }
            var plan = new ShotPlan();
            var anchors = new List<ShotAnchor>();
            long frame = 0;
            for (long tick = 0; tick <= MatchLimit; tick++)
            {
                if (tick > 0)
                {
                    Exception failed = null;
                    try { session.StepOnce(); }
                    catch (Exception e) { failed = e; }
                    if (failed != null)
                    {
                        StepFailed(failed);
                        yield break;
                    }
                }
                shots.Add(session.Current);
                plan.Observe(session.Current, anchors);
                foreach (ShotAnchor anchor in anchors)
                {
                    string name = anchor.FileName(prefix);
                    writeError = writeError ?? WriteShot(Path.Combine(dir, name), false);
                    string row = FormattableString.Invariant($"{name},{session.Tick},{session.StateHash:X16}\n");
                    writeError = writeError ?? TryWrite(() => File.AppendAllText(csv, row));
                    if (writeError != null)
                    {
                        Fail(writeError);
                        yield break;
                    }
                }
                writeError = writeError ?? WriteShot(Path.Combine(dir, FormattableString.Invariant($"{prefix}-frame-{frame++:0000}.png")), false);
                if (writeError != null)
                {
                    Fail(writeError);
                    yield break;
                }
                if (session.IsFinished)
                    break;
                yield return null; // one picture per player-loop frame keeps the editor responsive
            }
            if (!session.IsFinished)
            {
                Fail(FormattableString.Invariant($"the match did not end by tick {MatchLimit}"));
                yield break;
            }
            long real = frame;
            while (frame < VideoFrames)
            {
                writeError = writeError ?? WriteShot(Path.Combine(dir, FormattableString.Invariant($"{prefix}-frame-hold-{frame++:0000}.png")), true);
                if (writeError != null)
                {
                    Fail(writeError);
                    yield break;
                }
                yield return null;
            }
            capturing = false;
            Debug.Log(FormattableString.Invariant($"L10 shots written to {dir}: {real} frames + {frame - real} held; last tick {session.Tick}, hash {session.StateHash:X16}"));
            Quit(0);
        }

        // One picture, synchronously: the main camera renders the shown tick into a 1920 x 1080 texture, the label (tick, state
        // hash, player, height, HOLD) is stamped in the top-left corner, and the PNG is written. Any failure (including the
        // render or read-back) comes back as text; null means written.
        private string WriteShot(string path, bool hold)
        {
            Show(session.Current, session.Current, 0, hold);
            string error = TryWrite(() =>
            {
                Camera camera = Camera.main;
                if (target == null) target = new RenderTexture(ShotWidth, ShotHeight, 24);
                if (shot == null) shot = new Texture2D(ShotWidth, ShotHeight, TextureFormat.RGB24, false);
                RenderTexture active = RenderTexture.active;
                try
                {
                    camera.targetTexture = target;
                    camera.aspect = (float)ShotWidth / ShotHeight;
                    camera.Render();
                    RenderTexture.active = target;
                    shot.ReadPixels(new Rect(0, 0, ShotWidth, ShotHeight), 0, 0);
                }
                finally
                {
                    RenderTexture.active = active;
                    camera.targetTexture = null;
                    camera.ResetAspect();
                }
                Stamp(shot, ShotLabel(session.Current, session.StateHash, session.ControlledKey, hold));
                shot.Apply();
                File.WriteAllBytes(path, shot.EncodeToPNG());
            });
            if (error != null)
                return path + ": " + error;
            pictures++;
            lastPicture = clock.Elapsed.TotalSeconds;
            return null;
        }

        // The stamped label: upper case so every character has a glyph below.
        public static string[] ShotLabel(FrameView frame, ulong stateHash, ulong? player, bool hold)
        {
            string height = "PLAYERY -";
            if (player.HasValue && frame.TryGet(player.Value, out ViewEntity unit) && unit.Alive)
                height = FormattableString.Invariant($"PLAYERY {unit.Y:0.000}");
            var lines = new List<string>
            {
                FormattableString.Invariant($"TICK {frame.Tick}"),
                FormattableString.Invariant($"STATEHASH {stateHash:X16}"),
                "PLAYER " + (player.HasValue ? player.Value.ToString() : "-"),
                height,
            };
            if (hold)
                lines.Add("HOLD");
            return lines.ToArray();
        }

        public static bool HasGlyph(char c) => Array.FindIndex(Glyphs, g => g[0] == c) >= 0;

        // 3 x 5 pixel glyphs: the character, then 15 bits, rows top to bottom.
        private static readonly string[] Glyphs =
        {
            " 000000000000000", ".000000000000010", "-000000111000000",
            "0111101101101111", "1010110010010111", "2111001111100111", "3111001111001111", "4101101111001001",
            "5111100111001111", "6111100111101111", "7111001001001001", "8111101111101111", "9111101111001111",
            "A010101111101101", "B110101110101110", "C111100100100111", "D110101101101110", "E111100111100111",
            "F111100111100100", "H101101111101101", "I111010010010111", "K101101110101101", "L100100100100111",
            "O111101101101111", "P110101110100100", "R110101110101101", "S111100111001111", "T111010010010010",
            "Y101101010010010",
        };

        // White text on a black box in the top-left corner (ReadPixels rows start at the bottom).
        private static void Stamp(Texture2D image, string[] lines)
        {
            int cell = 4 * LabelScale, row = 7 * LabelScale, top = image.height - 1, width = 0;
            foreach (string line in lines)
                width = Mathf.Max(width, line.Length * cell + 2 * LabelScale);
            for (int y = 0; y < lines.Length * row + LabelScale; y++)
                for (int x = 0; x < width; x++) image.SetPixel(x, top - y, Color.black);
            for (int l = 0; l < lines.Length; l++)
                for (int c = 0; c < lines[l].Length; c++)
                {
                    string glyph = Array.Find(Glyphs, g => g[0] == lines[l][c]) ?? Glyphs[0];
                    for (int bit = 0; bit < 15; bit++)
                    {
                        if (glyph[1 + bit] != '1') continue;
                        int x0 = LabelScale + c * cell + (bit % 3) * LabelScale, y0 = LabelScale + l * row + (bit / 3) * LabelScale;
                        for (int sy = 0; sy < LabelScale; sy++)
                            for (int sx = 0; sx < LabelScale; sx++) image.SetPixel(x0 + sx, top - y0 - sy, Color.white);
                    }
                }
        }

        private void OnGUI()
        {
            if (session == null)
                return;
            style = style ?? new GUIStyle(GUI.skin.box) { fontSize = 22, alignment = TextAnchor.UpperLeft };
            GUI.Box(new Rect(12, 12, 760, 290), hud, style);
        }
    }
}
