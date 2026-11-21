using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace WarCore.Presentation
{
    public static partial class MapPreview
    {
        // The data as one JSON object, the same bytes for the same data: fixed key order, integers only, no spaces outside strings.
        // Cells are lists (shade; ground as indices into groundPalette; steep as the indices of the steep cells). Colours are
        // "#RRGGBB". The text is meant to sit inside a script element, so every "<" in a string is written as < (an id can be
        // anything a person typed); quotes, backslashes, control characters and the two line separators are escaped too.
        public static string ToJson(MapPreviewData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            var o = new StringBuilder(1 << 16);
            o.Append('{');
            Key(o, "id"); Str(o, data.Id);
            Key(o, "pack"); Str(o, data.Pack);
            Key(o, "width"); Num(o, data.Width);
            Key(o, "depth"); Num(o, data.Depth);
            Key(o, "originX"); Num(o, data.OriginX);
            Key(o, "originZ"); Num(o, data.OriginZ);
            Key(o, "shade"); Ints(o, data.Shade);
            var steep = new List<int>();
            for (int i = 0; i < data.Steep.Count; i++)
                if (data.Steep[i]) steep.Add(i);
            Key(o, "steep"); Ints(o, steep);
            var palette = new List<int>();
            var index = new Dictionary<int, int>();
            var cells = new List<int>(data.Ground.Count);
            foreach (int colour in data.Ground)
            {
                if (!index.TryGetValue(colour, out int at)) { at = palette.Count; index.Add(colour, at); palette.Add(colour); }
                cells.Add(at);
            }
            Key(o, "groundPalette"); o.Append('['); for (int i = 0; i < palette.Count; i++) { if (i > 0) o.Append(','); Colour(o, palette[i]); } o.Append(']');
            Key(o, "ground"); Ints(o, cells);
            Key(o, "walls"); List(o, data.Walls, (w, e) =>
            {
                Key(w, "id"); Num(w, e.Id); Key(w, "x"); Num(w, e.CellX); Key(w, "z"); Num(w, e.CellZ);
                Key(w, "w"); Num(w, e.SizeX); Key(w, "d"); Num(w, e.SizeZ); Key(w, "h"); Num(w, e.HeightRaw);
                Key(w, "raised"); Bool(w, e.Raised); Key(w, "base"); Num(w, e.BaseRaw);
                Key(w, "side"); Colour(w, e.SideColor); Key(w, "top"); Colour(w, e.TopColor);
            });
            Key(o, "plates"); List(o, data.Plates, (w, e) =>
            {
                Key(w, "id"); Num(w, e.Id); Key(w, "x"); Num(w, e.CellX); Key(w, "z"); Num(w, e.CellZ);
                Key(w, "w"); Num(w, e.SizeX); Key(w, "d"); Num(w, e.SizeZ); Key(w, "axis"); Str(w, e.Axis);
                Key(w, "from"); Num(w, e.FromRaw); Key(w, "to"); Num(w, e.ToRaw); Key(w, "thick"); Num(w, e.ThickRaw);
            });
            Key(o, "points"); List(o, data.Points, (w, e) =>
            {
                Key(w, "id"); Num(w, e.Id); Key(w, "x"); Num(w, e.XMm); Key(w, "z"); Num(w, e.ZMm);
                Key(w, "r"); Num(w, e.RadiusMm); Key(w, "owner"); Num(w, e.Owner); Key(w, "color"); Colour(w, e.Color);
            });
            Key(o, "bases"); List(o, data.Bases, (w, e) =>
            {
                Key(w, "id"); Num(w, e.Id); Key(w, "team"); Num(w, e.Team); Key(w, "x"); Num(w, e.XMm); Key(w, "z"); Num(w, e.ZMm);
                Key(w, "color"); Colour(w, e.Color);
            });
            Key(o, "units"); List(o, data.Units, (w, e) =>
            {
                Key(w, "key"); Num(w, e.Key); Key(w, "type"); Str(w, e.Type); Key(w, "team"); Num(w, e.Team); Key(w, "squad"); Num(w, e.Squad);
                Key(w, "x"); Num(w, e.XMm); Key(w, "z"); Num(w, e.ZMm); Key(w, "reserve"); Bool(w, e.Reserve); Key(w, "color"); Colour(w, e.Color);
            });
            Key(o, "pins"); List(o, data.Pins, (w, e) =>
            {
                Key(w, "problem"); Num(w, e.ProblemIndex); Key(w, "code"); Str(w, e.Code); Key(w, "id"); Str(w, e.Id); Key(w, "text"); Str(w, e.Text);
                Key(w, "x"); Num(w, e.XMm); Key(w, "z"); Num(w, e.ZMm);
            });
            Key(o, "problems"); List(o, data.Problems, (w, e) =>
            {
                Key(w, "code"); Str(w, e.Code); Key(w, "id"); Str(w, e.Id); Key(w, "text"); Str(w, e.Text);
            });
            Key(o, "notes"); List(o, data.Notes, (w, e) =>
            {
                Key(w, "name"); Str(w, e.Name); Key(w, "key"); Str(w, e.Key); Key(w, "text"); Str(w, e.Text);
            });
            o.Append('}');
            return o.ToString();
        }

        private static void Key(StringBuilder o, string name)
        {
            if (o[o.Length - 1] != '{') o.Append(',');
            o.Append('"').Append(name).Append("\":");
        }

        private static void Num(StringBuilder o, long value) => o.Append(value.ToString(CultureInfo.InvariantCulture));
        private static void Num(StringBuilder o, ulong value) => o.Append(value.ToString(CultureInfo.InvariantCulture));
        private static void Bool(StringBuilder o, bool value) => o.Append(value ? "true" : "false");
        private static void Colour(StringBuilder o, int rgb) => o.Append("\"#").Append((rgb & 0xFFFFFF).ToString("X6", CultureInfo.InvariantCulture)).Append('"');

        private static void Ints(StringBuilder o, IReadOnlyList<int> values)
        {
            o.Append('[');
            for (int i = 0; i < values.Count; i++)
            {
                if (i > 0) o.Append(',');
                Num(o, values[i]);
            }
            o.Append(']');
        }

        private static void List<T>(StringBuilder o, IReadOnlyList<T> items, Action<StringBuilder, T> one)
        {
            o.Append('[');
            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0) o.Append(',');
                o.Append('{');
                one(o, items[i]);
                o.Append('}');
            }
            o.Append(']');
        }

        private static void Str(StringBuilder o, string text)
        {
            o.Append('"');
            foreach (char c in text)
            {
                if (c == '"') o.Append("\\\"");
                else if (c == '\\') o.Append("\\\\");
                else if (c < ' ' || c == '<' || c == '\u2028' || c == '\u2029') o.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                else o.Append(c);
            }
            o.Append('"');
        }
    }
}
