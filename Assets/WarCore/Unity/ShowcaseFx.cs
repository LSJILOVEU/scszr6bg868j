using System.Collections.Generic;
using UnityEngine;
using WarCore.Presentation;

namespace WarCore.Unity
{
    // SHOW-1: drawn-only effects that run on real time, not on ticks: chips that fly off where a shot lands, the burst of the
    // grenade preview (flash, dust ring, chips, a scorch mark) and the preview grenade itself. Nothing here touches the
    // simulation: the grenade preview hurts nobody. Everything is pooled and capped. PLACEHOLDER art and numbers.
    // HC-4: a real blast is drawn from its own radii (BlastLook) with a fading ring at the outer radius; the grenade's reach
    // and flight time are read from ShowcaseActions, the one place they are set.
    public sealed class ShowcaseFx
    {
        public const string ChipName = "Chip", BurstName = "Burst", ScorchName = "Scorch", GrenadeName = "Grenade preview";
        public const string DangerRingName = "Danger ring";
        public const float BurstRadius = 5f;
        public static readonly float GrenadeSeconds = BlastLook.FlightSeconds(ShowcaseActions.FuseTicks);
        public static readonly float GrenadeReach = BlastLook.ReachMetres(ShowcaseActions.ReachMm);
        private const int MaxChips = 220, MaxBursts = 24, MaxScorches = 16, MaxRings = 8, RingSegments = 64;
        private const float Gravity = 14f;

        private struct Chip { public Transform Item; public Vector3 Speed; public float Left, Floor; }
        private struct Burst { public Transform Item; public float Age, Life, Size; }
        private struct Thrown { public Vector3 From, To; public float Age; public bool Live, Burst; }
        private struct Ring { public LineRenderer Line; public float Age; }

        private readonly Transform parent;
        private readonly Material dust, dirt, spark, blood, flash, smoke, scorch, shell, danger;
        private readonly List<Chip> chips = new List<Chip>();
        private readonly List<Burst> bursts = new List<Burst>();
        private readonly List<Transform> scorches = new List<Transform>();
        private readonly List<Ring> rings = new List<Ring>();
        private readonly List<Vector3> burstsThisFrame = new List<Vector3>();
        private readonly Transform grenade;
        private Thrown thrown;
        private int nextChip, nextBurst, nextScorch, nextRing;

        public ShowcaseFx(Transform parent)
        {
            this.parent = parent;
            dust = ShowcaseScene.Flat(new Color(0.75f, 0.72f, 0.65f));
            dirt = ShowcaseScene.Lit(new Color(0.42f, 0.36f, 0.3f));
            spark = ShowcaseScene.Flat(new Color(1f, 0.83f, 0.35f));
            blood = ShowcaseScene.Flat(new Color(0.85f, 0.1f, 0.1f));
            flash = ShowcaseScene.Flat(new Color(1f, 0.95f, 0.75f));
            smoke = ShowcaseScene.Flat(new Color(0.3f, 0.3f, 0.3f));
            scorch = ShowcaseScene.Flat(new Color(0.12f, 0.11f, 0.1f));
            shell = ShowcaseScene.Lit(new Color(0.2f, 0.3f, 0.2f));
            danger = ShowcaseScene.Flat(new Color(1f, 0.3f, 0.15f));
            grenade = Make(PrimitiveType.Sphere, GrenadeName, shell);
            grenade.localScale = new Vector3(0.28f, 0.28f, 0.28f);
            grenade.gameObject.SetActive(false);
        }

        public int LiveChips
        {
            get
            {
                int live = 0;
                foreach (Chip c in chips) if (c.Left > 0f) live++;
                return live;
            }
        }

        public bool GrenadeInFlight => thrown.Live;
        // Where grenade previews burst during the last Update (the host shakes the camera for each).
        public IReadOnlyList<Vector3> Bursts => burstsThisFrame;

        private Transform Make(PrimitiveType shape, string name, Material material)
        {
            GameObject made = GameObject.CreatePrimitive(shape);
            made.name = name;
            made.transform.SetParent(parent, false);
            made.GetComponent<Renderer>().sharedMaterial = material;
            return made.transform;
        }

        // count chips thrown up and outward from a point; red ones where a body was hit. They come to rest at height floor.
        public void Chips(Vector3 at, float floor, int count, bool red, float speed = 3.5f)
        {
            for (int i = 0; i < count; i++)
            {
                int slot = -1;
                for (int k = 0; k < chips.Count && slot < 0; k++)
                    if (chips[k].Left <= 0f) slot = k;
                if (slot < 0 && chips.Count < MaxChips)
                {
                    chips.Add(new Chip { Item = Make(PrimitiveType.Cube, ChipName, dirt) });
                    slot = chips.Count - 1;
                }
                if (slot < 0) slot = nextChip = (nextChip + 1) % chips.Count;
                Chip c = chips[slot];
                float angle = Random.value * 2f * Mathf.PI, out_ = Random.Range(0.3f, 1f) * speed, size = Random.Range(0.06f, 0.14f);
                c.Speed = new Vector3(Mathf.Cos(angle) * out_, Random.Range(0.6f, 1.3f) * speed, Mathf.Sin(angle) * out_);
                c.Left = Random.Range(0.45f, 0.9f);
                c.Floor = floor;
                c.Item.position = at;
                c.Item.localScale = new Vector3(size, size, size);
                c.Item.rotation = Random.rotation;
                c.Item.GetComponent<Renderer>().sharedMaterial = red ? blood : (i % 3 == 0 ? spark : dirt);
                c.Item.gameObject.SetActive(true);
                chips[slot] = c;
            }
        }

        // A ball that grows to size and vanishes over life seconds.
        private void Grow(Vector3 at, Material material, float size, float life)
        {
            int slot = -1;
            for (int k = 0; k < bursts.Count && slot < 0; k++)
                if (bursts[k].Age >= bursts[k].Life) slot = k;
            if (slot < 0 && bursts.Count < MaxBursts)
            {
                bursts.Add(new Burst { Item = Make(PrimitiveType.Sphere, BurstName, material) });
                slot = bursts.Count - 1;
            }
            if (slot < 0) slot = nextBurst = (nextBurst + 1) % bursts.Count;
            Burst b = bursts[slot];
            b.Age = 0f; b.Life = life; b.Size = size;
            b.Item.position = at;
            b.Item.localScale = Vector3.zero;
            b.Item.GetComponent<Renderer>().sharedMaterial = material;
            b.Item.gameObject.SetActive(true);
            bursts[slot] = b;
        }

        // The preview burst at a point on the ground: flash, smoke, a ring of dust, chips and a scorch mark.
        public void Explode(Vector3 at) => Blow(at, 3.2f, 4.6f, 2.2f, BurstRadius * 0.7f);

        // HC-4: a real blast, sized by its radii: the burst of the preview with the dust ring and scorch at the inner radius
        // (none when there is no full-harm core), and a ring on the ground at the outer radius that fades out.
        public void Explode(Vector3 at, BlastLook look)
        {
            Blow(at, look.FlashSize, look.SmokeSize, look.DustRadius, look.ScorchDiameter);
            DangerRing(at, look.RingRadius);
        }

        // The radius a danger ring is drawn at (its first point's distance from its centre on the ground).
        public static float RingRadiusOf(Transform ring)
        {
            Vector3 first = ring.GetComponent<LineRenderer>().GetPosition(0);
            return new Vector2(first.x, first.z).magnitude;
        }

        private void Blow(Vector3 at, float flashSize, float smokeSize, float dustRadius, float scorchDiameter)
        {
            Grow(at + Vector3.up * 0.6f, flash, flashSize, 0.16f);
            Grow(at + Vector3.up * 0.9f, smoke, smokeSize, 0.9f);
            for (int i = 0; dustRadius > 0f && i < 8; i++)
            {
                float angle = i * Mathf.PI / 4f;
                Grow(at + new Vector3(Mathf.Cos(angle) * dustRadius, 0.3f, Mathf.Sin(angle) * dustRadius), dust, 1.8f, 0.6f);
            }
            Chips(at + Vector3.up * 0.3f, at.y + 0.05f, 26, false, 7f);
            if (scorchDiameter > 0f)
            {
                Transform mark;
                if (scorches.Count < MaxScorches)
                {
                    mark = Make(PrimitiveType.Cylinder, ScorchName, scorch);
                    scorches.Add(mark);
                }
                else mark = scorches[nextScorch = (nextScorch + 1) % scorches.Count];
                mark.position = at + Vector3.up * 0.03f;
                mark.localScale = new Vector3(scorchDiameter, 0.01f, scorchDiameter);
            }
            burstsThisFrame.Add(at);
        }

        private void DangerRing(Vector3 at, float radius)
        {
            int slot = -1;
            for (int k = 0; k < rings.Count && slot < 0; k++)
                if (!rings[k].Line.gameObject.activeSelf) slot = k;
            if (slot < 0 && rings.Count < MaxRings)
            {
                var made = new GameObject(DangerRingName);
                made.transform.SetParent(parent, false);
                LineRenderer line = made.AddComponent<LineRenderer>();
                line.sharedMaterial = danger;
                line.useWorldSpace = false;
                line.loop = true;
                line.positionCount = RingSegments;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                rings.Add(new Ring { Line = line });
                slot = rings.Count - 1;
            }
            if (slot < 0) slot = nextRing = (nextRing + 1) % rings.Count;
            Ring r = rings[slot];
            for (int i = 0; i < RingSegments; i++)
            {
                float angle = i * 2f * Mathf.PI / RingSegments;
                r.Line.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
            }
            r.Line.transform.position = at + Vector3.up * BlastLook.RingLift;
            r.Line.widthMultiplier = BlastLook.RingWidth;
            r.Line.gameObject.SetActive(true);
            r.Age = 0f;
            rings[slot] = r;
        }

        // Start the preview grenade from the thrower's hand toward a point on the ground (cut to GrenadeReach). False while
        // one is already in the air. burst false (SHOW-2, the real grenade): it vanishes on landing without a burst, which the
        // simulation's blast then draws.
        public bool Throw(Vector3 from, Vector3 to, bool burst = true)
        {
            if (thrown.Live) return false;
            Vector3 flat = new Vector3(to.x - from.x, 0f, to.z - from.z);
            if (flat.magnitude > GrenadeReach) to = from + flat.normalized * GrenadeReach + Vector3.up * (to.y - from.y);
            thrown = new Thrown { From = from, To = to, Age = 0f, Live = true, Burst = burst };
            grenade.position = from;
            grenade.gameObject.SetActive(true);
            return true;
        }

        // Every frame with the real time since the last one. With dt <= 0 nothing moves.
        public void Update(float dt)
        {
            burstsThisFrame.Clear();
            if (dt <= 0f) return;
            if (thrown.Live)
            {
                thrown.Age += dt;
                float t = Mathf.Clamp01(thrown.Age / GrenadeSeconds);
                Vector3 at = Vector3.Lerp(thrown.From, thrown.To, t);
                at.y += 4f * t * (1f - t) * Mathf.Max(2.5f, Vector3.Distance(thrown.From, thrown.To) * 0.22f);
                grenade.position = at;
                grenade.Rotate(540f * dt, 300f * dt, 0f);
                if (t >= 1f)
                {
                    thrown.Live = false;
                    grenade.gameObject.SetActive(false);
                    if (thrown.Burst) Explode(thrown.To);
                }
            }
            for (int i = 0; i < chips.Count; i++)
            {
                Chip c = chips[i];
                if (c.Left <= 0f) continue;
                c.Left -= dt;
                c.Speed.y -= Gravity * dt;
                Vector3 at = c.Item.position + c.Speed * dt;
                if (at.y < c.Floor) { at.y = c.Floor; c.Speed = Vector3.zero; }
                c.Item.position = at;
                if (c.Left <= 0f) c.Item.gameObject.SetActive(false);
                chips[i] = c;
            }
            for (int i = 0; i < rings.Count; i++)
            {
                Ring r = rings[i];
                if (!r.Line.gameObject.activeSelf) continue;
                r.Age += dt;
                float left = BlastLook.RingAlpha(r.Age);
                r.Line.widthMultiplier = BlastLook.RingWidth * left;
                if (left <= 0f) r.Line.gameObject.SetActive(false);
                rings[i] = r;
            }
            for (int i = 0; i < bursts.Count; i++)
            {
                Burst b = bursts[i];
                if (b.Age >= b.Life) continue;
                b.Age += dt;
                float t = Mathf.Clamp01(b.Age / b.Life);
                // Out fast, then thin away.
                float size = b.Size * (t < 0.35f ? t / 0.35f : 1f - (t - 0.35f) / 0.65f * 0.85f);
                b.Item.localScale = new Vector3(size, size, size);
                if (b.Age >= b.Life) b.Item.gameObject.SetActive(false);
                bursts[i] = b;
            }
        }
    }
}
