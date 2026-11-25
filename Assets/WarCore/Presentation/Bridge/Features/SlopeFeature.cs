using System;
using WarCore.Content;
using WarCore.Sim;

namespace WarCore.Presentation
{
    // SLOPE-1: slopes slow the foot step. On only when the pack the world reads (WorldRecipe.Pack) has movement.slope or
    // stats.speed, so every other world keeps its exact systems, snapshots and hashes. Prepare adds the content key
    // "movement.step" (so a snapshot cannot be restored under another slope table or other clamps); RegisterRules hands the
    // table and the clamps to the locomotion. No component, no system.
    internal sealed class SlopeFeature : WorldStageFeature
    {
        internal static readonly SlopeFeature Instance = new SlopeFeature();
        internal const string ContentKeyName = "movement.step";
        // Fed first into the content key's digest, so it can never equal another key's value by accident.
        internal const string DigestPrefix = "movement.step.v1";

        public override string OptionId => "world.slope";

        public override bool IsActive(WorldAssembly a)
        {
            ContentPack pack = a.Recipe.Pack;
            return pack != null && (Slope(pack) != null || HasSpeedRow(pack));
        }

        public override void Prepare(WorldAssembly a)
        {
            a.ContentKeys.Add(new StableContentKey(ContentKeyName, Digest(a.Recipe.Pack)));
        }

        public override void RegisterRules(WorldAssembly a)
        {
            ContentPack pack = a.Recipe.Pack;
            StatClamps stats = pack.SectionOrNull<StatClamps>();
            a.Locomotion.SetStepLine(Slope(pack), stats?.FloorOf(StatClamps.Speed) ?? 0, stats?.CeilOf(StatClamps.Speed) ?? 0);
        }

        // The content key's digest: the prefix, then the slope table (-1 when absent; else its band counts and bands, up
        // first), then the speed row's floor and ceiling (0 when not written).
        internal static ulong Digest(ContentPack pack)
        {
            var hash = new StateHash();
            hash.Add(DigestPrefix);
            SlopeTable slope = Slope(pack);
            if (slope == null) hash.Add(-1);
            else
            {
                hash.Add(slope.UpCount);
                for (int i = 0; i < slope.UpCount; i++) { hash.Add(slope.UpAbove(i)); hash.Add(slope.UpSpeed(i)); }
                hash.Add(slope.DownCount);
                for (int i = 0; i < slope.DownCount; i++) { hash.Add(slope.DownAbove(i)); hash.Add(slope.DownSpeed(i)); }
            }
            StatClamps stats = pack.SectionOrNull<StatClamps>();
            hash.Add(stats?.FloorOf(StatClamps.Speed) ?? 0);
            hash.Add(stats?.CeilOf(StatClamps.Speed) ?? 0);
            return hash.Value;
        }

        private static SlopeTable Slope(ContentPack pack) => pack.SectionOrNull<MovementRules>()?.Slope;

        // Only the speed row turns the feature on: a layer with other stats rows (stats.spread) keeps the old step.
        private static bool HasSpeedRow(ContentPack pack) => pack.SectionOrNull<StatClamps>()?.Has(StatClamps.Speed) ?? false;
    }
}
