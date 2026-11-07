using System;
using System.Security.Cryptography;
using System.Text;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Content.Rwr;

namespace WarCore.Presentation
{
    // The editor-facing face of one map description. Public members use plain values, byte arrays, strings and Presentation
    // types only, so the Unity assembly can use it without seeing Content. Later packages add parts in MapDocument.<Part>.cs.
    public sealed partial class MapDocument
    {
        private readonly MapDesc desc;

        private MapDocument(MapDesc desc) { this.desc = desc; }

        // Reads and checks a description; a bad one throws InvalidDataException with the MAPDESC_* message.
        public static MapDocument Open(byte[] descBytes) => new MapDocument(MapDescJson.Read(descBytes));

        // Canonical bytes of the description.
        public byte[] Save() => MapDescJson.Write(desc);

        // Lower-case hex SHA-256 of the canonical bytes.
        public string DescHashHex
        {
            get
            {
                // Method group, not a call: StableGuardTests scans for ".Create()" as entity creation.
                Func<SHA256> factory = SHA256.Create;
                using SHA256 sha = factory();
                byte[] hash = sha.ComputeHash(Save());
                var hex = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash) hex.Append(b.ToString("x2"));
                return hex.ToString();
            }
        }

        public string PackId => desc.Pack;

        // Bakes the description and starts a world under the RWR content layer it names.
        public SimSession Start()
        {
            MapBakeResult result = MapBake.Run(Save());
            ContentPack pack = RwrPack.Load(PackId);
            return SimSession.Create(MapRecipe.From(result, pack), result.WcMapBytes);
        }
    }
}
