using System.Numerics;
namespace WarCore.Tests.Oracle
{
    // No product reference: select from a precedence table; all arithmetic is wide.
    public sealed class LifeOracle
    {
        public int State;
        public BigInteger Health=100,Entered,Due;
        public int Transitions;
        public void Tick(BigInteger tick,BigInteger damage,BigInteger healing,ulong tags,bool downed,int duration,ulong rescue,ulong terminate,ulong prevent=0)
        {
            if(State==2)return;
            Health=BigInteger.Max(-100,BigInteger.Min(100,Health-damage));
            Health=BigInteger.Max(-100,BigInteger.Min(100,Health+healing));
            bool lethal=Health<=0,recovered=Health>0 && (tags&rescue)==rescue;
            bool[] eligible={(tags&terminate)!=0,State==1 && recovered,State==1 && tick>=Due,State==0 && lethal};
            int[] destination={2,0,2,downed && (tags&prevent)==0?1:2};
            for(int i=0;i<eligible.Length;i++)if(eligible[i])
            {
                State=destination[i];Entered=tick;Due=State==1?BigInteger.Min(long.MaxValue-1,tick+duration):0;Transitions++;break;
            }
        }
    }
}
