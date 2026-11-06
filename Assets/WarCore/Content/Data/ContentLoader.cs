using System;
using System.Collections.Generic;
using System.Globalization;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Sim;

namespace WarCore.Content
{
    // Layer file shape (all names are exact; unknown names are errors):
    //   { "id": "<file name>", "extends": "<layer id>", "weapons": [ ... ], "melee": [ ... ], "unitTypes": [ ... ], "combat": { ... }, "match": { ... },
    //     "surfaces": [ ... ], "slopeRule": { ... } }   (the last two are the optional terrain rules, WP-22)
    // Inheritance is single. Entries with the same id merge field by field (the child's fields win, including an explicit
    // 0 or false); new ids are appended after the parent's entries; "combat" and "match" merge field by field.
    // Field tables: docs/content-data.md.
    public static class ContentLoader
    {
        public static ContentPack Load(IContentSource source, string packId)
        {
            Merged merged = LoadMerged(source, packId, new List<string>());
            return Build(packId, merged);
        }

        private sealed class Field
        {
            internal readonly string Name, Layer;
            internal readonly JsonNode Node;
            internal bool Used;
            internal Field(string name, JsonNode node, string layer) { Name = name; Node = node; Layer = layer; }
        }

        private sealed class Entry
        {
            internal readonly string Section, Id, FirstLayer;
            internal readonly List<Field> Fields = new List<Field>();
            internal Entry(string section, string id, string layer) { Section = section; Id = id; FirstLayer = layer; }
            internal string Label => Id == null ? Section : Section + " '" + Id + "'";
        }

        private sealed class Merged
        {
            internal readonly List<Entry> Weapons = new List<Entry>(), Melee = new List<Entry>(), UnitTypes = new List<Entry>(), Surfaces = new List<Entry>();
            internal Entry Combat, Match, SlopeRule;
        }

        private const long MaxMilli = 1_000_000_000_000L, MaxEffectRaw = 1L << 40;
        private static readonly string[] TopKeys = { "id", "extends", "weapons", "melee", "unitTypes", "combat", "match", "surfaces", "slopeRule" };

        private static Merged LoadMerged(IContentSource source, string layerId, List<string> chain)
        {
            if (chain.Contains(layerId))
                throw new ContentException("content layer inheritance cycle: " + string.Join(" -> ", chain) + " -> " + layerId);
            if (!source.TryRead(layerId, out string text, out string location))
                throw new ContentException("content layer '" + layerId + "' not found" + (chain.Count > 0 ? " (extended by '" + chain[chain.Count - 1] + "')" : "") + ", looked at: " + location);
            chain.Add(layerId);
            JsonNode root = ContentJson.Parse(text, "content layer '" + layerId + "'");
            if (root.Kind != JsonKind.Object) throw new ContentException("content layer '" + layerId + "' must be a JSON object");
            foreach (var member in root.Members)
                if (Array.IndexOf(TopKeys, member.Name) < 0)
                    throw new ContentException("content layer '" + layerId + "' has unknown field '" + member.Name + "' (line " + member.Value.Line + "); allowed: " + string.Join(", ", TopKeys));
            string declared = ScalarText(root.Find("id"), layerId, "id");
            if (declared != layerId) throw new ContentException("content layer file name is '" + layerId + "' but its id is '" + declared + "'");
            Merged merged;
            JsonNode extends = root.Find("extends");
            merged = extends == null ? new Merged() : LoadMerged(source, ScalarText(extends, layerId, "extends"), chain);
            chain.RemoveAt(chain.Count - 1);

            MergeArray(merged.Weapons, root.Find("weapons"), "weapon", layerId);
            MergeArray(merged.Melee, root.Find("melee"), "melee", layerId);
            MergeArray(merged.UnitTypes, root.Find("unitTypes"), "unit type", layerId);
            merged.Combat = MergeObject(merged.Combat, root.Find("combat"), "combat", layerId);
            merged.Match = MergeObject(merged.Match, root.Find("match"), "match", layerId);
            MergeArray(merged.Surfaces, root.Find("surfaces"), "surface", layerId);
            merged.SlopeRule = MergeObject(merged.SlopeRule, root.Find("slopeRule"), "slopeRule", layerId);
            return merged;
        }

        private static string ScalarText(JsonNode node, string layer, string field)
        {
            if (node == null) throw new ContentException("content layer '" + layer + "' is missing field '" + field + "'");
            if (node.Kind != JsonKind.String) throw new ContentException("content layer '" + layer + "' field '" + field + "' must be a string");
            return node.Text;
        }

        private static void MergeArray(List<Entry> target, JsonNode array, string section, string layer)
        {
            if (array == null) return;
            if (array.Kind != JsonKind.Array) throw new ContentException("content layer '" + layer + "' " + section + " must be an array");
            var seen = new List<string>();
            foreach (JsonNode item in array.Items)
            {
                if (item.Kind != JsonKind.Object) throw new ContentException("content layer '" + layer + "' " + section + " entry must be an object (line " + item.Line + ")");
                JsonNode idNode = item.Find("id");
                if (idNode == null || idNode.Kind != JsonKind.String || idNode.Text.Length == 0)
                    throw new ContentException("content layer '" + layer + "' " + section + " entry needs a string id (line " + item.Line + ")");
                string id = idNode.Text;
                if (seen.Contains(id)) throw new ContentException("content layer '" + layer + "' " + section + " '" + id + "' is duplicated");
                seen.Add(id);
                Entry entry = target.Find(e => e.Id == id);
                if (entry == null) { entry = new Entry(section, id, layer); target.Add(entry); }
                Overlay(entry, item, layer);
            }
        }

        private static Entry MergeObject(Entry current, JsonNode node, string section, string layer)
        {
            if (node == null) return current;
            if (node.Kind != JsonKind.Object) throw new ContentException("content layer '" + layer + "' " + section + " must be an object");
            if (current == null) current = new Entry(section, null, layer);
            Overlay(current, node, layer);
            return current;
        }

        private static void Overlay(Entry entry, JsonNode node, string layer)
        {
            foreach (var member in node.Members)
            {
                if (member.Name == "id" && entry.Id != null) continue;
                if (member.Value.Kind == JsonKind.Object || member.Value.Kind == JsonKind.Array)
                    throw new ContentException("content layer '" + layer + "' " + entry.Label + " field '" + member.Name + "' must be a number, string or boolean");
                entry.Fields.RemoveAll(f => f.Name == member.Name);
                entry.Fields.Add(new Field(member.Name, member.Value, layer));
            }
        }

        // ---- typed reading ----

        private sealed class Reader
        {
            private readonly string pack;
            private readonly Entry entry;
            internal Reader(string pack, Entry entry) { this.pack = pack; this.entry = entry; }

            internal ContentException Bad(Field f, string why) => new ContentException("content pack '" + pack + "' layer '" + f.Layer + "' " + entry.Label + " field '" + f.Name + "': " + why);

            private Field Take(string name)
            {
                foreach (var f in entry.Fields) if (f.Name == name) { f.Used = true; return f; }
                throw new ContentException("content pack '" + pack + "' layer '" + entry.FirstLayer + "' " + entry.Label + " is missing field '" + name + "'");
            }

            internal void Done()
            {
                foreach (var f in entry.Fields)
                    if (!f.Used) throw Bad(f, "unknown field name (misspelled?)");
            }

            // An optional string field: null when the entry has no such field.
            internal string StrOrNull(string name, out Field field)
            {
                foreach (var f in entry.Fields)
                    if (f.Name == name)
                    {
                        f.Used = true; field = f;
                        if (f.Node.Kind != JsonKind.String) throw Bad(f, "must be a string");
                        return f.Node.Text;
                    }
                field = null;
                return null;
            }

            internal bool Has(string name)
            {
                foreach (var f in entry.Fields) if (f.Name == name) return true;
                return false;
            }

            internal string Str(string name)
            {
                Field f = Take(name);
                if (f.Node.Kind != JsonKind.String) throw Bad(f, "must be a string");
                return f.Node.Text;
            }

            internal bool Bool(string name)
            {
                Field f = Take(name);
                if (f.Node.Kind != JsonKind.Bool) throw Bad(f, "must be true or false");
                return f.Node.Flag;
            }

            internal long Long(string name, long min = long.MinValue, long max = long.MaxValue)
            {
                Field f = Take(name);
                if (f.Node.Kind != JsonKind.Number || f.Node.Text.IndexOf('.') >= 0) throw Bad(f, "must be an integer");
                if (!long.TryParse(f.Node.Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long v)) throw Bad(f, "number too large");
                if (v < min || v > max) throw Bad(f, "out of range [" + min + ", " + max + "]");
                return v;
            }

            internal int Int(string name, int min = int.MinValue, int max = int.MaxValue) => (int)Long(name, min, max);

            // Decimal (a JSON number or a string such as "1.5") to SimFix with integer arithmetic only:
            // digits / 10^fraction, rounded as SimFix.Ratio rounds. The magnitude is checked before Ratio can overflow.
            internal SimFix Fix(string name)
            {
                Field f = Take(name);
                if (f.Node.Kind != JsonKind.Number && f.Node.Kind != JsonKind.String) throw Bad(f, "must be a number");
                string text = f.Node.Text;
                int start = text.Length > 0 && text[0] == '-' ? 1 : 0, dot = text.IndexOf('.');
                string whole = dot < 0 ? text.Substring(start) : text.Substring(start, dot - start);
                string frac = dot < 0 ? "" : text.Substring(dot + 1);
                if (whole.Length == 0 || !AllDigits(whole) || (dot >= 0 && (frac.Length == 0 || !AllDigits(frac))))
                    throw Bad(f, "must be a decimal number such as 34 or 1.5");
                if (whole.Length + frac.Length > 15) throw Bad(f, "too many digits (at most 15)");
                long scale = 1;
                for (int i = 0; i < frac.Length; i++) scale *= 10;
                long numerator = long.Parse(whole + frac, CultureInfo.InvariantCulture);
                if (numerator / scale > int.MaxValue) throw Bad(f, "value too large");
                return SimFix.Ratio(start == 1 ? -numerator : numerator, scale);
            }

            private static bool AllDigits(string text)
            {
                foreach (char c in text) if (c < '0' || c > '9') return false;
                return true;
            }
        }

        // Terrain rules (WP-22). Absent sections give null; a missing field takes its default, an explicit false or 0 is a value.
        private static TerrainRules BuildTerrain(string pack, Merged m)
        {
            if (m.Surfaces.Count == 0 && m.SlopeRule == null) return null;
            int num = HeightField.MaxSlopeNumerator, den = HeightField.MaxSlopeDenominator;
            if (m.SlopeRule != null)
            {
                var sr = new Reader(pack, m.SlopeRule);
                den = sr.Has("maxWalkableDen") ? sr.Int("maxWalkableDen", 1, HeightField.MaxWalkableDenominator) : den;
                num = sr.Has("maxWalkableNum") ? sr.Int("maxWalkableNum", 0, HeightField.MaxSlopeNumerator * den) : HeightField.MaxSlopeNumerator * den;
                sr.Done();
            }
            var defs = new SurfaceDef[m.Surfaces.Count];
            for (int i = 0; i < defs.Length; i++)
            {
                Entry e = m.Surfaces[i];
                var r = new Reader(pack, e);
                if (e.Id.Length == 0 || System.Text.Encoding.UTF8.GetByteCount(e.Id) > 255)
                    throw new ContentException("content pack '" + pack + "' layer '" + e.FirstLayer + "' " + e.Label + " id must be 1-255 bytes");
                bool passable = !r.Has("passable") || r.Bool("passable");
                int speed = r.Has("speedMilli") ? r.Int("speedMilli", 1, SurfaceDef.MaxSpeedMilli) : 1000;
                int cost = r.Has("cost") ? r.Int("cost", 1, SurfaceDef.MaxCost) : 1;
                r.Done();
                defs[i] = new SurfaceDef(e.Id, passable, speed, cost);
            }
            try { return new TerrainRules(pack, num, den, defs); }
            catch (ArgumentException ex) { throw new ContentException("content pack '" + pack + "' terrain is invalid: " + ex.Message); }
        }

        private static ContentPack Build(string pack, Merged m)
        {
            var weaponIds = new string[m.Weapons.Count];
            var weapons = new WeaponDef[m.Weapons.Count];
            for (int i = 0; i < weapons.Length; i++)
            {
                Entry e = m.Weapons[i];
                var r = new Reader(pack, e);
                weaponIds[i] = e.Id;
                // Bounds keep CombatSystem's arithmetic (tick * 1000 + interval, effectRaw * falloff) far from long overflow.
                long interval = r.Long("fireIntervalMilli", 1, MaxMilli), reload = r.Long("reloadMilli", 1, MaxMilli), effect = r.Long("effectRaw", 0, MaxEffectRaw);
                int magazine = r.Int("magazine");
                SimFix start = r.Fix("falloffStart"), end = r.Fix("falloffEnd");
                // Spread is in thousandths of a degree; all four zero means the weapon has no spread.
                int spreadBase = r.Int("spreadBase", 0, SpreadSpec.MaxLegalMilliDegrees), spreadGrowth = r.Int("spreadGrowth", 0, SpreadSpec.MaxLegalMilliDegrees);
                int spreadRecover = r.Int("spreadRecoverPerSec", 0, SpreadSpec.MaxLegalMilliDegrees), spreadMax = r.Int("spreadMax", 0, SpreadSpec.MaxLegalMilliDegrees);
                if (spreadMax < spreadBase) throw r.Bad(e.Fields.Find(f => f.Name == "spreadMax"), "must be at least spreadBase");
                r.Done();
                try { weapons[i] = new WeaponDef(interval, magazine, reload, start, end, effect, new SpreadSpec(spreadBase, spreadGrowth, spreadRecover, spreadMax)); }
                catch (ArgumentOutOfRangeException ex)
                {
                    throw new ContentException("content pack '" + pack + "' layer '" + (e.Fields.Find(f => f.Name == ex.ParamName)?.Layer ?? e.FirstLayer) + "' " + e.Label + " field '" + ex.ParamName + "' is out of its legal range");
                }
            }

            // Melee swings (WP-20): a separate table, so a pack without any keeps every weapon and digest it had.
            var meleeIds = new string[m.Melee.Count];
            var melee = new MeleeSpec[m.Melee.Count];
            for (int i = 0; i < melee.Length; i++)
            {
                Entry e = m.Melee[i];
                var r = new Reader(pack, e);
                meleeIds[i] = e.Id;
                int windup = r.Int("windupTicks"), active = r.Int("activeTicks"), recovery = r.Int("recoveryTicks");
                SimFix radius = r.Fix("radius"), halfHeight = r.Fix("halfHeight"), damage = r.Fix("damage"), engage = r.Fix("engageRange");
                int cosine = r.Int("cosHalfAnglePermille"), targets = r.Int("maxTargets");
                // Optional: a thrust (a straight narrow hit instead of the sector). Absent = a sector, exactly as before.
                SimFix lineHalfWidth = r.Has("lineHalfWidth") ? r.Fix("lineHalfWidth") : default;
                r.Done();
                try { melee[i] = new MeleeSpec(windup, active, recovery, radius, cosine, halfHeight, damage, targets, engage, lineHalfWidth); }
                catch (ArgumentOutOfRangeException ex)
                {
                    // The constructor parameters are named exactly like the JSON fields.
                    string name = ex.ParamName;
                    throw new ContentException("content pack '" + pack + "' layer '" + (e.Fields.Find(f => f.Name == name)?.Layer ?? e.FirstLayer) + "' " + e.Label + " field '" + name + "' is out of its legal range");
                }
            }

            var unitIds = new string[m.UnitTypes.Count];
            var unitWeapons = new int[m.UnitTypes.Count];
            var unitMelee = new int[m.UnitTypes.Count];
            for (int i = 0; i < unitIds.Length; i++)
            {
                Entry e = m.UnitTypes[i];
                var r = new Reader(pack, e);
                unitIds[i] = e.Id;
                string weaponId = r.Str("weapon");
                Field field = e.Fields.Find(f => f.Name == "weapon");
                string meleeId = r.StrOrNull("melee", out Field meleeField);
                r.Done();
                // An empty weapon id means no gun; such a unit type must carry a melee spec.
                int index = weaponId.Length == 0 ? -1 : Array.IndexOf(weaponIds, weaponId);
                if (index < 0 && weaponId.Length != 0) throw r.Bad(field, "referenced weapon '" + weaponId + "' does not exist");
                int swing = -1;
                if (!string.IsNullOrEmpty(meleeId))
                {
                    swing = Array.IndexOf(meleeIds, meleeId);
                    if (swing < 0) throw r.Bad(meleeField, "referenced melee '" + meleeId + "' does not exist");
                }
                if (index < 0 && swing < 0) throw r.Bad(field, "a unit type without a weapon needs a melee");
                unitWeapons[i] = index; unitMelee[i] = swing;
            }

            if (m.Combat == null) throw new ContentException("content pack '" + pack + "' has no combat section");
            var cr = new Reader(pack, m.Combat);
            SimFix maxHealth = cr.Fix("maxHealth");
            if (maxHealth.Raw <= 0) throw cr.Bad(m.Combat.Fields.Find(f => f.Name == "maxHealth"), "must be greater than 0");
            string resolver = cr.Str("resolver"), selector = cr.Str("selector");
            if (!ContentPolicies.IsResolver(resolver))
                throw cr.Bad(m.Combat.Fields.Find(f => f.Name == "resolver"), "unknown policy '" + resolver + "', allowed: " + ContentPolicies.ResolverIds);
            if (!ContentPolicies.IsSelector(selector))
                throw cr.Bad(m.Combat.Fields.Find(f => f.Name == "selector"), "unknown policy '" + selector + "', allowed: " + ContentPolicies.SelectorIds);
            bool friendlyFire = cr.Bool("friendlyFire"), friendlyFireLocked = cr.Bool("friendlyFireLocked");
            SimFix missRate = cr.Fix("allyCheckMissRate");
            if (missRate.Raw < 0 || missRate.Raw > SimFix.Scale) throw cr.Bad(m.Combat.Fields.Find(f => f.Name == "allyCheckMissRate"), "must be between 0 and 1");
            long holdMilli = cr.Long("allyHoldMilli", 1000, MaxMilli);
            cr.Done();

            MatchRules? rules = null;
            if (m.Match != null)
            {
                var mr = new Reader(pack, m.Match);
                Field Of(string name) => m.Match.Fields.Find(f => f.Name == name);
                int num = mr.Int("captureAdvantageNum", 1), den = mr.Int("captureAdvantageDen", 1);
                bool strict = mr.Bool("captureStrict");
                int captureTicks = mr.Int("captureTicks", 1);
                string interruptName = mr.Str("interrupt");
                int decay = mr.Int("decayPerTick", 1), interval = mr.Int("intervalTicks", 1), think = mr.Int("thinkInterval", 1), max = mr.Int("maxTicks", 1);
                SimFix sight = mr.Fix("sight");
                int columns = mr.Int("gridColumns", 1, 4096), rows = mr.Int("gridRows", 1, 4096);
                SimFix spacing = mr.Fix("gridSpacing");
                Field interruptField = Of("interrupt");
                mr.Done();
                if (num < den) throw mr.Bad(Of("captureAdvantageNum"), "must be at least captureAdvantageDen");
                if (sight.Raw <= 0) throw mr.Bad(Of("sight"), "must be greater than 0");
                if (spacing.Raw <= 0) throw mr.Bad(Of("gridSpacing"), "must be greater than 0");
                InterruptMode interrupt;
                switch (interruptName)
                {
                    case "reset": interrupt = InterruptMode.Reset; break;
                    case "pause": interrupt = InterruptMode.Pause; break;
                    case "decay": interrupt = InterruptMode.Decay; break;
                    default: throw mr.Bad(interruptField, "must be reset, pause or decay");
                }
                rules = new MatchRules(num, den, strict, captureTicks, interrupt, decay, interval, think, max, sight, columns, rows, spacing);
            }
            else if (selector == ContentPolicies.SightLimitedNearest)
                throw new ContentException("content pack '" + pack + "' uses selector sight-limited, which needs match.sight");

            return new ContentPack(pack, weaponIds, weapons, unitIds, unitWeapons, maxHealth.Raw, resolver, selector, rules,
                friendlyFire, friendlyFireLocked, (int)missRate.Raw, holdMilli, meleeIds, melee, unitMelee, BuildTerrain(pack, m));
        }
    }
}
