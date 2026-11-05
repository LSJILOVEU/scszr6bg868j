using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace WarCore.Content
{
    public readonly struct ContentWeaponChange
    {
        public readonly long? FireIntervalMilli, Magazine, ReloadMilli;

        public ContentWeaponChange(long? fireIntervalMilli = null, long? magazine = null, long? reloadMilli = null)
        {
            FireIntervalMilli = fireIntervalMilli; Magazine = magazine; ReloadMilli = reloadMilli;
        }
    }

    // Authoring only. Generated data contains integers; rate conversion never runs in Sim.
    public static class ContentWeaponEdit
    {
        public const long MaxMilli = 1_000_000_000_000L;
        // ContentLoader also constructs WeaponDef, which rejects times below one tick.
        public const long MinWeaponMilli = 1000;

        public static string Generate(IContentSource source, string baseLayerId, string layerId,
            IReadOnlyDictionary<string, ContentWeaponChange> changes)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (changes == null) throw new ArgumentNullException(nameof(changes));
            if (string.IsNullOrWhiteSpace(baseLayerId) || string.IsNullOrWhiteSpace(layerId))
                throw new ContentException("base and new content layer ids must not be empty");
            if (baseLayerId == layerId) throw new ContentException("new content layer '" + layerId + "' must differ from its base layer");
            ContentPack basis = ContentLoader.Load(source, baseLayerId);
            foreach (var change in changes)
            {
                basis.WeaponIndex(change.Key);
                Validate(layerId, change.Key, "fireIntervalMilli", change.Value.FireIntervalMilli, MinWeaponMilli, MaxMilli);
                Validate(layerId, change.Key, "magazine", change.Value.Magazine, 1, int.MaxValue);
                Validate(layerId, change.Key, "reloadMilli", change.Value.ReloadMilli, MinWeaponMilli, MaxMilli);
            }

            var json = new StringBuilder("{\n  \"id\": ").Append(Quote(layerId))
                .Append(",\n  \"extends\": ").Append(Quote(baseLayerId)).Append(",\n  \"weapons\": [");
            var weapons = basis.BuildCombat().Weapons;
            bool any = false;
            // Base order makes output stable even if the change dictionary was built in a different order.
            for (int i = 0; i < basis.WeaponCount; i++)
            {
                string id = basis.WeaponId(i);
                if (!changes.TryGetValue(id, out ContentWeaponChange change)) continue;
                var fields = new StringBuilder();
                AppendChange(fields, "fireIntervalMilli", change.FireIntervalMilli, weapons[i].FireIntervalMilli);
                AppendChange(fields, "magazine", change.Magazine, weapons[i].Magazine);
                AppendChange(fields, "reloadMilli", change.ReloadMilli, weapons[i].ReloadMilli);
                if (fields.Length == 0) continue;
                if (any) json.Append(',');
                json.Append("\n    { \"id\": ").Append(Quote(id)).Append(fields).Append(" }");
                any = true;
            }
            json.Append(any ? "\n  ]\n}\n" : "]\n}\n");
            string result = json.ToString();
            // Validate the actual emitted layer, including inheritance cycles and loader constraints.
            ContentLoader.Load(new OverlaySource(source, layerId, result), layerId);
            return result;
        }

        public static decimal ShotsPerSecond(long fireIntervalMilli)
        {
            Validate("display", "", "fireIntervalMilli", fireIntervalMilli, 1, MaxMilli);
            return 30000m / fireIntervalMilli;
        }

        public static long IntervalFromShotsPerSecond(decimal shotsPerSecond)
        {
            if (shotsPerSecond <= 0) throw new ContentException("shotsPerSecond must be greater than 0");
            // Check before division, so even decimal's smallest positive value cannot overflow.
            if (shotsPerSecond <= 30000m / MaxMilli) return MaxMilli;
            if (shotsPerSecond >= 30000m) return 1;
            return (long)Math.Round(30000m / shotsPerSecond, 0, MidpointRounding.AwayFromZero);
        }

        private static void Validate(string layer, string id, string field, long? value, long min, long max)
        {
            if (value.HasValue && (value.Value < min || value.Value > max))
                throw new ContentException("content layer '" + layer + "' weapon '" + id + "' field '" + field +
                    "' must be in " + min.ToString(CultureInfo.InvariantCulture) + ".." + max.ToString(CultureInfo.InvariantCulture));
        }

        private static void AppendChange(StringBuilder text, string field, long? value, long original)
        {
            if (value.HasValue && value.Value != original)
                text.Append(", \"").Append(field).Append("\": ").Append(value.Value.ToString(CultureInfo.InvariantCulture));
        }

        private static string Quote(string text)
        {
            var result = new StringBuilder("\"");
            foreach (char c in text)
            {
                if (c == '"' || c == '\\') result.Append('\\').Append(c);
                else if (c < ' ') result.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                else result.Append(c);
            }
            return result.Append('"').ToString();
        }

        private sealed class OverlaySource : IContentSource
        {
            private readonly IContentSource source;
            private readonly string id, json;
            internal OverlaySource(IContentSource source, string id, string json)
            { this.source = source; this.id = id; this.json = json; }

            public bool TryRead(string layerId, out string text, out string location)
            {
                if (layerId != id) return source.TryRead(layerId, out text, out location);
                text = json; location = "generated:" + id; return true;
            }
        }
    }
}
