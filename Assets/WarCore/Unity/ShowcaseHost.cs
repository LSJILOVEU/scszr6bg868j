using System;
using System.Collections;
using System.IO;
using UnityEngine;
using WarCore.Presentation;

namespace WarCore.Unity
{
    // SHOW-1: the playable showcase. One stage at a time (Showcase.Stages), built at run time by ShowcaseScene in any scene:
    // the host makes a camera and a light when the scene has none. Keys and mouse drive the controlled soldier; Tab goes to
    // the next stage, Backspace restarts it, P pauses, G throws a grenade (real where the stage has one, else the drawn-only
    // preview), C orders the team's stance where the stage takes orders (SHOW-2). The HUD lists what is on in
    // the stage and what is not built yet, and shows a small map with every soldier, the player's squad and the points.
    // With a shot directory (the capture menu item) it plays every stage's script instead and writes pictures at ShotTicks.
    public sealed class ShowcaseHost : MonoBehaviour
    {
        public const string ShotDirKey = "WarCore.Show1.ShotDir", StageKey = "WarCore.Show1.Stage";
        public const string Prefix = "SHOW1";
        public static readonly long[] ShotTicks = { 0, 150, 300, 600 };
        private const int ShotWidth = 1920, ShotHeight = 1080;
        private const float BarHeight = 44f, Margin = 12f, StubSeconds = 3f, MiniSize = 210f;
        private static readonly Func<KeyCode, bool> GetKey = Input.GetKey;

        private readonly IsoCameraInput cameraInput = new IsoCameraInput();
        private ShowcaseScene scene;
        private ShowcaseActions actions;
        private ShowcaseSound sound;
        private float stepWait, hurtBefore;
        private PlayerInputMapper mapper;
        private LivePlayerInput liveInput;
        private PlayerKeyPoller keys;
        private int index;
        private bool help = true, missing, paused, capturing, auto;
        private string dir = "", problem = "", selfCheck = "", status = "", shows = "";
        private float stubUntil = -1f;
        private Vector3? aimPoint;
        private GUIStyle box, banner, button;
        private RenderTexture target;
        private Texture2D shot;

        public ShowcaseScene Scene => scene;
        public int StageIndex => index;
        public string Problem => problem;

        private void Awake()
        {
#if UNITY_EDITOR
            dir = UnityEditor.SessionState.GetString(ShotDirKey, "");
            index = UnityEditor.SessionState.GetInt(StageKey, 0);
            UnityEditor.SessionState.EraseString(ShotDirKey);
            UnityEditor.SessionState.EraseInt(StageKey);
#endif
            auto = Environment.GetEnvironmentVariable("WARCORE_EXIT_AFTER_SHOTS") == "1";
            Application.runInBackground = true;
            EnsureCameraAndLight();
            sound = new ShowcaseSound(Camera.main.gameObject);
            try { keys = new PlayerKeyPoller(PlayerKeys.Default, GetKey); }
            catch (ArgumentException e)
            {
                problem = "key table: " + e.Message;
                Debug.LogError("SHOW-1 " + problem);
                return;
            }
            StartStage(index);
            if (dir != "")
            {
                capturing = true;
                StartCoroutine(Capture());
            }
        }

        private static void EnsureCameraAndLight()
        {
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
        }

        // Replace the running stage by stage i (wrapped). A stage that fails to start leaves its reason on screen.
        public void StartStage(int i)
        {
            int count = Showcase.Stages.Count;
            index = (i % count + count) % count;
            if (scene != null) scene.Dispose();
            scene = null;
            actions = null;
            problem = selfCheck = status = shows = "";
            paused = false;
            aimPoint = null;
            mapper = new PlayerInputMapper(PlayerKeys.Default);
            liveInput = new LivePlayerInput();
            ShowcaseStage stage = Showcase.Stages[index];
            try
            {
                scene = new ShowcaseScene(stage);
                actions = new ShowcaseActions(stage, stage.StanceScales().Length);
                selfCheck = scene.SelfCheck();
            }
            catch (Exception e)
            {
                if (scene != null) scene.Dispose();
                scene = null;
                problem = "stage '" + stage.Id + "' did not start: " + e.GetType().Name + ": " + e.Message;
                Debug.LogError("SHOW-1 " + problem);
                return;
            }
            if (selfCheck != "")
                Debug.LogError("SHOW-1 self-check failed in stage '" + stage.Id + "':\n" + selfCheck);
            shows = ShowcaseHud.Shows(stage);
            Refresh(0f);
        }

        private void OnDestroy()
        {
            if (scene != null) scene.Dispose();
            scene = null;
            if (target != null) Destroy(target);
            if (shot != null) Destroy(shot);
        }

        private float GuiScale => Mathf.Clamp(Screen.height / 1080f, 0.75f, 2.5f);

        private void Update()
        {
            if (capturing)
                return;
            bool focused = Application.isFocused;
            if (focused)
            {
                if (Input.GetKeyDown(KeyCode.Tab)) { StartStage(index + 1); return; }
                if (Input.GetKeyDown(KeyCode.Backspace)) { StartStage(index); return; }
                if (Input.GetKeyDown(KeyCode.H)) help = !help;
                if (Input.GetKeyDown(KeyCode.M)) missing = !missing;
                if (Input.GetKeyDown(KeyCode.P)) paused = !paused;
                if (Input.GetKeyDown(KeyCode.N)) sound.Muted = !sound.Muted;
                if (Input.GetKeyDown(KeyCode.G)) Throw();
                if (Input.GetKeyDown(KeyCode.C) && scene != null && actions != null && !paused && problem == "") actions.NextStance(scene.Session);
                if (scene != null && Input.GetKeyDown(KeyCode.V)) scene.SquadLines = !scene.SquadLines;
            }
            if (scene == null)
                return;
            SimSession session = scene.Session;
            if (focused)
                cameraInput.Update(scene.Camera, Time.unscaledDeltaTime);
            Vector3 mouse = Input.mousePosition;
            float w = Screen.width, h = Screen.height;
            bool inWindow = focused && w > 0 && h > 0 && mouse.x >= 0 && mouse.y >= 0 && mouse.x < w && mouse.y < h;
            bool onBar = mouse.y < (BarHeight + Margin) * GuiScale; // the stage buttons: a click there is not a shot
            AimTarget? aim = null;
            if (inWindow)
            {
                try
                {
                    FrameView shown = ShowcaseScene.Blend(session.Previous, session.Current, session.Alpha);
                    aim = AimPick.Pick(scene.Camera, mouse.x, mouse.y, w, h, scene.Ground, shown, session.ControlledKey,
                        session.Map.Plates, scene.Cut);
                }
                catch (ArgumentOutOfRangeException) { aim = null; } // a pick outside the world bounds
            }
            aimPoint = aim.HasValue ? new Vector3(aim.Value.X, aim.Value.Y, aim.Value.Z) : (Vector3?)null;
            float ix = focused ? (GetKey(KeyCode.D) ? 1 : 0) - (GetKey(KeyCode.A) ? 1 : 0) : 0;
            float iy = focused ? (GetKey(KeyCode.W) ? 1 : 0) - (GetKey(KeyCode.S) ? 1 : 0) : 0;
            bool fire = focused && !onBar && Input.GetMouseButton(0), click = focused && !onBar && Input.GetMouseButtonDown(0);
            if (!paused && problem == "")
            {
                try
                {
                    PlayerInput player = mapper.Map(scene.Camera, ix, iy, mouse.x, mouse.y, w, h, inWindow && aim.HasValue,
                        fire, keys.KeyDown(focused), aim);
                    liveInput.Apply(session, player, click);
                }
                catch (ArgumentException) { } // an aim the simulation refuses (off the map): this frame's input is dropped
                try
                {
                    if (session.Advance((long)(Time.unscaledDeltaTime * 1e6)) > 0)
                    {
                        scene.Stepped();
                        sound.NewTick();
                        if (scene.Shots.MineFired) sound.Shot(0f, true);
                        foreach (Vector3 from in scene.Shots.Fired)
                            sound.Shot((from - scene.PlayerAt).magnitude, false);
                    }
                }
                catch (Exception e)
                {
                    problem = "the stage stopped: " + e.GetType().Name + ": " + e.Message + "  (Backspace restarts it)";
                    Debug.LogError("SHOW-1 " + problem);
                }
            }
            bool frozen = paused || problem != "";
            Vector2 lead = inWindow ? new Vector2(2f * mouse.x / w - 1f, 2f * mouse.y / h - 1f) : Vector2.zero;
            Refresh(frozen ? 1f : session.Alpha, frozen ? 0f : Time.unscaledDeltaTime, lead);
            if (frozen) return;
            foreach (Vector3 burst in scene.Fx.Bursts)
                sound.Burst((burst - scene.PlayerAt).magnitude);
            if (scene.HurtLeft > hurtBefore) sound.Hurt();
            hurtBefore = scene.HurtLeft;
            // A footstep every 2.2 m the player walks.
            stepWait -= scene.PlayerSpeed * Time.unscaledDeltaTime;
            if (scene.PlayerAlive && scene.PlayerSpeed > 0.5f && stepWait <= 0f)
            {
                sound.Step();
                stepWait = 2.2f;
            }
        }

        // G: a real grenade where the stage has one (nothing happens while it cools down or the player is down), else the
        // drawn-only preview and its banner.
        private void Throw()
        {
            if (scene == null || actions == null) return;
            if (!actions.RealGrenades)
            {
                stubUntil = Time.unscaledTime + StubSeconds;
                if (aimPoint.HasValue && !paused) scene.ThrowPreview(aimPoint.Value);
                return;
            }
            if (!aimPoint.HasValue || paused || problem != "") return;
            Vector3 aim = aimPoint.Value;
            if (!actions.TryThrow(scene.Session, (long)Mathf.Round(aim.x * 1000f), (long)Mathf.Round(aim.z * 1000f), out long x, out long z)) return;
            float lx = x / 1000f, lz = z / 1000f;
            float ly = scene.Ground == null ? scene.Session.Map.FloorY : scene.Ground.HeightAt(lx, lz, scene.Session.Map.FloorY);
            scene.ThrowReal(new Vector3(lx, ly, lz));
        }

        // Draw the stage as it stands and rebuild the status text.
        private void Refresh(float alpha, float dt = 0f, Vector2 lead = default)
        {
            scene.Show(alpha, aimPoint, dt, lead);
            Camera eye = Camera.main;
            if (eye != null)
            {
                IsoCameraInput.Apply(scene.Camera, eye);
                eye.transform.position += eye.transform.right * scene.Shake.x + eye.transform.up * scene.Shake.y;
            }
            SimSession session = scene.Session;
            ulong? key = session.ControlledKey;
            UnitStatus? mine = key.HasValue && session.TryUnitStatus(key.Value, out UnitStatus found) ? found : (UnitStatus?)null;
            status = ShowcaseHud.Status(scene.Stage, index, Showcase.Stages.Count, session.Current, key, mine, scene.LostPlayer,
                scene.PlayerAlive, scene.InFlight) + (actions == null ? "" : actions.Line(session.Tick)) + ShowcaseHud.Squad(session.Current, key)
                + ShowcaseHud.Score(scene.Shots.PlayerShots, scene.Shots.PlayerKills);
        }

        // The small map: every living soldier as a dot in its team's colour (the player's squad brighter, the player white)
        // and every point as a square in its owner's colour. x runs to the right and z up.
        private void DrawMiniMap(Rect at)
        {
            SimSession session = scene.Session;
            ViewMap map = session.Map;
            FrameView frame = session.Current;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(at.x - 3f, at.y - 3f, at.width + 6f, at.height + 6f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.DrawTexture(at, scene.MiniMap);
            ulong? key = session.ControlledKey;
            ulong band = key.HasValue && frame.TryGet(key.Value, out ViewEntity me) ? me.Band : 0;
            foreach (ViewPoint p in frame.Points)
            {
                GUI.color = p.Owner == 0 ? ShowcaseScene.Rgb(scene.Colors.Neutral) : ShowcaseScene.Rgb(scene.Colors.Team[(p.Owner - 1) % scene.Colors.Team.Count]);
                Dot(at, map, p.X, p.Z, 9f);
            }
            foreach (ViewEntity e in frame.Entities)
            {
                if (!e.Alive || key.HasValue && e.Key == key.Value) continue;
                Color c = ShowcaseScene.Rgb(scene.Colors.Team[e.Team <= 0 ? 0 : (e.Team - 1) % scene.Colors.Team.Count]);
                bool mine = band != 0 && e.Band == band;
                GUI.color = mine ? Color.Lerp(c, Color.white, 0.6f) : c;
                Dot(at, map, e.X, e.Z, mine ? 4.5f : 3f);
            }
            if (key.HasValue && frame.TryGet(key.Value, out ViewEntity self) && self.Alive)
            {
                GUI.color = Color.white;
                Dot(at, map, self.X, self.Z, 6f);
            }
            GUI.color = Color.white;
        }

        private static void Dot(Rect at, ViewMap map, float x, float z, float size)
        {
            float px = at.x + (x - map.MinX) / map.Width * at.width, py = at.yMax - (z - map.MinZ) / map.Depth * at.height;
            GUI.DrawTexture(new Rect(px - size / 2f, py - size / 2f, size, size), Texture2D.whiteTexture);
        }

        private void OnGUI()
        {
            if (capturing)
                return;
            float scale = GuiScale;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float w = Screen.width / scale, h = Screen.height / scale;
            box = box ?? new GUIStyle(GUI.skin.box) { fontSize = 17, alignment = TextAnchor.UpperLeft, wordWrap = true, richText = false };
            banner = banner ?? new GUIStyle(GUI.skin.box) { fontSize = 22, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            button = button ?? new GUIStyle(GUI.skin.button) { fontSize = 16 };

            string left = status + (paused ? "PAUSED (P)\n" : "") + (!sound.Available ? "SOUND  off (no audio module)\n" : sound.Muted ? "SOUND  muted (N)\n" : "") + "\n" + shows + (missing ? "\n" + ShowcaseHud.Missing() : "\nM = what is not built yet\n");
            if (selfCheck != "") left += "\nSELF-CHECK FAILED (see the console)\n";
            float leftWidth = Mathf.Min(660f, w * 0.48f);
            GUI.Box(new Rect(Margin, Margin, leftWidth, box.CalcHeight(new GUIContent(left), leftWidth)), left, box);
            if (help)
            {
                float width = Mathf.Min(470f, w * 0.4f);
                GUI.Box(new Rect(w - width - Margin, Margin, width, box.CalcHeight(new GUIContent(ShowcaseHud.Keys), width)), ShowcaseHud.Keys, box);
            }
            if (scene != null && Event.current.type == EventType.Repaint)
            {
                Texture2D mini = scene.MiniMap;
                float mw = mini.width >= mini.height ? MiniSize : MiniSize * mini.width / mini.height;
                float mh = mini.height >= mini.width ? MiniSize : MiniSize * mini.height / mini.width;
                DrawMiniMap(new Rect(w - mw - Margin - 3f, h - BarHeight - Margin * 1.5f - mh - 3f, mw, mh));
                if (scene.HurtLeft > 0f)
                {
                    // Red at the edges of the picture while the player is being hit.
                    GUI.color = new Color(0.9f, 0.05f, 0.05f, 0.45f * Mathf.Clamp01(scene.HurtLeft / ShowcaseScene.HurtSeconds));
                    const float edge = 46f;
                    GUI.DrawTexture(new Rect(0f, 0f, w, edge), Texture2D.whiteTexture);
                    GUI.DrawTexture(new Rect(0f, h - edge, w, edge), Texture2D.whiteTexture);
                    GUI.DrawTexture(new Rect(0f, edge, edge, h - 2f * edge), Texture2D.whiteTexture);
                    GUI.DrawTexture(new Rect(w - edge, edge, edge, h - 2f * edge), Texture2D.whiteTexture);
                    GUI.color = Color.white;
                }
            }
            if (scene != null && scene.KillLeft > 0f)
                GUI.Box(new Rect(w * 0.5f - 70f, h * 0.24f, 140f, 40f), "KILL", banner);
            if (problem != "")
                GUI.Box(new Rect(w * 0.15f, h * 0.4f, w * 0.7f, 110f), problem, banner);
            else if (Time.unscaledTime < stubUntil)
                GUI.Box(new Rect(w * 0.15f, h * 0.72f, w * 0.7f, 64f), ShowcaseHud.GrenadeStub, banner);

            int count = Showcase.Stages.Count;
            float each = (w - 2 * Margin) / (count + 1);
            for (int i = 0; i <= count; i++)
            {
                var at = new Rect(Margin + i * each, h - BarHeight - Margin * 0.5f, each - 6f, BarHeight);
                if (i == count)
                {
                    if (GUI.Button(at, "Restart stage", button)) StartStage(index);
                    continue;
                }
                string label = (i == index ? "> " : "") + (i + 1) + "  " + Showcase.Stages[i].Title;
                if (GUI.Button(at, label, button) && i != index) StartStage(i);
            }
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
            problem = what;
            Debug.LogError("SHOW-1 capture failed: " + what);
            if (dir != "")
            {
                try { File.WriteAllText(Path.Combine(dir, Prefix + "-failed.txt"), what + "\n"); }
                catch (Exception e) { Debug.LogError("SHOW-1 report not written: " + e.GetType().Name); }
            }
            Quit(1);
        }

        // Every stage in turn with its script queued: step to the last of ShotTicks, one picture at each of them, one csv row
        // per picture. Any failure (a stage that does not start, a self-check line, a step or a write) ends the run with 1.
        private IEnumerator Capture()
        {
            for (int i = 0; i < 30; i++)
                yield return null; // let the editor finish compiling shaders before the first picture
            string csv = Path.Combine(dir, Prefix + "-shots.csv");
            string error = TryRun(() => File.WriteAllText(csv, "name,stage,tick,hash,alive1,alive2,inflight\n"));
            long last = ShotTicks[ShotTicks.Length - 1];
            for (int s = 0; s < Showcase.Stages.Count && error == null; s++)
            {
                StartStage(s);
                if (scene == null) { error = problem; break; }
                if (selfCheck != "") { error = "self-check, stage '" + scene.Stage.Id + "': " + selfCheck.Trim().Replace('\n', ';'); break; }
                error = TryRun(() =>
                {
                    foreach (var item in scene.Stage.Script())
                        scene.Session.SetPlayerInput(item.AtTick, item.Input);
                    foreach (BridgeCommand command in scene.Stage.Commands())
                        scene.Session.Enqueue(command);
                });
                for (long tick = 0; tick <= last && error == null; tick++)
                {
                    long now = tick;
                    error = TryRun(() =>
                    {
                        if (now > 0 && !scene.Session.IsFinished)
                        {
                            scene.Session.StepOnce();
                            scene.Stepped();
                        }
                        if (Array.IndexOf(ShotTicks, now) < 0) return;
                        string name = FormattableString.Invariant($"{Prefix}-{s + 1}-{scene.Stage.Id}-t{now:0000}.png");
                        WriteShot(Path.Combine(dir, name), s);
                        ViewMatch m = scene.Session.Current.Match;
                        File.AppendAllText(csv, FormattableString.Invariant(
                            $"{name},{scene.Stage.Id},{scene.Session.Tick},{scene.Session.StateHash:X16},{m.AliveOne},{m.AliveTwo},{scene.InFlight}\n"));
                    });
                    if (tick % 10 == 0)
                        yield return null; // keep the editor responsive
                }
            }
            if (error != null)
            {
                Fail(error);
                yield break;
            }
            capturing = false;
            Debug.Log("SHOW-1 shots written: " + Showcase.Stages.Count * ShotTicks.Length + " pictures");
            Quit(0);
        }

        private static string TryRun(Action run)
        {
            try { run(); return null; }
            catch (Exception e) { return e.GetType().Name + ": " + e.Message; }
        }

        // One picture, synchronously: the main camera renders the current tick into a 1920 x 1080 texture and the label
        // (tick, state hash, stage number) is stamped in the corner. The HUD is not in the picture (OnGUI is not rendered).
        private void WriteShot(string path, int stage)
        {
            Refresh(1f);
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
            TerrainPlayerHost.Stamp(shot, ShotLabel(scene.Session.Current.Tick, scene.Session.StateHash, stage));
            shot.Apply();
            File.WriteAllBytes(path, shot.EncodeToPNG());
        }

        // The stamped label; every character has a glyph in TerrainPlayerHost.
        public static string[] ShotLabel(long tick, ulong stateHash, int stage) => new[]
        {
            FormattableString.Invariant($"TICK {tick}"),
            FormattableString.Invariant($"STATEHASH {stateHash:X16}"),
            FormattableString.Invariant($"PART {stage + 1}"),
        };
    }
}
