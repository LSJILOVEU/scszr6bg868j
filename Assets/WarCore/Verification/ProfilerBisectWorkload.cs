using System;
using System.IO;
using WarCore.Sim;
using WarCore.Verification.Fixtures;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Verification
{
    // Construction, oracle capture and final verification are outside recorder windows.
    public sealed class ProfilerBisectWorkload
    {
        public const string Names = "empty events timers timers-events ray sphere cylinder capsule sector move-grid entities state-hash signature dense-signature dense-observer dense-queries dense-ray-any dense-ray-nearest dense-sphere dense-sector light dense spin-idle state-hash-new state-hash-strings state-hash-header state-hash-entities state-hash-scheduler state-hash-events state-hash-scheduler-populated state-hash-events-populated state-hash-rng state-hash-rng-empty state-hash-rng-many state-hash-systems state-hash-factions state-hash-movement m0c2-empty m0c2-full-Light m0c2-full-Heavy m0c2-full-DenseAimedRays m0c2-full-SphereOnly m0c2-full-RealScatteredRegular m0c2-full-RealScatteredPeak m0c2-full-RealDenseRegular m0c2-full-RealDensePeak m0c2-full-SemanticScatteredRegular m0c2-full-SemanticScatteredPeak m0c2-full-SemanticDenseRegular m0c2-full-SemanticDensePeak m0c2-full-DenseOverflow m0c2-full-ChurnScattered m0c2-full-ChurnDense m0c2-full-ReferenceScatteredA m0c2-full-ReferenceScatteredB m0c2-full-ReferenceDenseA m0c2-full-ReferenceDenseB";
        private enum Kind { Empty, Events, Timers, TimersEvents, Ray, Sphere, Cylinder, Capsule, Sector,
            MoveGrid, Entities, StateHash, Signature, DenseSignature, DenseObserver, DenseQueries,
            DenseRayAny, DenseRayNearest, DenseSphere, DenseSector, Light, Dense }
        private readonly M0c2FullFixture full;
        private readonly Kind kind;
        private readonly Simulation sim;
        private readonly LoadSystem load;
        private readonly SimEventBuffer events;
        private readonly SimScheduler scheduler;
        private readonly EntityStore entities;
        private readonly EntityId[] recycled;
        private readonly ProfilerSpatialWorkload spatial;
        private readonly ProfilerSignatureReplay signature;
        private readonly ProfilerDenseWorkload dense;
        private ulong checksum;
        private readonly ProfilerStateHashWorkload hashProbe;
        private readonly ProfilerSpinIdle spin;
        private long applied;
        public string Name { get; }
        public int Frames { get; private set; }
        public long Operations { get; private set; }
        public long QueryCalls => dense != null ? dense.QueryCalls : spatial != null ? spatial.QueryCalls : 0;

        public static bool IsValid(string name) => Array.IndexOf(Names.Split(' '), name) >= 0;

        public ProfilerBisectWorkload(string name, ProfilerSpinIdle spin = null)
        {
            string[] names = Names.Split(' ');
            int index = Array.IndexOf(names, name);
            if (index < 0) throw new ArgumentException("Unknown -g23Bisect. Choose: " + Names);
            kind = (Kind)index;
            Name = name;
            if(name.StartsWith("m0c2-full-",StringComparison.Ordinal)) {full=new M0c2FullFixture(Array.IndexOf(M0c2FullFixture.Names,name.Substring("m0c2-full-".Length)));sim=full.Sim;}
            if(name == "m0c2-empty") sim = M0c2Fixture.Create();
            if ((name == "spin-idle") != (spin != null)) throw new ArgumentException("spin-idle requires a spin plan, and only spin-idle may use one.");
            this.spin = spin;
            if (name.StartsWith("state-hash", StringComparison.Ordinal)) hashProbe = new ProfilerStateHashWorkload(name);
            if (kind == Kind.Empty || kind == Kind.Light)
            {
                sim = new Simulation(123, config: new SimConfig(8, 64, 64, 2000));
                if (kind != Kind.Empty)
                {
                    load = new LoadSystem(sim, 2000);
                    sim.Register("load", load);
                }
            }
            if (kind == Kind.Events || kind == Kind.TimersEvents)
            {
                events = new SimEventBuffer(8, 64, 64);
                events.CompleteWarmup();
            }
            if (kind == Kind.Timers || kind == Kind.TimersEvents)
            {
                scheduler = new SimScheduler(1024);
                scheduler.CompleteWarmup();
            }
            if (kind >= Kind.Ray && kind <= Kind.MoveGrid) spatial = new ProfilerSpatialWorkload(name);
            if (kind == Kind.Entities)
            {
                entities = new EntityStore(2000);
                entities.Register<Position>("position", new PositionSerializer());
                entities.Register<Health>("health", new HealthSerializer());
                recycled = new EntityId[16];
                entities.CompleteWarmup();
            }
            if (kind == Kind.Signature) signature = new ProfilerSignatureReplay();
            if ((kind >= Kind.DenseSignature && kind <= Kind.DenseSector) || kind == Kind.Dense)
                dense = new ProfilerDenseWorkload(name);
        }

        public void Step()
        {
            Frames++;
            if(full != null) {sim.Step(default);checksum=sim.StateHashRetained();Operations++;return;}
            if(Name == "m0c2-empty") { sim.Step(default); checksum=sim.StateHashRetained(); Operations++; return; }
            if (spin != null) { spin.Spin(Frames - 1); Operations++; return; }
            if (hashProbe != null)
            {
                ulong value = hashProbe.Sample();
                HashScenarios.Require(value == hashProbe.Expected, "Hash-only state changed.");
                checksum = unchecked(checksum + value);
                Operations++;
                return;
            }
            switch (kind)
            {
                case Kind.Empty:
                case Kind.Light:
                    sim.Step(default);
                    Operations++;
                    break;
                case Kind.Events:
                    events.BeginTick(Frames);
                    for (int i = 0; i < 16; i++) events.Publish(new SimEvent(new EventTypeId(31), tag: i));
                    events.EndTick();
                    Operations += 16;
                    break;
                case Kind.Timers:
                case Kind.TimersEvents:
                    if (events != null) events.BeginTick(Frames);
                    scheduler.Advance(Frames, events);
                    while (scheduler.TryDequeue(out ScheduledTask task))
                    {
                        checksum += (ulong)task.Event.Tag;
                        applied++;
                    }
                    for (int i = 0; i < 4; i++)
                        scheduler.Schedule(Frames + 2L, new SimEvent(new EventTypeId(30), tag: i + 1));
                    if (events != null) events.EndTick();
                    Operations += 4;
                    break;
                case Kind.Entities:
                    for (int i = 0; i < recycled.Length; i++) recycled[i] = entities.Create();
                    for (int i = 0; i < recycled.Length; i++) entities.Destroy(recycled[i]);
                    Operations += recycled.Length;
                    break;
                case Kind.Signature:
                    signature.Step();
                    Operations += signature.CallsPerFrame;
                    break;
                default:
                    if (spatial != null) { spatial.Step(Frames); Operations += spatial.OperationsPerFrame; }
                    else { dense.Step(); Operations += dense.OperationsPerFrame; }
                    break;
            }
        }

        public void Verify(int expectedFrames)
        {
            HashScenarios.Require(Frames == expectedFrames && Operations > 0, "Bisect frame coverage mismatch.");
            if (spin != null) HashScenarios.Require(Frames == spin.Frames, "Spin plan coverage mismatch.");
            if (hashProbe != null) HashScenarios.Require(checksum == unchecked(hashProbe.Expected * (ulong)Frames), "Hash probe checksum mismatch.");
            if (kind == Kind.Empty || kind == Kind.Light || Name == "m0c2-empty" || full != null)
                HashScenarios.Require(sim.Clock.Tick == Frames && sim.Events.EventsDroppedTotal == 0, "Bisect Sim coverage mismatch.");
            if (kind == Kind.Light)
                HashScenarios.Require(load.AppliedTasks == (Frames - 2L) * 4 && sim.Scheduler.PendingCount == 8, "Bisect Light work mismatch.");
            if (events != null)
                HashScenarios.Require(events.EventsDroppedTotal == 0 && events.NextSequence == 1 + (kind == Kind.Events ? Frames * 16L : applied), "Bisect events mismatch.");
            if (scheduler != null)
                HashScenarios.Require(applied == (Frames - 2L) * 4 && scheduler.PendingCount == 8 && scheduler.DueCount == 0
                    && checksum == (ulong)(Frames - 2L) * 10, "Bisect timer expiry mismatch.");
            if (entities != null)
            {
                int live = 0;
                foreach (EntityId entity in entities) live++;
                HashScenarios.Require(live == 0 && recycled[0].Generation == (ulong)Frames, "Bisect entity reuse mismatch.");
            }
            spatial?.Verify(Frames);
            signature?.Verify(Frames);
            dense?.Verify(Frames);
        }

        public void Describe(TextWriter output)
        {
            output.WriteLine("bisect|name={0};warmup=32;steady=1200;setup=before-recorder;one_workload_call_per_frame=true;one_selected_workload=true", Name);
            output.WriteLine("bisect.scope|empty=Sim.Step-no-systems;events=16-publish;timers=4-schedule-delay2-drain;timers-events=plus-expiry-mirrors;entities=16-create-destroy-reserved");
            output.WriteLine("bisect.scope|ray/sphere/cylinder/capsule/sector=one-Light-query;move-grid=2000-position-upserts;state-hash=public-Sim.StateHash-on-fixed-2000-entity-scene");
            output.WriteLine("bisect.scope|signature=legacy-Heavy-captured-output-replay;dense-signature=990-captured-semantic-output-checks;dense-observer=990-real-observer-pairs-no-query;dense-queries=990-query-calls-no-signatures");
            output.WriteLine("bisect.scope|dense-ray-any=330;dense-ray-nearest=330;dense-sphere=165;dense-sector=165;light/dense=original-full-workload;dense-full-observer=false;no-rate-based-acceptance");
            dense?.Describe(output);
            hashProbe?.Describe(output);
            spin?.Describe(output);
        }

        public void WriteFinal(TextWriter output)
        {
            output.WriteLine("Bisect-{0}.final|frames={1};operations={2};isolated_query_calls={3};applied={4};checksum=0x{5:X16}", Name, Frames, Operations, QueryCalls, applied, checksum);
            if (sim != null) output.WriteLine("bisect.state|tick={0};hash=0x{1:X16}", sim.Clock.Tick, (Name == "m0c2-empty" || full != null) ? sim.StateHashRetained() : sim.StateHash());
            if (hashProbe != null) output.WriteLine("bisect.state|tick=0;hash=0x{0:X16}", hashProbe.SceneHash);
            dense?.WriteFinal(output);
        }
    }
}
