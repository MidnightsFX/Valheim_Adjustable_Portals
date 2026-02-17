using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using System.Reflection;

namespace AdjustablePortals
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    //[NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    internal class AdjustablePortals : BaseUnityPlugin
    {
        public const string PluginGUID = "MidnightsFX.AdjustablePortals";
        public const string PluginName = "AdjustablePortals";
        public const string PluginVersion = "0.0.1";
        internal static Harmony Harmony = new Harmony(PluginGUID);

        public static CustomLocalization Localization = LocalizationManager.Instance.GetLocalization();
        internal static ManualLogSource Log;

        public void Awake()
        {
            new ValConfig(Config);
            Assembly assembly = Assembly.GetExecutingAssembly();
            Harmony.PatchAll(assembly);
        }
    }
}