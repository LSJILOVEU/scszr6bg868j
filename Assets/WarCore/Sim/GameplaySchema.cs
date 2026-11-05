using System;

namespace WarCore.Sim
{
    public sealed partial class GameplaySchema
    {
        private readonly GameplayResourceDef[] resources;
        private readonly GameplayDamageTypeDef[] damageTypes;
        private readonly GameplayPipelineDef[] pipelines;
        private readonly GameplayArchetypeDef[] archetypes;
        private readonly GameplayStatusDef[] statuses;
        public int StatusCount=>statuses.Length;
        public GameplayStatusDef StatusAt(int index)=>statuses[index];
        public ulong RulesDigest { get; }
        public ulong CapacityDigest { get; }
        public GameplayCapacities Capacities { get; }
        public string CriticalStream { get; }
        public int ResourceCount => resources.Length;
        public int DamageTypeCount => damageTypes.Length;
        public int PipelineCount => pipelines.Length;
        public int ArchetypeCount => archetypes.Length;
        public bool IsEmpty => resources.Length == 0 && damageTypes.Length == 0 && pipelines.Length == 0 && archetypes.Length == 0;
        public GameplayResourceDef ResourceAt(int index) => resources[index];
        public GameplayDamageTypeDef DamageTypeAt(int index) => damageTypes[index];
        public GameplayPipelineDef PipelineAt(int index) => pipelines[index];
        public GameplayArchetypeDef ArchetypeAt(int index) => archetypes[index];
        public GameplaySchema(ulong rulesDigest)
        {
            resources=new GameplayResourceDef[0]; damageTypes=new GameplayDamageTypeDef[0];
            pipelines=new GameplayPipelineDef[0]; archetypes=new GameplayArchetypeDef[0];
            statuses=new GameplayStatusDef[0];abilities=new GameplayAbilityDef[0];
            CriticalStream=string.Empty;
            RulesDigest=rulesDigest;
            ulong hash=14695981039346656037UL;
            StableEntityKey.Word(ref hash,1);
            for (int i=0;i<7;i++) StableEntityKey.Word(ref hash,0);
            CapacityDigest=StableEntityKey.Mix(hash);
        }
        public GameplaySchema(GameplayResourceDef[] resources, GameplayDamageTypeDef[] damageTypes,
            GameplayPipelineDef[] pipelines, GameplayArchetypeDef[] archetypes, GameplayCapacities capacities,
            string criticalStream,GameplayStatusDef[] statuses=null,GameplayAbilityDef[] abilities=null,GameplayInputAuthority inputAuthority=null,GameplayLifeRuleDef[] lifeRules=null)
        {
            ValidateArray(resources); ValidateArray(damageTypes); ValidateArray(pipelines); ValidateArray(archetypes);
            ValidateCapacity(capacities.Owners); ValidateCapacity(capacities.Resources);
            ValidateCapacity(capacities.Statuses);
            ValidateCapacity(capacities.Commands); ValidateCapacity(capacities.Notices);
            ValidateName(criticalStream);
            this.statuses=statuses==null?new GameplayStatusDef[0]:(GameplayStatusDef[])statuses.Clone();
            if(this.statuses.Length>GameplayMath.MaxPackets)throw new SimBoundaryException("M0C_STATUS_DEFINITION_CAPACITY");
            for(int i=0;i<this.statuses.Length;i++)if(this.statuses[i]==null)throw new SimBoundaryException("M0C_STATUS_DEFINITION_NULL");
            Array.Sort(this.statuses);
            this.resources=(GameplayResourceDef[])resources.Clone();
            this.damageTypes=(GameplayDamageTypeDef[])damageTypes.Clone();
            this.pipelines=(GameplayPipelineDef[])pipelines.Clone();
            this.archetypes=(GameplayArchetypeDef[])archetypes.Clone();
            for (int i=0;i<this.pipelines.Length;i++)
                if (this.pipelines[i] == null)
                    throw new SimBoundaryException("M0C_PIPELINE_DEFINITION: Null pipeline.");
            for (int i=0;i<this.archetypes.Length;i++)
                if (this.archetypes[i] == null)
                    throw new SimBoundaryException("M0C_ARCHETYPE_DEFINITION: Null archetype.");
            Array.Sort(this.resources); Array.Sort(this.damageTypes); Array.Sort(this.pipelines); Array.Sort(this.archetypes);
            Capacities=capacities; CriticalStream=criticalStream;
            InitAbilitySchema(abilities,inputAuthority);
            ValidateDefinitions();ValidateStatuses();InitLifeRules(lifeRules);ValidateAbilities();
            var capacityHash=new StateHash();
            capacityHash.Add(LifeRuleCount!=0?5UL:this.abilities.Length!=0?4UL:this.statuses.Length==0?2UL:3UL);
            // Owners, resources, cooldowns, casts, statuses, deferred, notices, commands.
            capacityHash.Add(capacities.Owners); capacityHash.Add(capacities.Resources);
            capacityHash.Add(capacities.Cooldowns);capacityHash.Add(capacities.Casts);capacityHash.Add(capacities.Statuses);capacityHash.Add(0UL);
            capacityHash.Add(capacities.Notices); capacityHash.Add(capacities.Commands);
            if(this.abilities.Length!=0){capacityHash.Add(capacities.InputCommands);capacityHash.Add(capacities.TargetsPerCast);}
            if(LifeRuleCount!=0)capacityHash.Add(NoticeStagingCapacity);
            CapacityDigest=StableEntityKey.Mix(capacityHash.Value);
            RulesDigest=ComputeRulesDigest();
        }

        private static void ValidateArray<T>(T[] values)
        {
            if (values == null || values.Length > GameplayMath.MaxPackets)
                throw new SimBoundaryException("M0C_GAMEPLAY_DEFINITIONS: Missing or excessive frozen definitions.");
        }
        private static void ValidateCapacity(int value)
        {
            if (value < 0 || value > GameplayMath.MaxPackets)
                throw new SimBoundaryException("M0C_GAMEPLAY_CAPACITY: Capacity is outside the declared packet domain.");
        }
        private static void ValidateName(string name)
        {
            // Reuse the content-name Unicode guard; no manifest identity is inferred here.
            new StableContentKey(name,0);
        }
        private void ValidateDefinitions()
        {
            for (int i=0;i<resources.Length;i++)
            {
                GameplayResourceDef resource=resources[i];
                ValidateName(resource.Name);
                if ((i != 0 && resources[i-1].Key == resource.Key)
                    || resource.LowerRaw < -GameplayMath.MaxMagnitude || resource.UpperRaw > GameplayMath.MaxMagnitude
                    || resource.LowerRaw > resource.UpperRaw || resource.InitialRaw < resource.LowerRaw
                    || resource.InitialRaw > resource.UpperRaw || resource.RegenAmountRaw < 0
                    || resource.RegenAmountRaw > GameplayMath.MaxMagnitude || resource.RegenPeriodTicks < 0
                    || (resource.RegenAmountRaw != 0 && resource.RegenPeriodTicks == 0)
                    || (resource.IsLife && (resource.LifeThresholdRaw < resource.LowerRaw || resource.LifeThresholdRaw > resource.UpperRaw))
                    || (!resource.IsLife && resource.LifeThresholdRaw != 0))
                    throw new SimBoundaryException("M0C_RESOURCE_RANGE: Invalid resource bounds, recovery, role or duplicate key.");
                for (int j=0;j<i;j++)
                    if (resources[j].Name == resource.Name)
                        throw new SimBoundaryException("M0C_RESOURCE_NAME: Duplicate resource name.");
            }
            for (int i=0;i<pipelines.Length;i++)
            {
                if (i != 0 && pipelines[i-1].Key == pipelines[i].Key)
                    throw new SimBoundaryException("M0C_PIPELINE_KEY: Duplicate pipeline key.");
                pipelines[i].Validate();
            }
            for (int i=0;i<damageTypes.Length;i++)
            {
                GameplayDamageTypeDef type=damageTypes[i];
                ValidateName(type.Name);
                if ((i != 0 && damageTypes[i-1].Key == type.Key)
                    || FindResource(type.ResourceKey) < 0 || FindPipeline(type.PipelineKey) < 0)
                    throw new SimBoundaryException("M0C_DAMAGE_TYPE: Duplicate key or missing resource/pipeline reference.");
                for (int j=0;j<i;j++)
                    if (damageTypes[j].Name == type.Name)
                        throw new SimBoundaryException("M0C_DAMAGE_TYPE_NAME: Duplicate damage type name.");
            }
            for (int i=0;i<archetypes.Length;i++)
            {
                GameplayArchetypeDef archetype=archetypes[i];
                if ((i != 0 && archetypes[i-1].Key == archetype.Key) || archetype.ResourceCount > Capacities.Resources)
                    throw new SimBoundaryException("M0C_ARCHETYPE: Duplicate key or insufficient resource capacity.");
                int lifeCount=0;
                for (int j=0;j<archetype.ResourceCount;j++)
                {
                    ulong key=archetype.ResourceKeyAt(j);
                    int resource=FindResource(key);
                    if (resource < 0 || (j != 0 && archetype.ResourceKeyAt(j-1) == key))
                        throw new SimBoundaryException("M0C_ARCHETYPE_RESOURCE: Missing or repeated resource reference.");
                    if (resources[resource].IsLife) lifeCount++;
                }
                if (lifeCount > 1)
                    throw new SimBoundaryException("M0C_ARCHETYPE_LIFE: At most one life resource may be bound.");
                for (int j=0;j<archetype.DefenseCount;j++)
                {
                    GameplayDefenseDef defense=archetype.DefenseAt(j);
                    if (FindDamageType(defense.DamageTypeKey) < 0 || defense.ResistanceRaw < 0
                        || defense.ResistanceRaw > GameplayMath.Scale
                        || (j != 0 && archetype.DefenseAt(j-1).DamageTypeKey == defense.DamageTypeKey))
                        throw new SimBoundaryException("M0C_ARCHETYPE_DEFENSE: Missing damage type, duplicate key or invalid resistance ratio.");
                }
            }
            if (!IsEmpty && (Capacities.Owners == 0 || Capacities.Resources == 0))
                throw new SimBoundaryException("M0C_GAMEPLAY_CAPACITY: Nonempty definitions require owner and resource capacity.");
            if (Capacities.Notices < Capacities.Resources)
                throw new SimBoundaryException("M0C_GAMEPLAY_CAPACITY: Notices must cover all final resource facts.");
        }
        public int FindResource(ulong key)
        {
            int low=0,high=resources.Length-1;
            while (low<=high) { int middle=low+(high-low)/2; ulong value=resources[middle].Key;
                if (value==key) return middle; if (value<key) low=middle+1; else high=middle-1; }
            return -1;
        }
        public int FindDamageType(ulong key)
        {
            int low=0,high=damageTypes.Length-1;
            while (low<=high) { int middle=low+(high-low)/2; ulong value=damageTypes[middle].Key;
                if (value==key) return middle; if (value<key) low=middle+1; else high=middle-1; }
            return -1;
        }
        public int FindPipeline(ulong key)
        {
            int low=0,high=pipelines.Length-1;
            while (low<=high) { int middle=low+(high-low)/2; ulong value=pipelines[middle].Key;
                if (value==key) return middle; if (value<key) low=middle+1; else high=middle-1; }
            return -1;
        }
        public int FindArchetype(ulong key)
        {
            int low=0,high=archetypes.Length-1;
            while (low<=high) { int middle=low+(high-low)/2; ulong value=archetypes[middle].Key;
                if (value==key) return middle; if (value<key) low=middle+1; else high=middle-1; }
            return -1;
        }
        private ulong ComputeRulesDigest()
        {
            var hash=new StateHash();
            hash.Add(LifeRuleCount!=0?"warcore.gameplay-rules-s4-v1":abilities.Length!=0?"warcore.gameplay-rules-s3-v1":statuses.Length==0?"warcore.gameplay-rules-s1-v1":"warcore.gameplay-rules-s2-v1"); hash.Add(CapacityDigest); hash.Add(CriticalStream);
            hash.Add(resources.Length);
            foreach (GameplayResourceDef value in resources)
            {
                hash.Add(value.Key); hash.Add(value.Name); hash.Add(value.LowerRaw); hash.Add(value.UpperRaw);
                hash.Add(value.InitialRaw); hash.Add(value.RegenAmountRaw); hash.Add(value.RegenPeriodTicks);
                hash.Add(value.AllowSpend); hash.Add(value.IsLife); hash.Add(value.LifeThresholdRaw);
            }
            hash.Add(damageTypes.Length);
            foreach (GameplayDamageTypeDef value in damageTypes)
            { hash.Add(value.Key); hash.Add(value.Name); hash.Add(value.ResourceKey); hash.Add(value.PipelineKey); }
            hash.Add(pipelines.Length);
            foreach (GameplayPipelineDef value in pipelines)
            {
                GameplayDamageSpec spec=value.Spec;
                hash.Add(value.Key); hash.Add(spec.BaseRaw); hash.Add((int)spec.EnabledStages);
                hash.Add(spec.ResistanceRaw); hash.Add(spec.PenetrationRaw); hash.Add(spec.CriticalChanceRaw);
                hash.Add(spec.CriticalFactorRaw); hash.Add(spec.MinimumRaw); hash.Add(spec.MaximumRaw);
                hash.Add(value.ModifierCount);
                for (int i=0;i<value.ModifierCount;i++)
                {
                    GameplayModifier modifier=value.ModifierAt(i); GameplayModifierKey key=modifier.Key;
                    hash.Add((int)modifier.Stage); hash.Add(modifier.MagnitudeRaw);
                    hash.Add(key.Source.Value); hash.Add(key.CastToken); hash.Add((ulong)key.ActionOrdinal);
                    hash.Add(key.StatusTarget.Value); hash.Add(key.StatusDefKey); hash.Add(key.HasGroupSource);
                    hash.Add(key.GroupSource.Value); hash.Add((ulong)key.GroupSourceGeneration);
                    hash.Add(key.StatusToken); hash.Add(key.ModifierKey);
                }
            }
            hash.Add(archetypes.Length);
            foreach (GameplayArchetypeDef value in archetypes)
            {
                hash.Add(value.Key); hash.Add(value.ResourceCount);
                for (int i=0;i<value.ResourceCount;i++) hash.Add(value.ResourceKeyAt(i));
                hash.Add(value.DefenseCount);
                for (int i=0;i<value.DefenseCount;i++)
                { hash.Add(value.DefenseAt(i).DamageTypeKey); hash.Add(value.DefenseAt(i).ResistanceRaw); }
            }
            if(statuses.Length!=0)HashStatuses(hash);
            if(abilities.Length!=0)HashAbilities(hash);
            if(LifeRuleCount!=0)HashLifeRules(hash);
            return StableEntityKey.Mix(hash.Value);
        }
    }
}
