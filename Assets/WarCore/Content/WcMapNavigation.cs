using System;
using WarCore.Sim;

namespace WarCore.Content
{
    public static class WcMapNavigation
    {
        public static NavGrid Create(WcMap map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            var bits = new byte[map.Passability.Count];
            for (int i = 0; i < bits.Length; i++) bits[i] = map.Passability[i];
            return new NavGrid((int)map.Width, (int)map.Depth, map.Min.X, map.Min.Z, bits);
        }
    }
}
