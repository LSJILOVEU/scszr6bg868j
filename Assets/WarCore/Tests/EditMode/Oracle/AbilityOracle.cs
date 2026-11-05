using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace WarCore.Tests.Oracle
{
    // Standalone reference: no product types, tasks, pools, or shared numeric helpers.
    // Phase boundaries are half-open intervals evaluated from an event ledger.
    internal static class AbilityOracle
    {
        internal enum Phase { Idle, Windup, Active, Recovery }
        internal enum Failure { None, Blocked, Busy, Cooldown, InsufficientResource }
        internal sealed class Definition
        {
            internal readonly int Windup, Active, Recovery, Period, Cooldown;
            internal readonly long[] Costs;
            internal Definition(int windup,int active,int recovery,int period,int cooldown,params long[] costs)
            {
                if(windup<0 || active<0 || recovery<0 || period<1 || cooldown<0 || costs.Any(c=>c<0))
                    throw new ArgumentOutOfRangeException();
                Windup=windup;Active=active;Recovery=recovery;Period=period;Cooldown=cooldown;Costs=costs;
            }
        }
        internal sealed class Admission
        {
            internal readonly long Start,Release,ActiveEnd,End,Ready;
            internal long? Cancelled;
            internal Admission(Definition definition,long start)
            {
                Start=start;Release=Future(start,definition.Windup);ActiveEnd=Future(Release,definition.Active);
                End=Future(ActiveEnd,definition.Recovery);Ready=Future(start,definition.Cooldown);
            }
            internal Phase At(long tick)
            {
                if(tick<Start || tick>=End || (Cancelled.HasValue && tick>=Cancelled.Value))return Phase.Idle;
                if(tick<Release)return Phase.Windup;
                return tick<ActiveEnd?Phase.Active:Phase.Recovery;
            }
        }
        internal sealed class Model
        {
            internal readonly Definition Definition;
            internal readonly List<Admission> Ledger=new List<Admission>();
            internal readonly long[] Resources;
            internal long BlockedUntil;
            internal Model(Definition definition,params long[] resources)
            {Definition=definition;Resources=(long[])resources.Clone();if(resources.Length!=definition.Costs.Length)throw new ArgumentException();}
            internal Failure Admit(long tick)
            {
                if(tick<BlockedUntil)return Failure.Blocked;
                if(Ledger.Any(c=>c.At(tick)!=Phase.Idle))return Failure.Busy;
                if(Ledger.Any(c=>c.Ready>tick))return Failure.Cooldown;
                for(int i=0;i<Resources.Length;i++)if(new BigInteger(Resources[i])<Definition.Costs[i])return Failure.InsufficientResource;
                for(int i=0;i<Resources.Length;i++)Resources[i]=(long)(new BigInteger(Resources[i])-Definition.Costs[i]);
                Ledger.Add(new Admission(Definition,tick));return Failure.None;
            }
            internal void Cancel(long tick)
            {foreach(Admission cast in Ledger.Where(c=>c.At(tick)!=Phase.Idle))cast.Cancelled=tick;}
            // Q1 follows Q0: pending windup is removed now; released casts stop
            // before the next tick, without erasing this tick's released events.
            internal void Control(long tick,int duration)
            {
                BlockedUntil=Future(tick,duration);
                foreach(Admission cast in Ledger.Where(c=>c.At(tick)!=Phase.Idle))
                    cast.Cancelled=tick<cast.Release?tick:Future(tick,1);
            }
            internal Phase At(long tick)=>Ledger.Select(c=>c.At(tick)).FirstOrDefault(p=>p!=Phase.Idle);
            internal long ReleaseCount(long throughTick)=>Ledger.LongCount(c=>c.Release<=throughTick && (!c.Cancelled.HasValue || c.Release<c.Cancelled.Value));
            internal long EndCount(long throughTick)=>Ledger.LongCount(c=>c.ActiveEnd<=throughTick && (!c.Cancelled.HasValue || c.ActiveEnd<c.Cancelled.Value));
            internal long PulseCount(long throughTick)
            {
                BigInteger total=0;
                foreach(Admission c in Ledger)
                {
                    BigInteger limit=BigInteger.Min(throughTick,new BigInteger(c.ActiveEnd)-1);
                    if(c.Cancelled.HasValue)limit=BigInteger.Min(limit,new BigInteger(c.Cancelled.Value)-1);
                    BigInteger distance=limit-c.Release;
                    if(distance>0)total+=distance/Definition.Period;
                }
                return (long)total;
            }
        }
        internal static long Future(long start,int duration)
        {
            if(start<0 || start>long.MaxValue-1 || duration<0)throw new ArgumentOutOfRangeException();
            return (long)BigInteger.Min(new BigInteger(start)+duration,new BigInteger(long.MaxValue)-1);
        }
        internal static ulong[] CanonicalActors(ulong[] actors,uint[] ordinals)
        {
            if(actors.Length!=ordinals.Length)throw new ArgumentException();
            return Enumerable.Range(0,actors.Length).OrderBy(i=>actors[i]).ThenBy(i=>ordinals[i]).Select(i=>actors[i]).ToArray();
        }
    }
}
