using AdjustablePortals.modules;
using BepInEx;
using BepInEx.Configuration;
using Jotunn.Entities;
using Jotunn.Managers;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AdjustablePortals {
    internal class ValConfig {
        public static ConfigFile cfg;
        public static ConfigEntry<bool> EnableDebugMode;

        public static ConfigEntry<float> PortalPieceActivationDistance;
        public static ConfigEntry<int> PortalNearbyPiecesForActivation;
        public static ConfigEntry<string> DefeatedEikthyrAllowedItems;
        public static ConfigEntry<string> DefeatedElderAllowedItems;
        public static ConfigEntry<string> DefeatedBonemassAllowedItems;
        public static ConfigEntry<string> DefeatedModerAllowItems;
        public static ConfigEntry<string> DefeatedYagluthAllowItems;
        public static ConfigEntry<string> DefeatedQueenAllowItems;
        public static ConfigEntry<string> DefeatedFaderAllowItems;
        public static ConfigEntry<bool> UsePrivateKeys;
        public static ConfigEntry<bool> EnablePortalPieceRequirements;
        public static ConfigEntry<bool> EnablePortalRequireFuel;
        public static ConfigEntry<int> PortalFuelUsagesPerBatch;
        public static ConfigEntry<int> PortalFuelBatchSize;
        public static ConfigEntry<string> PortalFuelPrefab;
        public static ConfigEntry<bool> EnableTeleportMounts;
        public static ConfigEntry<bool> EnableTeleportTames;
        public static ConfigEntry<float> TeleportTamesRadius;
        public static ConfigEntry<int> TeleportTamesMaxCount;
        public static ConfigEntry<bool> TeleportTamesRequireFollowing;
        public static ConfigEntry<bool> EnableTeleportCarts;
        public static ConfigEntry<bool> ReattachCartAfterTeleport;


        public ValConfig(ConfigFile cf) {
            // ensure all the config values are created
            cfg = cf;
            cfg.SaveOnConfigSet = true;
            CreateConfigValues(cf);
            Logger.setDebugLogging(EnableDebugMode.Value);
            SetupMainFileWatcher();
        }

        private void CreateConfigValues(ConfigFile Config) {
            // Debugmode
            EnableDebugMode = Config.Bind("Client config", "EnableDebugMode", false,
                new ConfigDescription("Enables Debug logging.",
                null,
                new ConfigurationManagerAttributes { IsAdvanced = true }));
            EnableDebugMode.SettingChanged += Logger.enableDebugLogging;

            EnablePortalPieceRequirements = BindServerConfig("PortalActivation", "EnablePortalPieceRequirements", true, "When enabled, portals require a set number of building pieces around them in order to activate");
            PortalNearbyPiecesForActivation = BindServerConfig("PortalActivation", "PortalNearbyPiecesForActivation", 300, "The number of building pieces required nearby in order for a portal to be activated.", false, 0, 10000);
            PortalPieceActivationDistance = BindServerConfig("PortalActivation", "PortalPieceActivationDistance", 100f, "The distance that will be checked for nearby building pieces to meet the required structures nearby.");

            EnablePortalRequireFuel = BindServerConfig("PortalActivation", "EnablePortalRequireFuel", false, "When enabled, portals require a fuel item to teleport users");
            PortalFuelUsagesPerBatch = BindServerConfig("PortalActivation", "PortalFuelUsagesPerBatch", 20, "The number of usages that one batch of the fuel type provides");
            PortalFuelBatchSize = BindServerConfig("PortalActivation", "PortalFuelBatchSize", 1, "The number of the portal fuel required for activation.");
            PortalFuelPrefab = BindServerConfig("PortalActivation", "PortalFuelPrefab", "SurtlingCore", "The prefab name that will be used for the portal costs.");

            EnableTeleportMounts = BindServerConfig("PortalCompanions", "EnableTeleportMounts", true, "When enabled, a player riding a mount into a portal takes the mount with them and is put back in the saddle on arrival.");
            EnableTeleportTames = BindServerConfig("PortalCompanions", "EnableTeleportTames", true, "When enabled, tamed creatures near the portal travel with the player to the destination.");
            TeleportTamesRadius = BindServerConfig("PortalCompanions", "TeleportTamesRadius", 5f, "The distance around the portal that tamed creatures are collected from.", false, 0f, 50f);
            TeleportTamesMaxCount = BindServerConfig("PortalCompanions", "TeleportTamesMaxCount", 5, "The most tamed creatures that one teleport will take along. The closest to the portal go first.", false, 0, 50);
            TeleportTamesRequireFollowing = BindServerConfig("PortalCompanions", "TeleportTamesRequireFollowing", true, "When enabled, only tames that have been told to follow the teleporting player travel with them. Turn this off to take every tame standing near the portal, penned livestock included.");
            EnableTeleportCarts = BindServerConfig("PortalCompanions", "EnableTeleportCarts", true, "When enabled, a cart the player is pulling travels with them. The cart may only hold items that the player themselves could teleport with, and blocks the teleport otherwise.");
            ReattachCartAfterTeleport = BindServerConfig("PortalCompanions", "ReattachCartAfterTeleport", true, "When enabled, a cart that travelled through a portal is hitched back up on arrival instead of being left loose at the destination.", null, true);


            UsePrivateKeys = BindServerConfig("PortalProgression", "UsePrivateKeys", false, "When enabled, the items below unlock for each player by the bosses that player has helped defeat, instead of by the bosses defeated anywhere in the world. Every player who landed a hit on a boss is credited with it when it dies.");
            UsePrivateKeys.SettingChanged += TeleportItems.ProgressionKeySourceChanged;
            DefeatedEikthyrAllowedItems = BindServerConfig("PortalProgression", "DefeatedEikthyrAllowedItems", "", "Comma seperated list of prefab items that will be allowed to teleported once Eikthyr is defeated.");
            DefeatedEikthyrAllowedItems.SettingChanged += TeleportItems.EikthyrAllowedTeleportsChanged;
            DefeatedElderAllowedItems = BindServerConfig("PortalProgression", "DefeatedElderAllowedItems", "", "Comma seperated list of prefab items that will be allowed to be teleported once The Elder is defeated.");
            DefeatedElderAllowedItems.SettingChanged += TeleportItems.ElderAllowedTeleportsChanged;
            DefeatedBonemassAllowedItems = BindServerConfig("PortalProgression", "DefeatedBonemassAllowedItems", "Bronze,Copper,Tin,CopperOre,TinOre,BronzeScrap", "Comma seperated list of prefab items that will be allowed to be teleported once Bonemass is defeated.");
            DefeatedBonemassAllowedItems.SettingChanged += TeleportItems.BonemassAllowedTeleportsChanged;
            DefeatedModerAllowItems = BindServerConfig("PortalProgression", "DefeatedModerAllowItems", "Iron,IronOre,Ironpit,IronScrap,chest_hildir1,chest_hildir2,chest_hildir3", "Comma seperated list of prefab items that will be allowed to be teleported once Moder is defeated.");
            DefeatedModerAllowItems.SettingChanged += TeleportItems.ModerAllowedTeleportsChanged;
            DefeatedYagluthAllowItems = BindServerConfig("PortalProgression", "DefeatedYagluthAllowItems", "Silver,SilverOre,DragonEgg", "Comma seperated list of prefab items that will be allowed to be teleported once Yagluth is defeated.");
            DefeatedYagluthAllowItems.SettingChanged += TeleportItems.YagluthAllowedTeleportsChanged;
            DefeatedQueenAllowItems = BindServerConfig("PortalProgression", "DefeatedQueenAllowItems", "MechanicalSpring,BlackMetal,BlackMetalScrap", "Comma seperated list of prefab items that will be allowed to be teleported once The Seeker Queen is defeated.");
            DefeatedQueenAllowItems.SettingChanged += TeleportItems.QueenAllowedTeleportsChanged;
            DefeatedFaderAllowItems = BindServerConfig("PortalProgression", "DefeatedFaderAllowItems", "DvergrNeedle", "Comma seperated list of prefab items that will be allowed to be teleported once Fader is defeated.");
            DefeatedFaderAllowItems.SettingChanged += TeleportItems.FaderAllowedTeleportsChanged;
        }





        // Held statically so the watcher is not collected while the game is running.
        private static FileSystemWatcher configWatcher;

        internal static void SetupMainFileWatcher() {
            // Setup a file watcher to detect changes to the config file
            configWatcher = new FileSystemWatcher();
            configWatcher.NotifyFilter = NotifyFilters.LastWrite;
            configWatcher.Path = Path.GetDirectoryName(cfg.ConfigFilePath);
            // Ignore changes to other files
            configWatcher.Filter = Path.GetFileName(cfg.ConfigFilePath);
            configWatcher.Changed += OnConfigFileChanged;
            configWatcher.SynchronizingObject = ThreadingHelper.SynchronizingObject;
            configWatcher.EnableRaisingEvents = true;
        }

        private static void OnConfigFileChanged(object sender, FileSystemEventArgs e) {
            // We only want the config changes being allowed if this is a server (ie in game in a hosted world or dedicated ideally)
            // ZNet does not exist at the main menu, where the file can still be edited.
            if (ZNet.instance == null || ZNet.instance.IsServer() == false) {
                return;
            }
            // Handle the config file change event
            Logger.LogInfo("Configuration file has been changed, reloading settings.");
            cfg.Reload();
        }

        /// <summary>
        /// Binds a server configuration entry for a list of strings with the specified category, key, default value,
        /// and description. This config will be server authoratative, editable by admins.
        /// </summary>
        /// <param name="catagory">The category under which the configuration entry is grouped. Cannot be null or empty.</param>
        /// <param name="key">The unique key identifying the configuration entry within the specified category. Cannot be null or empty.</param>
        /// <param name="value">The default list of strings to use for the configuration entry if no value is set.</param>
        /// <param name="description">A description of the configuration entry, used for documentation and display purposes.</param>
        /// <param name="advanced">Indicates whether the configuration entry is considered advanced. If <see langword="true"/>, the entry may
        /// be hidden from standard configuration views.</param>
        /// <returns>A <see cref="ConfigEntry{List{string}}"/> representing the bound server configuration entry.</returns>
        public static ConfigEntry<List<string>> BindServerConfig(string catagory, string key, List<string> value, string description, bool advanced = false) {
            return cfg.Bind(catagory, key, value,
                new ConfigDescription(description,
                null,
                new ConfigurationManagerAttributes { IsAdminOnly = true, IsAdvanced = advanced })
                );
        }

        /// <summary>
        /// Helper to bind configs for float types
        /// </summary>
        /// <param name="config_file"></param>
        /// <param name="catagory"></param>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="description"></param>
        /// <param name="advanced"></param>
        /// <param name="valmin"></param>
        /// <param name="valmax"></param>
        /// <returns></returns>
        public static ConfigEntry<float[]> BindServerConfig(string catagory, string key, float[] value, string description, bool advanced = false, float valmin = 0, float valmax = 150) {
            return cfg.Bind(catagory, key, value,
                new ConfigDescription(description,
                new AcceptableValueRange<float>(valmin, valmax),
                new ConfigurationManagerAttributes { IsAdminOnly = true, IsAdvanced = advanced })
                );
        }

        /// <summary>
        ///  Helper to bind configs for bool types
        /// </summary>
        /// <param name="config_file"></param>
        /// <param name="catagory"></param>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="description"></param>
        /// <param name="acceptableValues"></param>>
        /// <param name="advanced"></param>
        /// <returns></returns>
        public static ConfigEntry<bool> BindServerConfig(string catagory, string key, bool value, string description, AcceptableValueBase acceptableValues = null, bool advanced = false) {
            return cfg.Bind(catagory, key, value,
                new ConfigDescription(description,
                    acceptableValues,
                new ConfigurationManagerAttributes { IsAdminOnly = true, IsAdvanced = advanced })
                );
        }

        /// <summary>
        /// Helper to bind configs for int types
        /// </summary>
        /// <param name="config_file"></param>
        /// <param name="catagory"></param>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="description"></param>
        /// <param name="advanced"></param>
        /// <param name="valmin"></param>
        /// <param name="valmax"></param>
        /// <returns></returns>
        public static ConfigEntry<int> BindServerConfig(string catagory, string key, int value, string description, bool advanced = false, int valmin = 0, int valmax = 150) {
            return cfg.Bind(catagory, key, value,
                new ConfigDescription(description,
                new AcceptableValueRange<int>(valmin, valmax),
                new ConfigurationManagerAttributes { IsAdminOnly = true, IsAdvanced = advanced })
                );
        }

        /// <summary>
        /// Helper to bind configs for float types
        /// </summary>
        /// <param name="config_file"></param>
        /// <param name="catagory"></param>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="description"></param>
        /// <param name="advanced"></param>
        /// <param name="valmin"></param>
        /// <param name="valmax"></param>
        /// <returns></returns>
        public static ConfigEntry<float> BindServerConfig(string catagory, string key, float value, string description, bool advanced = false, float valmin = 0, float valmax = 150) {
            return cfg.Bind(catagory, key, value,
                new ConfigDescription(description,
                new AcceptableValueRange<float>(valmin, valmax),
                new ConfigurationManagerAttributes { IsAdminOnly = true, IsAdvanced = advanced })
                );
        }

        /// <summary>
        /// Helper to bind configs for strings
        /// </summary>
        /// <param name="config_file"></param>
        /// <param name="catagory"></param>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="description"></param>
        /// <param name="advanced"></param>
        /// <returns></returns>
        public static ConfigEntry<string> BindServerConfig(string catagory, string key, string value, string description, AcceptableValueList<string> acceptableValues = null, bool advanced = false) {
            return cfg.Bind(catagory, key, value,
                new ConfigDescription(
                    description,
                    acceptableValues,
                new ConfigurationManagerAttributes { IsAdminOnly = true, IsAdvanced = advanced })
                );
        }
    }
}
