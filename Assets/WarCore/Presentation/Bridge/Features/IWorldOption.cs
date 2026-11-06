namespace WarCore.Presentation
{
    // A switch a recipe carries to turn one world feature on. The id names the switch: adding the same id twice is the same switch.
    // Nothing here mentions the simulation, so the public surface of this assembly stays free of it.
    public interface IWorldOption
    {
        string OptionId { get; }
    }
}
