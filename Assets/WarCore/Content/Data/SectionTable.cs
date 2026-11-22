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
    //   * Unlisted sections (TRV-2: traversal; SLOPE-1: movement, stats) come after the listed ones. Their keys are accepted,
    //     merged and built like the others but left out of the "allowed" list, so that recorded message stays the same.
    //     Sections not listed stay unlisted until MIX-1 lists every optional section at once; a layer that turns one on does
    //     not list it by itself.
    internal sealed class SectionTable
    {
        // Weapons come before unit types and combat before match: later sections read what earlier ones built.
        internal static readonly SectionTable Default = new SectionTable(new IContentSection[]
        {
            new WeaponsSection(), new MeleeSection(), new BlockSection(), new UnitTypesSection(), new CombatSection(), new MatchSection(),
            new SurfacesSection(), new SlopeRuleSection(), new AiSection(), new RulesSection(),
        }, new IContentSection[] { new TraversalSection(), new MovementSection(), new StatsSection() });

        private readonly IContentSection[] sections, all, buildOrder;
        private readonly string[] allowedKeys;
        private readonly string listedKeys;

        internal SectionTable(IContentSection[] sections, IContentSection[] unlisted = null)
        {
            this.sections = sections;
            unlisted = unlisted ?? Array.Empty<IContentSection>();
            all = new IContentSection[sections.Length + unlisted.Length];
            sections.CopyTo(all, 0);
            unlisted.CopyTo(all, sections.Length);
            buildOrder = (IContentSection[])all.Clone();
            // A stable sort: Array.Sort is not, so order by stage and then by position in the table.
            var position = new Dictionary<IContentSection, int>();
            for (int i = 0; i < all.Length; i++) position[all[i]] = i;
            Array.Sort(buildOrder, (a, b) => a.BuildStage != b.BuildStage ? a.BuildStage.CompareTo(b.BuildStage) : position[a].CompareTo(position[b]));
            allowedKeys = new string[all.Length + 2];
            allowedKeys[0] = "id"; allowedKeys[1] = "extends";
            for (int i = 0; i < all.Length; i++) allowedKeys[i + 2] = all[i].Key;
            listedKeys = string.Join(", ", allowedKeys, 0, sections.Length + 2);
        }

        // The listed sections, in table order (a test that adds a row builds its table from these).
        internal IReadOnlyList<IContentSection> Sections => sections;

        // Every section the loader merges, listed then unlisted.
        internal IReadOnlyList<IContentSection> All => all;

        // "id" and "extends" belong to the loader; every other key is a section.
        internal bool IsAllowedKey(string key) => Array.IndexOf(allowedKeys, key) >= 0;

        internal string AllowedKeys => listedKeys;

        internal ContentPack Build(string pack, MergedLayers merged)
        {
            var build = new SectionBuild(pack, merged);
            foreach (IContentSection section in buildOrder) section.Build(build);
            return build.ToPack();
        }
    }
}
