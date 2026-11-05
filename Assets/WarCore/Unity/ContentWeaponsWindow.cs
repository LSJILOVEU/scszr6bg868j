#if UNITY_EDITOR
// Compiled in WarCore.Unity so the window can call Presentation without widening the Editor asmdef.
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using WarCore.Presentation;

namespace WarCore.Unity.Editor
{
    public sealed class ContentWeaponsWindow : EditorWindow
    {
        private const string BaseLayer = "rwr-m6", LocalLayer = "rwr-local";
        private const string LayerKey = "WarCore.Content.Layer";
        private readonly List<Row> rows = new List<Row>();
        private Vector2 scroll;
        private bool loaded;
        private string error = "", notice = "";

        private sealed class Row
        {
            internal string Id, Units, Rate;
            internal long Magazine, ReloadMilli;
        }

        [MenuItem("WarCore/Content/Weapons")]
        public static void Open() => GetWindow<ContentWeaponsWindow>("Weapons");

        private void OnEnable() => Reload();

        private void Reload()
        {
            loaded = false;
            rows.Clear();
            error = "";
            try
            {
                string layer = EditorPrefs.GetString(LayerKey, "") == LocalLayer ? LocalLayer : BaseLayer;
                foreach (ContentWeaponRow weapon in ContentLayers.Weapons(layer))
                {
                    rows.Add(new Row { Id = weapon.Id, Units = weapon.UnitTypes == "" ? "(unused)" : weapon.UnitTypes,
                        Rate = ContentLayers.ShotsPerSecond(weapon.FireIntervalMilli).ToString("G29", CultureInfo.InvariantCulture),
                        Magazine = weapon.Magazine, ReloadMilli = weapon.ReloadMilli });
                }
                rows.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
                loaded = true;
            }
            catch (Exception e) { error = e.Message; }
        }

        private void OnGUI()
        {
            string active = EditorPrefs.GetString(LayerKey, "");
            EditorGUILayout.LabelField("Active layer (next M6 / M7 / P0 session)", active == "" ? BaseLayer + " (default)" : active);
            EditorGUILayout.LabelField("Editing", LocalLayer + " extends " + BaseLayer);
            EditorGUILayout.HelpBox("Save, then start a new session. Shots/sec uses 30 ticks/sec. " +
                "Rates above 30 shots/sec clamp to 30. Time fields must be 1000..1000000000000 milli " +
                "(1 tick = 1000 milli). Use a dot for decimals.", MessageType.Info);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                scroll = EditorGUILayout.BeginScrollView(scroll);
                foreach (Row row in rows) DrawRow(row);
                EditorGUILayout.EndScrollView();
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(!loaded))
                        if (GUILayout.Button("Save")) Save();
                    if (GUILayout.Button("Restore defaults"))
                    {
                        EditorPrefs.DeleteKey(LayerKey);
                        notice = "Defaults enabled. The local file is kept.";
                        Reload();
                    }
                    if (GUILayout.Button("Reload")) { notice = ""; Reload(); }
                }
            }
            if (error != "") EditorGUILayout.HelpBox(error, MessageType.Error);
            if (notice != "") EditorGUILayout.HelpBox(notice, MessageType.Info);
        }

        private static void DrawRow(Row row)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(row.Id, EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Unit types", row.Units);
                row.Rate = EditorGUILayout.TextField("Shots per second", row.Rate);
                try
                {
                    long interval = Interval(row);
                    EditorGUILayout.LabelField("Fire interval (milli)", interval.ToString(CultureInfo.InvariantCulture));
                    EditorGUILayout.LabelField("Stored shots per second",
                        ContentLayers.ShotsPerSecond(interval).ToString("G29", CultureInfo.InvariantCulture));
                }
                catch (Exception e) { EditorGUILayout.HelpBox(e.Message, MessageType.Error); }
                row.Magazine = EditorGUILayout.LongField("Magazine (rounds)", row.Magazine);
                row.ReloadMilli = EditorGUILayout.LongField("Reload (milli)", row.ReloadMilli);
            }
        }

        private static long Interval(Row row)
        {
            if (!decimal.TryParse(row.Rate, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal rate))
                throw new FormatException("Weapon '" + row.Id + "': shots per second must be a decimal number.");
            return ContentLayers.IntervalFromShotsPerSecond(rate);
        }

        private void Save()
        {
            error = ""; notice = "";
            try
            {
                var changes = new List<ContentWeaponRow>();
                foreach (Row row in rows)
                    changes.Add(new ContentWeaponRow(row.Id, row.Units, Interval(row), row.Magazine, row.ReloadMilli));
                string path = ContentLayers.SaveOverride(BaseLayer, LocalLayer, changes);
                AssetDatabase.Refresh();
                EditorPrefs.SetString(LayerKey, LocalLayer);
                Reload();
                notice = "Saved " + path + ". Start a new M6, M7 or P0 session to apply it.";
            }
            catch (Exception e) { error = e.Message; }
        }
    }
}
#endif
