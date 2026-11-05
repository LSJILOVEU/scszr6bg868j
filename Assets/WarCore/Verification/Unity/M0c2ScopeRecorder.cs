using System;
using System.Collections.Generic;
using Unity.Profiling;
using Unity.Profiling.LowLevel;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using WarCore.Verification.Fixtures;

namespace WarCore.Verification.Unity
{
    // Plan A: whole-frame bytes, completed on the following Unity frame.
    // Deliberately does not implement IM0c2Scope: synchronous End is invalid here.
    public sealed class M0c2ScopeRecorder : IDisposable
    {
        public const string Description="GC Allocated In Frame;category=Memory;read=LastValue;scope=whole-frame-all-threads;unit=Bytes";
        private ProfilerRecorder recorder;
        private int frame, count;
        private bool pending;
        public M0c2ScopeRecorder(int capacity=65536)
        {
            if(!Debug.isDebugBuild)throw new InvalidOperationException("Development Build required");
            try
            {
                recorder=ProfilerRecorder.StartNew(ProfilerCategory.Memory,M0c2CounterContract.Name,capacity,
                    ProfilerRecorderOptions.SumAllSamplesInFrame);
                if(!recorder.Valid || !recorder.IsRunning || recorder.UnitType!=ProfilerMarkerDataUnit.Bytes)
                    throw new InvalidOperationException("recorder invalid, stopped or not Bytes");
            }
            catch(Exception error)
            {
                recorder.Dispose();
                // Enumerate only on failed initialization, before any measurement window.
                var handles=new List<ProfilerRecorderHandle>();
                ProfilerRecorderHandle.GetAvailable(handles);
                var candidates=new List<M0c2CounterCandidate>();
                foreach(var handle in handles)
                {
                    var info=ProfilerRecorderHandle.GetDescription(handle);
                    candidates.Add(new M0c2CounterCandidate(info.Name,info.Category.Name,info.UnitType.ToString()));
                }
                string reason=error.Message;
                try
                {
                    int selected=M0c2CounterContract.Select(candidates);
                    reason+="; available target="+candidates[selected];
                }
                catch(InvalidOperationException selectionError) { reason+="; "+selectionError.Message; }
                throw new InvalidOperationException(M0c2CounterContract.Failure(reason,candidates),error);
            }
        }
        public void BeginFrame()
        {
            if(pending || !recorder.Valid || !recorder.IsRunning)
                throw new InvalidOperationException("frame pending or recorder unavailable");
            frame=Time.frameCount; count=recorder.Count; pending=true;
        }
        public long ReadNextFrame()
        {
            if(!pending || Time.frameCount!=frame+1 || !recorder.Valid || !recorder.IsRunning
                || recorder.Count!=count+1 || recorder.Count>=recorder.Capacity)
                throw new InvalidOperationException("frame counter unavailable: stale/skipped/saturated");
            long value=recorder.LastValue;
            if(value<=0)throw new InvalidOperationException("LastValue=0 or negative: counter unavailable");
            pending=false;
            return value;
        }
        public void Dispose() => recorder.Dispose();
    }
}
