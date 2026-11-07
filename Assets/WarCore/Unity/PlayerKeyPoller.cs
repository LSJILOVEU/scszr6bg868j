using System;
using System.Collections.Generic;
using UnityEngine;
using WarCore.Presentation;

namespace WarCore.Unity
{
    // Reads the player's bound keys from a key source (the MP host passes Input.GetKey). Key names are KeyCode enum names,
    // parsed once here; the delegate handed to PlayerInputMapper.Map is built once, so polling allocates nothing per frame.
    public sealed class PlayerKeyPoller
    {
        private readonly Dictionary<string, KeyCode> codes = new Dictionary<string, KeyCode>(StringComparer.Ordinal);
        private readonly Func<KeyCode, bool> source;
        private readonly Func<string, bool> isKeyDown;

        public PlayerKeyPoller(IReadOnlyList<PlayerKeyBinding> bindings, Func<KeyCode, bool> keySource)
        {
            if (bindings == null) throw new ArgumentNullException(nameof(bindings));
            source = keySource ?? throw new ArgumentNullException(nameof(keySource));
            foreach (PlayerKeyBinding binding in bindings)
            {
                if (codes.ContainsKey(binding.Key)) continue;
                // A numeric name parses to any value, so only names of defined KeyCode members are accepted.
                if (!Enum.TryParse(binding.Key, false, out KeyCode code) || !Enum.IsDefined(typeof(KeyCode), code))
                    throw new ArgumentException("Unknown key name: " + binding.Key, nameof(bindings));
                codes.Add(binding.Key, code);
            }
            isKeyDown = IsKeyDown;
        }

        // The cached reader while the window has focus; null otherwise, which the mapper reads as no key held.
        public Func<string, bool> KeyDown(bool focused) => focused ? isKeyDown : null;

        private bool IsKeyDown(string key) => codes.TryGetValue(key, out KeyCode code) && source(code);
    }
}
