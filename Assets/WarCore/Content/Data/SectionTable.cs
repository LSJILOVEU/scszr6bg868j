using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

// The test assembly builds its own table (a fake section) and loads through the production loader with it.
[assembly: InternalsVisibleTo("WarCore.Sim.Tests")]

namespace WarCore.Content
{
    // The data sections of a content layer, in one ordered table. Adding a data section is one new file under Sections/
    // and one row here.
    //   * Merge order and the order of the "allowed" list in the unknown-key error are the table order.
    //   * Build order is (BuildStage, table order): that is how surfaces merge before the slope rule yet build after it.
    internal sealed class SectionTable
    {
        // Weapons come before unit types and combat before match: later sections read what earlier ones built.
        internal static readonly SectionTable Default = new SectionTable(new IContentSection[]
        {
            new WeaponsSection(), new MeleeSection(), new BlockSection(), new UnitTypesSection(), new CombatSection(), new MatchSection(),
            new SurfacesSection(), new SlopeRuleSection(), new AiSection(), new RulesSection(),
        });

        private readonly IContentSection[] sections, buildOrder;
        private readonly string[] allowedKeys;

        internal SectionTable(IContentSection[] sections)
        {
            this.sections = sections;
            buildOrder = (IContentSection[])sections.Clone();
            // A stable sort: Array.Sort is not, so order by stage and then by position in the table.
            var position = new Dictionary<IContentSection, int>();
            for (int i = 0; i < sections.Length; i++) position[sections[i]] = i;
            Array.Sort(buildOrder, (a, b) => a.BuildStage != b.BuildStage ? a.BuildStage.CompareTo(b.BuildStage) : position[a].CompareTo(position[b]));
            allowedKeys = new string[sections.Length + 2];
            allowedKeys[0] = "id"; allowedKeys[1] = "extends";
            for (int i = 0; i < sections.Length; i++) allowedKeys[i + 2] = sections[i].Key;
        }

        internal IReadOnlyList<IContentSection> Sections => sections;

        // "id" and "extends" belong to the loader; every other key is a section.
        internal bool IsAllowedKey(string key) => Array.IndexOf(allowedKeys, key) >= 0;

        internal string AllowedKeys => string.Join(", ", allowedKeys);

        internal ContentPack Build(string pack, MergedLayers merged)
        {
            var build = new SectionBuild(pack, merged);
            foreach (IContentSection section in buildOrder) section.Build(build);
            return build.ToPack();
        }
    }
}
