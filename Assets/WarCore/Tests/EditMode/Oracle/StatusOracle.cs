using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace WarCore.Tests.Oracle
{
    // This model deliberately has no reference to WarCore.Sim. It reduces complete
    // application sets with arbitrary precision and enumerates time arithmetically;
    // it does not use product pools, incremental merges, or scheduler tasks.
    internal static class StatusOracle
    {
        internal enum Policy { Refresh, Stack, Independent, Strongest }
        internal readonly struct Key : IComparable<Key>
        {
            internal readonly ulong Source, Cast, ProducerKind, Producer, ProducerToken, Action, Pulse, Target;
            internal Key(ulong source,ulong cast,ulong producerKind,ulong producer,ulong producerToken,ulong action,ulong pulse,ulong target)
            {Source=source;Cast=cast;ProducerKind=producerKind;Producer=producer;ProducerToken=producerToken;Action=action;Pulse=pulse;Target=target;}
            public int CompareTo(Key other)
            {
                ulong[] a={Source,Cast,ProducerKind,Producer,ProducerToken,Action,Pulse,Target};
                ulong[] b={other.Source,other.Cast,other.ProducerKind,other.Producer,other.ProducerToken,other.Action,other.Pulse,other.Target};
                for(int i=0;i<a.Length;i++)if(a[i]!=b[i])return a[i]<b[i]?-1:1;
                return 0;
            }
        }
        internal sealed class Application
        {
            internal readonly Key Key;
            internal readonly long Tick, Duration, Strength, Amount;
            internal readonly int Stacks;
            internal Application(Key key,long tick,long duration,long strength,int stacks=1,long amount=0)
            {Key=key;Tick=tick;Duration=duration;Strength=strength;Stacks=stacks;Amount=amount;}
        }
        internal sealed class Row
        {
            internal Key Attribution;
            internal long Start,Expiry,FirstPulse,Strength,Absorb;
            internal int Stacks;
            internal Row Copy()=>(Row)MemberwiseClone();
        }
        internal static long Future(long tick,long duration)
        {
            if(tick<0 || tick>long.MaxValue-1 || duration<0 || duration>int.MaxValue)throw new ArgumentOutOfRangeException();
            return (long)BigInteger.Min(new BigInteger(tick)+duration,new BigInteger(long.MaxValue)-1);
        }
        internal static long ScaleStacks(long amount,int stacks,long maximum)
            => (long)BigInteger.Min(BigInteger.Max(new BigInteger(amount)*stacks,0),maximum);
        internal static long StackedModifierDamage(long basis,long raw,int stacks,int stage,out long saturations)
        {
            const long m=140737488355327,f=2147483647,s=65536;
            BigInteger domain=stage==1 || stage==6?m:f;
            BigInteger lower=stage==3?0:-domain;
            BigInteger product=new BigInteger(raw)*stacks;
            BigInteger scaled=BigInteger.Min(domain,BigInteger.Max(lower,product));
            saturations=scaled==product?0:1;
            BigInteger value;
            if(stage==1 || stage==6)value=new BigInteger(basis)+scaled;
            else
            {
                BigInteger factor=stage==2?new BigInteger(s)+scaled:scaled;
                BigInteger bounded=BigInteger.Min(f,BigInteger.Max(0,factor));
                if(factor!=bounded)saturations++;
                value=new BigInteger(basis)*bounded/s;
            }
            BigInteger result=BigInteger.Min(m,BigInteger.Max(0,value));
            if(value!=result)saturations++;
            return (long)result;
        }
        internal static long StackedPeriodicResource(long initial,long amount,int stacks,bool damage,out long saturations)
        {
            const long limit=140737488355327;
            BigInteger requested=new BigInteger(amount)*stacks;
            BigInteger released=BigInteger.Min(limit,BigInteger.Max(-limit,requested));
            BigInteger value=new BigInteger(initial)+(damage?-released:released);
            BigInteger result=BigInteger.Min(limit,BigInteger.Max(0,value));
            saturations=(requested==released?0:1)+(value==result?0:1);
            return (long)result;
        }
        internal static long[] Pulses(long start,long duration,int delay,int period)
        {
            if(delay<1 || period<1)throw new ArgumentOutOfRangeException();
            BigInteger end=Future(start,duration), first=Future(start,delay);
            if(first>=end)return new long[0];
            BigInteger count=(end-first-1)/period+1;
            if(count>100000)throw new ArgumentOutOfRangeException("oracle enumeration budget");
            return Enumerable.Range(0,(int)count).Select(i=>(long)(first+new BigInteger(i)*period)).ToArray();
        }
        internal static Row[] Reduce(Policy policy,Row[] previous,Application[] incoming,long now,int maxStacks,int maxInstances,int delay)
        {
            if(maxStacks<1 || maxStacks>65535 || maxInstances<1)throw new ArgumentOutOfRangeException();
            Row[] live=previous.Where(r=>r.Expiry>now).Select(r=>r.Copy()).ToArray();
            Application[] ordered=incoming.OrderBy(a=>a.Key).ToArray();
            if(ordered.Length==0)return live;
            if(policy==Policy.Independent)
            {
                if(new BigInteger(live.Length)+ordered.Length>maxInstances)throw new InvalidOperationException("StatusCapacity");
                return live.Concat(ordered.Select(a=>NewRow(a,delay))).ToArray();
            }
            if(live.Length>1)throw new ArgumentOutOfRangeException();
            if(policy==Policy.Strongest)
            {
                Application candidate=ordered.OrderByDescending(a=>a.Strength).ThenBy(a=>a.Key).First();
                if(live.Length!=0 && (live[0].Strength>candidate.Strength ||
                    (live[0].Strength==candidate.Strength && live[0].Attribution.CompareTo(candidate.Key)<=0)))return live;
                return new[]{NewRow(candidate,delay)};
            }
            Row merged=live.Length==0?NewRow(ordered[0],delay):live[0];
            BigInteger total=live.Length==0?0:live[0].Stacks;
            foreach(Application a in ordered)total+=a.Stacks;
            merged.Stacks=policy==Policy.Refresh?1:(int)BigInteger.Min(total,maxStacks);
            merged.Expiry=ordered.Select(a=>Future(a.Tick,a.Duration)).Concat(new[]{merged.Expiry}).Max();
            merged.Attribution=ordered[0].Key;
            merged.Strength=ordered[0].Strength;
            merged.Absorb=(long)BigInteger.Min(ordered.Aggregate(BigInteger.Zero,(sum,a)=>sum+a.Amount)+(live.Length==0?0:live[0].Absorb),140737488355327L);
            return new[]{merged};
        }
        private static Row NewRow(Application a,int delay)=>new Row
        { Attribution=a.Key,Start=a.Tick,Expiry=Future(a.Tick,a.Duration),FirstPulse=delay==0?0:Future(a.Tick,delay),
          Strength=a.Strength,Stacks=a.Stacks,Absorb=a.Amount };
    }
}
