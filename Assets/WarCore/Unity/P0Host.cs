using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using WarCore.Presentation;

namespace WarCore.Unity
{
    // P0: the M7 match seen through an isometric camera that follows one AI soldier, with a HUD and anchor screenshots.
    // Read-only: it never enqueues a command. The menu sets ModeKey, M2Host sees it and hands the scene over to this.
    public sealed class P0Host : MonoBehaviour
    {
        public const string ModeKey = "WarCore.P0.Mode";
        public const string ShotDirKey = "WarCore.P0.ShotDir";
        private const float DefaultZoom = 20f;
        private const long MatchLimit = 9000;
        private const int VideoFrameEvery = 2, VideoLastTick = 1500; // 15 frames a second at 1x: a 50 s video

        private SimSession session;
        private EntityViews views;
        private PointViews points;
        private ShotLines shots;
        private IsoCamera isoCamera;
        private readonly IsoCameraInput input = new IsoCameraInput();
        private ulong followKey;
        private bool capturing;
        private string hud = "";
        private GUIStyle style;

        private void Awake()
        {
            string dir = "";
#if UNITY_EDITOR
            UnityEditor.SessionState.EraseBool(ModeKey);
            dir = UnityEditor.SessionState.GetString(ShotDirKey, "");
            UnityEditor.SessionState.EraseString(ShotDirKey);
#endif
            byte[] bytes = File.ReadAllBytes(Path.Combine(Application.streamingAssetsPath, M2Host.M7MapPath));
            string layer = "rwr-m6";
#if UNITY_EDITOR
            layer = UnityEditor.EditorPrefs.GetString("WarCore.Content.Layer", "");
            if (string.IsNullOrEmpty(layer)) layer = "rwr-m6";
#endif
            WorldRecipe recipe = ContentLayers.M7(layer);
            session = SimSession.Create(recipe, bytes);
            MapBake baked = FindAnyObjectByType<MapBake>();
            if (baked == null || baked.fileHash != session.Map.FileHash)
            {
                Debug.LogError("Baked scene does not match the map file: run WarCore/M7 Bake Scene.");
                session = null;
                return;
            }
            isoCamera = new IsoCamera(DefaultZoom, session.Map.FloorY);
            var palette = new[] { new Color(0.2f, 0.5f, 1f), new Color(1f, 0.85f, 0.2f) };
            views = new EntityViews(transform, new Vector3(0.5f, 1f, 0.5f), 0.5f, palette);
            points = new PointViews(transform);
            shots = new ShotLines(transform, palette, 1.5f);
            foreach (ViewEntity entity in session.Current.Entities) // sorted by key: the first live team-1 soldier
                if (entity.Team == 1 && entity.Alive)
                {
                    followKey = entity.Key;
                    break;
                }
            Show(session.Previous, session.Current, 0);
            if (dir != "")
            {
                capturing = true;
                StartCoroutine(Capture(dir));
            }
        }

        // One picture of the world: units, points, shots, camera on the followed soldier, HUD text.
        private void Show(FrameView previous, FrameView current, float alpha, ShotAnchor? at = null)
        {
            points.Apply(current);
            views.Apply(previous, current, alpha);
            FrameView shown = FrameView.Interpolate(previous, current, alpha);
            if (shown.TryGet(followKey, out ViewEntity followed))
                isoCamera.Follow(followed.X, followed.Z);
            if (at.HasValue && at.Value.HasFocus) // an event picture looks at the event, not at the soldier
                isoCamera.Follow(at.Value.FocusX, at.Value.FocusZ);
            IsoCameraInput.Apply(isoCamera, Camera.main);
            GroundPick? pick = null;
            Vector3 mouse = Input.mousePosition;
            if (mouse.x >= 0 && mouse.y >= 0 && mouse.x <= Screen.width && mouse.y <= Screen.height)
                pick = isoCamera.ScreenToGround(mouse.x, mouse.y, Screen.width, Screen.height);
            UnitStatus? status = session.TryUnitStatus(followKey, out UnitStatus unit) ? unit : (UnitStatus?)null;
            hud = HudLines.Build(current, session.StateHash, followKey, status, pick);
        }

        private void Update()
        {
            if (session == null || capturing)
                return;
            input.Update(isoCamera, Time.unscaledDeltaTime);
            session.Advance((long)(Time.unscaledDeltaTime * 1e6));
            shots.Add(session.Current);
            Show(session.Previous, session.Current, session.Alpha);
        }

        // Step tick by tick, show exactly that tick, and write a PNG for every anchor the plan fires.
        private IEnumerator Capture(string dir)
        {
            string frames = Path.Combine(dir, "frames");
            Directory.CreateDirectory(frames);
            for (int i = 0; i < 30; i++)
                yield return null; // let the editor finish compiling shaders before the first picture
            var plan = new ShotPlan();
            var anchors = new List<ShotAnchor>();
            for (long tick = 0; tick <= MatchLimit; tick++)
            {
                if (tick > 0)
                    session.StepOnce();
                shots.Add(session.Current);
                plan.Observe(session.Current, anchors);
                // Each picture is one tick shown exactly (Current, no interpolation) and written after it has rendered.
                foreach (ShotAnchor anchor in anchors)
                    yield return Write(Path.Combine(dir, anchor.FileName("P0")), anchor);
                if (tick <= VideoLastTick && tick % VideoFrameEvery == 0)
                    yield return Write(Path.Combine(frames, $"f{tick:0000}.png"), null);
                if (session.IsFinished)
                    break;
            }
            capturing = false;
            Debug.Log($"P0 shots written to {dir}; match end tick {session.EndTick}, hash {session.StateHash:X16}");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
            if (System.Environment.GetEnvironmentVariable("WARCORE_EXIT_AFTER_SHOTS") == "1")
                UnityEditor.EditorApplication.Exit(0);
#endif
        }

        private IEnumerator Write(string path, ShotAnchor? at)
        {
            Show(session.Current, session.Current, 0, at);
            yield return null;
            yield return new WaitForEndOfFrame();
            Texture2D image = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(path, image.EncodeToPNG());
            Destroy(image);
        }

        private void OnGUI()
        {
            if (session == null)
                return;
            style = style ?? new GUIStyle(GUI.skin.box) { fontSize = 22, alignment = TextAnchor.UpperLeft };
            GUI.Box(new Rect(12, 12, 640, 160), hud, style);
        }
    }
}
