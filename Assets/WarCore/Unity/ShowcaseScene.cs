using System;
using System.Collections.Generic;
using UnityEngine;
using WarCore.Presentation;

namespace WarCore.Unity
{
    // SHOW-1: one stage of the showcase built at run time: the session, the map (ground, walls, plates), the control points
    // with a flag each, the soldiers, the gunfire and the drawn-only effects. No input and no HUD: the host owns those, and
    // the EditMode tests build a scene without one. Everything it creates hangs under Root and MapRoot and goes with Dispose.
    // Feel, all of it drawn only: the camera runs a little ahead of the player toward the mouse and eases after it, and it
    // shakes when the player fires, is hit, or something lands or bursts close by. With dt <= 0 the camera sits on the
    // player and nothing shakes (pictures, tests).
    public sealed class ShowcaseScene : IDisposable
    {
        public const string FlagPrefix = "Flag ", AimName = "Aim ring";
        public const float LeadShare = 0.3f, HurtSeconds = 0.35f, KillSeconds = 0.6f;
        private const int AimSegments = 20, MiniPixels = 192;
        private const float FollowRate = 9f, TraumaFade = 1.6f, ShakeMetres = 0.55f, NearMetres = 7f;

        private readonly List<ViewProjectile> flying = new List<ViewProjectile>();
        private readonly List<ViewImpact> landed = new List<ViewImpact>();
        private readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        private readonly Renderer[] flags;
        private readonly Material[] flagColours;
        private readonly LineRenderer aimRing;
        private readonly PlayerTracker tracker;
        private HashSet<ulong> covered = new HashSet<ulong>();
        private long statusTick = -1;
        private float focusX, focusZ, trauma, clock;
        private bool focused;
        private int shotsSeen, killsSeen;
        private Vector3 playerAt;

        public ShowcaseStage Stage { get; }
        public SimSession Session { get; }
        public ShowcaseColors Colors { get; }
        public GameObject MapRoot { get; }
        public GameObject Root { get; }
        public IsoCamera Camera { get; }
        public ShowcaseUnits Units { get; }
        public ShowcaseShots Shots { get; }
        public ShowcaseFx Fx { get; }
        // SCALE-2a: the session's body sizes (BodySizes.Of), or null for a world without a body (SHOW-1's fixed sizes).
        public BodySizes Sizes { get; }
        // The map from above, one colour per cell (walls, ground or its surfaces): x to the right, z up.
        public Texture2D MiniMap { get; }
        // How far the picture is pushed sideways and up this frame, in metres on the camera's own axes.
        public Vector2 Shake { get; private set; }
        // Seconds left of "the player was just hit" and "the player just killed" (the host draws both).
        public float HurtLeft { get; private set; }
        public float KillLeft { get; private set; }
        public int PlayerKills => Shots.PlayerKills;
        // Where the controlled unit was last drawn, and how fast it moves (metres a second).
        public Vector3 PlayerAt => playerAt;
        public float PlayerSpeed { get; private set; }
        public PointViews Points { get; }
        public PlateViews Plates { get; }
        public ViewGround Ground { get; }
        public LayerCutResult Cut { get; private set; }
        public int InFlight => flying.Count;
        public bool LostPlayer { get; private set; }
        public bool PlayerAlive { get; private set; }
        public bool SquadLines { get; set; } = true;

        public ShowcaseScene(ShowcaseStage stage)
        {
            Stage = stage ?? throw new ArgumentNullException(nameof(stage));
            Session = stage.Start();
            Sizes = BodySizes.Of(Session);
            ViewMap map = Session.Map;
            Colors = stage.Colors(map);
            Ground = ViewGround.For(map);
            Material wall = Own(Lit(Rgb(Colors.Wall)));
            Material ground = Own(Lit(map.SurfaceCount == 1 ? Rgb(Colors.Surface[0]) : Rgb(Colors.Ground)));
            MapRoot = MapView.Build(map, ground, wall);
            if (map.HasHeightField && map.SurfaceCount > 1) PaintGround(map, ground);
            Plates = new PlateViews(MapRoot.transform, map.Plates, map, Own(Lit(Rgb(Colors.Floor))), wall);
            Root = new GameObject("Showcase " + stage.Id);
            var teams = new Color[Colors.Team.Count + 1];
            teams[0] = Rgb(Colors.Neutral);
            for (int i = 0; i < Colors.Team.Count; i++) teams[i + 1] = Rgb(Colors.Team[i]);
            Points = new PointViews(Root.transform, Ground, teams);
            Units = new ShowcaseUnits(Root.transform, Session, Colors.Team, stage.StanceScales());
            Fx = new ShowcaseFx(Root.transform);
            Shots = new ShowcaseShots(Root.transform, Colors.Team, Ground, Fx, Sizes);
            MiniMap = Own(Mini(map));
            Camera = new IsoCamera(stage.Zoom, map.FloorY);
            tracker = new PlayerTracker(Session.ControlledKey);

            flagColours = new Material[teams.Length];
            for (int i = 0; i < teams.Length; i++) flagColours[i] = Own(Flat(teams[i]));
            Material pole = Own(Lit(new Color(0.9f, 0.9f, 0.9f)));
            ReadOnlySpan<ViewPoint> points = Session.Current.Points;
            flags = new Renderer[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                float y = Ground == null ? map.FloorY : Ground.HeightAt(points[i].X, points[i].Z, map.FloorY);
                var at = new Vector3(points[i].X, y, points[i].Z);
                var holder = new GameObject(FlagPrefix + points[i].Id);
                holder.transform.SetParent(Root.transform, false);
                holder.transform.position = at;
                Box(holder.transform, "Pole", new Vector3(0f, 2.5f, 0f), new Vector3(0.12f, 5f, 0.12f), pole);
                flags[i] = Box(holder.transform, "Cloth", new Vector3(0.75f, 4.4f, 0f), new Vector3(1.4f, 0.9f, 0.06f), flagColours[0]);
            }
            aimRing = new GameObject(AimName).AddComponent<LineRenderer>();
            aimRing.transform.SetParent(Root.transform, false);
            aimRing.useWorldSpace = true;
            aimRing.loop = true;
            aimRing.widthMultiplier = 0.06f;
            aimRing.positionCount = AimSegments;
            aimRing.sharedMaterial = Own(Flat(Color.white));
            aimRing.enabled = false;
            Show(0f, null);
        }

        // FrameView.Interpolate takes 0 <= alpha < 1; a picture (alpha 1) shows the committed frame itself.
        public static FrameView Blend(FrameView previous, FrameView current, float alpha) =>
            alpha >= 1f ? current : FrameView.Interpolate(previous, current, Mathf.Max(0f, alpha));

        public static Color Rgb(int rgb) => new Color((rgb >> 16 & 255) / 255f, (rgb >> 8 & 255) / 255f, (rgb & 255) / 255f);

        // A shaded material (the built-in Standard shader); a flat one where a part must read at any angle.
        public static Material Lit(Color color) => new Material(Shader.Find("Standard") ?? Shader.Find("Unlit/Color")) { color = color };
        public static Material Flat(Color color) => new Material(Shader.Find("Unlit/Color")) { color = color };

        // Destroy that also works outside Play mode (the EditMode tests).
        public static void Kill(UnityEngine.Object item)
        {
            if (item == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(item);
            else UnityEngine.Object.DestroyImmediate(item);
        }

        private T Own<T>(T item) where T : UnityEngine.Object { owned.Add(item); return item; }

        private static Renderer Box(Transform parent, string name, Vector3 at, Vector3 scale, Material material)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent, false);
            box.transform.localPosition = at;
            box.transform.localScale = scale;
            Renderer drawn = box.GetComponent<Renderer>();
            drawn.sharedMaterial = material;
            return drawn;
        }

        // One texel per navigation cell in its surface's colour, laid over the height-field mesh by its x and z.
        private void PaintGround(ViewMap map, Material ground)
        {
            Transform g = MapRoot.transform.Find("Ground");
            MeshFilter filter = g == null ? null : g.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) return;
            var texture = Own(new Texture2D(map.Width, map.Depth, TextureFormat.RGB24, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp });
            for (int z = 0; z < map.Depth; z++)
                for (int x = 0; x < map.Width; x++)
                {
                    Color c = Rgb(Colors.Surface[map.SurfaceIndex(x, z)]);
                    // A faint checker so that slopes and distances read on a single colour.
                    texture.SetPixel(x, z, (x + z) % 2 == 0 ? c : c * 0.94f);
                }
            texture.Apply();
            Vector3[] vertices = filter.sharedMesh.vertices;
            var uv = new Vector2[vertices.Length];
            for (int v = 0; v < uv.Length; v++)
                uv[v] = new Vector2((vertices[v].x - map.MinX) / map.Width, (vertices[v].z - map.MinZ) / map.Depth);
            filter.sharedMesh.uv = uv;
            ground.color = Color.white;
            ground.mainTexture = texture;
        }

        // The map from above for the HUD: the longer side MiniPixels wide, walls over ground (or over the surface colours).
        private Texture2D Mini(ViewMap map)
        {
            float scale = (float)MiniPixels / Mathf.Max(map.Width, map.Depth);
            int w = Mathf.Max(1, Mathf.RoundToInt(map.Width * scale)), h = Mathf.Max(1, Mathf.RoundToInt(map.Depth * scale));
            var texture = new Texture2D(w, h, TextureFormat.RGB24, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            Color plain = Rgb(map.SurfaceCount == 1 ? Colors.Surface[0] : Colors.Ground) * 0.55f;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int cx = Mathf.Min(map.Width - 1, (int)(x / scale)), cz = Mathf.Min(map.Depth - 1, (int)(y / scale));
                    texture.SetPixel(x, y, map.SurfaceCount > 1 ? Rgb(Colors.Surface[map.SurfaceIndex(cx, cz)]) * 0.6f : plain);
                }
            Color wall = Rgb(Colors.Wall);
            foreach (ViewWall box in map.Walls)
            {
                int x0 = Mathf.FloorToInt((box.CenterX - box.SizeX / 2 - map.MinX) * scale), x1 = Mathf.CeilToInt((box.CenterX + box.SizeX / 2 - map.MinX) * scale);
                int y0 = Mathf.FloorToInt((box.CenterZ - box.SizeZ / 2 - map.MinZ) * scale), y1 = Mathf.CeilToInt((box.CenterZ + box.SizeZ / 2 - map.MinZ) * scale);
                for (int y = Mathf.Max(0, y0); y < Mathf.Min(h, Mathf.Max(y1, y0 + 1)); y++)
                    for (int x = Mathf.Max(0, x0); x < Mathf.Min(w, Mathf.Max(x1, x0 + 1)); x++) texture.SetPixel(x, y, wall);
            }
            texture.Apply();
            return texture;
        }

        // Shake the picture: 0 is nothing, 1 the most there is. Adds up and fades by itself.
        public void Jolt(float amount) => trauma = Mathf.Clamp01(trauma + amount);

        // The grenade preview (drawn only, hurts nobody): from the controlled unit's hand to the aimed point. False when
        // there is no living controlled unit or one is already in the air. The hand: the body's muzzle with sizes (the
        // simulation has no throw yet, so no release point to read), else SHOW-1's fixed height.
        public bool ThrowPreview(Vector3 at) => Throw(at, true);

        // SHOW-2: the drawn grenade of a real throw (ShowcaseActions queued its blast); it lands without a burst, and the
        // simulation's blast is drawn when it goes off (Stepped).
        public bool ThrowReal(Vector3 at) => Throw(at, false);

        private bool Throw(Vector3 at, bool burst)
        {
            ulong? key = Session.ControlledKey;
            if (!key.HasValue || !Session.Current.TryGet(key.Value, out ViewEntity me) || !me.Alive) return false;
            return Fx.Throw(new Vector3(me.X, me.Y + HandHeight(me), me.Z), at, burst);
        }

        public float HandHeight(ViewEntity me) => Sizes == null ? ShowcaseLegacySizes.ThrowHeight : Sizes.Muzzle(me);

        // Where chips fly off a hit unit, over its feet.
        public float ChipsHeight(ViewEntity e) => Sizes == null ? ShowcaseLegacySizes.ChipsHeight : DrawnParts.ChipsHeight(Sizes.Top(e));

        // Empty when the drawn map, the simulation and the control points agree (TerrainHost.SelfCheck).
        public string SelfCheck() => TerrainHost.SelfCheck(Session.Map, Session, Session.Current, MapRoot.transform, Root.transform);

        // After the session moved on by one tick or more: this tick's shots, projectiles and landings.
        public void Stepped()
        {
            FrameView frame = Session.Current;
            ulong? key = Session.ControlledKey;
            Shots.Add(frame, Units, Session.Map.FloorY, key);
            flying.Clear();
            landed.Clear();
            ViewProjectiles.Read(Session, flying, landed);
            Shots.AddProjectiles(frame, Units, flying, landed, key);
            if (Shots.PlayerShots != shotsSeen) Jolt(0.12f);
            if (Shots.PlayerKills != killsSeen) KillLeft = KillSeconds;
            shotsSeen = Shots.PlayerShots;
            killsSeen = Shots.PlayerKills;
            foreach (Vector3 at in Shots.Landed)
                if ((at - playerAt).sqrMagnitude < NearMetres * NearMetres) Jolt(0.07f);
            // SHOW-2: the blasts of this tick (a blast of a tick stepped over within one frame is not drawn).
            foreach (ViewBlast blast in frame.Blasts) DrawBlast(blast);
        }

        // HC-4: one real blast, drawn from its own radii, and the camera shake it gives the player.
        public void DrawBlast(ViewBlast blast)
        {
            var at = new Vector3(blast.X, blast.Y, blast.Z);
            Fx.Explode(at, BlastLook.Of(blast.Inner, blast.Radius));
            Jolt((at - playerAt).sqrMagnitude < 4f * blast.Radius * blast.Radius ? 0.5f : 0.15f);
        }

        // Every frame. aim: the point the player aims at, or null (no mouse over the picture). dt: the real time since the
        // last frame. lead: where the mouse is on the picture, -1..1 from its middle on both axes.
        public void Show(float alpha, Vector3? aim, float dt = 0f, Vector2 lead = default)
        {
            FrameView previous = Session.Previous, current = Session.Current;
            ulong? key = Session.ControlledKey;
            bool wasAlive = PlayerAlive;
            LostPlayer = tracker.Update(current, key);
            PlayerAlive = tracker.Alive;
            if (wasAlive && !PlayerAlive && dt > 0f)
            {
                HurtLeft = 2f * HurtSeconds;
                Jolt(0.6f);
            }
            FrameView shown = Blend(previous, current, alpha);
            ViewEntity? me = key.HasValue && shown.TryGet(key.Value, out ViewEntity found) ? found : (ViewEntity?)null;
            if (me.HasValue)
            {
                playerAt = new Vector3(me.Value.X, me.Value.Y, me.Value.Z);
                PlayerSpeed = me.Value.Alive && previous.TryGet(me.Value.Key, out ViewEntity before) && current.TryGet(me.Value.Key, out ViewEntity after)
                    ? Mathf.Sqrt((after.X - before.X) * (after.X - before.X) + (after.Z - before.Z) * (after.Z - before.Z)) * SimSession.TickRate : 0f;
                float wantX = me.Value.X, wantZ = me.Value.Z;
                if (dt > 0f && me.Value.Alive)
                {
                    var ahead = Camera.MoveAxes(Mathf.Clamp(lead.x, -1f, 1f), Mathf.Clamp(lead.y, -1f, 1f));
                    wantX += ahead.X * Camera.Zoom * LeadShare;
                    wantZ += ahead.Z * Camera.Zoom * LeadShare;
                }
                float ease = dt > 0f && focused ? 1f - Mathf.Exp(-FollowRate * dt) : 1f;
                focusX += (wantX - focusX) * ease;
                focusZ += (wantZ - focusZ) * ease;
                focused = true;
                Camera.Follow(focusX, focusZ);
            }
            if (dt > 0f)
            {
                clock += dt;
                trauma = Mathf.Max(0f, trauma - TraumaFade * dt);
                HurtLeft = Mathf.Max(0f, HurtLeft - dt);
                KillLeft = Mathf.Max(0f, KillLeft - dt);
                float reach = trauma * trauma * ShakeMetres * Camera.Zoom / 16f;
                Shake = new Vector2((Mathf.PerlinNoise(clock * 28f, 0.3f) * 2f - 1f) * reach, (Mathf.PerlinNoise(0.7f, clock * 28f) * 2f - 1f) * reach);
            }
            else Shake = Vector2.zero;
            Cut = LayerCut.Run(Session.Map.Plates, Session.Map, me, Camera.Forward, LayerCut.SeeRadius, BodySizes.Of(Session));
            covered = Silhouettes.Run(Session.Map.Plates, Session.Map, Cut, me, shown.Entities, Camera.Forward, BodySizes.Of(Session));
            Plates.Apply(Cut);
            Points.Apply(current);
            for (int i = 0; i < flags.Length; i++)
                flags[i].sharedMaterial = flagColours[current.Points[i].Owner % flagColours.Length];
            Units.Apply(previous, current, alpha, Cut, covered, key, aim, SquadLines, dt);
            if (statusTick != current.Tick)
            {
                statusTick = current.Tick;
                foreach (ViewEntity e in current.Entities)
                {
                    if (!e.Alive || !Session.TryUnitStatus(e.Key, out UnitStatus status) || !Units.SetStatus(e.Key, status.Blocking, status.Health)) continue;
                    Fx.Chips(new Vector3(e.X, e.Y + ChipsHeight(e), e.Z), e.Y, 4, true);
                    if (key.HasValue && key.Value == e.Key)
                    {
                        HurtLeft = HurtSeconds;
                        Jolt(0.45f);
                    }
                }
            }
            Fx.Update(dt);
            foreach (Vector3 burst in Fx.Bursts)
                Jolt(Mathf.Lerp(0.9f, 0.25f, Mathf.Clamp01((burst - playerAt).magnitude / 30f)));
            Shots.Show(current.Tick, alpha, flying);
            bool ring = aim.HasValue && me.HasValue && me.Value.Alive;
            if (aimRing.enabled != ring) aimRing.enabled = ring;
            if (ring)
                for (int k = 0; k < AimSegments; k++)
                {
                    float angle = k * 2 * Mathf.PI / AimSegments;
                    aimRing.SetPosition(k, aim.Value + new Vector3(Mathf.Cos(angle) * 0.45f, 0.08f, Mathf.Sin(angle) * 0.45f));
                }
        }

        public void Dispose()
        {
            if (MapRoot != null)
                foreach (MeshFilter f in MapRoot.GetComponentsInChildren<MeshFilter>(true))
                    if (f.sharedMesh != null && (f.name == "Ground" && Session.Map.HasHeightField || f.name.StartsWith(PlateViews.Prefix, StringComparison.Ordinal)))
                        Kill(f.sharedMesh);
            Kill(MapRoot);
            Kill(Root);
            foreach (UnityEngine.Object item in owned) Kill(item);
            owned.Clear();
        }
    }
}
