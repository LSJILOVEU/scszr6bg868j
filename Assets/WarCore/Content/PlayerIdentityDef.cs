namespace WarCore.Content
{
    public readonly struct PlayerIdentityDef
    {
        public string Id { get; }
        public string LocalizationKey { get; }
        public PlayerIdentityDef(string id, string localizationKey)
        {
            Id = id; LocalizationKey = localizationKey;
        }
    }
}
