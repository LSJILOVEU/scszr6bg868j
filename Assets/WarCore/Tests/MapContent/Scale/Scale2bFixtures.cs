using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // SCALE-2b fixtures: in-memory test layers over the shipped Rwr layers, the "scale" section text with any fields
    // changed, and loading helpers that turn a refusal into an assertion failure (so a mutation that makes a load throw is
    // caught on an assertion).
    internal static class Scale2bFixtures
    {
        internal const long M = SimFix.Scale, R = CombatSection.UnitRadiusRaw;

        // The shipped rwr-scale numbers, in section order.
        internal static readonly (string Field, int Mm)[] Standard =
        {
            ("storeyMm", 3000), ("plateMm", 500), ("lowWallMm", 1000), ("chestWallMm", 1400), ("highWallMm", 2500),
            ("windowSillMm", 900), ("windowTopMm", 2000), ("railingMm", 1000), ("stairRiseMm", 175), ("stairRunMm", 280),
            ("laneMm", 3250), ("sidewalkMm", 2000), ("alleyMm", 3000),
        };

        // "scale": { ... } with the standard numbers, each change replacing one field's value text; a field changed to null
        // is left out.
        internal static string ScaleText(params (string Field, string Value)[] changes)
        {
            var parts = new List<string>();
            foreach ((string field, int mm) in Standard)
            {
                string value = mm.ToString(System.Globalization.CultureInfo.InvariantCulture);
                foreach ((string f, string v) in changes)
                    if (f == field) value = v;
                if (value != null) parts.Add("\"" + field + "\": " + value);
            }
            foreach ((string f, string v) in changes)
                if (v != null && Standard.All(s => s.Field != f)) parts.Add("\"" + f + "\": " + v);
            return "\"scale\": { " + string.Join(", ", parts) + " }";
        }

        // The same with integer changes only.
        internal static string Scale(params (string Field, int Mm)[] changes) =>
            ScaleText(changes.Select(c => (c.Field, c.Mm.ToString(System.Globalization.CultureInfo.InvariantCulture))).ToArray());

        // A layer text: id, parent, then the given top-level members.
        internal static string Layer(string id, string parent, params string[] members) =>
            "{ \"id\": \"" + id + "\", \"extends\": \"" + parent + "\"" + string.Concat(members.Select(m => ", " + m)) + " }";

        // combat { body } (and stances when given, as JSON array text).
        internal static string Body(string height, string eye, string muzzle, string stancesJson = null) =>
            "\"combat\": { \"body\": { \"height\": " + height + ", \"eye\": " + eye + ", \"muzzle\": " + muzzle + " }"
            + (stancesJson == null ? "" : ", \"stances\": " + stancesJson) + " }";

        internal static string Stances(params (string Height, string Eye, string Muzzle)[] stances) =>
            "[ " + string.Join(", ", stances.Select((s, i) => "{ \"id\": \"s" + i + "\", \"height\": " + s.Height + ", \"eye\": " + s.Eye
                + ", \"muzzle\": " + s.Muzzle + ", \"speedPermille\": 500, \"spreadPermille\": 700, \"ticks\": 6 }")) + " ]";

        // Shipped Rwr layers plus in-memory test layers (never written to disk).
        internal sealed class Source : IContentSource
        {
            private readonly IContentSource shipped = FileContentSource.Rwr();
            private readonly Dictionary<string, string> extra = new Dictionary<string, string>();
            internal Source(params string[] layers)
            {
                foreach (string text in layers) extra[IdOf(text)] = text;
            }
            public bool TryRead(string layerId, out string text, out string location)
            {
                if (extra.TryGetValue(layerId, out text)) { location = "memory:" + layerId; return true; }
                return shipped.TryRead(layerId, out text, out location);
            }
        }

        private static string IdOf(string layerText)
        {
            int start = layerText.IndexOf("\"id\": \"", StringComparison.Ordinal) + 7;
            return layerText.Substring(start, layerText.IndexOf('"', start) - start);
        }

        // Loads the first layer (with the others available); a refusal fails on an assertion.
        internal static ContentPack Loads(string layer, params string[] more)
        {
            ContentPack pack = null;
            Assert.DoesNotThrow(() => pack = ContentLoader.Load(new Source(new[] { layer }.Concat(more).ToArray()), IdOf(layer)));
            return pack;
        }

        // The first layer's refusal message (a load that succeeds fails on an assertion).
        internal static string Refused(string layer, params string[] more) =>
            Assert.Throws<ContentException>(() => ContentLoader.Load(new Source(new[] { layer }.Concat(more).ToArray()), IdOf(layer))).Message;

        internal static ContentPack Shipped(string id) => ContentLoader.Load(FileContentSource.Rwr(), id);

        // The scale rules a layer with this section over the parent publishes.
        internal static ScaleRules Rules(string parent, params (string Field, int Mm)[] changes) =>
            Loads(Layer("scale2b-test", parent, Scale(changes))).SectionOrNull<ScaleRules>();

        // The failing items of a refused layer (the message after "scale: ", split on "; ").
        internal static string[] Items(string message)
        {
            int at = message.IndexOf(" scale: ", StringComparison.Ordinal);
            Assert.That(at, Is.GreaterThan(0), message);
            return message.Substring(at + 8).Split(new[] { "; " }, StringSplitOptions.None);
        }

        // The field names of the failing items, in order.
        internal static string[] Fields(string message) => Items(message).Select(i => i.Substring(0, i.IndexOf(' '))).ToArray();

        // Accepted over the parent (a refusal fails on an assertion) or refused naming exactly the field.
        internal static void Accepts(string parent, params (string Field, int Mm)[] changes) =>
            Assert.That(Rules(parent, changes), Is.Not.Null);

        internal static void RefusesOnly(string field, string parent, params (string Field, int Mm)[] changes) =>
            Assert.That(Fields(Refused(Layer("scale2b-test", parent, Scale(changes)))), Is.EqualTo(new[] { field }));

        // A flat 30 x 20 description on the layer, ceiling 20 m (four storeys of 3 m fit).
        internal static MapDocument Tall(string pack) => MapDocument.New(new MapNewSettings
        {
            Id = "scale2b", Content = "none", Pack = pack, Width = 30, Depth = 20, FloorRaw = 0, CeilingRaw = 20 * M, Seed = 7,
        });

        // A bare description for prefab and gate calls: no units, one layer "walls".
        internal static MapDesc Desc(string pack, int width, int depth, int[][] heights, params MapWall[] walls) =>
            new MapDesc("scale2b", "none", pack, new MapXZ(width, depth), new MapXZ(0, 0), 0, 20 * M, new[] { new MapLayer("walls", 1) },
                heights, null, walls, new MapPoint[0], new MapBase[0], new MapUnit[0], new MapRun(41, "teamIdle"),
                new[] { MapEdit.Hand }, null, null, new MapStamp[0], new MapSrc[0], new MapNext((ulong)walls.Length + 1, 1, 1, 1));
    }

    // A throw-away cartridge: every rwr layer plus "scale2b-sill" (rwr-scale with a 1 m window sill) and "scale2b-odd"
    // (rwr-stance with OddScale: every raw value distinct, eight of them odd before padding). WARCORE_CONTENT_DIRS points
    // at it until Dispose.
    internal sealed class Scale2bSillCartridge : IDisposable
    {
        internal const string Layer = "scale2b-sill", OddLayer = "scale2b-odd";

        internal static readonly (string Field, int Mm)[] OddScale =
        {
            ("storeyMm", 3001), ("plateMm", 501), ("lowWallMm", 1001), ("chestWallMm", 1401), ("highWallMm", 2501), ("windowSillMm", 901),
            ("windowTopMm", 2001), ("railingMm", 1003), ("stairRiseMm", 176), ("stairRunMm", 281), ("laneMm", 3251), ("sidewalkMm", 2003),
            ("alleyMm", 3005),
        };
        private readonly Ed0Dirs dirs = new Ed0Dirs();

        internal Scale2bSillCartridge()
        {
            string dir = dirs.Dir("Sill");
            foreach (string file in Directory.GetFiles(FileContentSource.RwrDirectory, "*.json"))
                File.Copy(file, Path.Combine(dir, Path.GetFileName(file)));
            File.WriteAllText(Path.Combine(dir, Layer + ".json"), "{ \"id\": \"" + Layer + "\", \"extends\": \"rwr-scale\", "
                + "\"scale\": { \"windowSillMm\": 1000 } }");
            File.WriteAllText(Path.Combine(dir, OddLayer + ".json"), Scale2bFixtures.Layer(OddLayer, "rwr-stance", Scale2bFixtures.Scale(OddScale)));
            Ed0Dirs.UseList(dir);
        }

        internal CombatBody Body => ContentCatalog.Find().Load(Layer).Body.Value;

        public void Dispose() => dirs.Dispose();
    }
}
