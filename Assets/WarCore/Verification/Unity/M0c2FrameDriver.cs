#if ENABLE_IL2CPP && !UNITY_EDITOR
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using UnityEngine;
using WarCore.Verification.Fixtures;
namespace WarCore.Verification.Unity
{
    public static class M0c2PlayerEntry
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Start()
        {
            if(HashVectorRunner.Argument("-m0c2Scope","false")!="true")return;
            new GameObject("M0c2 frame evidence").AddComponent<M0c2FrameDriver>();
        }
    }
    public sealed class M0c2FrameDriver : MonoBehaviour
    {
        private static readonly string[] Arms={"pilot","idle-pre","spin-pre","inject16-pre","inject64-pre","inject1k-pre",
            "clean","inject16-post","inject64-post","inject1k-post","clean-post","spin-post","idle-post"};
        private static readonly string[] Empty={"empty-a","empty-b","restored-a","restored-b"};
        private struct Row
        {
            internal int Frame,ReadFrame;
            internal long Bytes,Step,Hash,Tick,Target,Duration,Created,Notices;
            internal ulong Digest;
            internal int Queries,Rays,E,Alive,Truncated;
        }
        private string directory,build,arch;
        private string[] groups;
        private bool full,pending,done;
        private int batch,group,arm,index,warmup;
        private M0c2ScopeRecorder recorder;
        private M0c2FrameWorkload sim;
        private Row[] rows;
        private long[] targets;
        private byte[] retained;
        private int payload;
        private bool idle,spin;
        private void Start()
        {
            directory=HashVectorRunner.Argument("-m0c2Directory","");
            try
            {
                CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;
                Directory.CreateDirectory(directory);
                full=HashVectorRunner.Argument("-m0c2Profile","empty")=="full";
                groups=full?M0c2FrameWorkload.FullGroups:Empty;
                batch=int.Parse(HashVectorRunner.Argument("-m0c2Batch","0"));
                build=HashVectorRunner.Argument("-m0c2Build","");
                arch=System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
                UnityEngine.Profiling.Profiler.enableBinaryLog=false;
                QualitySettings.vSyncCount=0;Application.targetFrameRate=-1;
                File.WriteAllText(Path.Combine(directory,"scope.txt"),M0c2ScopeRecorder.Description+"\n");
                if(full)M0c2FullMeasurement.WriteReferenceControls(Path.Combine(directory,"reference-control.csv"));
                targets=new long[300];
                Prepare();
            }
            catch(Exception error){Fail(error);}
        }
        private void Prepare()
        {
            recorder?.Dispose();recorder=new M0c2ScopeRecorder(8192);
            idle=Arms[arm].StartsWith("idle",StringComparison.Ordinal);
            spin=Arms[arm].StartsWith("spin",StringComparison.Ordinal);
            payload=Arms[arm].StartsWith("inject16",StringComparison.Ordinal)?16:
                Arms[arm].StartsWith("inject64",StringComparison.Ordinal)?64:
                Arms[arm].StartsWith("inject1k",StringComparison.Ordinal)?1024:0;
            rows=new Row[idle||spin?1200:300]; index=0;warmup=0;
            sim=idle||spin?null:new M0c2FrameWorkload(full,group);
        }
        private void Update()
        {
            if(done)return;
            try
            {
                if(pending)
                {
                    rows[index].Bytes=recorder.ReadNextFrame();rows[index].ReadFrame=Time.frameCount;
                    pending=false;index++;
                    if(index==rows.Length)
                    {
                        Save();arm++;
                        if(arm==Arms.Length){arm=0;group++;}
                        if(group==groups.Length){done=true;recorder.Dispose();Application.Quit(0);return;}
                        Prepare();return; // Setup and file IO never enter a measured frame.
                    }
                }
                if(warmup<40)
                {
                    if(sim!=null){sim.Step();sim.Hash();}
                    retained=new byte[payload];warmup++;
                    if(warmup==40 && !full && group>=2 && sim!=null)
                        sim.Restore();
                    return; // Restored tick 41 is measured next frame, with no hidden Sim warmup.
                }
                recorder.BeginFrame();
                long start=Stopwatch.GetTimestamp();
                if(sim!=null)sim.Step();
                long split=Stopwatch.GetTimestamp();
                ulong hash=sim==null?0:sim.Hash();
                long end=Stopwatch.GetTimestamp();
                long target=spin?targets[index%300]:0;
                if(spin)while(Stopwatch.GetTimestamp()-start<target){}
                long duration=Stopwatch.GetTimestamp()-start;
                // Equal object-header pedestal in every arm. Payload delta is 16/64/1024 B.
                retained=new byte[payload];if(payload>0)retained[0]=1;
                rows[index]=new Row{Frame=Time.frameCount,Step=sim==null?0:split-start,Hash=sim==null?0:end-split,
                    Tick=sim==null?0:sim.Tick,Digest=hash,Target=target,Duration=duration,
                    Queries=sim==null?0:sim.Queries,Rays=sim==null?0:sim.Rays,E=sim==null?0:sim.Affected,
                    Alive=sim==null?0:sim.Alive,Truncated=sim==null?0:sim.Truncated,
                    Created=sim==null?0:sim.Created,Notices=sim==null?0:sim.Notices};
                if(arm==0)targets[index]=duration;
                pending=true;
            }
            catch(Exception error){Fail(error);}
        }
        private void Save(int count=-1)
        {
            string path=Path.Combine(directory,groups[group]+"--"+Arms[arm]+(count<0?".csv":".partial.csv"));
            using(var output=new StreamWriter(new FileStream(path,FileMode.CreateNew)))
            {
                output.WriteLine("build,arch,protocol,batch,group,arm,index,frame,readFrame,bytes,elapsedTicks,hashTicks,totalTicks,durationTicks,targetTicks,frequency,tick,hash,queries,rays,E,alive,created,notices,truncated,payload");
                for(int i=0;i<(count<0?rows.Length:count);i++)
                {
                    Row r=rows[i];
                    output.WriteLine(string.Join(",",build,arch,"retained-v6/snapshot-v8",batch,groups[group],Arms[arm],i,
                        r.Frame,r.ReadFrame,r.Bytes,r.Step,r.Hash,r.Step+r.Hash,r.Duration,r.Target,Stopwatch.Frequency,r.Tick,r.Digest.ToString("X16"),
                        r.Queries,r.Rays,r.E,r.Alive,r.Created,r.Notices,r.Truncated,payload));
                }
            }
            GC.KeepAlive(retained);
        }
        private void Fail(Exception error)
        {
            done=true;recorder?.Dispose();Directory.CreateDirectory(directory);
            if(rows!=null && index>0)
            {
                try{Save(index);}catch(Exception archiveError){error=new AggregateException(error,archiveError);}
            }
            File.WriteAllText(Path.Combine(directory,"error.txt"),error.ToString());
            UnityEngine.Debug.LogException(error);Application.Quit(1);
        }
    }
}
#endif
