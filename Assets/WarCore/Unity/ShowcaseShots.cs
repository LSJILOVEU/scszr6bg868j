using System.Collections.Generic;
using UnityEngine;
using WarCore.Presentation;

namespace WarCore.Unity
{
    // SHOW-1: placeholder gunfire. A shot that lands on the tick it is fired is a tracer line with a muzzle flash and a puff
    // where it ends (red on a kill); a projectile is a short streak that moves with the bullet, with a flash where it was fired
    // and a puff where it stopped; chips fly off where a shot lands (ShowcaseFx). Lines and puffs are pooled: nothing is
    // created once the pools are warm. It also counts what the controlled unit did (shots, kills) and lists where shots
    // landed on the last tick.
    public sealed class ShowcaseShots
    {
        public const string TracerName = "Tracer", BulletName = "Bullet", PuffName = "Puff";
        public const float MuzzleHeight = 1.4f;
        private const int TracerTicks = 4, FlashTicks = 2, PuffTicks = 8, MaxTracers = 400, MaxPuffs = 300;
        private const float Streak = 0.6f; // the drawn part of a projectile's last step

        private struct Timed<T> { public T Item; public long Born, Until; public float Size; }

        private readonly Transform parent;
        private readonly ViewGround ground;
        private readonly Material[] team;
        private readonly Material flash, dust, blood;
        private readonly List<Timed<LineRenderer>> tracers = new List<Timed<LineRenderer>>();
        private readonly List<LineRenderer> bullets = new List<LineRenderer>();
        private readonly List<Timed<GameObject>> puffs = new List<Timed<GameObject>>();
        private readonly List<(Vector3 At, int Team)> launched = new List<(Vector3, int)>(), launching = new List<(Vector3, int)>();
        private readonly List<Vector3> landedNow = new List<Vector3>(), firedNow = new List<Vector3>();
        private readonly ShowcaseFx fx;
        private int nextTracer, nextPuff;

        // Shots the controlled unit fired, and how many of them killed (instant shots only: a projectile's kill has no owner
        // in the view data).
        public int PlayerShots { get; private set; }
        public int PlayerKills { get; private set; }
        // Where shots ended on the last tick given to Add and AddProjectiles.
        public IReadOnlyList<Vector3> Landed => landedNow;
        // Where shots were fired from on that tick; MineFired: whether the controlled unit fired one of them.
        public IReadOnlyList<Vector3> Fired => firedNow;
        public bool MineFired { get; private set; }

        // colors: 0xRRGGBB per team, team 1 first. ground: the map's height field, or null on a flat map.
        // fx: where the chips go, or null for none.
        public ShowcaseShots(Transform parent, IReadOnlyList<int> colors, ViewGround ground, ShowcaseFx fx = null)
        {
            this.parent = parent;
            this.ground = ground;
            this.fx = fx;
            team = new Material[colors.Count];
            for (int i = 0; i < team.Length; i++)
                team[i] = ShowcaseScene.Flat(Color.Lerp(ShowcaseScene.Rgb(colors[i]), Color.white, 0.55f));
            flash = ShowcaseScene.Flat(new Color(1f, 0.83f, 0.35f));
            dust = ShowcaseScene.Flat(new Color(0.75f, 0.72f, 0.65f));
            blood = ShowcaseScene.Flat(new Color(0.85f, 0.1f, 0.1f));
        }

        public int LiveTracers(long tick)
        {
            int live = 0;
            foreach (var t in tracers) if (t.Until > tick) live++;
            return live;
        }

        public int LivePuffs(long tick)
        {
            int live = 0;
            foreach (var p in puffs) if (p.Until > tick) live++;
            return live;
        }

        public int ShownBullets
        {
            get
            {
                int shown = 0;
                foreach (LineRenderer b in bullets) if (b.enabled) shown++;
                return shown;
            }
        }

        private Material Team(int unitTeam) => team[unitTeam <= 0 ? 0 : (unitTeam - 1) % team.Length];

        private float Ground(float x, float z, float fallback) => ground == null ? fallback : ground.HeightAt(x, z, fallback);

        // The shots of the frame's tick. units turns the shooters and tells the height each shot leaves at; it may be null.
        // player: the controlled unit, whose shots and kills are counted.
        public void Add(FrameView frame, ShowcaseUnits units, float floorY, ulong? player = null)
        {
            landedNow.Clear();
            firedNow.Clear();
            MineFired = false;
            foreach (ViewFire fire in frame.Fires)
            {
                ulong? shooter = units?.Fired(frame, fire.X0, fire.Z0, fire.X1, fire.Z1, fire.Team);
                float feet = shooter.HasValue && frame.TryGet(shooter.Value, out ViewEntity who) ? who.Y : Ground(fire.X0, fire.Z0, floorY);
                var from = new Vector3(fire.X0, feet + MuzzleHeight, fire.Z0);
                // On a height field the shot ends chest-high over the ground there; elsewhere it stays level.
                float floorThere = Ground(fire.X1, fire.Z1, floorY);
                var to = new Vector3(fire.X1, ground == null ? from.y : floorThere + 1.1f, fire.Z1);
                Tracer(frame.Tick, from, to, Team(fire.Team), fire.Lethal ? 0.14f : 0.06f);
                Puff(frame.Tick, Tip(from, to), flash, 0.5f, FlashTicks);
                Puff(frame.Tick, to, fire.Lethal ? blood : dust, fire.Lethal ? 0.5f : 0.25f, PuffTicks);
                fx?.Chips(to, ground == null ? feet : floorThere, fire.Lethal ? 5 : 3, fire.Lethal);
                landedNow.Add(to);
                firedNow.Add(from);
                if (player.HasValue && shooter == player)
                {
                    MineFired = true;
                    PlayerShots++;
                    if (fire.Lethal) PlayerKills++;
                }
            }
        }

        // The end of the gun: a little way from the shooter's middle toward the target.
        private static Vector3 Tip(Vector3 from, Vector3 to)
        {
            var flat = new Vector3(to.x - from.x, 0f, to.z - from.z);
            return flat.sqrMagnitude < 1e-6f ? from : from + flat.normalized * 0.9f;
        }

        // The projectiles fired and landed on the frame's tick (SimSession.ReadProjectiles of that tick). A projectile has
        // no direction on the tick it is fired, so its shooter turns (and the flash at the gun's end shows) one tick later.
        public void AddProjectiles(FrameView frame, ShowcaseUnits units, List<ViewProjectile> flying, List<ViewImpact> landed, ulong? player = null)
        {
            launching.Clear();
            foreach (ViewProjectile p in flying)
            {
                var from = new Vector3(p.FromX, p.FromY, p.FromZ);
                if (p.Fresh)
                {
                    launching.Add((from, p.Team));
                    firedNow.Add(from);
                    continue;
                }
                if (!launched.Contains((from, p.Team))) continue;
                ulong? shooter = units?.Fired(frame, p.FromX, p.FromZ, p.X, p.Z, p.Team);
                Puff(frame.Tick, Tip(from, new Vector3(p.X, p.Y, p.Z)), flash, 0.5f, FlashTicks);
                if (player.HasValue && shooter == player)
                {
                    MineFired = true;
                    PlayerShots++;
                }
            }
            launched.Clear();
            launched.AddRange(launching);
            foreach (ViewImpact i in landed)
            {
                var at = new Vector3(i.X, i.Y, i.Z);
                Puff(frame.Tick, at, i.Damaged ? blood : dust, i.Damaged ? 0.5f : 0.25f, PuffTicks);
                fx?.Chips(at, Ground(i.X, i.Z, at.y - 1.2f), i.Damaged ? 5 : 3, i.Damaged);
                landedNow.Add(at);
            }
        }

        // Every frame: the streak of each projectile in flight alpha of the way through its last step, and the fading of
        // tracers and puffs at tick + alpha.
        public void Show(long tick, float alpha, List<ViewProjectile> flying)
        {
            int shown = 0;
            if (flying != null)
                foreach (ViewProjectile p in flying)
                {
                    if (p.Fresh) continue;
                    if (shown == bullets.Count) bullets.Add(Line(BulletName, flash, 0.09f));
                    LineRenderer line = bullets[shown++];
                    var from = new Vector3(p.FromX, p.FromY, p.FromZ);
                    var to = new Vector3(p.X, p.Y, p.Z);
                    Vector3 head = Vector3.Lerp(from, to, alpha);
                    line.SetPosition(0, head - (to - from) * Streak);
                    line.SetPosition(1, head);
                    line.sharedMaterial = Team(p.Team);
                    if (!line.enabled) line.enabled = true;
                }
            for (int i = shown; i < bullets.Count; i++)
                if (bullets[i].enabled) bullets[i].enabled = false;
            float now = tick + alpha;
            for (int i = 0; i < tracers.Count; i++)
            {
                Timed<LineRenderer> t = tracers[i];
                bool live = t.Until > tick;
                if (t.Item.enabled != live) t.Item.enabled = live;
                if (live) t.Item.widthMultiplier = t.Size * Mathf.Clamp01((t.Until - now) / (t.Until - t.Born));
            }
            for (int i = 0; i < puffs.Count; i++)
            {
                Timed<GameObject> p = puffs[i];
                bool live = p.Until > tick;
                if (p.Item.activeSelf != live) p.Item.SetActive(live);
                if (live)
                {
                    float size = p.Size * Mathf.Clamp01((p.Until - now) / (p.Until - p.Born));
                    p.Item.transform.localScale = new Vector3(size, size, size);
                }
            }
        }

        private LineRenderer Line(string name, Material material, float width)
        {
            var line = new GameObject(name).AddComponent<LineRenderer>();
            line.transform.SetParent(parent, false);
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.widthMultiplier = width;
            line.sharedMaterial = material;
            return line;
        }

        private void Tracer(long tick, Vector3 from, Vector3 to, Material material, float width)
        {
            int slot = Free(tracers, tick, MaxTracers, ref nextTracer);
            if (slot == tracers.Count) tracers.Add(new Timed<LineRenderer> { Item = Line(TracerName, material, width) });
            Timed<LineRenderer> t = tracers[slot];
            t.Born = tick; t.Until = tick + TracerTicks; t.Size = width;
            t.Item.sharedMaterial = material;
            t.Item.SetPosition(0, from);
            t.Item.SetPosition(1, to);
            tracers[slot] = t;
        }

        private void Puff(long tick, Vector3 at, Material material, float size, int ticks)
        {
            int slot = Free(puffs, tick, MaxPuffs, ref nextPuff);
            if (slot == puffs.Count)
            {
                GameObject made = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                made.name = PuffName;
                made.transform.SetParent(parent, false);
                puffs.Add(new Timed<GameObject> { Item = made });
            }
            Timed<GameObject> p = puffs[slot];
            p.Born = tick; p.Until = tick + ticks; p.Size = size;
            p.Item.GetComponent<Renderer>().sharedMaterial = material;
            p.Item.transform.position = at;
            puffs[slot] = p;
        }

        // A slot whose item has run out; a new one while the pool may grow; else the next in turn (the oldest is overwritten).
        private static int Free<T>(List<Timed<T>> pool, long tick, int max, ref int next)
        {
            for (int i = 0; i < pool.Count; i++)
                if (pool[i].Until <= tick) return i;
            if (pool.Count < max) return pool.Count;
            next = (next + 1) % pool.Count;
            return next;
        }
    }
}
