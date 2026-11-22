using System;
using System.Collections;
using System.IO;
using UnityEngine;
using WarCore.Presentation;

namespace WarCore.Unity
{
    // SCALE-2a: the scale lineup (Scale2aDemo.Stage), drawn by ShowcaseScene like a showcase stage but outside the showcase's
    // stage list: three soldiers standing, crouched and prone side by side next to a 2.0 m doorway and a 0.9 m window sill.
    // No player and no HUD; the camera looks at the three. It plays on by itself. With a shot directory (the capture menu item)
    // it steps to the last of ShowcaseHost.ShotTicks, writes a picture at each of them with the same stamped label, and a csv
    // row per picture and soldier: tick, state hash, the soldier's stance level and drawn top.
    public sealed class Scale2aLineupHost : MonoBehaviour
    {
        public const string ShotDirKey = "WarCore.Scale2a.ShotDir", Prefix = "SCALE2A";
        private const int ShotWidth = 1920, ShotHeight = 1080;

        private ShowcaseScene scene;
        private string dir = "", problem = "";
        private bool capturing, auto;
        private RenderTexture target;
        private Texture2D shot;

        public ShowcaseScene Scene => scene;
        public string Problem => problem;

        // A new lineup scene at tick 0 with the camera on the three soldiers.
        public static ShowcaseScene Open()
        {
            var made = new ShowcaseScene(Scale2aDemo.Stage());
            made.Camera.Follow(Scale2aDemo.LookX, Scale2aDemo.LookZ);
            made.Show(1f, null);
            return made;
        }

        private void Awake()
        {
#if UNITY_EDITOR
            dir = UnityEditor.SessionState.GetString(ShotDirKey, "");
            UnityEditor.SessionState.EraseString(ShotDirKey);
#endif
            auto = Environment.GetEnvironmentVariable("WARCORE_EXIT_AFTER_SHOTS") == "1";
            Application.runInBackground = true;
            if (Camera.main == null)
            {
                var eye = new GameObject("Main Camera") { tag = "MainCamera" };
                Camera made = eye.AddComponent<Camera>();
                made.clearFlags = CameraClearFlags.SolidColor;
                made.backgroundColor = new Color(0.16f, 0.19f, 0.22f);
            }
            if (FindAnyObjectByType<Light>() == null)
            {
                Light sun = new GameObject("Directional Light").AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.intensity = 1.1f;
                sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            }
            try { scene = Open(); }
            catch (Exception e)
            {
                problem = "the lineup did not start: " + e.GetType().Name + ": " + e.Message;
                Debug.LogError("SCALE-2a " + problem);
                if (dir != "") Quit(1);
                return;
            }
            Draw(1f);
            if (dir != "")
            {
                capturing = true;
                StartCoroutine(Capture());
            }
        }

        private void Draw(float alpha)
        {
            scene.Show(alpha, null);
            if (Camera.main != null) IsoCameraInput.Apply(scene.Camera, Camera.main);
        }

        private void Update()
        {
            if (capturing || scene == null) return;
            try
            {
                if (scene.Session.Advance((long)(Time.unscaledDeltaTime * 1e6)) > 0) scene.Stepped();
            }
            catch (Exception e)
            {
                problem = e.GetType().Name + ": " + e.Message;
                Debug.LogError("SCALE-2a " + problem);
                scene.Dispose();
                scene = null;
                return;
            }
            Draw(scene.Session.Alpha);
        }

        private void OnDestroy()
        {
            if (scene != null) scene.Dispose();
            scene = null;
            if (target != null) Destroy(target);
            if (shot != null) Destroy(shot);
        }

        // The csv rows of one tick: one per soldier.
        public static string Rows(ShowcaseScene scene, string picture)
        {
            var text = new System.Text.StringBuilder();
            foreach (ViewEntity e in scene.Session.Current.Entities)
                text.Append(FormattableString.Invariant(
                    $"{picture},{scene.Session.Tick},{scene.Session.StateHash:X16},{e.Key},{e.Stance},{BodySizes.TopOf(scene.Sizes, e):0.000}\n"));
            return text.ToString();
        }

        private IEnumerator Capture()
        {
            for (int i = 0; i < 30; i++)
                yield return null; // let the editor finish compiling shaders before the first picture
            string csv = Path.Combine(dir, Prefix + "-lineup.csv");
            string error = TryRun(() => File.WriteAllText(csv, "name,tick,hash,unit,level,top\n"));
            long last = ShowcaseHost.ShotTicks[ShowcaseHost.ShotTicks.Length - 1];
            for (long tick = 0; tick <= last && error == null; tick++)
            {
                long now = tick;
                error = TryRun(() =>
                {
                    if (now > 0)
                    {
                        scene.Session.StepOnce();
                        scene.Stepped();
                    }
                    if (Array.IndexOf(ShowcaseHost.ShotTicks, now) < 0) return;
                    string name = FormattableString.Invariant($"{Prefix}-lineup-t{now:0000}.png");
                    WriteShot(Path.Combine(dir, name));
                    File.AppendAllText(csv, Rows(scene, name));
                });
                if (tick % 10 == 0)
                    yield return null; // keep the editor responsive
            }
            capturing = false;
            if (error != null)
            {
                problem = error;
                Debug.LogError("SCALE-2a capture failed: " + error);
                TryRun(() => File.WriteAllText(Path.Combine(dir, Prefix + "-failed.txt"), error + "\n"));
                Quit(1);
                yield break;
            }
            Debug.Log("SCALE-2a shots written: " + ShowcaseHost.ShotTicks.Length + " pictures");
            Quit(0);
        }

        private static string TryRun(Action run)
        {
            try { run(); return null; }
            catch (Exception e) { return e.GetType().Name + ": " + e.Message; }
        }

        // One picture of the current tick at 1920 x 1080 with ShowcaseHost's label (tick, state hash) stamped in the corner.
        private void WriteShot(string path)
        {
            Draw(1f);
            Camera eye = Camera.main;
            if (target == null) target = new RenderTexture(ShotWidth, ShotHeight, 24);
            if (shot == null) shot = new Texture2D(ShotWidth, ShotHeight, TextureFormat.RGB24, false);
            RenderTexture active = RenderTexture.active;
            try
            {
                eye.targetTexture = target;
                eye.aspect = (float)ShotWidth / ShotHeight;
                eye.Render();
                RenderTexture.active = target;
                shot.ReadPixels(new Rect(0, 0, ShotWidth, ShotHeight), 0, 0);
            }
            finally
            {
                RenderTexture.active = active;
                eye.targetTexture = null;
                eye.ResetAspect();
            }
            TerrainPlayerHost.Stamp(shot, ShowcaseHost.ShotLabel(scene.Session.Current.Tick, scene.Session.StateHash, 0));
            shot.Apply();
            File.WriteAllBytes(path, shot.EncodeToPNG());
        }

        private void Quit(int code)
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
            if (auto)
                UnityEditor.EditorApplication.Exit(code);
#endif
        }
    }
}
