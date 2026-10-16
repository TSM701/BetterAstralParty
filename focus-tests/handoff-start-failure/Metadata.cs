namespace BetterAstralParty
{
    public static class Plugin
    {
        public const string Guid = "kr.betterastralparty.mod", UpdateProtocol = "4", SettingsSchema = "1";
#if BAP_METADATA_OLD
        public const string Version = "1.3.0-dev";
#else
        public const string Version = "1.3.0";
#endif
        static Plugin() { throw new System.InvalidOperationException("Inert metadata must never execute."); }
    }
}
