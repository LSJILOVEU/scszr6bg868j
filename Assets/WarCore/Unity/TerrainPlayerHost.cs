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

        private void Awake()
        {
#if UNITY_EDITOR
            dir = UnityEditor.SessionState.GetString(ShotDirKey, "");
            prefix = UnityEditor.SessionState.GetString(PrefixKey, DefaultPrefix);
            UnityEditor.SessionState.EraseString(ShotDirKey);
            UnityEditor.SessionState.EraseString(PrefixKey);
#endif
            auto = Environment.GetEnvironmentVariable("WARCORE_EXIT_AFTER_SHOTS") == "1";
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
                StartCoroutine(Capture());
            }
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
                    yield return Write(Path.Combine(dir, name), false);
                    string row = FormattableString.Invariant($"{name},{session.Tick},{session.StateHash:X16}\n");
                    writeError = writeError ?? TryWrite(() => File.AppendAllText(csv, row));
                    if (writeError != null)
                    {
                        Fail(writeError);
                        yield break;
                    }
                }
                yield return Write(Path.Combine(dir, FormattableString.Invariant($"{prefix}-frame-{frame++:0000}.png")), false);
                if (writeError != null)
                {
                    Fail(writeError);
                    yield break;
                }
                if (session.IsFinished)
                    break;
            }
            long real = frame;
            while (frame < VideoFrames)
            {
                yield return Write(Path.Combine(dir, FormattableString.Invariant($"{prefix}-frame-hold-{frame++:0000}.png")), true);
                if (writeError != null)
                {
                    Fail(writeError);
                    yield break;
                }
            }
            capturing = false;
            Debug.Log(FormattableString.Invariant($"L10 shots written to {dir}: {real} frames + {frame - real} held; last tick {session.Tick}, hash {session.StateHash:X16}"));
            Quit(0);
        }

        private IEnumerator Write(string path, bool hold)
        {
            Show(session.Current, session.Current, 0, hold);
            yield return null;
            yield return new WaitForEndOfFrame();
            Texture2D image = ScreenCapture.CaptureScreenshotAsTexture();
            string error = TryWrite(() => File.WriteAllBytes(path, image.EncodeToPNG()));
            Destroy(image);
            if (error != null)
                writeError = path + ": " + error;
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
