using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
namespace WarCore.Tests
{
    public static class M0c2FullMeasurement
    {
        private struct Sample
        {
            internal long Step, Hash, Tick, Created, Notices;
            internal ulong Digest;
            internal int Queries, Rays, Affected, Alive, Truncated;
            internal M0c2ScopeSample Scope;
        }
        private static Sample Capture(M0c2FullFixture f, IM0c2Scope scope, bool inject)
        {
            scope.Begin(); long start=Stopwatch.GetTimestamp();
            f.Sim.Step(default); if(inject)M0c2Measurement.Inject(64);
            long split=Stopwatch.GetTimestamp(); ulong hash=f.Sim.StateHashRetained();
            long end=Stopwatch.GetTimestamp(); M0c2ScopeSample allocation=scope.End();
            return new Sample {Step=split-start,Hash=end-split,Digest=hash,Tick=f.Sim.Clock.Tick,
                Queries=f.QueryCount,Rays=f.RayCount,Affected=f.Affected,Alive=f.Alive,Truncated=f.Truncated,
                Created=f.Churn==null?0:f.Churn.Created,Notices=f.Churn?.References==null?0:f.Churn.References.Notices,Scope=allocation};
        }
        private static M0c2ScopeSample Control(IM0c2Scope scope)
        { scope.Begin(); M0c2Measurement.Inject(32768); return scope.End(); }
        public static void Replay(string path)
        {
            using(var output=new StreamWriter(path))
            {
                output.Write("{");
                for(int g=0;g<M0c2FullFixture.Names.Length;g++)
                {
                    Console.WriteLine("replay "+M0c2FullFixture.Names[g]);
                    var a=new M0c2FullFixture(g); var b=new M0c2FullFixture(g);
                    if(g!=0)output.Write(","); output.Write("\""+M0c2FullFixture.Names[g]+"\":[");
                    for(int i=0;i<340;i++)
                    {
                        // Mid-window checkpoints and retirement/rotation boundaries. Reused cold
                        // destination; allocation order reversed each restore; no timed work here.
                        if(i==0 || i==1 || i==19 || i==32 || i==40 || i==173 || i==339)
                        {
                            byte[] bytes=a.Sim.SaveRetainedSnapshot(); b.Sim.RestoreRetainedSnapshot(bytes,true);
                            if(a.Sim.StateHashRetained()!=b.Sim.StateHashRetained())throw new InvalidOperationException("Restore hash mismatch");
                            byte[] again=b.Sim.SaveRetainedSnapshot();
                            if(bytes.Length!=again.Length)throw new InvalidOperationException("Restore byte length mismatch");
                            for(int j=0;j<bytes.Length;j++)if(bytes[j]!=again[j])throw new InvalidOperationException("Restore byte mismatch");
                        }
                        a.Sim.Step(default); b.Sim.Step(default);
                        ulong digest=a.Sim.StateHashRetained();
                        if(digest!=b.Sim.StateHashRetained())throw new InvalidOperationException("Continuation hash mismatch: "+M0c2FullFixture.Names[g]+" tick="+(i+1));
                        if(i<40)continue;
                        if(i>40)output.Write(",");
                        output.Write("{\"hash\":\""+digest.ToString("X16")+"\",\"queries\":"+a.QueryCount+",\"rays\":"+a.RayCount+",\"E\":"+a.Affected
                            +",\"alive\":"+a.Alive+",\"created\":"+(a.Churn==null?0:a.Churn.Created)+",\"notices\":"+(a.Churn?.References==null?0:a.Churn.References.Notices)+",\"truncated\":"+a.Truncated+"}");
                    }
                    output.Write("]"); output.Flush();
                }
                output.WriteLine("}");
            }
        }
        private static void ReferenceControl(M0c2FullFixture a,M0c2FullFixture b,string layout,StreamWriter output)
        {
            a.Churn.References.VerifyPrepared();b.Churn.References.VerifyPrepared();
            ReferenceChurnBench.Sweep(a.Churn,16);ReferenceChurnBench.Sweep(b.Churn,16);
            for(int i=0;i<31;i++)
            {
                long first=ReferenceChurnBench.Sweep(a.Churn,16),second=ReferenceChurnBench.Sweep(a.Churn,16);
                long newer,older;
                if(i%2==0){newer=ReferenceChurnBench.Sweep(a.Churn,16);older=ReferenceChurnBench.Sweep(b.Churn,16);}
                else{older=ReferenceChurnBench.Sweep(b.Churn,16);newer=ReferenceChurnBench.Sweep(a.Churn,16);}
                output.WriteLine(string.Join(",",layout,i+1,newer,older,Math.Abs(first-second),Stopwatch.Frequency,16,104,64,3,2184));
            }
            output.Flush();
        }
        public static void WriteReferenceControls(string path)
        {
            using(var output=new StreamWriter(path))
            {
                output.WriteLine("layout,sample,newTicks,oldTicks,aaTicks,frequency,repeats,deaths,refs,stores,capacity");
                ReferenceControl(new M0c2FullFixture(15),new M0c2FullFixture(16),"scatter",output);
                ReferenceControl(new M0c2FullFixture(17),new M0c2FullFixture(18),"dense",output);
            }
        }
        public static void Run(string directory,int batch,string build,IM0c2Scope scope,bool inject)
        {
            CultureInfo.CurrentCulture=CultureInfo.InvariantCulture; Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory,"scope.txt"),scope.Description+"\n");
            using(var output=new StreamWriter(Path.Combine(directory,"raw.csv")))
            using(var controls=new StreamWriter(Path.Combine(directory,"controls.json")))
            using(var reference=new StreamWriter(Path.Combine(directory,"reference-control.csv")))
            {
                output.WriteLine("build,protocol,package,batch,group,tick,elapsedTicks,hashTicks,totalTicks,frequency,queries,rays,E,abilitySuccess,statusUpdates,effectPackets,GC,hwm,hash,scopeEvents,scopeValid,scopeSaturated,scopeUnit,scopeValue,alive,created,notices,truncated");
                reference.WriteLine("layout,sample,newTicks,oldTicks,aaTicks,frequency,repeats,deaths,refs,stores,capacity");
                controls.Write("{");
                for(int g=0;g<M0c2FullFixture.Names.Length;g++)
                {
                    int count=g>=15?2:1;
                    var fixtures=new M0c2FullFixture[count]; var samples=new Sample[count][];
                    var before=new M0c2ScopeSample[count]; var after=new M0c2ScopeSample[count];
                    for(int k=0;k<count;k++)
                    {
                        fixtures[k]=new M0c2FullFixture(g+k); samples[k]=new Sample[300];
                    }
                    if(count==2)ReferenceControl(fixtures[0],fixtures[1],g==15?"scatter":"dense",reference);
                    for(int k=0;k<count;k++)
                    {
                        for(int i=0;i<40;i++){fixtures[k].Sim.Step(default);fixtures[k].Sim.StateHashRetained();}
                        Stopwatch.GetTimestamp(); scope.Begin();scope.End();M0c2Measurement.Inject(1);before[k]=Control(scope);
                    }
                    for(int i=0;i<300;i++)
                        for(int n=0;n<count;n++)
                        {
                            int k=count==1?0:((i+batch)%2==0?n:1-n);
                            samples[k][i]=Capture(fixtures[k],scope,inject);
                        }
                    for(int k=0;k<count;k++)after[k]=Control(scope);
                    if(count==2)
                        for(int i=0;i<300;i++)if(samples[0][i].Digest!=samples[1][i].Digest)
                            throw new InvalidOperationException("Reference A/B full hash mismatch");
                    for(int k=0;k<count;k++)
                    {
                        string group=M0c2FullFixture.Names[g+k]; Console.WriteLine("measured "+group);
                        if(g+k!=0)controls.Write(",");controls.Write("\""+group+"\":{\"before\":"+before[k].Json()+",\"after\":"+after[k].Json()+"}");
                        foreach(var s in samples[k])
                            output.WriteLine(string.Join(",",build,"retained-v6/snapshot-v8",g+k>=15?((g+k)%2==1?"a":"b"):"a",batch,group,s.Tick,
                                s.Step,s.Hash,s.Step+s.Hash,Stopwatch.Frequency,s.Queries,s.Rays,s.Affected,0,0,0,s.Scope.Unit=="Bytes"?s.Scope.Value.ToString():"unavailable",0,
                                s.Digest.ToString("X16"),s.Scope.Events,s.Scope.Valid?1:0,s.Scope.Saturated?1:0,s.Scope.Unit,s.Scope.Value,s.Alive,s.Created,s.Notices,s.Truncated));
                    }
                    output.Flush();controls.Flush();g+=count-1;
                }
                controls.WriteLine("}");
            }
        }
    }
}
