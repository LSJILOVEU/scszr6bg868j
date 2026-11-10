using WarCore.Combat;

namespace WarCore.Content
{
    internal sealed class BlockSection : ArraySection
    {
        internal const string SectionKey = "block";
        internal BlockSection() : base(SectionKey, "block") { }

        public override void Build(SectionBuild b)
        {
            var entries = b.Entries(Key);
            var blockIds = new string[entries.Count];
            var block = new BlockSpec[entries.Count];
            for (int i = 0; i < block.Length; i++)
            {
                ContentEntry e = entries[i];
                var r = new EntryReader(b.Pack, e);
                blockIds[i] = e.Id;
                int cosine = r.Int("cosHalfAnglePermille", 0, 1000);
                int reduction = r.Int("reductionPermille", 0, 1000);
                int aiRaise = r.Int("aiRaisePermille", 0, 1000);
                r.Done();
                block[i] = new BlockSpec(cosine, reduction, aiRaise);
            }
            b.BlockIds = blockIds; b.Block = block;
        }
    }
}
