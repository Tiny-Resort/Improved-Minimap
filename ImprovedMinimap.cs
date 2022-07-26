using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Core.Logging.Interpolation;
using BepInEx.Logging;
using Mirror;
using UnityEngine;
using HarmonyLib;
using System.Reflection;


namespace ImprovedMinimap {
    
    [BepInPlugin(pluginGuid, pluginName, pluginVersion)]
    public class ImprovedMinimap : BaseUnityPlugin {

        public static ManualLogSource StaticLogger;
        public const string pluginGuid = "tinyresort.dinkum.improvedminimap";
        public const string pluginName = "Improved Minimap";
        public const string pluginVersion = "0.2.0";
        public static ConfigEntry<bool> debugMode;
        public static ConfigEntry<KeyCode> lockHotkey;
        public static ConfigEntry<KeyCode> zoomInHotkey;
        public static ConfigEntry<KeyCode> zoomOutHotkey;
        public static ConfigEntry<float> defaultZoom;
        public static ConfigEntry<bool> lockMinimapRotation;
        public static ConfigEntry<KeyCode> hideMinimapHotkey;
        public static bool forceClearNotification;
        public static float Zoom = 1;
        public static bool minimapRotationLocked;
        public static bool showMinimap = true;

        private void Awake() {

            lockMinimapRotation = Config.Bind<bool>("General", "LockMinimapRotation", true, "If true, the minimap will always point north.");
            defaultZoom = Config.Bind<float>("General", "DefaultZoom", 1f, "Default zoom level of minimap. Value of 1 keeps normal game zoom. Lower values are more zoomed out. Higher values are more zoomed in. Range: 0.20 - 1.25");
            debugMode = Config.Bind<bool>("General", "DebugMode", false, "If true, the BepinEx console will print out debug messages related to this mod.");
            lockHotkey = Config.Bind<KeyCode>("Keybinds", "MinimapLockHotkey", KeyCode.None, "Keybind for toggling the minimap rotation lock. (Set to None to disable)");
            hideMinimapHotkey = Config.Bind<KeyCode>("Keybinds", "HideMinimapHotkey", KeyCode.None, "Keybind for toggling whether or not the minimap is shown. (Set to None to disable)");
            zoomInHotkey = Config.Bind<KeyCode>("Keybinds", "ZoomInHotkey", KeyCode.None, "Keybind for zooming in on the minimap. (Set to None to disable)");
            zoomOutHotkey = Config.Bind<KeyCode>("Keybinds", "ZoomOutHotkey", KeyCode.None, "Keybind for zooming out on the minimap. (Set to None to disable)");
            Zoom = defaultZoom.Value;
            minimapRotationLocked = lockMinimapRotation.Value;

            #region Logging
            StaticLogger = Logger;
            BepInExInfoLogInterpolatedStringHandler handler = new BepInExInfoLogInterpolatedStringHandler(18, 1, out var flag);
            if (flag) { handler.AppendLiteral("Plugin " + pluginGuid + " (v" + pluginVersion + ") loaded!"); }
            StaticLogger.LogInfo(handler);
            #endregion

            #region Patching
            Harmony harmony = new Harmony(pluginGuid);
            MethodInfo runMapFollow = AccessTools.Method(typeof(RenderMap), "runMapFollow");
            MethodInfo runMapFollowPrefix = AccessTools.Method(typeof(ImprovedMinimap), "runMapFollowPrefix");
            MethodInfo notification = AccessTools.Method(typeof(NotificationManager), "makeTopNotification");
            MethodInfo notificationPrefix = AccessTools.Method(typeof(ImprovedMinimap), "makeTopNotificationPrefix");
            harmony.Patch(runMapFollow, new HarmonyMethod(runMapFollowPrefix));
            harmony.Patch(notification, new HarmonyMethod(notificationPrefix));
            #endregion

        }

        public void Update() {
            if (Input.GetKeyDown(lockHotkey.Value)) {
                minimapRotationLocked = !minimapRotationLocked;
                notify("Improved Minimap", "Rotation Lock: " + (minimapRotationLocked ? "ENABLED" : "DISABLED"));
            }
            if (Input.GetKeyDown(zoomInHotkey.Value)) { Zoom += 0.05f; }
            if (Input.GetKeyDown(zoomOutHotkey.Value)) { Zoom -= 0.05f; }
            if (Input.GetKeyDown(hideMinimapHotkey.Value)) { showMinimap = !showMinimap; }
            Zoom = Mathf.Clamp(Zoom, 0.2f, 1.25f);
        }

        [HarmonyPrefix]
        public static void runMapFollowPrefix(RenderMap __instance) {

            if (TownManager.manage.mapUnlocked && !__instance.mapOpen &&
                !MenuButtonsTop.menu.subMenuOpen && 
                !WeatherManager.manage.isInside() && 
                !ChestWindow.chests.chestWindowOpen && 
                !CraftingManager.manage.craftMenuOpen && 
                !CheatScript.cheat.cheatMenuOpen && __instance.mapCircle.gameObject.activeSelf != showMinimap)
                __instance.mapCircle.gameObject.SetActive(showMinimap);

            if (!__instance.mapOpen) {
                RenderMap.map.scale = Zoom * 20f;
                RenderMap.map.lockMapNorth = minimapRotationLocked;
                if (minimapRotationLocked) { __instance.charPointer.localRotation = Quaternion.Euler(0f, 0f, 0f); }
            }
        }

        public static void notify(string title, string subtitle) {
            forceClearNotification = true;
            NotificationManager.manage.makeTopNotification(title, subtitle);
        }

        // Forcibly clears the top notification so that it can be replaced immediately
        [HarmonyPrefix]
        public static bool makeTopNotificationPrefix(NotificationManager __instance) {
            
            if (forceClearNotification) {
                forceClearNotification = false;
                
                var toNotify = (List<string>)AccessTools.Field(typeof(NotificationManager), "toNotify").GetValue(__instance);
                var subTextNot = (List<string>)AccessTools.Field(typeof(NotificationManager), "subTextNot").GetValue(__instance);
                var soundToPlay = (List<ASound>)AccessTools.Field(typeof(NotificationManager), "soundToPlay").GetValue(__instance);
                var topNotificationRunning = AccessTools.Field(typeof(NotificationManager), "topNotificationRunning");
                var topNotificationRunningRoutine = topNotificationRunning.GetValue(__instance);
                
                // Clears existing notifications in the queue
                toNotify.Clear();
                subTextNot.Clear();
                soundToPlay.Clear();

                // Stops the current coroutine from continuing
                if (topNotificationRunningRoutine != null) {
                    __instance.StopCoroutine((Coroutine) topNotificationRunningRoutine);
                    topNotificationRunning.SetValue(__instance, null);
                }
                
                // Resets all animations related to the notificatin bubble appearing/disappearing
                __instance.StopCoroutine("closeWithMask");
                __instance.topNotification.StopAllCoroutines();
                var Anim = __instance.topNotification.GetComponent<WindowAnimator>();
                Anim.StopAllCoroutines();
                Anim.maskChild.enabled = false;
                Anim.contents.gameObject.SetActive(false);
                Anim.gameObject.SetActive(false);
                
                return true;
                
            } else return true;
        }

    }

}
