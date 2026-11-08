using System;
namespace WarCore.Sim
{
    public sealed partial class GameplaySchema
    {
        private GameplayAbilityDef[] abilities;
        public GameplayInputAuthority InputAuthority
        {
            get;
            private set;
        }
        public int AbilityCount => abilities.Length;
        public GameplayAbilityDef AbilityAt(int index) => abilities[index];
        private void InitAbilitySchema(GameplayAbilityDef[] definitions, GameplayInputAuthority authority)
        {
            if (definitions != null && definitions.Length > GameplayMath.MaxPackets)
                throw new SimBoundaryException("M0C_ABILITY_DEFINITION_CAPACITY");
            abilities = definitions == null ? new GameplayAbilityDef[0] : (GameplayAbilityDef[])definitions.Clone();
            for (int i = 0; i < abilities.Length; i++)
                if (abilities[i] == null)
                    throw new SimBoundaryException("M0C_ABILITY_DEFINITION_NULL");
            Array.Sort(abilities);
            InputAuthority = authority;
        }
        public int FindAbility(ulong key)
        {
            int lo = 0, hi = abilities.Length - 1;
            while (lo <= hi)
            {
                int m = lo + (hi - lo) / 2;
                ulong k = abilities[m].Key;
                if (k == key)
                    return m;
                if (k < key)
                    lo = m + 1;
                else
                    hi = m - 1;
            }
            return -1;
        }
        public int FindAbilitySlot(ulong archetype, ulong slot)
        {
            for (int i = 0; i < abilities.Length; i++)
                if (abilities[i].ArchetypeKey == archetype && abilities[i].SlotKey == slot)
                    return i;
            return -1;
        }
        private void ValidateAbilities()
        {
            ValidateCapacity(Capacities.Cooldowns);
            ValidateCapacity(Capacities.Casts);
            ValidateCapacity(Capacities.InputCommands);
            ValidateCapacity(Capacities.TargetsPerCast);
            if (abilities.Length == 0)
            {
                if (Capacities.Cooldowns != 0 || Capacities.Casts != 0 || Capacities.InputCommands != 0 || Capacities.TargetsPerCast != 0 || InputAuthority != null)
                    throw new SimBoundaryException("M0C_ABILITY_CAPACITY");
                return;
            }
            if (InputAuthority == null || Capacities.Casts == 0 || Capacities.InputCommands == 0 || Capacities.TargetsPerCast == 0
                || (ulong)Capacities.Casts * (ulong)Capacities.TargetsPerCast > (ulong)GameplayMath.MaxPackets)
                throw new SimBoundaryException("M0C_ABILITY_CAPACITY");
            const long limit = (long)SimVec3.WorldLimitMetres * SimFix.Scale;
            for (int i = 0; i < abilities.Length; i++)
            {
                var d = abilities[i];
                if (d.AllowWhileDowned && LifeRuleCount == 0)
                    throw new SimBoundaryException("M0C_ABILITY_LIFE_RULE");
                ValidateName(d.Name);
                int ai = FindArchetype(d.ArchetypeKey);
                if (i > 0 && abilities[i - 1].Key == d.Key || ai < 0 || d.CooldownTicks < 0 || d.WindupTicks < 0 || d.ActiveTicks < 0 || d.RecoveryTicks < 0
                    || d.PulsePeriodTicks < 0 || d.CooldownTicks > 0 && Capacities.Cooldowns == 0)
                    throw new SimBoundaryException("M0C_ABILITY_DEFINITION");
                for (int j = 0; j < i; j++)
                    if (abilities[j].Name == d.Name || abilities[j].ArchetypeKey == d.ArchetypeKey && abilities[j].SlotKey == d.SlotKey)
                        throw new SimBoundaryException("M0C_ABILITY_SLOT");
                var t = d.Target;
                if ((int)t.Mode < 0 || (int)t.Mode > 2 || (int)t.Shape < 0 || (int)t.Shape > 3 || (int)t.Selection < 0 || (int)t.Selection > 1
                    || (int)t.Timing < 0 || (int)t.Timing > 1 || (int)t.Relation < 0 || (int)t.Relation > 2 || t.RangeRaw < 0 || t.RangeRaw > limit
                    || t.RadiusRaw < 0 || t.RadiusRaw > limit || t.HalfHeightRaw < 0 || t.HalfHeightRaw > limit || t.CosineHalfAnglePermille < -1000 || t.CosineHalfAnglePermille > 1000
                    || t.MaximumTargets < 0 || t.MaximumTargets > Capacities.TargetsPerCast || t.Mode != GameplayTargetMode.Query && t.MaximumTargets != 1
                    || t.Selection == GameplayTargetSelection.Nearest && t.MaximumTargets != 1)
                    throw new SimBoundaryException("M0C_TARGET_SPEC");
                var archetype = ArchetypeAt(ai);
                if (archetype.ResourceCount > GameplaySourceSnapshot.MaximumAttributes || d.ActionCount > GameplaySourceSnapshot.MaximumActions)
                    throw new SimBoundaryException("M0C_SOURCE_CAPTURE_CAPACITY");
                for (int c = 0; c < d.CostCount; c++)
                {
                    var cost = d.CostAt(c);
                    GameplayMath.RequireMagnitude(cost.AmountRaw);
                    int ri = FindResource(cost.ResourceKey);
                    bool found = false;
                    for (int r = 0; r < archetype.ResourceCount; r++)
                        if (archetype.ResourceKeyAt(r) == cost.ResourceKey)
                            found = true;
                    if (ri < 0 || !found || !ResourceAt(ri).AllowSpend)
                        throw new SimBoundaryException("M0C_ABILITY_COST");
                }
                bool pulse = false;
                long captureBudget = d.ActionCount;
                for (int a = 0; a < d.ActionCount; a++)
                {
                    var action = d.ActionAt(a);
                    if (a > 0 && d.ActionAt(a - 1).Ordinal == action.Ordinal || (int)action.Trigger < 0 || (int)action.Trigger > 2
                        || (int)action.Primitive < 0 || (int)action.Primitive > 6 || action.Maximum < 0 || action.Maximum > GameplayMath.MaxPackets)
                        throw new SimBoundaryException("M0C_ABILITY_ACTION");
                    pulse |= action.Trigger == GameplayActionTrigger.ActivePulse;
                    if (action.Primitive == GameplayPrimitive.ChangeResource)
                        GameplayMath.RequireSignedMagnitude(action.AmountRaw);
                    else if (action.Primitive != GameplayPrimitive.ApplyStatus || action.AmountRaw != -1)
                        GameplayMath.RequireMagnitude(action.AmountRaw);
                    if (action.Primitive == GameplayPrimitive.Damage && FindDamageType(action.Definition) < 0
                        || action.Primitive == GameplayPrimitive.ChangeResource && FindResource(action.Definition) < 0
                        || (action.Primitive == GameplayPrimitive.ApplyStatus || action.Primitive == GameplayPrimitive.GrantAbsorb) && FindStatus(action.Definition) < 0)
                        throw new SimBoundaryException("M0C_ABILITY_ACTION_REFERENCE");
                    if (action.Primitive == GameplayPrimitive.GrantAbsorb && !StatusAt(FindStatus(action.Definition)).HasAbsorbResource)
                        throw new SimBoundaryException("M0C_ABILITY_ABSORB");
                    if (action.HasAmountAttribute || action.HasMultiplierAttribute)
                    {
                        bool amount = !action.HasAmountAttribute, multiplier = !action.HasMultiplierAttribute;
                        for (int r = 0; r < archetype.ResourceCount; r++)
                        {
                            ulong key = archetype.ResourceKeyAt(r);
                            var resource = ResourceAt(FindResource(key));
                            if (action.HasAmountAttribute && key == action.AmountAttributeKey)
                            {
                                amount = true;
                                if (resource.LowerRaw < 0 && action.Primitive != GameplayPrimitive.ChangeResource)
                                    throw new SimBoundaryException("M0C_ABILITY_ATTRIBUTE_RANGE");
                            }
                            if (action.HasMultiplierAttribute && key == action.MultiplierAttributeKey)
                            {
                                multiplier = true;
                                if (resource.LowerRaw < 0 || resource.UpperRaw > GameplayMath.MaxFactor)
                                    throw new SimBoundaryException("M0C_ABILITY_MULTIPLIER_RANGE");
                            }
                        }
                        if (!amount || !multiplier || action.HasMultiplierAttribute && action.Primitive != GameplayPrimitive.Damage)
                            throw new SimBoundaryException("M0C_ABILITY_ATTRIBUTE_REFERENCE");
                    }
                    if (action.Primitive == GameplayPrimitive.ApplyStatus || action.Primitive == GameplayPrimitive.GrantAbsorb)
                        captureBudget += StatusAt(FindStatus(action.Definition)).PeriodicActionCount;
                    if ((action.Primitive == GameplayPrimitive.RequestMove || action.Primitive == GameplayPrimitive.SpawnPlaceholder) && action.RequiredCapability)
                        throw new SimBoundaryException("M0C_REQUIRED_CAPABILITY");
                }
                if (captureBudget > GameplaySourceSnapshot.MaximumActions)
                    throw new SimBoundaryException("M0C_SOURCE_CAPTURE_CAPACITY");
                if (pulse && d.PulsePeriodTicks == 0 || !pulse && d.PulsePeriodTicks != 0)
                    throw new SimBoundaryException("M0C_ABILITY_PERIOD");
                if ((ulong)d.ActionCount * (ulong)t.MaximumTargets > (ulong)GameplayMath.MaxPackets)
                    throw new SimBoundaryException("M0C_ABILITY_FANOUT");
            }
        }
        private void HashAbilities(StateHash h)
        {
            h.Add(InputAuthority.Key);
            h.Add(InputAuthority.ActorCount);
            for (int i = 0; i < InputAuthority.ActorCount; i++)
                h.Add(InputAuthority.ActorAt(i).Value);
            h.Add(archetypes.Length);
            for (int i = 0; i < archetypes.Length; i++)
            {
                h.Add(archetypes[i].Key);
                h.Add(archetypes[i].FactionKey);
                h.Add(archetypes[i].Tags);
            }
            h.Add(abilities.Length);
            for (int i = 0; i < abilities.Length; i++)
            {
                var d = abilities[i];
                var t = d.Target;
                h.Add(d.Key);
                h.Add(d.Name);
                h.Add(d.SlotKey);
                h.Add(d.ArchetypeKey);
                h.Add(d.CooldownKey);
                h.Add(d.CooldownTicks);
                h.Add(d.WindupTicks);
                h.Add(d.ActiveTicks);
                h.Add(d.RecoveryTicks);
                h.Add(d.PulsePeriodTicks);
                h.Add(d.InterruptTags);
                h.Add((int)t.Mode);
                h.Add((int)t.Shape);
                h.Add((int)t.Selection);
                h.Add((int)t.Timing);
                h.Add((int)t.Relation);
                h.Add(t.RangeRaw);
                h.Add(t.RadiusRaw);
                h.Add(t.HalfHeightRaw);
                h.Add((ulong)t.LayerMask);
                h.Add((ulong)t.OcclusionMask);
                h.Add(t.MaximumTargets);
                h.Add(t.CosineHalfAnglePermille);
                h.Add(t.RequiresAll);
                h.Add(t.UseTargetPoint);
                h.Add(t.ExcludeSelf);
                h.Add(t.RequiredTags);
                h.Add(t.RejectedTags);
                h.Add(d.CostCount);
                for (int j = 0; j < d.CostCount; j++)
                {
                    h.Add(d.CostAt(j).ResourceKey);
                    h.Add(d.CostAt(j).AmountRaw);
                }
                h.Add(d.ActionCount);
                for (int j = 0; j < d.ActionCount; j++)
                {
                    var a = d.ActionAt(j);
                    h.Add((ulong)a.Ordinal);
                    h.Add((int)a.Trigger);
                    h.Add((int)a.Primitive);
                    h.Add(a.Definition);
                    h.Add(a.AmountRaw);
                    h.Add(a.Maximum);
                    h.Add(a.RequiredCapability);
                    h.Add(a.HasAmountAttribute);
                    h.Add(a.AmountAttributeKey);
                    h.Add(a.HasMultiplierAttribute);
                    h.Add(a.MultiplierAttributeKey);
                }
            }
        }
    }
}
