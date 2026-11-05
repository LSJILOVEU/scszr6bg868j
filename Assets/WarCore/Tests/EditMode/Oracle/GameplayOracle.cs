using System;
using System.Numerics;

namespace WarCore.Tests.Oracle
{
    // No product assembly reference: integer rational expressions use BCL arbitrary precision.
    internal static class GameplayOracle
    {
        internal const long S=65536, M=140737488355327, F=2147483647;
        internal static long Clamp(BigInteger value, long low, long high, ref long saturations)
        {
            if (value < low) { saturations++; return low; }
            if (value > high) { saturations++; return high; }
            return (long)value;
        }
        internal static long Change(long value, long delta, long low, long high, ref long saturations)
            => Clamp(new BigInteger(value)+delta,low,high,ref saturations);
        internal static long Product(long value, long factor, ref long saturations)
            => Clamp(new BigInteger(value)*factor/S,0,M,ref saturations);
        internal static long FutureTick(long now, int duration, ref long saturations)
        {
            BigInteger limit=(BigInteger.One<<63)-2;
            if(now<0 || new BigInteger(now)>limit || duration<=0 || saturations<0)
                throw new ArgumentOutOfRangeException();
            BigInteger sum=new BigInteger(now)+duration;
            BigInteger future=BigInteger.Min(sum,limit);
            if(future<=now)throw new ArgumentOutOfRangeException();
            if(sum>limit)
            {
                BigInteger counter=new BigInteger(saturations)+1;
                if(counter>long.MaxValue)throw new ArgumentOutOfRangeException();
                saturations=(long)counter;
            }
            return (long)future;
        }
        // A stage is evaluated from a complete list, not a cursor through product implementation data.
        internal static long Damage(long initial, int enabled, long resistance, long penetration,
            long chance, long criticalFactor, long minimum, long maximum, long roll,
            int[] stages, long[] magnitudes, ref long saturations, out bool critical)
        {
            BigInteger x=initial;
            for (int phase=1; phase<=7; phase++)
            {
                if ((enabled & (1 << (phase-1)))==0) continue;
                if (phase==1 || phase==6)
                {
                    BigInteger sum=0;
                    for (int i=0; i<stages.Length; i++) if (stages[i]==phase) sum+=magnitudes[i];
                    x=Clamp(x+sum,0,M,ref saturations);
                }
                else if (phase==2)
                {
                    BigInteger sum=S;
                    for (int i=0; i<stages.Length; i++) if (stages[i]==2) sum+=magnitudes[i];
                    long rate=Clamp(sum,0,F,ref saturations);
                    x=Clamp(x*rate/S,0,M,ref saturations);
                }
                else if (phase==3)
                {
                    for (int i=0; i<stages.Length; i++) if (stages[i]==3)
                        x=Clamp(x*magnitudes[i]/S,0,M,ref saturations);
                }
                else if (phase==4)
                {
                    long reduction=Clamp(new BigInteger(resistance)-penetration,0,S,ref saturations);
                    x=Clamp(x*(S-reduction)/S,0,M,ref saturations);
                }
                else if (phase==5 && roll<chance)
                    x=Clamp(x*criticalFactor/S,0,M,ref saturations);
                else if (phase==7)
                    x=Clamp(x,minimum,maximum,ref saturations);
            }
            critical=(enabled&16)!=0 && roll<chance;
            return (long)x;
        }
    }
}
