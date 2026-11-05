using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("Round4Fuzz")]
    public sealed class Round4FuzzTests
    {
        private sealed class Actor
        {
            internal StableEntityKey Key;
            internal SimVec3 Position;
            internal ulong Host;
            internal bool Attached;
            internal bool Alive = true;
            internal bool Static;
            internal ulong Counter;
        }
        private struct Op
        {
            internal int Kind;
            internal ulong Target;
            internal ulong Host;
            internal ulong Ordinal;
            internal SimVec3 Position;
            internal StableRequestKey Key => new StableRequestKey(5,0,new StableId(Target),1,Ordinal,(ulong)(10+Kind));
        }
        private sealed class World
        {
            internal readonly Simulation Sim = Simulation.CreateRetainedWorld(913,
                new SimConfig(entityCapacity:128,eventInitialCapacity:256,eventHardLimit:256),new RetainedWorldOptions(2));
            internal readonly ComponentType<Position> Positions;
            internal readonly ComponentType<Collider> Colliders;
            internal World()
            {
                Positions = Sim.Entities.Register<Position>("position",new PositionSerializer());
                Colliders = Sim.Entities.Register<Collider>("collider",new ColliderSerializer());
                Sim.ConfigureSpatial(Positions,Colliders,SimFix.FromInt(4));
            }
            internal void Create(Actor actor)
            {
                EntityId entity = Sim.Entities.Create(actor.Key);
                Sim.Entities.Set(Positions,entity,new Position(actor.Position));
                Sim.Entities.Set(Colliders,entity,new Collider(SimFix.Ratio(1,4),SimFix.FromInt(2)));
            }
            internal void Kill(ulong id) => Sim.Entities.Destroy(Sim.Entities.Resolve(new EntityLink(new StableId(id))));
            internal void Request(Op op)
            {
                var target = new EntityLink(new StableId(op.Target));
                var host = new EntityLink(new StableId(op.Host));
                if (op.Kind == 0)
                    Sim.Spawns.Enqueue(target,op.Key,new SpawnValues(op.Position,SimVec3.Metres(1,0,0),op.Ordinal));
                else if (op.Kind == 1)
                    Sim.RequestPosition(target,op.Position,op.Key);
                else if (op.Kind == 2)
                    Sim.Attach(target,host,op.Position,op.Key);
                else if (op.Kind == 3)
                    Sim.Detach(target,op.Key);
                else
                    Sim.RequestStableColliderStatic(target,true,op.Key);
            }
        }

        [TestCase(913), TestCase(2401), TestCase(104729), TestCase(887), Timeout(10000)]
        public void RandomLifecycleRequestsMatchNaiveOracleAndReversedSubmission(int seed)
        {
            var random = new Random(seed);
            var a = new World();
            var b = new World();
            var actors = new Actor[32];
            var history = new Dictionary<ulong,Actor>();
            var children = new List<StableId>();
            var totals = new long[11];
            var coverage = new int[11];
            for (int i = 0; i < actors.Length; i++)
            {
                actors[i] = new Actor { Key=StableEntityKey.World(77,0,(ulong)i), Position=SimVec3.Metres(i,0,0) };
                history.Add(actors[i].Key.Id.Value,actors[i]);
                a.Create(actors[i]);
            }
            for (int i = actors.Length-1; i >= 0; i--)
                b.Create(actors[i]);
            for (int tick = 1; tick <= 200; tick++)
            {
                foreach (StableId child in children)
                {
                    a.Kill(child.Value);
                    b.Kill(child.Value);
                }
                children.Clear();
                a.Sim.CompleteSpatialBoundary();
                b.Sim.CompleteSpatialBoundary();
                var ops = new List<Op>();
                for (int i = 0; i < 24; i++)
                {
                    int kind = i < 5 ? i : random.Next(5);
                    int target = i < 5 ? i : random.Next(16);
                    int host = 16+random.Next(16);
                    ops.Add(new Op { Kind=kind, Target=actors[target].Key.Id.Value, Host=actors[host].Key.Id.Value,
                        Ordinal=(ulong)i, Position=SimVec3.Metres(random.Next(20),kind == 2 ? 2 : 0,random.Next(10)) });
                }
                foreach (Op op in ops)
                    a.Request(op);
                for (int i = ops.Count-1; i >= 0; i--)
                    b.Request(ops[i]);
                // Forced cases ensure each seeded run includes the adversarial death windows.
                var killed = new List<int>();
                for (int i = 0; i < actors.Length; i++)
                    if (random.Next(8) == 0 || i == (tick % 5))
                        killed.Add(i);
                if (tick % 3 == 0)
                {
                    ulong host = ops[2].Host;
                    for (int i = 16; i < actors.Length; i++)
                        if (actors[i].Key.Id.Value == host && !killed.Contains(i))
                            killed.Add(i);
                }
                foreach (int index in killed)
                    actors[index].Alive = false;
                foreach (Actor actor in actors)
                    if (actor.Alive && actor.Attached && !history[actor.Host].Alive)
                    {
                        actor.Attached = false;
                        totals[(int)StableRequestKind.FollowHost]++;
                        coverage[(int)StableRequestKind.FollowHost]++;
                    }
                foreach (int index in killed)
                    a.Kill(actors[index].Key.Id.Value);
                for (int i = killed.Count-1; i >= 0; i--)
                    b.Kill(actors[killed[i]].Key.Id.Value);
                a.Sim.CompleteSpatialBoundary();
                b.Sim.CompleteSpatialBoundary();
                foreach (int index in killed)
                {
                    Actor replacement = new Actor { Key=StableEntityKey.World(77,tick,(ulong)index), Position=SimVec3.Metres(index,0,0) };
                    history.Add(replacement.Key.Id.Value,replacement);
                    actors[index] = replacement;
                    a.Create(replacement);
                    b.Create(replacement);
                }
                // Reuse the same local slot repeatedly while requests still contain the old StableIds.
                for (ulong i = 0; i < 3; i++)
                {
                    var transient = new Actor { Key=StableEntityKey.World(88,tick,i) };
                    a.Create(transient);
                    b.Create(transient);
                    a.Kill(transient.Key.Id.Value);
                    b.Kill(transient.Key.Id.Value);
                    a.Sim.CompleteSpatialBoundary();
                    b.Sim.CompleteSpatialBoundary();
                }
                ops.Sort(CompareIndependent);
                var winners = new Dictionary<string,Op>();
                var expectedChildren = new List<Op>();
                foreach (Op op in ops)
                {
                    Actor target = history[op.Target];
                    if (op.Kind == 0)
                    {
                        children.Add(StableEntityKey.Spawned(new StableId(op.Target),target.Counter++,(ulong)(10+op.Kind)).Id);
                        expectedChildren.Add(op);
                        if (!target.Alive)
                            coverage[0]++;
                        continue;
                    }
                    StableRequestKind deadKind = op.Kind == 1 ? StableRequestKind.Position : op.Kind == 2
                        ? StableRequestKind.AttachTarget : op.Kind == 3 ? StableRequestKind.Detach : StableRequestKind.ColliderStatic;
                    if (!target.Alive)
                    {
                        totals[(int)deadKind]++;
                        coverage[(int)deadKind]++;
                        continue;
                    }
                    if (op.Kind == 2 && !history[op.Host].Alive)
                    {
                        totals[(int)StableRequestKind.AttachHost]++;
                        coverage[(int)StableRequestKind.AttachHost]++;
                        continue;
                    }
                    int group = op.Kind == 3 ? 2 : op.Kind;
                    winners[op.Target+":"+group] = op;
                }
                foreach (Op op in winners.Values)
                {
                    Actor actor = history[op.Target];
                    if (op.Kind == 1)
                        actor.Position = op.Position;
                    else if (op.Kind == 2)
                    {
                        actor.Attached = true;
                        actor.Host = op.Host;
                    }
                    else if (op.Kind == 3)
                        actor.Attached = false;
                    else
                        actor.Static = true;
                }
                // Remember attachment offsets independently of the simulation's component storage.
                foreach (Op op in winners.Values)
                    if (op.Kind == 2)
                        offsets[Key(seed,op.Target)] = op.Position;
                foreach (Actor actor in actors)
                    if (actor.Attached)
                        actor.Position = history[actor.Host].Position+offsets[Key(seed,actor.Key.Id.Value)];
                a.Sim.Step(default);
                b.Sim.Step(default);
                foreach (Actor actor in actors)
                {
                    EntityId x = a.Sim.Entities.Resolve(new EntityLink(actor.Key.Id));
                    EntityId y = b.Sim.Entities.Resolve(new EntityLink(actor.Key.Id));
                    Assert.That(a.Sim.Entities.Read(a.Positions,x).Value,Is.EqualTo(actor.Position),"seed="+seed+" tick="+tick);
                    Assert.That(b.Sim.Entities.Read(b.Positions,y).Value,Is.EqualTo(actor.Position));
                    Assert.That(a.Sim.Entities.Read(a.Colliders,x).IsStatic,Is.EqualTo(actor.Static));
                }
                for (int i = 0; i < children.Count; i++)
                {
                    EntityId child = a.Sim.Entities.Resolve(new EntityLink(children[i]));
                    SpawnCapture capture = a.Sim.Entities.Read(a.Sim.Entities.Resolve<SpawnCapture>(),child);
                    Assert.That(capture.Spawner.Id.Value,Is.EqualTo(expectedChildren[i].Target));
                    Assert.That(capture.Values.Position,Is.EqualTo(expectedChildren[i].Position));
                    Assert.That(capture.Values.Ownership,Is.EqualTo(expectedChildren[i].Ordinal));
                }
                for (int kind = 1; kind <= 10; kind++)
                {
                    Assert.That(a.Sim.RequestDiagnostics.Count((StableRequestKind)kind),Is.EqualTo(totals[kind]),"diagnostic kind="+kind);
                    Assert.That(b.Sim.RequestDiagnostics.Count((StableRequestKind)kind),Is.EqualTo(totals[kind]),"reversed diagnostic kind="+kind);
                }
                Assert.That(a.Sim.StateHashRetained(),Is.EqualTo(b.Sim.StateHashRetained()),"order seed="+seed+" tick="+tick);
                if (tick % 17 == 0)
                    b.Sim.RestoreRetainedSnapshot(a.Sim.SaveRetainedSnapshot(),true);
            }
            for (int kind = 0; kind <= 6; kind++)
                Assert.That(coverage[kind],Is.GreaterThan(0),"uncovered death kind="+kind);
            TestContext.WriteLine("MF4 fuzz seed={0}; ticks=200; requests=4800; repeated-slot-reuse=600; death-coverage={1}",seed,string.Join(",",coverage));
        }
        private readonly Dictionary<string,SimVec3> offsets = new Dictionary<string,SimVec3>();
        private static string Key(int seed, ulong id) => seed+":"+id;
        private static int CompareIndependent(Op a, Op b)
        {
            int c = a.Target.CompareTo(b.Target);
            return c != 0 ? c : a.Ordinal.CompareTo(b.Ordinal);
        }
    }
}
