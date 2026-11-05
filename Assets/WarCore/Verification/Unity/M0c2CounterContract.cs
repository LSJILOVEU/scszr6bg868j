using System;
using System.Collections.Generic;

namespace WarCore.Verification.Unity
{
    public readonly struct M0c2CounterCandidate
    {
        public readonly string Name, Category, Unit;
        public M0c2CounterCandidate(string name,string category,string unit)
        { Name=name; Category=category; Unit=unit; }
        public override string ToString() => "name="+Name+";category="+Category+";unit="+Unit;
    }
    public static class M0c2CounterContract
    {
        public const string Name="GC Allocated In Frame";
        public const string Category="Memory";
        public static int Select(IReadOnlyList<M0c2CounterCandidate> candidates)
        {
            int selected=-1,matches=0;
            for(int i=0;i<candidates.Count;i++)
                if(candidates[i].Name==Name && candidates[i].Category==Category && candidates[i].Unit=="Bytes")
                { selected=i; matches++; }
            if(matches==1)return selected;
            throw new InvalidOperationException(Failure(matches==0?"target unavailable or wrong unit":"ambiguous target",candidates));
        }
        public static string Failure(string reason,IReadOnlyList<M0c2CounterCandidate> candidates)
        {
            var lines=new string[candidates.Count];
            for(int i=0;i<candidates.Count;i++)lines[i]=candidates[i].ToString();
            Array.Sort(lines,StringComparer.Ordinal);
            return "M0c2 direct counter failure: "+reason+"; required name="+Name+";category="+Category+";unit=Bytes\nAvailable handles:\n"
                +(lines.Length==0?"<none>":string.Join("\n",lines));
        }
    }
}
