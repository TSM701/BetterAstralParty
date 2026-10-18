using System;
namespace BepInEx
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class BepInPlugin : Attribute
    {
        public BepInPlugin(string guid, string name, string version) { }
    }
}
namespace BetterAstralParty
{
#if !NO_ATTRIBUTE
#if WRONG_ATTRIBUTE
    [BepInEx.BepInPlugin(Plugin.Guid, Plugin.Name, "9.0.0")]
#else
    [BepInEx.BepInPlugin(Plugin.Guid, Plugin.Name, Plugin.Version)]
#endif
#endif
#if DUPLICATE_ATTRIBUTE
    [BepInEx.BepInPlugin(Plugin.Guid, Plugin.Name, Plugin.Version)]
#endif
    public static class Plugin
    {
        public const string Guid = "kr.betterastralparty.mod", Version = "1.4.0", UpdateProtocol = "5", SettingsSchema = "1";
#if WRONG_NAME
        public const string Name = "OtherPlugin";
#else
        public const string Name = "BetterAstralParty";
#endif
#if WRONG_PROTOCOL
        public const int InstallBundleProtocol = 2;
#elif STRING_PROTOCOL
        public const string InstallBundleProtocol = "1";
#elif NONLITERAL_PROTOCOL
        public static readonly int InstallBundleProtocol = 1;
#else
        public const int InstallBundleProtocol = 1;
#endif
    }
}
