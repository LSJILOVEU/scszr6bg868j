using System;
using System.Text;

namespace WarCore.Sim
{
    public sealed class NavProviderRegistry : IStateHash, IStableStateSerializer
    {
        private readonly INavProvider[] providers = new INavProvider[3];
        private bool frozen;
        private string manifestId;
        private byte[] manifestBytes;
        public string StateTypeId => "warcore.navigation";
        public int StateVersion => 1;
        internal string ManifestId => manifestId;

        public NavProviderRegistry()
        {
            CacheManifest();
        }

        private void CacheManifest()
        {
            // Registration is cold; caching the original UTF-8 preserves the established hash encoding.
            var result = new StringBuilder();
            for (int i = 0; i < providers.Length; i++)
            {
                result.Append(i).Append(':');
                if (providers[i] != null)
                    result.Append(providers[i].StateTypeId.Length).Append(':').Append(providers[i].StateTypeId).Append(':').Append(providers[i].StateVersion);
                result.Append(';');
            }
            manifestId = result.ToString();
            manifestBytes = Encoding.UTF8.GetBytes(manifestId);
        }

        public void Register(MoveMode mode, INavProvider provider)
        {
            if (mode < MoveMode.Ground || mode > MoveMode.Water || mode == MoveMode.Flying)
                throw new ArgumentOutOfRangeException(nameof(mode), "Flying uses direct velocity integration.");
            if (frozen)
                throw new InvalidOperationException("Navigation registration is frozen.");
            if (provider == null || string.IsNullOrEmpty(provider.StateTypeId) || provider.StateVersion <= 0)
                throw new ArgumentException("A versioned navigation provider is required.", nameof(provider));
            if (providers[(int)mode] != null)
                throw new InvalidOperationException("Navigation mode is already registered.");
            providers[(int)mode] = provider;
            CacheManifest();
        }

        internal void Freeze()
        {
            frozen = true;
        }

        public INavProvider For(MoveMode mode)
        {
            if (mode < MoveMode.Ground || mode > MoveMode.Water)
                throw new ArgumentOutOfRangeException(nameof(mode));
            return providers[(int)mode] ?? throw new InvalidOperationException("No navigation provider for mode: " + mode);
        }

        public void WriteState(StateHash hash)
        {
            hash.AddUtf8(manifestBytes);
            for (int i = 0; i < providers.Length; i++)
                if (providers[i] != null)
                    providers[i].WriteState(hash);
        }

        public void SaveState(SimStateWriter writer)
        {
            writer.String(ManifestId);
            for (int i = 0; i < providers.Length; i++)
            {
                if (providers[i] == null)
                    continue;
                if (writer.IsStable)
                {
                    writer.Section(providers[i]);
                    continue;
                }
                var section = new SimStateWriter();
                providers[i].SaveState(section);
                writer.Bytes(section.ToArray());
            }
        }

        public void LoadState(SimStateReader reader)
        {
            SimStateReader.Require(reader.String() == ManifestId, "Navigation provider registration manifest mismatch.");
            for (int i = 0; i < providers.Length; i++)
            {
                if (providers[i] == null)
                    continue;
                var section = reader.Section();
                providers[i].LoadState(section);
                section.End();
            }
        }
    }
}
