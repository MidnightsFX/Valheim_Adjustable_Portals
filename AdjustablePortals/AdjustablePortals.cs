using AdjustablePortals.modules;
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
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    [BepInDependency("org.bepinex.plugins.targetportal", BepInDependency.DependencyFlags.SoftDependency)]
    internal class AdjustablePortals : BaseUnityPlugin
    {
        public const string PluginGUID = "MidnightsFX.AdjustablePortals";
        public const string PluginName = "AdjustablePortals";
        public const string PluginVersion = "0.4.0";
        internal static Harmony Harmony = new Harmony(PluginGUID);

        public static CustomLocalization Localization = LocalizationManager.Instance.GetLocalization();
        internal static ManualLogSource Log;
        // Held so modules without a MonoBehaviour of their own have somewhere to run a
        // coroutine that has to outlive the frame it was started on.
        internal static AdjustablePortals Instance;

        public void Awake() {
            Log = this.Logger;
            Instance = this;
            new ValConfig(Config);
            TeleportItems.SetupTeleportLists();
            Compatibility.CheckModCompat();
            Assembly assembly = Assembly.GetExecutingAssembly();
            Harmony.PatchAll(assembly);
            Compatibility.TargetPortalCompat();
        }
    }
}