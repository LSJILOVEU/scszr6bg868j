using System.Collections.Generic;

namespace WarCore.Presentation
{
    // Default key table for the player's held actions. Key names are Unity KeyCode enum names.
    // PLACEHOLDER: the keys are to be tuned; there is no rebinding screen yet.
    // Keys already taken elsewhere: W A S D move, Mouse0 primary attack, Q E R F and Mouse2 camera.
    public static class PlayerKeys
    {
        // A new list on every call, so a caller that edits it cannot change the next caller's table.
        public static IReadOnlyList<PlayerKeyBinding> Default => new[]
        {
            // Two block keys, so blocking works without a right mouse button.
            new PlayerKeyBinding("Mouse1", PlayerKeyAction.Block),
            new PlayerKeyBinding("Space", PlayerKeyAction.Block),
            new PlayerKeyBinding("G", PlayerKeyAction.Throw),
            new PlayerKeyBinding("Alpha1", PlayerKeyAction.Skill, 1),
            new PlayerKeyBinding("Alpha2", PlayerKeyAction.Skill, 2),
            new PlayerKeyBinding("Alpha3", PlayerKeyAction.Skill, 3),
            new PlayerKeyBinding("Alpha4", PlayerKeyAction.Skill, 4),
        };
    }
}
