using System;
using WarCore.Content;
using WarCore.Sim;

namespace WarCore.Presentation
{
    // SLOPE-1: slopes slow the foot step. On only when the pack the world reads (WorldRecipe.Pack, else TerrainPack) has
    // movement.slope or stats.speed, so every other world keeps its exact systems, snapshots and hashes. Prepare checks the
    // two packs name the same content and adds the content key "movement.step" (so a snapshot cannot be restored under another
    // slope table or other clamps); RegisterRules hands the table and the clamps to the locomotion. No component, no system.
    internal sealed class SlopeFeature : WorldStageFeature
    {
        internal static readonly SlopeFeature Instance = new SlopeFeature();
        internal const string ContentKeyName = "movement.step";
        // Fed first into the content key's digest, so it can never equal another key's value by accident.
        internal const string DigestPrefix = "movement.step.v1";

        public override string OptionId => "world.slope";

        // The pack the world's features read: the recipe's pack, else its terrain pack (a recipe built by hand that only
        // calls WithTerrainRules still gets its slopes).
        internal static ContentPack PackOf(WorldRecipe recipe) => recipe.Pack ?? recipe.TerrainPack;

        public override bool IsActive(WorldAssembly a)
        {
            ContentPack pack = PackOf(a.Recipe);
            return pack != null && (Slope(pack) != null || SpeedRow(pack) != null);
        }

        public override void Prepare(WorldAssembly a)
        {
            ContentPack pack = a.Recipe.Pack, terrain = a.Recipe.TerrainPack;
            if (pack != null && terrain != null && pack.Id != terrain.Id)
                throw new ArgumentException("The recipe's pack '" + pack.Id + "' and its terrain pack '" + terrain.Id + "' differ.", "recipe");
            a.ContentKeys.Add(new StableContentKey(ContentKeyName, Digest(PackOf(a.Recipe))));
        }

        public override void RegisterRules(WorldAssembly a)
        {
            ContentPack pack = PackOf(a.Recipe);
            StatClampRow speed = SpeedRow(pack);
            a.Locomotion.SetStepLine(Slope(pack), speed == null ? 0 : speed.DragFloorPermille, speed == null ? 0 : speed.DragCeilPermille);
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
            StatClampRow speed = SpeedRow(pack);
            hash.Add(speed == null ? 0 : speed.DragFloorPermille);
            hash.Add(speed == null ? 0 : speed.DragCeilPermille);
            return hash.Value;
        }

        private static SlopeTable Slope(ContentPack pack) => pack.SectionOrNull<MovementRules>()?.Slope;

        private static StatClampRow SpeedRow(ContentPack pack) => pack.SectionOrNull<StatClamps>()?.Speed;
    }
}
