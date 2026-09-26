using BepInEx.Configuration;
using UnityEngine;

namespace ValheimTweaks
{
    /// <summary>
    /// Mod options. Sections are numbered because the Configuration Manager
    /// sorts alphabetically.
    ///
    /// Conventions used in the options:
    ///   float 0   = do not override (use whatever the game menu sets)
    ///   int  -2   = do not override, -1 = no limit (in the light options)
    /// </summary>
    internal static class ModConfig
    {
        // ---------- 00 - General ----------
        internal static ConfigEntry<bool> HotReload;

        // ---------- 01 - Diagnostics ----------
        internal static ConfigEntry<bool> DumpOnWorldLoad;
        internal static ConfigEntry<bool> DumpNow;
        internal static ConfigEntry<bool> DumpChestsNow;
        internal static ConfigEntry<bool> ProfileNow;
        internal static ConfigEntry<float> ProfileSeconds;
        internal static ConfigEntry<bool> ProfileSystems;
        internal static ConfigEntry<bool> ChestAuditLog;
        internal static ConfigEntry<bool> ChestAuditContents;

        // ---------- 07 - Backup ----------
        internal static ConfigEntry<bool> ChestSnapshotEnabled;
        internal static ConfigEntry<float> ChestSnapshotMinutes;
        internal static ConfigEntry<bool> RestoreSnapshotNow;
        internal static ConfigEntry<string> RestoreSnapshotFile;

        // ---------- 02 - Network ----------
        internal static ConfigEntry<bool> TimeoutEnabled;
        internal static ConfigEntry<float> TimeoutSeconds;

        // ---------- 03 - Visual ----------
        internal static ConfigEntry<bool> ForceAnisotropic;
        internal static ConfigEntry<int> AnisotropicLevel;
        internal static ConfigEntry<bool> FullResTextures;
        internal static ConfigEntry<bool> FogEnabled;
        internal static ConfigEntry<float> FogDensityScale;
        internal static ConfigEntry<float> AmbientBrightness;
        internal static ConfigEntry<bool> TrilightAmbient;
        internal static ConfigEntry<float> LodBiasOverride;
        internal static ConfigEntry<bool> SsaoOverride;
        internal static ConfigEntry<int> SsaoSampleCount;
        internal static ConfigEntry<bool> SsaoFullResolution;
        internal static ConfigEntry<float> SsaoIntensity;
        internal static ConfigEntry<float> SsaoRadius;
        internal static ConfigEntry<float> SsaoPower;
        internal static ConfigEntry<bool> AaOverride;
        internal static ConfigEntry<bool> AaUseTaa;
        internal static ConfigEntry<int> FxaaPreset;
        internal static ConfigEntry<float> TaaJitterSpread;
        internal static ConfigEntry<float> TaaSharpen;
        internal static ConfigEntry<float> TaaStationaryBlending;
        internal static ConfigEntry<float> TaaMotionBlending;
        internal static ConfigEntry<float> ShadowDistance;
        internal static ConfigEntry<int> ShadowCascades;
        internal static ConfigEntry<int> PointLightLimit;
        internal static ConfigEntry<int> PointLightShadowLimit;
        internal static ConfigEntry<float> ClutterDistance;
        internal static ConfigEntry<float> FieldOfView;
        internal static ConfigEntry<int> ShadowResolution;
        internal static ConfigEntry<int> Tesselation;
        internal static ConfigEntry<float> ClutterAmountScale;
        internal static ConfigEntry<bool> FreezeTimeOfDay;
        internal static ConfigEntry<float> TimeOfDay;
        internal static ConfigEntry<string> ForceWeather;
        internal static ConfigEntry<int> DisplayMode;
        internal static ConfigEntry<int> DisplayWidth;
        internal static ConfigEntry<int> DisplayHeight;

        // ---------- 04 - Performance ----------
        internal static ConfigEntry<int> MaxQueuedFrames;
        internal static ConfigEntry<float> ZoneGenBudgetMs;
        internal static ConfigEntry<float> ZdoReleaseIntervalSec;
        internal static ConfigEntry<float> GcSliceMs;
        internal static ConfigEntry<int> MaxSmoke;
        internal static ConfigEntry<bool> FadeDistantSmokeFirst;
        internal static ConfigEntry<float> TeleportSpeed;
        internal static ConfigEntry<int> TeleportSimDistance;
        internal static ConfigEntry<BepInEx.Configuration.KeyboardShortcut> StoreMarkKey;
        internal static ConfigEntry<BepInEx.Configuration.KeyboardShortcut> StoreMarkedKey;
        internal static ConfigEntry<BepInEx.Configuration.KeyboardShortcut> StoreHoveredKey;
        internal static ConfigEntry<float> StoreRadius;
        internal static ConfigEntry<bool> StoreFallbackAnyChest;
        internal static ConfigEntry<bool> StoreNonOwnedChests;
        internal static ConfigEntry<float> StoreHandshakeWaitSec;
        internal static ConfigEntry<bool> StoreRollback;
        internal static ConfigEntry<float> StoreVerifySeconds;
        internal static ConfigEntry<bool> StoreHudEnabled;
        internal static ConfigEntry<float> StoreHudX;
        internal static ConfigEntry<float> StoreHudY;
        internal static ConfigEntry<float> StoreHudSeconds;
        internal static ConfigEntry<int> StoreHudMaxLines;
        internal static ConfigEntry<float> StoreHudFontSize;
        internal static ConfigEntry<bool> ChestPeekEnabled;
        internal static ConfigEntry<float> ChestPeekDistance;
        internal static ConfigEntry<float> ChestPeekHideDelay;
        internal static ConfigEntry<float> ChestPeekX;
        internal static ConfigEntry<BepInEx.Configuration.KeyboardShortcut> AutoMineKey;
        internal static ConfigEntry<string> AutoMineOres;
        internal static ConfigEntry<float> AutoMineRangeScale;
        internal static ConfigEntry<float> AutoMineHudY;
        internal static ConfigEntry<bool> AutoMineDebug;
        internal static ConfigEntry<float> AutoMineToggleSeconds;
        internal static ConfigEntry<bool> ChestSearchEnabled;
        internal static ConfigEntry<float> ChestSearchRadius;
        internal static ConfigEntry<float> ChestSearchRefresh;
        internal static ConfigEntry<float> ChestSearchScrollSpeed;
        internal static ConfigEntry<int> ChestSearchColumns;
        internal static ConfigEntry<float> ChestSearchSlotSize;
        internal static ConfigEntry<float> ChestSearchLabelSize;
        internal static ConfigEntry<float> ChestSearchWidth;
        internal static ConfigEntry<float> ChestSearchHeight;
        internal static ConfigEntry<float> ChestSearchX;
        internal static ConfigEntry<float> ChestSearchY;
        internal static ConfigEntry<float> ChestSearchButtonX;
        internal static ConfigEntry<float> ChestSearchButtonY;
        internal static ConfigEntry<bool> ChestSearchBlockMove;
        internal static ConfigEntry<bool> ChestSearchDebug;
        internal static ConfigEntry<BepInEx.Configuration.KeyboardShortcut> ChestSearchKey;
        internal static ConfigEntry<bool> AutoRepairOnOpen;
        internal static ConfigEntry<bool> RepairButtonRepairsAll;
        internal static ConfigEntry<bool> SimDistanceEnabled;
        internal static ConfigEntry<int> SimDistanceNear;
        internal static ConfigEntry<int> SimDistanceFar;

        // ---------- 06 - Building ----------
        internal static ConfigEntry<bool> BuildFromChestsEnabled;
        internal static ConfigEntry<float> BuildFromChestsRadius;
        internal static ConfigEntry<float> BuildFromChestsRefresh;

        internal static void Init(ConfigFile cfg)
        {
            HotReload = cfg.Bind("00 - General", "HotReload", true,
                "Applies changes to this file on the fly, without needing to close the game.");

            // ------------------------------------------------------------------
            // 01 - Diagnostics
            // ------------------------------------------------------------------
            DumpOnWorldLoad = cfg.Bind("01 - Diagnostics", "DumpOnWorldLoad", true,
                "When you enter a world, writes a summary of the active video settings to the log.");

            DumpNow = cfg.Bind("01 - Diagnostics", "DumpNow", false,
                "Generates that summary now. Unchecks itself afterwards.");

            DumpChestsNow = cfg.Bind("01 - Diagnostics", "DumpChestsNow", false,
                "Writes to the log the contents of every chest currently loaded, with the " +
                "chest's id, owner and position. Use it to find where an item ended up: turn " +
                "it on near a base and search the log for the item name. Unchecks itself.");

            ProfileNow = cfg.Bind("01 - Diagnostics", "ProfileNow", false,
                "Measures performance for the next few seconds and writes the result to the log. " +
                "Unchecks itself afterwards.");

            ProfileSeconds = cfg.Bind("01 - Diagnostics", "ProfileSeconds", 20f,
                new ConfigDescription("Duration of the measurement, in seconds.",
                    new AcceptableValueRange<float>(5f, 120f)));

            ProfileSystems = cfg.Bind("01 - Diagnostics", "ProfileSystems", true,
                "Includes in the measurement the time each part of the game spends per frame.");

            ChestAuditLog = cfg.Bind("01 - Diagnostics", "ChestAuditLog", true,
                "Writes to the log every chest interaction on the network: who opened, " +
                "stacked or took all from which chest, whether the owner allowed it, and " +
                "the chest's owner and id. This is what shows the other player's actions. " +
                "Useful to investigate a missing item; can be turned off on a busy base.");

            ChestAuditContents = cfg.Bind("01 - Diagnostics", "ChestAuditContents", true,
                "Includes the full list of items (and amounts) whenever a chest's contents " +
                "or owner change. This is the detailed part of ChestAuditLog; turn it off " +
                "to keep the log smaller if you only care about who touched which chest.");

            // ------------------------------------------------------------------
            // 07 - Backup
            // ------------------------------------------------------------------
            ChestSnapshotEnabled = cfg.Bind("07 - Backup", "ChestSnapshotEnabled", true,
                "Every few minutes, writes the contents of every loaded chest to a small " +
                "file next to the config. It is the only way to bring back an item that " +
                "went missing, since the game keeps no history. Costs almost nothing.");

            ChestSnapshotMinutes = cfg.Bind("07 - Backup", "ChestSnapshotMinutes", 5f,
                new ConfigDescription(
                    "How often the snapshot is taken, in minutes. Shorter means less can be " +
                    "lost between snapshots.",
                    new AcceptableValueRange<float>(0.5f, 30f)));

            RestoreSnapshotNow = cfg.Bind("07 - Backup", "RestoreSnapshotNow", false,
                "Puts back, into the chests loaded around you, every item present in the " +
                "snapshot but missing now. Read the log: it lists what was restored and " +
                "warns about chests whose zone was not loaded. Unchecks itself afterwards.");

            RestoreSnapshotFile = cfg.Bind("07 - Backup", "RestoreSnapshotFile", "",
                "Which snapshot file to restore from, e.g. snapshot-20260922-233000.txt. " +
                "Leave empty to use the most recent one.");

            // ------------------------------------------------------------------
            // 02 - Network
            // ------------------------------------------------------------------
            TimeoutEnabled = cfg.Bind("02 - Network", "TimeoutEnabled", true,
                "Adjusts how long the game tolerates an unresponsive connection before " +
                "dropping it. Everyone on the server needs the mod and the same value, because each " +
                "person closes their own connection.");

            TimeoutSeconds = cfg.Bind("02 - Network", "TimeoutSeconds", 90f,
                new ConfigDescription(
                    "Seconds before giving up on a stalled connection. The game uses 30.",
                    new AcceptableValueRange<float>(30f, 600f)));

            // ------------------------------------------------------------------
            // 03 - Visual
            // ------------------------------------------------------------------
            ForceAnisotropic = cfg.Bind("03 - Visual", "ForceAnisotropic", true,
                "Keeps the ground, roads and floors sharp when seen at an angle, instead of " +
                "blurry. Almost no cost.");

            AnisotropicLevel = cfg.Bind("03 - Visual", "AnisotropicLevel", 16,
                new ConfigDescription("Filter strength. 1 turns it off, 16 is the maximum.",
                    new AcceptableValueRange<int>(1, 16)));

            FullResTextures = cfg.Bind("03 - Visual", "FullResTextures", true,
                "Keeps textures at full resolution.");

            FogEnabled = cfg.Bind("03 - Visual", "FogEnabled", true,
                "Unchecking removes the fog entirely. It looks artificial, but it shows " +
                "how much of it is hiding the landscape.");

            FogDensityScale = cfg.Bind("03 - Visual", "FogDensityScale", 1.0f,
                new ConfigDescription(
                    "How much fog the game draws. 1.0 is the default; below that the horizon " +
                    "opens up and distant colors stop washing out. No performance cost.",
                    new AcceptableValueRange<float>(0f, 2f)));

            AmbientBrightness = cfg.Bind("03 - Visual", "AmbientBrightness", 1.0f,
                new ConfigDescription(
                    "Brightens or darkens the ambient light. Above 1 shadows become less " +
                    "crushed; below 1 increases contrast.",
                    new AcceptableValueRange<float>(0.25f, 2f)));

            TrilightAmbient = cfg.Bind("03 - Visual", "TrilightAmbient", false,
                "Ambient light coming from sky, horizon and ground separately, instead of a single " +
                "color. Gives objects more volume, but can change the tone of scenes. Experimental.");

            LodBiasOverride = cfg.Bind("03 - Visual", "LodBiasOverride", 0f,
                new ConfigDescription(
                    "Distance at which items on the ground, stones and details stop being " +
                    "drawn. Doubling the value doubles the distance, and charges performance " +
                    "accordingly. 0 uses whatever is in the game menu.",
                    new AcceptableValueRange<float>(0f, 20f)));

            SsaoOverride = cfg.Bind("03 - Visual", "SsaoOverride", true,
                "Soft shadow where objects touch the ground and in corners. It is what removes the " +
                "look of things pasted on top of the scenery.");

            SsaoSampleCount = cfg.Bind("03 - Visual", "SsaoSampleCount", 2,
                new ConfigDescription(
                    "Contact shadow quality: 0 low, 1 medium, 2 high, 3 very high.",
                    new AcceptableValueRange<int>(0, 3)));

            SsaoFullResolution = cfg.Bind("03 - Visual", "SsaoFullResolution", true,
                "Computes the contact shadow at full resolution. It looks sharper and costs " +
                "more GPU. Uncheck if you need to save performance.");

            SsaoIntensity = cfg.Bind("03 - Visual", "SsaoIntensity", 1.0f,
                new ConfigDescription("Strength of the contact shadow.",
                    new AcceptableValueRange<float>(0f, 3f)));

            SsaoRadius = cfg.Bind("03 - Visual", "SsaoRadius", 2.0f,
                new ConfigDescription(
                    "Reach of the shadow, in meters. Small only marks the corners; large " +
                    "shades entire areas.",
                    new AcceptableValueRange<float>(0.25f, 8f)));

            SsaoPower = cfg.Bind("03 - Visual", "SsaoPower", 1.8f,
                new ConfigDescription("Shadow contrast. Higher darkens the core.",
                    new AcceptableValueRange<float>(0.5f, 6f)));

            AaOverride = cfg.Bind("03 - Visual", "AaOverride", true,
                "Controls anti-aliasing, which the game only turns on and off without letting you " +
                "choose the quality.");

            AaUseTaa = cfg.Bind("03 - Visual", "AaUseTaa", false,
                "Uses TAA instead of FXAA. Much cleaner edges on foliage, ropes and " +
                "fences, but can leave trailing behind moving objects.");

            FxaaPreset = cfg.Bind("03 - Visual", "FxaaPreset", 4,
                new ConfigDescription(
                    "FXAA quality, from 0 (fastest) to 4 (cleanest). Only applies with " +
                    "AaUseTaa unchecked.",
                    new AcceptableValueRange<int>(0, 4)));

            TaaJitterSpread = cfg.Bind("03 - Visual", "TaaJitterSpread", 0.75f,
                new ConfigDescription("TAA smoothing. Higher smooths more and blurs more.",
                    new AcceptableValueRange<float>(0.1f, 1f)));

            TaaSharpen = cfg.Bind("03 - Visual", "TaaSharpen", 0.3f,
                new ConfigDescription("Compensates for TAA blur. Too much creates halos on edges.",
                    new AcceptableValueRange<float>(0f, 3f)));

            TaaStationaryBlending = cfg.Bind("03 - Visual", "TaaStationaryBlending", 0.95f,
                new ConfigDescription("Image stability with the camera still.",
                    new AcceptableValueRange<float>(0f, 0.99f)));

            TaaMotionBlending = cfg.Bind("03 - Visual", "TaaMotionBlending", 0.85f,
                new ConfigDescription("Lower this if you see trailing behind moving objects.",
                    new AcceptableValueRange<float>(0f, 0.99f)));

            ShadowDistance = cfg.Bind("03 - Visual", "ShadowDistance", 0f,
                new ConfigDescription(
                    "How far the sun's shadows appear, in meters. The game menu goes up to " +
                    "150. 0 uses the menu.",
                    new AcceptableValueRange<float>(0f, 500f)));

            ShadowCascades = cfg.Bind("03 - Visual", "ShadowCascades", 0,
                new ConfigDescription(
                    "Sun shadow cascades: more cascades keep the shadow sharp even at a " +
                    "distance. Accepts 1, 2 or 4. 0 uses the menu.",
                    new AcceptableValueRange<int>(0, 4)));

            ShadowResolution = cfg.Bind("03 - Visual", "ShadowResolution", 0,
                new ConfigDescription(
                    "Shadow sharpness: 1 low, 2 medium, 3 high, 4 very high. The game " +
                    "menu stops at high. 0 uses the menu.",
                    new AcceptableValueRange<int>(0, 4)));

            PointLightLimit = cfg.Bind("03 - Visual", "PointLightLimit", -2,
                new ConfigDescription(
                    "How many torches and campfires stay lit at the same time. The game offers " +
                    "4, 15, 40 or no limit. Here you can set any number. " +
                    "-2 uses the menu, -1 is no limit.",
                    new AcceptableValueRange<int>(-2, 64)));

            PointLightShadowLimit = cfg.Bind("03 - Visual", "PointLightShadowLimit", -2,
                new ConfigDescription(
                    "How many of those lights cast shadows. It is the heaviest setting in a base " +
                    "full of fire. The game offers 0, 1, 3 or no limit; something between 6 and 8 " +
                    "is usually the sweet spot. -2 uses the menu, -1 is no limit.",
                    new AcceptableValueRange<int>(-2, 32)));

            ClutterDistance = cfg.Bind("03 - Visual", "ClutterDistance", 0f,
                new ConfigDescription(
                    "How far the grass is drawn, in meters. The game uses 45, and that is why " +
                    "the grass seems to grow in front of you as you walk. Raising it pushes that " +
                    "limit back, but the area grows squared and costs a lot. 0 uses the default.",
                    new AcceptableValueRange<float>(0f, 150f)));

            ClutterAmountScale = cfg.Bind("03 - Visual", "ClutterAmountScale", 0f,
                new ConfigDescription(
                    "Grass density, regardless of range. 2.0 doubles the amount. " +
                    "0 uses the default.",
                    new AcceptableValueRange<float>(0f, 4f)));

            Tesselation = cfg.Bind("03 - Visual", "Tesselation", 0,
                new ConfigDescription(
                    "Gives the terrain real relief instead of a flat texture. Only costs GPU. " +
                    "0 uses the menu, 1 on, 2 off.",
                    new AcceptableValueRange<int>(0, 2)));

            FieldOfView = cfg.Bind("03 - Visual", "FieldOfView", 0f,
                new ConfigDescription(
                    "Field of view. The game uses 65 and won't let you change it. Above 100 the edges " +
                    "distort. 0 keeps the default.",
                    new AcceptableValueRange<float>(0f, 120f)));

            DisplayMode = cfg.Bind("03 - Visual", "DisplayMode", 0,
                new ConfigDescription(
                    "Window mode: 1 exclusive fullscreen, 2 borderless, 3 windowed. " +
                    "Exclusive usually makes movement smoother, and the game menu doesn't " +
                    "offer that option. 0 keeps it as is.",
                    new AcceptableValueRange<int>(0, 3)));

            DisplayWidth = cfg.Bind("03 - Visual", "DisplayWidth", 0,
                new ConfigDescription("Width. 0 uses the desktop resolution.",
                    new AcceptableValueRange<int>(0, 7680)));

            DisplayHeight = cfg.Bind("03 - Visual", "DisplayHeight", 0,
                new ConfigDescription("Height. 0 uses the desktop resolution.",
                    new AcceptableValueRange<int>(0, 4320)));

            FreezeTimeOfDay = cfg.Bind("03 - Visual", "FreezeTimeOfDay", false,
                "Freezes the time of day on your screen, useful for comparing settings without the sun " +
                "moving. Only affects you: the world's time and the other players' " +
                "continues normally.");

            TimeOfDay = cfg.Bind("03 - Visual", "TimeOfDay", 0.5f,
                new ConfigDescription(
                    "Time used when the previous option is checked. 0 midnight, 0.25 dawn, " +
                    "0.5 noon, 0.75 dusk.",
                    new AcceptableValueRange<float>(0f, 1f)));

            ForceWeather = cfg.Bind("03 - Visual", "ForceWeather", "",
                "Freezes the weather on your screen. Empty follows the biome's normal. " +
                "Ex.: Clear, Misty, Rain, ThunderStorm, SnowStorm.");

            // ------------------------------------------------------------------
            // 04 - Performance
            // ------------------------------------------------------------------
            // ------------------------------------------------------------------
            // 05 - Convenience
            // ------------------------------------------------------------------
            TeleportSpeed = cfg.Bind("05 - Convenience", "TeleportSpeed", 4f,
                new ConfigDescription(
                    "Speeds up the portal. The game imposes a fixed 8-second wait on a long " +
                    "trip, even when the destination has already loaded; 4 turns that into 2 " +
                    "seconds. The check that the destination is ready still applies, so only " +
                    "the artificial wait goes away, never the real one. 1 keeps the default.",
                    new AcceptableValueRange<float>(1f, 8f)));

            TeleportSimDistance = cfg.Bind("05 - Convenience", "TeleportSimDistance", 2,
                new ConfigDescription(
                    "Reduces the simulation distance ONLY during the portal crossing and " +
                    "restores it on arrival. It is by far the biggest gain: the destination only releases " +
                    "when the central zone finishes loading, and with the normal distance it " +
                    "competes in the queue with over a hundred zones. Measured: 15s at default versus 1.5s " +
                    "with 2 and 0.7s with 1. Lower values are faster but can leave " +
                    "objects popping in gradually on arrival. 0 turns it off. " +
                    "Does not affect other players.",
                    new AcceptableValueRange<int>(0, 8)));

            // --- Store in nearby chests ---
            // The three keys below ship with NO shortcut since the chest panel started
            // accepting Ctrl+click. The LeftControl here in particular clashed with
            // it. If you prefer the old way, just pick a key in the F1 menu.
            StoreMarkKey = cfg.Bind("05 - Convenience", "StoreMarkKey",
                new KeyboardShortcut(KeyCode.None),
                "With the inventory open, point at an item and press this key to mark it " +
                "as 'store in chests'. The marked item gets a bluish icon and the mark " +
                "follows the item, even if it changes location.");

            StoreMarkedKey = cfg.Bind("05 - Convenience", "StoreMarkedKey",
                new KeyboardShortcut(KeyCode.None),
                "Stores all marked items in nearby chests at once.");

            StoreHoveredKey = cfg.Bind("05 - Convenience", "StoreHoveredKey",
                new KeyboardShortcut(KeyCode.None),
                "Immediately stores only the pointed item, without needing to mark it first.");

            StoreRadius = cfg.Bind("05 - Convenience", "StoreRadius", 12f,
                new ConfigDescription(
                    "Distance in meters to search for chests.",
                    new AcceptableValueRange<float>(2f, 50f)));

            StoreFallbackAnyChest = cfg.Bind("05 - Convenience", "StoreFallbackAnyChest", false,
                "By default an item only goes into a chest that ALREADY has that item, to avoid " +
                "messing up the organization. With this on, whatever is left goes into any " +
                "chest with space.");

            StoreNonOwnedChests = cfg.Bind("05 - Convenience", "StoreNonOwnedChests", true,
                "Also stores into chests currently owned by the other player's client. The " +
                "game's ownership handshake hands the chest over first, and the write then " +
                "waits for that client's copy of the contents to arrive (StoreHandshakeWaitSec) " +
                "before touching anything -- without that wait the write is discarded by the " +
                "network and the items vanish at the next ownership handover. A chest whose " +
                "owner is offline is skipped (nobody can hand it over, and the request would " +
                "just stall). Turn this off to only ever touch chests you own: it is the one " +
                "setting that removes this whole class of problem, at the cost of leaving " +
                "items in your backpack when only the other player's chests have room.");

            StoreHandshakeWaitSec = cfg.Bind("05 - Convenience", "StoreHandshakeWaitSec", 2f,
                new ConfigDescription(
                    "How long to wait, after the other player's client hands a chest over, for " +
                    "its copy of the contents to arrive before writing. The handover answer is " +
                    "instant but the contents travel on the slow ZDO loop; writing in between " +
                    "produces a save the rest of the network throws away. Normally the copy " +
                    "arrives well inside this and the wait ends early -- this is only the " +
                    "ceiling. Raise it on a laggy server, lower it if storing feels sluggish. " +
                    "Chests you already own are never delayed.",
                    new AcceptableValueRange<float>(0.2f, 5f)));

            StoreRollback = cfg.Bind("05 - Convenience", "StoreRollback", true,
                "After storing, watches whether the network keeps the items. Saving a chest " +
                "stamps a revision on it, so the moment that revision is replaced is the only " +
                "moment a store can be undone -- that is when the check looks, instead of on a " +
                "timer. If the items are gone and the chest was still yours, nobody could have " +
                "taken them and they go back to your backpack. If the chest had already been " +
                "handed to another player, it only reports, because they may have taken them. " +
                "Items you took back out yourself are never counted as lost.");

            StoreVerifySeconds = cfg.Bind("05 - Convenience", "StoreVerifySeconds", 120f,
                new ConfigDescription(
                    "How long to keep watching a store. The overwrite arrives when the chest " +
                    "changes owner, which in a measured session was 27s to 3min after the store, " +
                    "so a short window sees nothing. Costs nothing while waiting: the check only " +
                    "runs when the chest reads new contents off the network. A chest whose zone " +
                    "unloads stops being observable and the watch is dropped.",
                    new AcceptableValueRange<float>(5f, 600f)));

            StoreHudEnabled = cfg.Bind("05 - Convenience", "StoreHudEnabled", true,
                "Shows a list in the bottom-left corner of what was stored, with the item's " +
                "icon and, when the chest has a custom name, an arrow and the name.");

            StoreHudX = cfg.Bind("05 - Convenience", "StoreHudX", 20f,
                new ConfigDescription("Distance from the left edge, in pixels.",
                    new AcceptableValueRange<float>(0f, 800f)));

            StoreHudY = cfg.Bind("05 - Convenience", "StoreHudY", 240f,
                new ConfigDescription(
                    "Height from the bottom edge, in pixels. The default already goes over " +
                    "the health and food HUD; adjust if it overlaps at your resolution.",
                    new AcceptableValueRange<float>(0f, 900f)));

            StoreHudSeconds = cfg.Bind("05 - Convenience", "StoreHudSeconds", 7f,
                new ConfigDescription("How long each line stays on screen.",
                    new AcceptableValueRange<float>(1f, 20f)));

            StoreHudMaxLines = cfg.Bind("05 - Convenience", "StoreHudMaxLines", 8,
                new ConfigDescription("Maximum number of lines at the same time.",
                    new AcceptableValueRange<int>(1, 20)));

            StoreHudFontSize = cfg.Bind("05 - Convenience", "StoreHudFontSize", 14f,
                new ConfigDescription("Font size of the list.",
                    new AcceptableValueRange<float>(8f, 28f)));

            ChestPeekEnabled = cfg.Bind("05 - Convenience", "ChestPeekEnabled", true,
                "When aiming at a chest up close, shows its contents on the right side without " +
                "needing to open it. Also works for carts and ships.");

            ChestPeekDistance = cfg.Bind("05 - Convenience", "ChestPeekDistance", 5f,
                new ConfigDescription(
                    "Maximum distance to peek. Aiming from far away isn't enough.",
                    new AcceptableValueRange<float>(1f, 20f)));

            ChestPeekHideDelay = cfg.Bind("05 - Convenience", "ChestPeekHideDelay", 0.3f,
                new ConfigDescription(
                    "Seconds the panel stays after you look away. Avoids " +
                    "flickering when your aim passes close by.",
                    new AcceptableValueRange<float>(0f, 3f)));

            ChestPeekX = cfg.Bind("05 - Convenience", "ChestPeekX", 20f,
                new ConfigDescription("Distance from the right edge, in pixels.",
                    new AcceptableValueRange<float>(0f, 800f)));

            AutoMineKey = cfg.Bind("05 - Convenience", "AutoMineKey",
                new KeyboardShortcut(KeyCode.Alpha3, KeyCode.LeftAlt),
                "Turns auto-mining on and off. With the pickaxe in hand and the ore " +
                "under your aim, the character swings by itself until it breaks and stops. Looking away, " +
                "switching items or the target breaking stops it immediately.");

            AutoMineOres = cfg.Bind("05 - Convenience", "AutoMineOres",
                "copper,tin,silver,iron,obsidian,meteorite,flametal,blackmetal",
                "What counts as ore. The mod looks at what the target DROPS and compares it with " +
                "this list, so common stone (which drops Stone) is left out. Separate with " +
                "commas; for ore from another mod, add a piece of its name.");

            AutoMineRangeScale = cfg.Bind("05 - Convenience", "AutoMineRangeScale", 1f,
                new ConfigDescription(
                    "Multiplies the real reach of the equipped pickaxe. The distance is measured " +
                    "to the nearest point of the ore, not to its center. Below 1 " +
                    "requires getting closer; above, accepts from a bit farther away.",
                    new AcceptableValueRange<float>(0.5f, 2f)));

            AutoMineHudY = cfg.Bind("05 - Convenience", "AutoMineHudY", 120f,
                new ConfigDescription(
                    "Height of the 'auto-mining' indicator on screen, from the bottom.",
                    new AcceptableValueRange<float>(0f, 900f)));

            AutoMineToggleSeconds = cfg.Bind("05 - Convenience", "AutoMineToggleSeconds", 1.5f,
                new ConfigDescription(
                    "How long the indicator highlights ON/OFF after pressing the key. " +
                    "After that, while on, only the discreet label remains.",
                    new AcceptableValueRange<float>(0.3f, 6f)));

            AutoMineDebug = cfg.Bind("05 - Convenience", "AutoMineDebug", false,
                "When auto-mining refuses a target, logs which component it uses " +
                "and what it drops. Useful for finding out the name of an ore that is missing " +
                "from the list, instead of guessing in the dark.");

            ChestSearchEnabled = cfg.Bind("05 - Convenience", "ChestSearchEnabled", true,
                "Adds a button to the inventory that opens a panel with everything that is " +
                "in nearby chests, with search by name. Clicking an item brings it to your backpack.");

            ChestSearchRadius = cfg.Bind("05 - Convenience", "ChestSearchRadius", 20f,
                new ConfigDescription(
                    "How far to search for chests. Also applies to carts and ships. " +
                    "Chests in areas the game hasn't loaded yet won't appear, no matter " +
                    "how much you increase this number.",
                    new AcceptableValueRange<float>(5f, 64f)));

            ChestSearchRefresh = cfg.Bind("05 - Convenience", "ChestSearchRefresh", 0.5f,
                new ConfigDescription(
                    "How often, in seconds, the panel rechecks the chests while " +
                    "it is open.",
                    new AcceptableValueRange<float>(0.2f, 3f)));

            ChestSearchScrollSpeed = cfg.Bind("05 - Convenience", "ChestSearchScrollSpeed", 60f,
                new ConfigDescription(
                    "How far the mouse wheel scrolls the item list, in pixels per notch. " +
                    "Raise it if scrolling feels slow.",
                    new AcceptableValueRange<float>(10f, 300f)));

            ChestSearchColumns = cfg.Bind("05 - Convenience", "ChestSearchColumns", 0,
                new ConfigDescription(
                    "How many items per row in the panel. At 0 it decides on its own based on " +
                    "the available space, which is what you want in most resolutions.",
                    new AcceptableValueRange<int>(0, 12)));

            ChestSearchSlotSize = cfg.Bind("05 - Convenience", "ChestSearchSlotSize", 40f,
                new ConfigDescription("Size of each panel slot, in pixels.",
                    new AcceptableValueRange<float>(28f, 80f)));

            ChestSearchLabelSize = cfg.Bind("05 - Convenience", "ChestSearchLabelSize", 8.5f,
                new ConfigDescription("Size of the name below each item.",
                    new AcceptableValueRange<float>(6f, 16f)));

            ChestSearchWidth = cfg.Bind("05 - Convenience", "ChestSearchWidth", 0f,
                new ConfigDescription(
                    "Panel width. At 0 it uses the same as the game's crafting " +
                    "panel, which is what aligns correctly at any resolution.",
                    new AcceptableValueRange<float>(0f, 900f)));

            ChestSearchHeight = cfg.Bind("05 - Convenience", "ChestSearchHeight", 0f,
                new ConfigDescription(
                    "Panel height. At 0 it uses the same as the crafting panel.",
                    new AcceptableValueRange<float>(0f, 1200f)));

            ChestSearchX = cfg.Bind("05 - Convenience", "ChestSearchX", 0f,
                "Shifts the panel horizontally from its default position. Negative " +
                "pulls it left, positive pushes it right.");

            ChestSearchY = cfg.Bind("05 - Convenience", "ChestSearchY", 0f,
                "Moves the panel up (positive) or down (negative) from its default position.");

            ChestSearchButtonX = cfg.Bind("05 - Convenience", "ChestSearchButtonX", -8f,
                "Horizontal position of the button inside the inventory.");

            ChestSearchButtonY = cfg.Bind("05 - Convenience", "ChestSearchButtonY", -36f,
                "Vertical position of the button inside the inventory.");

            ChestSearchBlockMove = cfg.Bind("05 - Convenience", "ChestSearchBlockMove", true,
                "Locks movement while the chest panel is open, so typing in the " +
                "search doesn't make your character move. Turn it off if you prefer to walk with " +
                "it open.");

            ChestSearchDebug = cfg.Bind("05 - Convenience", "ChestSearchDebug", false,
                "Writes the chest panel's step-by-step to the log. Only useful for " +
                "investigating a problem; when on it floods the log.");

            ChestSearchKey = cfg.Bind("05 - Convenience", "ChestSearchKey",
                new BepInEx.Configuration.KeyboardShortcut(KeyCode.None),
                "Key to open the chest panel directly, without needing to open the " +
                "inventory and click the button. If the inventory is closed it " +
                "opens both. Comes unbound; the button still works.");

            AutoRepairOnOpen = cfg.Bind("05 - Convenience", "AutoRepairOnOpen", true,
                "Repairs all worn equipment as soon as you open the inventory near " +
                "a workbench, forge or equivalent station. Repairing in Valheim doesn't cost " +
                "materials, so this just saves clicks.");

            RepairButtonRepairsAll = cfg.Bind("05 - Convenience", "RepairButtonRepairsAll", true,
                "The repair button fixes everything at once instead of one item per click.");

            SimDistanceEnabled = cfg.Bind("04 - Performance", "SimDistanceEnabled", true,
                "Controls the distance at which the world stays active around you. The host " +
                "sets the ceiling for everyone; others can use less, never more.");

            SimDistanceNear = cfg.Bind("04 - Performance", "SimDistanceNear", 5,
                new ConfigDescription(
                    "Distance at which enemies, animals and objects actually exist. Each step " +
                    "here is 64 more meters and a lot more work for the host: " +
                    "2 gives 160m, 3 gives 224m, 5 gives 352m, 6 gives 416m. The game menu stops at 6.",
                    new AcceptableValueRange<int>(1, 8)));

            SimDistanceFar = cfg.Bind("04 - Performance", "SimDistanceFar", 2,
                new ConfigDescription(
                    "Extra ring of background scenery only, with no creatures and no simulation. " +
                    "It is cheap: gives you a horizon without the cost of the setting above. The game uses 2.",
                    new AcceptableValueRange<int>(0, 6)));

            MaxQueuedFrames = cfg.Bind("04 - Performance", "MaxQueuedFrames", 0,
                new ConfigDescription(
                    "Frames prepared in advance. 1 responds faster to the mouse; " +
                    "2 is the game's default. 0 leaves it alone.",
                    new AcceptableValueRange<int>(0, 4)));

            MaxSmoke = cfg.Bind("04 - Performance", "MaxSmoke", 0,
                new ConfigDescription(
                    "Limit of smoke particles. Each one is physics-simulated, so " +
                    "in a base with many campfires it weighs heavily. The game uses 100. 0 leaves it alone.",
                    new AcceptableValueRange<int>(0, 100)));

            FadeDistantSmokeFirst = cfg.Bind("04 - Performance", "FadeDistantSmokeFirst", true,
                "When hitting the limit, fades out the most distant smoke first instead of the " +
                "oldest, which is usually the one right in front of you.");


            ZoneGenBudgetMs = cfg.Bind("04 - Performance", "ZoneGenBudgetMs", 0f,
                new ConfigDescription(
                    "Maximum time per frame generating new terrain, in milliseconds. " +
                    "Helps in a newly created world, where the game allows itself long pauses. " +
                    "0 leaves it alone.",
                    new AcceptableValueRange<float>(0f, 100f)));

            ZdoReleaseIntervalSec = cfg.Bind("04 - Performance", "ZdoReleaseIntervalSec", 2f,
                new ConfigDescription(
                    "How often the server redistributes objects among the " +
                    "players. Increasing it eases the load on the host with many people connected, at " +
                    "the cost of objects taking longer to change owner. The game uses 2.",
                    new AcceptableValueRange<float>(1f, 10f)));

            GcSliceMs = cfg.Bind("04 - Performance", "GcSliceMs", 0f,
                new ConfigDescription(
                    "How much time per frame the game spends freeing memory. Lower values " +
                    "spread that work out instead of concentrating it. 0 leaves it alone.",
                    new AcceptableValueRange<float>(0f, 10f)));

            // ------------------------------------------------------------------
            // 06 - Building
            // ------------------------------------------------------------------
            BuildFromChestsEnabled = cfg.Bind("06 - Building", "BuildFromChestsEnabled", true,
                "While building with the hammer, lets you use the materials stored in " +
                "nearby chests instead of carrying them in the backpack. The build menu " +
                "marks the piece as buildable when the chests cover the cost, and the " +
                "materials are taken from them on placement. Applies only to building, " +
                "not to crafting at a station.");

            BuildFromChestsRadius = cfg.Bind("06 - Building", "BuildFromChestsRadius", 20f,
                new ConfigDescription(
                    "How far away a chest can be and still count. Carts and ships count " +
                    "too. A chest the game has not loaded yet cannot be used, no matter " +
                    "how high this is.",
                    new AcceptableValueRange<float>(4f, 64f)));

            BuildFromChestsRefresh = cfg.Bind("06 - Building", "BuildFromChestsRefresh", 0.5f,
                new ConfigDescription(
                    "How often the nearby chests are recounted while building. Lower is " +
                    "more accurate right after you store something, at the cost of a " +
                    "little more work.",
                    new AcceptableValueRange<float>(0.2f, 3f)));
        }
    }
}
