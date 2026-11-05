using System.IO;
using UnityEngine;
using WarCore.Presentation;

namespace WarCore.Unity
{
    // Editor menu entry: M2Host hands over before it creates a session.
    public sealed class MpHost : MonoBehaviour
    {
        public const string ModeKey = "WarCore.MP.Mode";

#if UNITY_EDITOR
        private SimSession session;
        private EntityViews views;
        private PointViews points;
        private ShotLines shots;
        private IsoCamera isoCamera;
        private readonly IsoCameraInput cameraInput = new IsoCameraInput();
        private readonly PlayerInputMapper mapper = new PlayerInputMapper();
        private readonly LivePlayerInput liveInput = new LivePlayerInput();
        private ulong? lastControlledKey;
        private bool lostPlayer;
        private string hud = "";
        private GUIStyle style;

        private void Awake()
        {
            UnityEditor.SessionState.EraseBool(ModeKey);
            byte[] bytes = File.ReadAllBytes(Path.Combine(Application.streamingAssetsPath, M2Host.M7MapPath));
            session = SimSession.Create(DemoRecipes.MpDemo(), bytes);
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
            lastControlledKey = session.ControlledKey;
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
            float ix = focused ? (Input.GetKey(KeyCode.D) ? 1 : 0) - (Input.GetKey(KeyCode.A) ? 1 : 0) : 0;
            float iy = focused ? (Input.GetKey(KeyCode.W) ? 1 : 0) - (Input.GetKey(KeyCode.S) ? 1 : 0) : 0;
            Vector3 mouse = Input.mousePosition;
            PlayerInput player = mapper.Map(isoCamera, ix, iy, mouse.x, mouse.y,
                Screen.width, Screen.height, MouseInWindow(mouse), focused && Input.GetMouseButton(0));
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
            bool alive = key.HasValue && current.TryGet(key.Value, out ViewEntity controlled) && controlled.Alive;
            // Advance can span both the death tick and the fallback tick. The old corpse
            // is still in Current even when the newly controlled soldier is already alive.
            if (!alive || (lastControlledKey.HasValue && key != lastControlledKey
                && current.TryGet(lastControlledKey.Value, out ViewEntity old) && !old.Alive))
                lostPlayer = true;
            lastControlledKey = key;

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
            if (lostPlayer)
                hud += alive ? $"\nDown - now controlling #{key.Value}" : "\nDown";
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
            float height = style.CalcHeight(new GUIContent(hud), width);
            GUI.Box(new Rect(12, 12, width, height), hud, style);
        }
#endif
    }
}
