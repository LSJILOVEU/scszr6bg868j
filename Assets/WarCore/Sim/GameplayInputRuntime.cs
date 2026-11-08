using System;
namespace WarCore.Sim
{
    internal sealed partial class GameplayState
    {
        private bool abilityInputInvalid;
        private static int CompareAbilityInput(GameplayInputCommand a, GameplayInputCommand b)
        {
            int c = a.CompareTo(b);
            if (c != 0)
                return c;
            c = a.Actor.Generation.CompareTo(b.Actor.Generation);
            if (c != 0)
                return c;
            c = ((int)a.Kind).CompareTo((int)b.Kind);
            if (c != 0)
                return c;
            c = a.SlotKey.CompareTo(b.SlotKey);
            if (c != 0)
                return c;
            c = ((int)a.TargetMode).CompareTo((int)b.TargetMode);
            if (c != 0)
                return c;
            c = a.HasExplicitTarget.CompareTo(b.HasExplicitTarget);
            if (c != 0)
                return c;
            c = a.ExplicitTarget.Id.CompareTo(b.ExplicitTarget.Id);
            if (c != 0)
                return c;
            c = a.ExplicitTarget.Generation.CompareTo(b.ExplicitTarget.Generation);
            if (c != 0)
                return c;
            c = CompareAbilityVector(a.AimDirection, b.AimDirection);
            return c != 0 ? c : CompareAbilityVector(a.TargetPoint, b.TargetPoint);
        }
        private static int CompareAbilityVector(SimVec3 a, SimVec3 b)
        {
            int c = a.X.Raw.CompareTo(b.X.Raw);
            if (c != 0)
                return c;
            c = a.Y.Raw.CompareTo(b.Y.Raw);
            return c != 0 ? c : a.Z.Raw.CompareTo(b.Z.Raw);
        }
        private void AbilityInputSift(int root, int length)
        {
            while (root < length / 2)
            {
                int child = root * 2 + 1;
                if (child + 1 < length && CompareAbilityInput(abilityInput[child], abilityInput[child + 1]) < 0)
                    child++;
                if (CompareAbilityInput(abilityInput[root], abilityInput[child]) >= 0)
                    return;
                var swap = abilityInput[root];
                abilityInput[root] = abilityInput[child];
                abilityInput[child] = swap;
                root = child;
            }
        }
        private void SortAbilityInput()
        {
            for (int root = abilityInputCount / 2; root > 0;)
            {
                root--;
                AbilityInputSift(root, abilityInputCount);
            }
            for (int end = abilityInputCount - 1; end > 0; end--)
            {
                var swap = abilityInput[0];
                abilityInput[0] = abilityInput[end];
                abilityInput[end] = swap;
                AbilityInputSift(0, end);
            }
        }
        internal void CaptureAbilityInput(InputFrame input, long tick)
        {
            if (schema.AbilityCount == 0)
                return;
            abilityInputCount = resultCount = 0;
            abilityInputInvalid = false;
            if (!input.HasValidSkillCommandShape || input.SkillCommandCount > abilityInput.Length)
            {
                RecordAbilityFailure(GameplayAbilityFailure.InvalidInput);
                QueueNotice(10, default, default, 5, new StableSimEvent(GameplayEvents.AbilityFailure, tag: (int)GameplayAbilityFailure.InvalidInput));
                return;
            }
            abilityInputCount = input.SkillCommandCount;
            for (int i = 0; i < abilityInputCount; i++)
                abilityInput[i] = input.SkillCommandAt(i);
            abilityInputInvalid = abilityInputCount != 0 && input.GameplayTick != tick;
            SortAbilityInput(); // S3_MUTATION:input_canonical_sort
        }
        private void RecordAbilityFailure(GameplayAbilityFailure failure)
        {
            if (failure == GameplayAbilityFailure.None)
                return;
            Count(ref abilityFailures[(int)failure]);
            RefreshDiagnostics();
        }
        private GameplayAbilityFailure ValidateAbilityInput(GameplayInputCommand input, bool duplicate, out int owner, out int definition)
        {
            owner = definition = -1;
            if (abilityInputInvalid || (int)input.Kind < 0 || (int)input.Kind > 1 || (int)input.TargetMode < 0 || (int)input.TargetMode > 2
                || !AbilityVectorValid(input.AimDirection) || !AbilityVectorValid(input.TargetPoint))
                return GameplayAbilityFailure.InvalidInput;
            if (duplicate)
                return GameplayAbilityFailure.DuplicateCommand; // S3_MUTATION:input_duplicate
            if (!schema.InputAuthority.Allows(input.Actor.Id))
                return GameplayAbilityFailure.UnauthorizedActor; // S3_MUTATION:input_authority
            owner = OwnerIndex(input.Actor.Id);
            if (owner < 0 || !sim.Entities.TryResolve(input.Actor.Id, out _))
                return GameplayAbilityFailure.ActorGone;
            if (generations[owner] != input.Actor.Generation || Generation(input.Actor.Id) != input.Actor.Generation)
                return GameplayAbilityFailure.ActorLifeMismatch;
            definition = schema.FindAbilitySlot(archetypes[owner], input.SlotKey);
            if (definition < 0)
                return GameplayAbilityFailure.UnknownAbility;
            return GameplayAbilityFailure.None;
        }
        internal void RunAbilityQ0()
        {
            if (schema.AbilityCount == 0)
                return;
            // Complete half-open casts that are already Idle at this tick. Other
            // due releases wait until ordered inputs, so Cancel never jumps ahead of Activate.
            AdvanceCompletedAbilitiesBeforeInput();
            for (int i = 0; i < abilityInputCount; i++)
            {
                var command = abilityInput[i];
                bool duplicate = i > 0 && abilityInput[i - 1].CompareTo(command) == 0
                    || i + 1 < abilityInputCount && abilityInput[i + 1].CompareTo(command) == 0;
                var failure = ValidateAbilityInput(command, duplicate, out int owner, out int definition);
                ulong token = 0;
                int written = 0, total = 0;
                if (failure == GameplayAbilityFailure.None)
                {
                    if (command.Kind == GameplayInputKind.Cancel)
                    {
                        int cast = FindAbilityCast(command.Actor.Id);
                        if (cast >= 0)
                            DeleteAbilityCast(cast);
                    }
                    else
                        failure = AdmitAbility(command, owner, schema.AbilityAt(definition), out token, out written, out total);
                }
                RecordAbilityFailure(failure);
                AbilityFailureNotice(command.Actor, command.CommandOrdinal, failure, token);
                abilityResults[resultCount++] = new GameplayInputResult(command, failure, token, written, total);
            }
            AdvanceAbilityReleases();
            RefreshAbilityHash();
            hasAdmission = false; // TASK_B: separate input and script admission domains
        }
    }
}
