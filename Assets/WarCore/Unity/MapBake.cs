using UnityEngine;

namespace WarCore.Unity
{
    // Marks a baked map root: which .wcmap (by its trailing whole-file SHA-256) the visible objects came from.
    public sealed class MapBake : MonoBehaviour
    {
        public string fileHash;
    }
}
