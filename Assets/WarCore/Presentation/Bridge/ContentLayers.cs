using System;
using System.Collections.Generic;
using System.IO;
using WarCore.Content;

namespace WarCore.Presentation
{
    public readonly struct ContentWeaponRow
    {
        public string Id { get; }
        public string UnitTypes { get; }
        public long FireIntervalMilli { get; }
        public long Magazine { get; }
        public long ReloadMilli { get; }

        public ContentWeaponRow(string id, string unitTypes, long fireIntervalMilli, long magazine, long reloadMilli)
        {
            Id = id; UnitTypes = unitTypes; FireIntervalMilli = fireIntervalMilli;
            Magazine = magazine; ReloadMilli = reloadMilli;
        }
    }

    // The engine-facing authoring API uses only Presentation and standard library types.
    public static class ContentLayers
    {
        public static WorldRecipe M6(string layerId) => DemoRecipes.M6(ContentLoader.Load(FileContentSource.Rwr(), layerId));
        public static WorldRecipe M7(string layerId) => DemoRecipes.M7(ContentLoader.Load(FileContentSource.Rwr(), layerId));

        public static IReadOnlyList<ContentWeaponRow> Weapons(string layerId)
        {
            ContentPack pack = ContentLoader.Load(FileContentSource.Rwr(), layerId);
            var weapons = pack.BuildCombat().Weapons;
            var rows = new ContentWeaponRow[pack.WeaponCount];
            for (int i = 0; i < rows.Length; i++)
            {
                var units = new List<string>();
                for (int u = 0; u < pack.UnitTypeCount; u++)
                {
                    string id = pack.UnitTypeId(u);
                    if (pack.WeaponOfUnitType(id) == i) units.Add(id);
                }
                rows[i] = new ContentWeaponRow(pack.WeaponId(i), string.Join(", ", units),
                    weapons[i].FireIntervalMilli, weapons[i].Magazine, weapons[i].ReloadMilli);
            }
            return Array.AsReadOnly(rows);
        }

        public static string SaveOverride(string baseLayer, string newLayer, IReadOnlyList<ContentWeaponRow> rows)
        {
            ValidateFileId(baseLayer, nameof(baseLayer));
            ValidateFileId(newLayer, nameof(newLayer));
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            var changes = new Dictionary<string, ContentWeaponChange>();
            foreach (ContentWeaponRow row in rows)
                changes.Add(row.Id, new ContentWeaponChange(row.FireIntervalMilli, row.Magazine, row.ReloadMilli));
            var source = FileContentSource.Rwr();
            // Generate omits unchanged fields and validates the full layer before any file is replaced.
            string json = ContentWeaponEdit.Generate(source, baseLayer, newLayer, changes);
            source.TryRead(baseLayer, out _, out string basePath);
            string path = Path.Combine(Path.GetDirectoryName(basePath), newLayer + ".json");
            File.WriteAllText(path, json);
            return path;
        }

        public static decimal ShotsPerSecond(long fireIntervalMilli) => ContentWeaponEdit.ShotsPerSecond(fireIntervalMilli);

        // One tick is the minimum accepted by the loader: rates above 30 shots/sec clamp to 1000 milli.
        public static long IntervalFromShotsPerSecond(decimal shotsPerSecond) =>
            Math.Max(ContentWeaponEdit.MinWeaponMilli, ContentWeaponEdit.IntervalFromShotsPerSecond(shotsPerSecond));

        private static void ValidateFileId(string id, string parameter)
        {
            if (string.IsNullOrWhiteSpace(id) || id == "." || id == "..")
                throw new ArgumentException("Content layer id must be a file name, not a path.", parameter);
            foreach (char c in id)
                if (!(c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '-' || c == '_' || c == '.'))
                    throw new ArgumentException("Content layer id must contain only letters, digits, '-', '_' or '.'.", parameter);
        }
    }
}
