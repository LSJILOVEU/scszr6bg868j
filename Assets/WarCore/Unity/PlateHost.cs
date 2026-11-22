using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using WarCore.Presentation;

namespace WarCore.Unity
{
    // ML-10: the plate demo (PlateDemo: the demo house with a first floor and a stair) built at run time in an empty scene.
    // SEE-1: the same host also plays the street demo (StreetDemo, chosen with DemoKey = "street", pictures named SEE1-...); in both
    // the layer cut runs with LayerCut.SeeRadius and the team mates the walls cover are drawn as silhouettes (Silhouettes).
    // Play: WASD moves the player, the mouse aims (AimPick on the ground and the plates the cut leaves, with the layer), the
    // left button fires; Q/E/R/F and the wheel turn, tilt and zoom; the camera follows the player and the layer cut follows
    // the player (LayerCut). Capture (the ML-10 menu sets the directory): the scripted walk (PlateDemo.Script), one picture
    // per tick up to CaptureTicks for the video and a named picture at each ShotTicks tick, each stamped with the tick, the
    // state hash and the player's layer. Before the first tick it runs TerrainHost.SelfCheck and refuses to run when any
    // link disagrees.
    public sealed class PlateHost : MonoBehaviour
    {
        public const string ShotDirKey = "WarCore.ML10.ShotDir";
        public const string Prefix = "ML10";
        public const string DemoKey = "WarCore.SEE1.Demo", StreetDemoName = "street", StreetPrefix = "SEE1";
        public static readonly long[] ShotTicks = { 0, 150, 300, 600 };
        public const long CaptureTicks = 900; // 30 s at 30 ticks a second
        private const int ShotWidth = 1920, ShotHeight = 1080;

        private static readonly Func<KeyCode, bool> GetKey = Input.GetKey;
        private SimSession session;
        private EntityViews views;
        private PointViews points;
        private ShotLines shots;
        private PlateViews plateViews;
        private IsoCamera isoCamera;
        private readonly IsoCameraInput cameraInput = new IsoCameraInput();
        private readonly PlayerInputMapper mapper = new PlayerInputMapper(PlayerKeys.Default);
        private readonly LivePlayerInput liveInput = new LivePlayerInput();
        private PlayerKeyPoller keys;
        private LayerCutResult cut;
        private HashSet<ulong> covered = new HashSet<ulong>();
        private string dir = "", hud = "", prefix = Prefix;
        private bool street;
        private bool capturing;
        private GUIStyle style;
        private RenderTexture target;
        private Texture2D shot;

        private void Awake()
        {
#if UNITY_EDITOR
            dir = UnityEditor.SessionState.GetString(ShotDirKey, "");
            UnityEditor.SessionState.EraseString(ShotDirKey);
            street = UnityEditor.SessionState.GetString(DemoKey, "") == StreetDemoName;
            UnityEditor.SessionState.EraseString(DemoKey);
#endif
            if (street) prefix = StreetPrefix;
            Application.runInBackground = true;
            session = street ? SimSession.Create(StreetDemo.Recipe(), StreetDemo.MapBytes())
                : SimSession.Create(PlateDemo.Recipe(), PlateDemo.MapBytes());
            if (dir != "")
                foreach (var item in street ? StreetDemo.Script() : PlateDemo.Script())
                    session.SetPlayerInput(item.AtTick, item.Input);
            try { keys = new PlayerKeyPoller(PlayerKeys.Default, GetKey); }
            catch (ArgumentException e)
            {
                Debug.LogError("ML-10 key table: " + e.Message);
                session = null;
                return;
            }
            Material wall = NewMaterial(new Color(0.45f, 0.42f, 0.4f));
            GameObject mapRoot = MapView.Build(session.Map, NewMaterial(new Color(0.62f, 0.66f, 0.48f)), wall);
            plateViews = new PlateViews(mapRoot.transform, session.Map.Plates, session.Map, NewMaterial(new Color(0.7f, 0.6f, 0.45f)), wall);
            var palette = new[] { new Color(0.2f, 0.5f, 1f), new Color(1f, 0.85f, 0.2f) };
            views = new EntityViews(transform, new Vector3(0.5f, 1f, 0.5f), 0.5f, palette, BodySizes.Of(session));
            points = new PointViews(transform, ViewGround.For(session.Map));
            shots = new ShotLines(transform, palette, 1.5f, ViewGround.For(session.Map), BodySizes.Of(session), session.Map.FloorY);
            isoCamera = new IsoCamera(12f, session.Map.FloorY);
            Show(session.Previous, session.Current, 0);

            string problems = TerrainHost.SelfCheck(session.Map, session, session.Current, mapRoot.transform, transform);
            if (problems != "")
            {
                Debug.LogError("ML-10 self-check failed:\n" + problems);
                if (dir != "") TryWrite(() => File.WriteAllText(Path.Combine(dir, prefix + "-selfcheck-failed.txt"), problems));
                session = null;
                Quit(1);
                return;
            }
            Debug.Log("ML-10 self-check clean");
            if (dir != "")
            {
                capturing = true;
                StartCoroutine(Capture());
            }
        }

        private void OnDestroy()
        {
            if (target != null) Destroy(target);
            if (shot != null) Destroy(shot);
        }

        private void Update()
        {
            if (session == null || capturing)
                return;
            bool focused = Application.isFocused;
            if (focused)
                cameraInput.Update(isoCamera, Time.unscaledDeltaTime);
            Vector3 mouse = Input.mousePosition;
            float w = Screen.width, h = Screen.height;
            bool inWindow = focused && w > 0 && h > 0 && mouse.x >= 0 && mouse.y >= 0 && mouse.x < w && mouse.y < h;
            FrameView shown = FrameView.Interpolate(session.Previous, session.Current, session.Alpha);
            AimTarget? aim = null;
            if (inWindow)
            {
                try
                {
                    aim = AimPick.Pick(isoCamera, mouse.x, mouse.y, w, h, ViewGround.For(session.Map), shown, session.ControlledKey,
                        session.Map.Plates, cut, BodySizes.Of(session));
                }
                catch (ArgumentOutOfRangeException) { aim = null; } // a pick outside the world bounds: keep the last aim
            }
            float ix = focused ? (GetKey(KeyCode.D) ? 1 : 0) - (GetKey(KeyCode.A) ? 1 : 0) : 0;
            float iy = focused ? (GetKey(KeyCode.W) ? 1 : 0) - (GetKey(KeyCode.S) ? 1 : 0) : 0;
            PlayerInput player = mapper.Map(isoCamera, ix, iy, mouse.x, mouse.y, w, h, inWindow && aim.HasValue,
                focused && Input.GetMouseButton(0), keys.KeyDown(focused), aim);
            try
            {
                liveInput.Apply(session, player, focused && Input.GetMouseButtonDown(0));
                if (session.Advance((long)(Time.unscaledDeltaTime * 1e6)) > 0)
                    shots.Add(session.Current);
            }
            catch (Exception e)
            {
                Debug.LogError("ML-10 step failed: " + e.GetType().Name + ": " + e.Message);
                session = null;
                return;
            }
            Show(session.Previous, session.Current, session.Alpha);
        }

        // The player's unit as shown this frame (interpolated), or null.
        private ViewEntity? Player(FrameView shown)
        {
            ulong? key = session.ControlledKey;
            return key.HasValue && shown.TryGet(key.Value, out ViewEntity me) ? me : (ViewEntity?)null;
        }

        private void Show(FrameView previous, FrameView current, float alpha)
        {
            FrameView shown = FrameView.Interpolate(previous, current, alpha);
            ViewEntity? me = Player(shown);
            if (me.HasValue)
                isoCamera.Follow(me.Value.X, me.Value.Z);
            cut = LayerCut.Run(session.Map.Plates, session.Map, me, isoCamera.Forward, LayerCut.SeeRadius, BodySizes.Of(session));
            covered = Silhouettes.Run(session.Map.Plates, session.Map, cut, me, shown.Entities, isoCamera.Forward, BodySizes.Of(session));
            plateViews.Apply(cut);
            points.Apply(current);
            views.Apply(previous, current, alpha, cut, covered);
            IsoCameraInput.Apply(isoCamera, Camera.main);
            hud = TerrainPlayerHost.Hud(current, session.StateHash, session.Map, session.ControlledKey)
                + "\nLayer " + (me.HasValue ? me.Value.Layer.ToString() : "-") + (cut.Indoors ? "   indoors" : "   outdoors")
                + "\nSilhouettes " + covered.Count;
        }

        // Step tick by tick: one picture per tick (the video) and a named one at each ShotTicks tick, listed in the csv.
        private IEnumerator Capture()
        {
            for (int i = 0; i < 30; i++)
                yield return null; // let the editor finish compiling shaders before the first picture
            string csv = Path.Combine(dir, prefix + "-shots.csv");
            string error = TryWrite(() => File.WriteAllText(csv, "name,tick,hash,layer,indoors\n"));
            for (long tick = 0; tick <= CaptureTicks && error == null; tick++)
            {
                if (tick > 0)
                {
                    try { session.StepOnce(); }
                    catch (Exception e) { error = "step " + tick + ": " + e.GetType().Name + ": " + e.Message; break; }
                }
                shots.Add(session.Current);
                Show(session.Current, session.Current, 0);
                error = WriteShot(Path.Combine(dir, FormattableString.Invariant($"{prefix}-frame-{tick:0000}.png")));
                if (error == null && Array.IndexOf(ShotTicks, tick) >= 0)
                {
                    string name = FormattableString.Invariant($"{prefix}-tick-{tick:0000}.png");
                    error = WriteShot(Path.Combine(dir, name));
                    ViewEntity? me = Player(session.Current);
                    string row = FormattableString.Invariant($"{name},{session.Tick},{session.StateHash:X16},{(me.HasValue ? me.Value.Layer : -1)},{(cut.Indoors ? 1 : 0)}\n");
                    error = error ?? TryWrite(() => File.AppendAllText(csv, row));
                }
                yield return null;
            }
            capturing = false;
            if (error != null)
            {
                Debug.LogError("ML-10 capture failed: " + error);
                Quit(1);
                yield break;
            }
            Debug.Log(FormattableString.Invariant($"ML-10 shots written to {dir}: last tick {session.Tick}, hash {session.StateHash:X16}"));
            Quit(0);
        }

        // One picture of the shown tick into a 1920 x 1080 texture, stamped in the top-left corner; null when written.
        private string WriteShot(string path)
        {
            return TryWrite(() =>
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
                ViewEntity? me = Player(session.Current);
                TerrainPlayerHost.Stamp(shot, new[]
                {
                    FormattableString.Invariant($"TICK {session.Tick}"),
                    FormattableString.Invariant($"STATEHASH {session.StateHash:X16}"),
                    "LAYER " + (me.HasValue ? me.Value.Layer.ToString() : "-"),
                });
                shot.Apply();
                File.WriteAllBytes(path, shot.EncodeToPNG());
            });
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
            if (dir == "") return;
            UnityEditor.EditorApplication.isPlaying = false;
            if (Environment.GetEnvironmentVariable("WARCORE_EXIT_AFTER_SHOTS") == "1")
                UnityEditor.EditorApplication.Exit(code);
#endif
        }

        private void OnGUI()
        {
            if (session == null)
                return;
            style = style ?? new GUIStyle(GUI.skin.box) { fontSize = 20, alignment = TextAnchor.UpperLeft };
            GUI.Box(new Rect(12, 12, 720, 300), hud, style);
        }
    }
}
