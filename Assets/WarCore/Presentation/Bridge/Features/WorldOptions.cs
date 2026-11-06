namespace WarCore.Presentation
{
    // The switches of the features that ship with the builder. The recipe's WithFacing, WithMotion and WithStandingOrders add them.
    internal sealed class SwitchOption : IWorldOption
    {
        public string OptionId { get; }
        internal SwitchOption(string id) { OptionId = id; }
    }

    internal static class WorldOptions
    {
        internal static readonly IWorldOption Facing = new SwitchOption("world.facing");
        internal static readonly IWorldOption Motion = new SwitchOption("world.motion");
        internal static readonly IWorldOption Standing = new SwitchOption("world.standing");
    }
}
