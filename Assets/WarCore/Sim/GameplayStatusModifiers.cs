using System;
namespace WarCore.Sim
{
    internal sealed partial class GameplayState
    {
        private long ScaleStatusModifier(long raw, int stacks, long limit)
        {
            // Definition validation gives |raw| <= M and 1 <= stacks <= 65535.
            // Keep the complete product in 128 bits before clamping to the stage domain.
            FixedUInt128 product = FixedUInt128.Multiply(FixedInt128.Abs(raw), (ulong)stacks);
            if (product.High != 0 || product.Low > (ulong)limit)
            {
                Count(ref numericSaturations);
                return raw < 0 ? -limit : limit;
            }
            long value = (long)product.Low;
            return raw < 0 ? -value : value;
        }
        private GameplayDamageResult EvaluateStatusDamage(GameplayCommand item, GameplayPipelineDef pipeline,
            long amount, long roll, long resistance)
        {
            if (schema.StatusCount == 0)
                return pipeline.Evaluate(amount, roll, resistance, ref numericSaturations);
            int count = 0;
            for (int i = 0; i < pipeline.ModifierCount; i++)
                statusModifiers[count++] = pipeline.ModifierAt(i);
            int low = 0, high = frozenStatusCount;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (frozenStatuses[middle].Target.Id.CompareTo(item.Target.Id) < 0)
                    low = middle + 1;
                else
                    high = middle;
            }
            for (int i = low; i < frozenStatusCount && frozenStatuses[i].Target.Id.Equals(item.Target.Id); i++)
            {
                GameplayStatusRow row = frozenStatuses[i];
                GameplayStatusDef definition = schema.StatusAt(schema.FindStatus(row.Key.Definition));
                for (int j = 0; j < definition.ModifierCount; j++)
                {
                    GameplayStatusModifierDef modifier = definition.ModifierAt(j);
                    if (modifier.DamageType != item.Definition)
                        continue;
                    GameplayDamageStages stage = modifier.Stage == GameplayModifierStage.PreFlat ? GameplayDamageStages.PreFlat :
                        modifier.Stage == GameplayModifierStage.Rate ? GameplayDamageStages.Rate :
                        modifier.Stage == GameplayModifierStage.Product ? GameplayDamageStages.Product : GameplayDamageStages.PostFlat;
                    if ((pipeline.Spec.EnabledStages & stage) == 0)
                        continue;
                    long limit = modifier.Stage == GameplayModifierStage.Rate || modifier.Stage == GameplayModifierStage.Product
                        ? GameplayMath.MaxFactor : GameplayMath.MaxMagnitude;
                    long magnitude = ScaleStatusModifier(modifier.MagnitudeRaw, row.Stacks, limit);
                    var key = new GameplayModifierKey(row.Attribution.Source, row.Attribution.CastToken,
                        row.Attribution.ActionOrdinal, modifier.Key, row.Key.Target, row.Key.Definition,
                        row.Key.HasGroupSource, row.Key.GroupSource.Id, row.Key.GroupSource.Generation, row.Key.Token);
                    statusModifiers[count++] = new GameplayModifier(modifier.Stage, magnitude, key);
                }
            }
            ValueOrder.Sort(statusModifiers.AsSpan(0, count));
            GameplayDamageSpec spec = new GameplayDamageSpec(amount, pipeline.Spec.EnabledStages, resistance,
                pipeline.Spec.PenetrationRaw, pipeline.Spec.CriticalChanceRaw, pipeline.Spec.CriticalFactorRaw,
                pipeline.Spec.MinimumRaw, pipeline.Spec.MaximumRaw);
            return GameplayDamage.Evaluate(in spec, statusModifiers, count, roll, ref numericSaturations);
        }
        private int CompareAbsorb(int a, int b)
        {
            GameplayStatusRow left = statuses[a], right = statuses[b];
            int order = schema.StatusAt(schema.FindStatus(left.Key.Definition)).Priority.CompareTo(
                schema.StatusAt(schema.FindStatus(right.Key.Definition)).Priority);
            return order != 0 ? order : left.Key.CompareTo(right.Key);
        }
        private void AbsorbSift(int root, int length)
        {
            while (root < length / 2)
            {
                int child = root * 2 + 1;
                if (child + 1 < length && CompareAbsorb(statusOrder[child], statusOrder[child + 1]) < 0)
                    child++;
                if (CompareAbsorb(statusOrder[root], statusOrder[child]) >= 0)
                    return;
                int swap = statusOrder[root];
                statusOrder[root] = statusOrder[child];
                statusOrder[child] = swap;
                root = child;
            }
        }
        private long AbsorbDamage(StableId target, ulong resource, long amount)
        {
            int count = 0, lo = 0, hi = statusCount;
            while (lo < hi)
            {
                int mid = lo + (hi - lo) / 2;
                if (statuses[mid].Target.Id.CompareTo(target) < 0)
                    lo = mid + 1;
                else
                    hi = mid;
            }
            for (int i = lo; i < statusCount && statuses[i].Target.Id.Equals(target); i++)
                if (statuses[i].AbsorbRemainingRaw > 0 && schema.StatusAt(schema.FindStatus(statuses[i].Key.Definition)).HasAbsorbResource
                    && schema.StatusAt(schema.FindStatus(statuses[i].Key.Definition)).AbsorbResourceKey == resource)
                    statusOrder[count++] = i;
            // In-place heapsort keeps the temporary owner range bounded and allocation free.
            for (int root = count / 2; root > 0;)
            {
                root--;
                AbsorbSift(root, count);
            }
            for (int end = count - 1; end > 0; end--)
            {
                int swap = statusOrder[0];
                statusOrder[0] = statusOrder[end];
                statusOrder[end] = swap;
                AbsorbSift(0, end);
            }
            for (int n = 0; n < count && amount > 0; n++)
            {
                int i = statusOrder[n];
                GameplayStatusRow row = statuses[i];
                long used = row.AbsorbRemainingRaw < amount ? row.AbsorbRemainingRaw : amount;
                row.AbsorbRemainingRaw -= used;
                amount -= used;
                WriteStatus(i, row);
            }
            return amount;
        }
    }
}
