using System;
namespace BepInEx
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class BepInPlugin : Attribute
    { public BepInPlugin(string guid, string name, string version) { } }
}
namespace BetterAstralParty
{
    [BepInEx.BepInPlugin(Guid, Name, Version)]
    public sealed class Plugin
    {
        public const string Guid = "kr.betterastralparty.mod", Name = "BetterAstralParty", Version = FixtureBoundary.Version, UpdateProtocol = "4", SettingsSchema = "1";
        public const int InstallBundleProtocol = 1;
    }
}
