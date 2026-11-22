using System.Collections.Generic;
using UnityEngine;
using WarCore.Presentation;

namespace WarCore.Unity
{
    // SHOW-1: placeholder soldiers. One "Soldier <key>" per unit of the frame: a capsule body in the team colour, a head, and
    // a gun (a unit with a weapon) or a blade (a unit without one) pointing where the unit last fired or moved. Above the head
    // a cube marks a pinned (yellow) or broken (red) unit and a diamond a squad leader; a thin line ties a squad member to
    // its leader. The controlled unit stands in a white ring. Layer cut and silhouettes as EntityViews.
    // Motion, all of it drawn only (the simulation never reads it): a walking unit bobs and leans, a fleeing one leans further,
    // a pinned one ducks, a shooter's gun kicks, a unit that lost health flashes white and staggers, a dead one falls on its
    // back and turns grey. dt is the real time since the last frame; with dt <= 0 every pose jumps to its end (pictures, tests).
    // PLACEHOLDER art: every size, speed and colour here is a stand-in.
    public sealed class ShowcaseUnits
    {
        public const string Prefix = "Soldier ", RingName = "Player ring";
        public const float BodyHeight = 1.7f, BodyWidth = 0.5f;
        public const float DuckScale = 0.62f, FallSeconds = 0.35f, FlashSeconds = 0.09f;
        private const int AimTicks = 20, RingSegments = 24;
        private const float RingRadius = 0.7f, ShooterReach = 0.6f, WalkSpeed = 0.5f;

        private sealed class Body
        {
            public GameObject Root, Tool, Shield, NerveMark, LeaderMark, Silhouette;
            public Renderer Torso, Head, Nerve;
            public LineRenderer Link;
            public Material Colour;
            public Vector3 ToolAt;
            public float Yaw, ShownYaw, Health = -1f, Flash, Stagger, StaggerSide = 1f, Kick, Duck = 1f, Lean, Phase, Fall, FallYaw;
            public long AimUntil = -1;
            public bool Dead, Blocking;
            public int Team;
        }

        private readonly Transform parent;
        private readonly SimSession session;
        private readonly Dictionary<ulong, Body> bodies = new Dictionary<ulong, Body>();
        private readonly List<ulong> removed = new List<ulong>();
        private readonly Material[] team, link, silhouette;
        private readonly Material skin, dark, steel, dead, pinned, broken, leader, white;
        private readonly LineRenderer ring;
        private readonly float[] stanceScales;

        // session: asked once per unit whether it carries a weapon. colors: 0xRRGGBB per team, team 1 first. stanceScales
        // (SHOW-2): how tall each stance level draws as a share of standing (ShowcaseStage.StanceScales); null: all standing.
        public ShowcaseUnits(Transform parent, SimSession session, IReadOnlyList<int> colors, float[] stanceScales = null)
        {
            this.parent = parent;
            this.session = session;
            this.stanceScales = stanceScales ?? new[] { 1f };
            team = new Material[colors.Count];
            link = new Material[colors.Count];
            silhouette = new Material[colors.Count];
            for (int i = 0; i < team.Length; i++)
            {
                Color c = ShowcaseScene.Rgb(colors[i]);
                team[i] = ShowcaseScene.Lit(c);
                link[i] = ShowcaseScene.Flat(Color.Lerp(c, Color.white, 0.45f));
                Shader see = Shader.Find(EntityViews.SilhouetteShader);
                silhouette[i] = see == null ? ShowcaseScene.Flat(c) : new Material(see) { color = new Color(c.r * 0.7f, c.g * 0.7f, c.b * 0.7f, 0.6f) };
            }
            skin = ShowcaseScene.Lit(new Color(0.85f, 0.69f, 0.55f));
            dark = ShowcaseScene.Lit(new Color(0.15f, 0.16f, 0.18f));
            steel = ShowcaseScene.Lit(new Color(0.78f, 0.8f, 0.84f));
            dead = ShowcaseScene.Lit(new Color(0.35f, 0.35f, 0.35f));
            pinned = ShowcaseScene.Flat(new Color(1f, 0.85f, 0.1f));
            broken = ShowcaseScene.Flat(new Color(1f, 0.15f, 0.1f));
            leader = ShowcaseScene.Flat(Color.white);
            white = ShowcaseScene.Flat(Color.white);
            ring = new GameObject(RingName).AddComponent<LineRenderer>();
            ring.transform.SetParent(parent, false);
            ring.useWorldSpace = true;
            ring.loop = true;
            ring.widthMultiplier = 0.08f;
            ring.positionCount = RingSegments;
            ring.sharedMaterial = white;
            ring.enabled = false;
        }

        public int Count => bodies.Count;

        // A shot or a projectile of that team left (x0, z0) toward (x1, z1) on the frame's tick: the living unit of the team
        // nearest that spot (within ShooterReach) turns its gun that way and takes the recoil. Returns its key, or null.
        public ulong? Fired(FrameView frame, float x0, float z0, float x1, float z1, int unitTeam)
        {
            float dx = x1 - x0, dz = z1 - z0;
            if (dx * dx + dz * dz < 1e-6f) return null;
            float best = ShooterReach * ShooterReach;
            ulong? shooter = null;
            foreach (ViewEntity e in frame.Entities)
            {
                if (!e.Alive || e.Team != unitTeam) continue;
                float d = (e.X - x0) * (e.X - x0) + (e.Z - z0) * (e.Z - z0);
                if (d <= best) { best = d; shooter = e.Key; }
            }
            if (!shooter.HasValue || !bodies.TryGetValue(shooter.Value, out Body body)) return shooter;
            body.Yaw = Mathf.Atan2(dx, dz) * Mathf.Rad2Deg;
            body.AimUntil = frame.Tick + AimTicks;
            body.Kick = 1f;
            return shooter;
        }

        // The unit's state at the committed tick: it raises or lowers its shield, and a drop in health makes it flash and
        // stagger. Returns true when the health dropped since the last call (unknown keys: false).
        public bool SetStatus(ulong key, bool blocking, float health)
        {
            if (!bodies.TryGetValue(key, out Body body)) return false;
            if (body.Blocking != blocking)
            {
                body.Blocking = blocking;
                body.Shield.SetActive(blocking && !body.Dead);
            }
            bool hit = body.Health >= 0f && health < body.Health - 0.01f;
            body.Health = health;
            if (hit)
            {
                body.Flash = FlashSeconds;
                body.Stagger = 1f;
                body.StaggerSide = -body.StaggerSide;
            }
            return hit;
        }

        // Whether the unit's body is drawn white this frame (it was just hit or just died).
        public bool Flashing(ulong key) => bodies.TryGetValue(key, out Body body) && body.Flash > 0f;

        // player: the controlled unit's key; aim: the point it aims at (the controlled unit faces it). links: draw the squad lines.
        public void Apply(FrameView previous, FrameView current, float alpha, LayerCutResult cut, ICollection<ulong> covered,
            ulong? player, Vector3? aim, bool links, float dt = 0f)
        {
            bool still = dt <= 0f;
            removed.Clear();
            foreach (var pair in bodies)
                if (!current.TryGet(pair.Key, out _)) removed.Add(pair.Key);
            foreach (ulong key in removed)
            {
                ShowcaseScene.Kill(bodies[key].Root);
                if (bodies[key].Silhouette != null) ShowcaseScene.Kill(bodies[key].Silhouette);
                bodies.Remove(key);
            }
            FrameView shown = ShowcaseScene.Blend(previous, current, alpha);
            bool ringOn = false;
            foreach (ViewEntity entity in shown.Entities)
            {
                if (!bodies.TryGetValue(entity.Key, out Body body))
                {
                    body = Make(entity);
                    bodies.Add(entity.Key, body);
                }
                bool isPlayer = player.HasValue && player.Value == entity.Key;
                if (entity.Alive)
                {
                    if (isPlayer && aim.HasValue)
                    {
                        float ax = aim.Value.x - entity.X, az = aim.Value.z - entity.Z;
                        if (ax * ax + az * az > 1e-4f) body.Yaw = Mathf.Atan2(ax, az) * Mathf.Rad2Deg;
                    }
                    else if (current.Tick >= body.AimUntil && previous.TryGet(entity.Key, out ViewEntity old) && current.TryGet(entity.Key, out ViewEntity now))
                    {
                        float mx = now.X - old.X, mz = now.Z - old.Z;
                        if (mx * mx + mz * mz > 1e-6f) body.Yaw = Mathf.Atan2(mx, mz) * Mathf.Rad2Deg;
                    }
                }
                if (!entity.Alive && !body.Dead) LieDown(body, entity.Key);
                float speed = 0f;
                if (entity.Alive && previous.TryGet(entity.Key, out ViewEntity was) && current.TryGet(entity.Key, out ViewEntity isNow))
                    speed = Mathf.Sqrt((isNow.X - was.X) * (isNow.X - was.X) + (isNow.Z - was.Z) * (isNow.Z - was.Z)) * SimSession.TickRate;
                Pose(body, entity, speed, dt, still);
                bool visible = cut == null || !cut.IsHidden(entity);
                if (body.Root.activeSelf != visible) body.Root.SetActive(visible);
                if (!body.Dead)
                {
                    Marks(body, entity);
                    Link(body, entity, shown, links && visible);
                }
                Silhouette(body, entity, visible && covered != null && covered.Contains(entity.Key));
                if (isPlayer && entity.Alive && visible)
                {
                    ringOn = true;
                    for (int k = 0; k < RingSegments; k++)
                    {
                        float angle = k * 2 * Mathf.PI / RingSegments;
                        ring.SetPosition(k, new Vector3(entity.X + Mathf.Cos(angle) * RingRadius, entity.Y + 0.06f, entity.Z + Mathf.Sin(angle) * RingRadius));
                    }
                }
            }
            if (ring.enabled != ringOn) ring.enabled = ringOn;
        }

        private Body Make(ViewEntity entity)
        {
            var body = new Body { Team = entity.Team, Root = new GameObject(Prefix + entity.Key) };
            body.Root.transform.SetParent(parent, false);
            Material colour = body.Colour = team[Index(entity.Team)];
            body.Torso = Part(body.Root, PrimitiveType.Capsule, "Body", new Vector3(0f, BodyHeight / 2, 0f), new Vector3(BodyWidth, BodyHeight / 2, BodyWidth), colour);
            body.Head = Part(body.Root, PrimitiveType.Sphere, "Head", new Vector3(0f, BodyHeight - 0.12f, 0f), new Vector3(0.3f, 0.3f, 0.3f), skin);
            bool armed = session != null && session.TryUnitStatus(entity.Key, out UnitStatus status) && status.HasWeapon;
            // A gun is short, dark and level; a blade is long, bright and tilted up.
            body.Tool = armed
                ? Part(body.Root, PrimitiveType.Cube, "Gun", new Vector3(0.2f, 1.25f, 0.45f), new Vector3(0.08f, 0.1f, 0.9f), dark).gameObject
                : Part(body.Root, PrimitiveType.Cube, "Blade", new Vector3(0.28f, 1.3f, 0.5f), new Vector3(0.05f, 0.1f, 1.1f), steel).gameObject;
            if (!armed) body.Tool.transform.localRotation = Quaternion.Euler(-35f, 0f, 0f);
            body.ToolAt = body.Tool.transform.localPosition;
            body.Shield = Part(body.Root, PrimitiveType.Cube, "Shield", new Vector3(-0.1f, 1.05f, 0.42f), new Vector3(0.7f, 0.9f, 0.08f), steel).gameObject;
            body.Shield.SetActive(false);
            body.Nerve = Part(body.Root, PrimitiveType.Cube, "Nerve", new Vector3(0f, BodyHeight + 0.35f, 0f), new Vector3(0.28f, 0.28f, 0.28f), pinned);
            body.NerveMark = body.Nerve.gameObject;
            body.NerveMark.SetActive(false);
            body.LeaderMark = Part(body.Root, PrimitiveType.Cube, "Leader", new Vector3(0f, BodyHeight + 0.8f, 0f), new Vector3(0.3f, 0.3f, 0.3f), leader).gameObject;
            body.LeaderMark.transform.localRotation = Quaternion.Euler(45f, 0f, 45f);
            body.LeaderMark.SetActive(false);
            body.Link = new GameObject("Squad line").AddComponent<LineRenderer>();
            body.Link.transform.SetParent(body.Root.transform, false);
            body.Link.useWorldSpace = true;
            body.Link.widthMultiplier = 0.05f;
            body.Link.positionCount = 2;
            body.Link.sharedMaterial = link[Index(entity.Team)];
            body.Link.enabled = false;
            return body;
        }

        private int Index(int unitTeam) => unitTeam <= 0 ? 0 : (unitTeam - 1) % team.Length;

        private static Renderer Part(GameObject root, PrimitiveType shape, string name, Vector3 at, Vector3 scale, Material material)
        {
            GameObject part = GameObject.CreatePrimitive(shape);
            part.name = name;
            part.transform.SetParent(root.transform, false);
            part.transform.localPosition = at;
            part.transform.localScale = scale;
            Renderer drawn = part.GetComponent<Renderer>();
            drawn.sharedMaterial = material;
            return drawn;
        }

        // The unit's place, turn, lean, duck, recoil, stagger and fall for this frame.
        private void Pose(Body body, ViewEntity entity, float speed, float dt, bool still)
        {
            Transform t = body.Root.transform;
            body.Flash = still ? 0f : Mathf.Max(0f, body.Flash - dt);
            Material torso = body.Flash > 0f ? white : body.Dead ? dead : body.Colour;
            if (body.Torso.sharedMaterial != torso) body.Torso.sharedMaterial = torso;
            if (body.Dead)
            {
                body.Fall = still ? 1f : Mathf.Min(1f, body.Fall + dt / FallSeconds);
                float down = 1f - (1f - body.Fall) * (1f - body.Fall);
                t.position = new Vector3(entity.X, entity.Y + down * BodyWidth / 2, entity.Z);
                t.rotation = Quaternion.Euler(-90f * down, body.FallYaw, 0f);
                t.localScale = Vector3.one;
                return;
            }
            bool walking = speed > WalkSpeed;
            float stance = entity.Stance >= 0 && entity.Stance < stanceScales.Length ? stanceScales[entity.Stance] : 1f;
            float duck = Mathf.Min(stance, entity.Nerve == 1 ? DuckScale : 1f), lean = !walking ? 0f : entity.Nerve >= 2 ? 20f : 8f;
            body.Duck = still ? duck : Mathf.MoveTowards(body.Duck, duck, dt * 3f);
            body.Lean = still ? lean : Mathf.MoveTowards(body.Lean, lean, dt * 60f);
            body.ShownYaw = still ? body.Yaw : Mathf.MoveTowardsAngle(body.ShownYaw, body.Yaw, dt * 720f);
            body.Kick = still ? 0f : Mathf.Max(0f, body.Kick - dt * 10f);
            body.Stagger = still ? 0f : Mathf.Max(0f, body.Stagger - dt * 5f);
            if (!still && walking) body.Phase += dt * speed * 2.4f;
            float bob = walking && !still ? Mathf.Abs(Mathf.Sin(body.Phase)) * 0.07f : 0f;
            t.position = new Vector3(entity.X, entity.Y + bob, entity.Z);
            t.rotation = Quaternion.Euler(body.Lean - body.Kick * 5f, body.ShownYaw, body.StaggerSide * body.Stagger * 16f);
            t.localScale = new Vector3(1f, body.Duck, 1f);
            body.Tool.transform.localPosition = body.ToolAt - new Vector3(0f, 0f, body.Kick * 0.15f);
        }

        private void LieDown(Body body, ulong key)
        {
            body.Dead = true;
            body.Flash = FlashSeconds;
            // Onto its back, turned a little to one side or the other so that a heap of bodies does not look combed.
            body.FallYaw = body.ShownYaw + ((key * 37) % 80) - 40f;
            body.Head.sharedMaterial = dead;
            body.Tool.SetActive(false);
            body.Shield.SetActive(false);
            body.NerveMark.SetActive(false);
            body.LeaderMark.SetActive(false);
            body.Link.enabled = false;
        }

        private void Marks(Body body, ViewEntity entity)
        {
            bool nerve = entity.Nerve != 0;
            if (body.NerveMark.activeSelf != nerve) body.NerveMark.SetActive(nerve);
            if (nerve) body.Nerve.sharedMaterial = entity.Nerve >= 2 ? broken : pinned;
            bool leads = entity.Band != 0 && entity.Band == entity.Key;
            if (body.LeaderMark.activeSelf != leads) body.LeaderMark.SetActive(leads);
        }

        private void Link(Body body, ViewEntity entity, FrameView shown, bool on)
        {
            on = on && entity.Band != 0 && entity.Band != entity.Key && shown.TryGet(entity.Band, out ViewEntity head) && head.Alive;
            if (body.Link.enabled != on) body.Link.enabled = on;
            if (!on) return;
            shown.TryGet(entity.Band, out ViewEntity to);
            body.Link.SetPosition(0, new Vector3(entity.X, entity.Y + 0.15f, entity.Z));
            body.Link.SetPosition(1, new Vector3(to.X, to.Y + 0.15f, to.Z));
        }

        // SEE-1: a flat see-through copy of the body that no wall hides, made the first time the unit is covered.
        private void Silhouette(Body body, ViewEntity entity, bool on)
        {
            if (body.Silhouette == null)
            {
                if (!on) return;
                body.Silhouette = GameObject.CreatePrimitive(PrimitiveType.Cube);
                body.Silhouette.name = EntityViews.SilhouettePrefix + entity.Key;
                body.Silhouette.transform.SetParent(parent, false);
                body.Silhouette.transform.localScale = new Vector3(BodyWidth, BodyHeight, BodyWidth);
                body.Silhouette.GetComponent<Renderer>().sharedMaterial = silhouette[Index(entity.Team)];
            }
            body.Silhouette.transform.position = new Vector3(entity.X, entity.Y + BodyHeight / 2, entity.Z);
            if (body.Silhouette.activeSelf != on) body.Silhouette.SetActive(on);
        }
    }
}
