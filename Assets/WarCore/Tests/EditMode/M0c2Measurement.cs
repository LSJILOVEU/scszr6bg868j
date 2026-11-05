using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    public readonly struct M0c2ScopeSample
    {
        public readonly long Value, Events;
        public readonly bool Valid, Saturated;
        public readonly string Unit;
        public M0c2ScopeSample(long value,long events,bool valid,bool saturated,string unit)
        { Value=value; Events=events; Valid=valid; Saturated=saturated; Unit=unit; }
        public string Json() => "{\"value\":"+Value+",\"events\":"+Events+",\"valid\":"+(Valid?1:0)+",\"saturated\":"+(Saturated?1:0)+",\"unit\":\""+Unit+"\"}";
    }
    public interface IM0c2Scope : IDisposable
    {
        string Description { get; }
        void Begin();
        M0c2ScopeSample End();
    }
    public sealed class M0c2MonoScope : IM0c2Scope
    {
        public string Description => "mono-current-thread-bytes;events=presence-only";
        private long before;
        public void Begin() => before=GC.GetAllocatedBytesForCurrentThread();
        public M0c2ScopeSample End()
        {
            long delta=GC.GetAllocatedBytesForCurrentThread()-before;
            // Mono reports bytes only; Events is a presence indicator, not an event count.
            return new M0c2ScopeSample(delta,delta>0?1:0,true,false,"Bytes");
        }
        public void Dispose() { }
    }
    public static class M0c2Measurement
    {
        private struct Sample
        {
            internal long Step,Hash,Tick;
            internal ulong Digest;
            internal M0c2ScopeSample Scope;
        }
        private static byte[] keepAlive;
        private static readonly string[] groups={"empty-a","empty-b","restored-a","restored-b"};
        public static void Inject(int payload) { keepAlive=new byte[payload]; keepAlive[0]=1; }
        private static M0c2ScopeSample Control(IM0c2Scope scope)
        { scope.Begin(); Inject(32768); return scope.End(); }
        public static void Replay(string path)
        {
            using(var output=new StreamWriter(path))
            {
                output.Write("{");
                for(int g=0;g<groups.Length;g++)
                {
                    if(g!=0)output.Write(","); output.Write("\""+groups[g]+"\":[");
                    var sim=M0c2Fixture.Create(g%2==0?0:ulong.MaxValue);
                    for(int i=0;i<40;i++)sim.Step(default);
                    for(int i=0;i<300;i++)
                    {
                        // Cold independent restored instance; no timing data from this path.
                        var restored=M0c2Fixture.Create(g%2==0?0:ulong.MaxValue);
                        restored.RestoreRetainedSnapshot(sim.SaveRetainedSnapshot(),true);
                        sim.Step(default); restored.Step(default);
                        if(sim.StateHashRetained()!=restored.StateHashRetained())throw new InvalidOperationException("Replay mismatch");
                        if(i!=0)output.Write(","); output.Write("\""+restored.StateHashRetained().ToString("X16")+"\"");
                    }
                    output.Write("]");
                }
                output.WriteLine("}");
            }
        }
        public static void Run(string directory,int batch,string build,IM0c2Scope scope,bool inject)
        {
            CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory,"scope.txt"),scope.Description+"\n");
            using(var output=new StreamWriter(Path.Combine(directory,"raw.csv")))
            using(var controls=new StreamWriter(Path.Combine(directory,"controls.json")))
            {
                output.WriteLine("build,protocol,package,batch,group,tick,elapsedTicks,hashTicks,totalTicks,frequency,queries,rays,E,abilitySuccess,statusUpdates,effectPackets,GC,hwm,hash,scopeEvents,scopeValid,scopeSaturated,scopeUnit,scopeValue");
                controls.Write("{");
                for(int g=0;g<groups.Length;g++)
                {
                    var sim=M0c2Fixture.Create(g%2==0?0:ulong.MaxValue);
                    for(int i=0;i<40;i++){sim.Step(default);sim.StateHashRetained();}
                    if(g>=2)sim.RestoreRetainedSnapshot(sim.SaveRetainedSnapshot(),true);
                    // The restored path is measured immediately. No hidden warmup after restoration.
                    var samples=new Sample[300];
                    Stopwatch.GetTimestamp(); scope.Begin(); scope.End(); Inject(1);
                    M0c2ScopeSample before=Control(scope);
                    for(int i=0;i<samples.Length;i++)
                    {
                        scope.Begin(); long start=Stopwatch.GetTimestamp();
                        sim.Step(default);
                        if(inject)Inject(64);
                        long split=Stopwatch.GetTimestamp(); ulong digest=sim.StateHashRetained();
                        long end=Stopwatch.GetTimestamp(); M0c2ScopeSample allocation=scope.End();
                        samples[i]=new Sample{Step=split-start,Hash=end-split,Tick=sim.Clock.Tick,Digest=digest,Scope=allocation};
                    }
                    M0c2ScopeSample after=Control(scope);
                    if(g!=0)controls.Write(",");
                    controls.Write("\""+groups[g]+"\":{\"before\":"+before.Json()+",\"after\":"+after.Json()+"}");
                    foreach(var s in samples)
                    {
                        output.WriteLine(string.Join(",",build,"retained-v6/snapshot-v8",groups[g].Substring(groups[g].Length-1),batch,groups[g],s.Tick,
                            s.Step,s.Hash,s.Step+s.Hash,Stopwatch.Frequency,0,0,0,0,0,0,s.Scope.Unit=="Bytes"?s.Scope.Value.ToString():"unavailable",0,
                            s.Digest.ToString("X16"),s.Scope.Events,s.Scope.Valid?1:0,s.Scope.Saturated?1:0,s.Scope.Unit,s.Scope.Value));
                    }
                }
                controls.WriteLine("}");
            }
            GC.KeepAlive(keepAlive);
        }
    }
}
