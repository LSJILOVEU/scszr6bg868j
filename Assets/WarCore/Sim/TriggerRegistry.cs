using System;
using System.Collections.Generic;
using System.Text;

namespace WarCore.Sim
{
    public sealed class TriggerRegistry : IStateHash, IStableStateSerializer
    {
        private readonly SortedDictionary<int, ITriggerCondition> conditions = new SortedDictionary<int, ITriggerCondition>();
        private readonly SortedDictionary<int, ITriggerAction> actions = new SortedDictionary<int, ITriggerAction>();
        private bool frozen;
        private ITriggerCondition[] orderedConditions;
        private ITriggerAction[] orderedActions;
        public string StateTypeId => "warcore.trigger-registry";
        public int StateVersion => 1;
        internal string ManifestId { get; private set; }

        private void RequireRegistration(int id, ITriggerOperation operation)
        {
            if (frozen)
                throw new InvalidOperationException("Trigger registration is frozen.");
            if (id <= 0 || operation == null || string.IsNullOrEmpty(operation.StateTypeId) || operation.StateVersion <= 0)
                throw new ArgumentException("Trigger operations require a positive id and an explicit versioned serializer.");
        }

        public void RegisterCondition(int id, ITriggerCondition condition)
        {
            RequireRegistration(id, condition);
            if (conditions.ContainsKey(id))
                throw new InvalidOperationException("Duplicate trigger condition registration: " + id);
            conditions.Add(id, condition);
        }

        public void RegisterAction(int id, ITriggerAction action)
        {
            RequireRegistration(id, action);
            if (actions.ContainsKey(id))
                throw new InvalidOperationException("Duplicate trigger action registration: " + id);
            actions.Add(id, action);
        }

        internal ITriggerCondition Condition(int id) => conditions.TryGetValue(id, out ITriggerCondition value)
            ? value : throw new InvalidOperationException("Missing trigger condition registration: " + id);
        internal ITriggerAction Action(int id) => actions.TryGetValue(id, out ITriggerAction value)
            ? value : throw new InvalidOperationException("Missing trigger action registration: " + id);

        internal void Freeze()
        {
            if (frozen)
                return;
            var text = new StringBuilder();
            text.Append("conditions:");
            foreach (var pair in conditions)
                Append(text, pair.Key, pair.Value);
            text.Append("actions:");
            foreach (var pair in actions)
                Append(text, pair.Key, pair.Value);
            orderedConditions = new ITriggerCondition[conditions.Count];
            orderedActions = new ITriggerAction[actions.Count];
            conditions.Values.CopyTo(orderedConditions,0);
            actions.Values.CopyTo(orderedActions,0);
            ManifestId = text.ToString();
            frozen = true;
        }

        private static void Append(StringBuilder text, int id, ITriggerOperation operation)
        {
            text.Append(id).Append(':').Append(operation.StateTypeId.Length).Append(':');
            text.Append(operation.StateTypeId).Append(':').Append(operation.StateVersion).Append(';');
        }

        public void WriteState(StateHash hash)
        {
            Freeze();
            hash.Add(ManifestId);
            foreach (ITriggerCondition condition in orderedConditions)
                condition.WriteState(hash);
            foreach (ITriggerAction action in orderedActions)
                action.WriteState(hash);
        }

        private static void Save(SimStateWriter writer, ITriggerOperation operation)
        {
            if (writer.IsStable)
            {
                writer.Section(operation);
                return;
            }
            var section = new SimStateWriter();
            operation.SaveState(section);
            writer.Bytes(section.ToArray());
        }

        private static void Load(SimStateReader reader, ITriggerOperation operation)
        {
            var section = reader.Section();
            operation.LoadState(section);
            section.End();
        }

        public void SaveState(SimStateWriter writer)
        {
            Freeze();
            writer.String(ManifestId);
            foreach (ITriggerCondition condition in orderedConditions)
                Save(writer, condition);
            foreach (ITriggerAction action in orderedActions)
                Save(writer, action);
        }

        public void LoadState(SimStateReader reader)
        {
            Freeze();
            SimStateReader.Require(reader.String() == ManifestId, "Trigger operation registration manifest mismatch.");
            foreach (ITriggerCondition condition in orderedConditions)
                Load(reader, condition);
            foreach (ITriggerAction action in orderedActions)
                Load(reader, action);
        }
    }
}
