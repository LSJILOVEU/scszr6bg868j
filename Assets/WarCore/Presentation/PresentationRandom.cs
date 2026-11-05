namespace WarCore.Presentation
{
    public sealed class PresentationRandom
    {
        private readonly System.Random random;
        public PresentationRandom(int seed) { random = new System.Random(seed); }
        public double NextUnit() => random.NextDouble();
    }
}
