namespace BetterAstralParty
{
    public static class Plugin
    {
        public const string Guid = "kr.betterastralparty.mod", UpdateProtocol = "5", SettingsSchema = "1";
#if BAP_METADATA_OLD
        public const string Version = "1.4.0-dev";
#else
        public const string Version = "1.4.0";
#endif
        static Plugin() { throw new System.InvalidOperationException("Inert metadata must never execute."); }
    }
}
