using System;
using System.IO;
using UnityEngine;
using WarCore.Presentation;

namespace WarCore.Unity
{
    // Editor menu entry: M2Host hands over before it creates a session.
    public sealed class MpHost : MonoBehaviour
    {
        public const string ModeKey = "WarCore.MP.Mode";

        // The host's input step: WASD, the mouse aim and the bound keys make one PlayerInput. Outside UNITY_EDITOR so that
        // EditMode tests call the same code with their own key source. Without focus no key counts and the move is zero.
        public static PlayerInput ReadPlayer(PlayerInputMapper mapper, PlayerKeyPoller poller, IsoCamera camera, bool focused,
            Func<KeyCode, bool> key, Vector3 mouse, float w, float h, bool primaryHeld)
        {
            float ix = focused ? (key(KeyCode.D) ? 1 : 0) - (key(KeyCode.A) ? 1 : 0) : 0;
            float iy = focused ? (key(KeyCode.W) ? 1 : 0) - (key(KeyCode.S) ? 1 : 0) : 0;
            bool mouseInWindow = focused && w > 0 && h > 0 && mouse.x >= 0 && mouse.y >= 0 && mouse.x < w && mouse.y < h;
            return mapper.Map(camera, ix, iy, mouse.x, mouse.y, w, h, mouseInWindow, focused && primaryHeld, poller.KeyDown(focused));
        }

#if UNITY_EDITOR
        // Built once: a method group converted every frame would allocate a new delegate.
        private static readonly Func<KeyCode, bool> GetKey = Input.GetKey;
        private SimSession session;
        private EntityViews views;
        private PointViews points;
        private ShotLines shots;
        private IsoCamera isoCamera;
        private readonly IsoCameraInput cameraInput = new IsoCameraInput();
        private readonly PlayerInputMapper mapper = new PlayerInputMapper(PlayerKeys.Default);
        private PlayerKeyPoller keys;
        private readonly LivePlayerInput liveInput = new LivePlayerInput();
        private PlayerTracker tracker;
        private string hud = "";
        private GUIStyle style;

        private void Awake()
        {
            UnityEditor.SessionState.EraseBool(ModeKey);
            byte[] bytes = File.ReadAllBytes(Path.Combine(Application.streamingAssetsPath, M2Host.M7MapPath));
            session = SimSession.Create(DemoRecipes.MpRespawnDemo(), bytes);
            try { keys = new PlayerKeyPoller(PlayerKeys.Default, GetKey); }
            catch (ArgumentException e)
            {
                Debug.LogError("MP key table: " + e.Message);
                session = null;
                return;
            }
            MapBake baked = FindAnyObjectByType<MapBake>();
            if (baked == null || baked.fileHash != session.Map.FileHash)
            {
                Debug.LogError("Baked scene does not match the map file: run WarCore/M7/Bake Scene.");
                session = null;
                return;
            }
            if (Camera.main == null)
            {
                Debug.LogError("MP requires the M7 scene's Main Camera: run WarCore/M7/Bake Scene.");
                session = null;
                return;
            }
            isoCamera = new IsoCamera(20f, session.Map.FloorY);
            var palette = new[] { new Color(0.2f, 0.5f, 1f), new Color(1f, 0.85f, 0.2f) };
            views = new EntityViews(transform, new Vector3(0.5f, 1f, 0.5f), 0.5f, palette);
            points = new PointViews(transform);
            shots = new ShotLines(transform, palette, 1.5f);
            tracker = new PlayerTracker(session.ControlledKey);
            Show();
        }

        private static bool MouseInWindow(Vector3 mouse)
            => Application.isFocused && Screen.width > 0 && Screen.height > 0
                && mouse.x >= 0 && mouse.y >= 0 && mouse.x < Screen.width && mouse.y < Screen.height;

        private void Update()
        {
            if (session == null)
                return;
            bool focused = Application.isFocused;
            if (focused)
                cameraInput.Update(isoCamera, Time.unscaledDeltaTime);
            PlayerInput player = ReadPlayer(mapper, keys, isoCamera, focused, GetKey, Input.mousePosition,
                Screen.width, Screen.height, Input.GetMouseButton(0));
            liveInput.Apply(session, player, focused && Input.GetMouseButtonDown(0));
            int advanced = session.Advance((long)(Time.unscaledDeltaTime * 1e6));
            // A zero-tick frame still displays the same fire records. Do not draw them twice.
            if (advanced > 0)
                shots.Add(session.Current);
            Show();
        }

        private void Show()
        {
            FrameView current = session.Current;
            ulong? key = session.ControlledKey;
            // Advance can span both the death tick and the takeover tick (PlayerTracker keeps the rule).
            bool lostPlayer = tracker.Update(current, key);
            bool alive = tracker.Alive;

            points.Apply(current);
            views.Apply(session.Previous, current, session.Alpha);
            FrameView shown = FrameView.Interpolate(session.Previous, current, session.Alpha);
            if (key.HasValue && shown.TryGet(key.Value, out ViewEntity followed))
                isoCamera.Follow(followed.X, followed.Z);
            IsoCameraInput.Apply(isoCamera, Camera.main);

            Vector3 mouse = Input.mousePosition;
            GroundPick? pick = MouseInWindow(mouse)
                ? isoCamera.ScreenToGround(mouse.x, mouse.y, Screen.width, Screen.height) : (GroundPick?)null;
            UnitStatus? status = key.HasValue && session.TryUnitStatus(key.Value, out UnitStatus unit)
                ? unit : (UnitStatus?)null;
            hud = HudLines.Build(current, session.StateHash, key, status, pick);
            string takeover = HudLines.Takeover(lostPlayer, alive, key);
            if (takeover != null)
                hud += "\n" + takeover;
            string respawn = HudLines.Respawn(current);
            if (respawn != null)
                hud += "\n" + respawn;
            if (alive && status.HasValue && !session.IsFinished)
            {
                UnitStatus value = status.Value;
                if (!value.HasWeapon) hud += "\nNo weapon";
                else if (value.Reloading) hud += $"\nReloading: {value.WaitTicksLeft} ticks left";
                else if (value.WaitTicksLeft > 0) hud += $"\nNext shot in {value.WaitTicksLeft} ticks";
                else hud += "\nReady to fire";
            }
            if (session.IsFinished)
                hud += $"\nMatch over: team {session.Winner} wins; stop play and restart from the MP menu";
        }

        private void OnGUI()
        {
            if (session == null)
                return;
            style = style ?? new GUIStyle(GUI.skin.box) { fontSize = 20, alignment = TextAnchor.UpperLeft, wordWrap = true };
            float width = Mathf.Max(1f, Mathf.Min(720f, Screen.width - 24f));
            // One spare line: CalcHeight came out short once the Blocking line was added and the last line was cut off.
            float height = style.CalcHeight(new GUIContent(hud), width) + style.lineHeight;
            GUI.Box(new Rect(12, 12, width, height), hud, style);
        }
#endif
    }
}
