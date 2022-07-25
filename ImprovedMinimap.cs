using System;
using System.Collections;
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
        public const string pluginVersion = "0.1.0";
        public static ConfigEntry<bool> debugMode;

        private void Awake() {
            
            //debugMode = Config.Bind<bool>("General", "DebugMode", false, "If true, the BepinEx console will print out debug messages related to this mod.");

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
            harmony.Patch(runMapFollow, new HarmonyMethod(runMapFollowPrefix));
            #endregion

        }

        [HarmonyPrefix]
        public static void runMapFollowPrefix(RenderMap __instance) {
            RenderMap.map.lockMapNorth = true;
            __instance.charPointer.localRotation = Quaternion.Euler(0f, 0f, 0f);
        }

    }

}
