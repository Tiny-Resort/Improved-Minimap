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
using TMPro;
using UnityEngine.UI;

namespace ImprovedMinimap {
    
    [BepInPlugin(pluginGuid, pluginName, pluginVersion)]
    public class ImprovedMinimap : BaseUnityPlugin {

        public static ManualLogSource StaticLogger;
        public const string pluginGuid = "tinyresort.dinkum.improvedminimap";
        public const string pluginName = "Improved Minimap";
        public const string pluginVersion = "0.6.0";
        public static ConfigEntry<bool> isDebug;
        public static ConfigEntry<KeyCode> zoomInHotkey;
        public static ConfigEntry<KeyCode> zoomOutHotkey;
        public static ConfigEntry<float> defaultZoom;
        public static ConfigEntry<KeyCode> hideMinimapHotkey;
        public static float Zoom = 1;
        public static bool showMinimap = true;
        public static ConfigEntry<int> nexusID;
        public static Vector3 currentPosition;

        private static RectTransform BiomeNameBox;
        public static TextMeshProUGUI BiomeNameText;
        public static ConfigEntry<bool> showBiomeName;
        public static ConfigEntry<bool> showNearbyAnimals;

        private static float updateAnimalsTimer;
        public static List<AnimalAI> Animals = new List<AnimalAI>();
        public static LayerMask AnimalMask;
        private static Image AnimalDot;
        private static List<Image> AvailableDots = new List<Image>();
        private static List<Image> DotsInUse = new List<Image>();
        private static RectTransform AnimalDotParent;

        public static void Dbgl(string str = "") {
            if (isDebug.Value) { StaticLogger.LogInfo(str); }
        }
        
        private void Awake() {

            showBiomeName = Config.Bind<bool>("General", "ShowBiomeName", true, "Shows the player's current biome name under the minimap.");
            showNearbyAnimals = Config.Bind<bool>("General", "ShowNearbyAnimals", true, "Shows red dots on the minimap where animals are located.");
            defaultZoom = Config.Bind<float>("General", "DefaultZoom", 1f, "Default zoom level of minimap. Value of 1 keeps normal game zoom. Lower values are more zoomed out. Higher values are more zoomed in. Range: 0.20 - 1.25");
            nexusID = Config.Bind<int>("General", "NexusID", 18, "Nexus Mod ID. You can find it on the mod's page on nexusmods.com");
            isDebug = Config.Bind<bool>("General", "DebugMode", false, "If true, the BepinEx console will print out debug messages related to this mod.");
            hideMinimapHotkey = Config.Bind<KeyCode>("Keybinds", "HideMinimapHotkey", KeyCode.None, "Keybind for toggling whether or not the minimap is shown. (Set to None to disable)");
            zoomInHotkey = Config.Bind<KeyCode>("Keybinds", "ZoomInHotkey", KeyCode.None, "Keybind for zooming in on the minimap. (Set to None to disable)");
            zoomOutHotkey = Config.Bind<KeyCode>("Keybinds", "ZoomOutHotkey", KeyCode.None, "Keybind for zooming out on the minimap. (Set to None to disable)");
            Zoom = defaultZoom.Value;

            #region Logging
            StaticLogger = Logger;
            BepInExInfoLogInterpolatedStringHandler handler = new BepInExInfoLogInterpolatedStringHandler(18, 1, out var flag);
            if (flag) { handler.AppendLiteral("Plugin " + pluginGuid + " (v" + pluginVersion + ") loaded!"); }
            StaticLogger.LogInfo(handler);
            #endregion

            #region Patching

            Harmony harmony = new Harmony(pluginGuid);

            MethodInfo updatePrefix = AccessTools.Method(typeof(ImprovedMinimap), "updatePrefix");
            MethodInfo update = AccessTools.Method(typeof(CharInteract), "Update");
            harmony.Patch(update, new HarmonyMethod(updatePrefix));
            
            MethodInfo runMapFollow = AccessTools.Method(typeof(RenderMap), "runMapFollow");
            MethodInfo runMapFollowPrefix = AccessTools.Method(typeof(ImprovedMinimap), "runMapFollowPrefix");
            harmony.Patch(runMapFollow, new HarmonyMethod(runMapFollowPrefix));
            
            MethodInfo mapIconUpdate = AccessTools.Method(typeof(mapIcon), "Update");
            MethodInfo mapIconUpdatePostfix = AccessTools.Method(typeof(ImprovedMinimap), "mapIconUpdatePostfix");
            harmony.Patch(mapIconUpdate, new HarmonyMethod(mapIconUpdatePostfix));
            
            #endregion
            
            AnimalMask = LayerMask.GetMask("Prey", "Predator");
            Texture2D tex = new Texture2D(64, 64);
            var radius = 32;
            var x = 32;
            var y = 32;

            for (int u = x - radius; u < x + radius + 1; u++)
                for (int v = y - radius; v < y + radius + 1; v++)
                    if ((x - u) * (x - u) + (y - v) * (y - v) < radius * radius) {
                        tex.SetPixel(u, v, Color.white);
                    }
            
            tex.Apply();

            AnimalDot = new GameObject().AddComponent<Image>();
            var sprite = Sprite.Create(tex, new Rect(Vector2.zero, Vector2.one * 64), Vector2.one * 0.5f);
            sprite.name = "Animal Marker";
            AnimalDot.sprite = sprite;
            AnimalDot.rectTransform.anchorMin = Vector2.zero;
            AnimalDot.rectTransform.anchorMax = Vector2.zero;
            AnimalDot.rectTransform.sizeDelta = Vector2.one * 32f;
            AnimalDot.rectTransform.pivot = Vector2.one * 0.5f;
            AnimalDot.maskable = false;
            AnimalDot.gameObject.SetActive(false);

        }

        [HarmonyPrefix]
        public static void updatePrefix(CharInteract __instance) {
            
            if (!__instance.isLocalPlayer) return;
            currentPosition = __instance.transform.position;

            if (showNearbyAnimals.Value) {

                updateAnimalsTimer -= Time.deltaTime;
                if (updateAnimalsTimer <= 0) {

                    updateAnimalsTimer = 0.1f;
                    var hits = Physics.OverlapSphere(currentPosition, 60, AnimalMask);
                    Animals.Clear();
                    Debug.Log("CHECK AT " + Time.time + " from position " + currentPosition);
                    foreach (var hit in hits) {
                        Debug.Log(hit.gameObject.name + " at " + hit.transform.position);
                        var animal = hit.GetComponentInParent<AnimalAI>();
                        if (!Animals.Contains(animal)) { Animals.Add(animal); }
                    }

                }

            }
            
        }

        public void Update() {
            if (Input.GetKeyDown(zoomInHotkey.Value)) { Zoom += 0.05f; }
            if (Input.GetKeyDown(zoomOutHotkey.Value)) { Zoom -= 0.05f; }
            if (Input.GetKeyDown(hideMinimapHotkey.Value)) { showMinimap = !showMinimap; }
            Zoom = Mathf.Clamp(Zoom, 0.2f, 1.25f);
        }

        private static Image GetAnimalDot() {

            Image dot;
            if (AvailableDots.Count > 0) {
                dot = AvailableDots[0];
                AvailableDots.RemoveAt(0);
            } else {
                var GO = Instantiate(AnimalDot.gameObject, AnimalDotParent);
                dot = GO.GetComponent<Image>();
            }

            dot.gameObject.SetActive(true);
            return dot;
            
        }

        private static void ReleaseDot(Image dot) {
            DotsInUse.Remove(dot);
            AvailableDots.Add(dot);
            dot.gameObject.SetActive(false);
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

            if (AnimalDotParent == null) {
                AnimalDotParent = new GameObject().AddComponent<RectTransform>();
                AnimalDotParent.transform.SetParent(RenderMap.map.mapImage.transform);
                AnimalDotParent.anchorMin = Vector2.zero;
                AnimalDotParent.anchorMax = Vector2.zero;
                AnimalDotParent.sizeDelta = RenderMap.map.mapImage.rectTransform.sizeDelta;
                AnimalDotParent.pivot = Vector2.one * 0.5f;
                AnimalDotParent.anchoredPosition = Vector2.zero;
            }

            // Makes sure the right number of dots are visible and in use
            while (Animals.Count > DotsInUse.Count) { DotsInUse.Add(GetAnimalDot()); }
            while (DotsInUse.Count > Animals.Count) { ReleaseDot(DotsInUse[0]); }
            
            for (var i = 0; i < Animals.Count; i++) {
                //DotsInUse[i].rectTransform.anchoredPosition = new Vector2(Animals[i].transform.position.x / 4f, Animals[i].transform.position.z / 4f);
                DotsInUse[i].rectTransform.anchoredPosition = new Vector2(0, 0);
                DotsInUse[i].color = Color.red;

            }

            if (!__instance.mapOpen) {
                
                // Minimap zoom settings
                RenderMap.map.scale = Zoom * 20f;
                RenderMap.map.desiredScale = RenderMap.map.scale;

                if (showBiomeName.Value) {

                    // Create a biome name box to go under the minimap
                    if (BiomeNameBox == null) {
                        
                        // Clones the box that's normally for a map open button prompt and deletes its icon children
                        BiomeNameBox = GameObject.Instantiate(RenderMap.map.buttonPrompt, RenderMap.map.buttonPrompt.parent);
                        BiomeNameBox.transform.SetSiblingIndex(1);
                        Destroy(BiomeNameBox.transform.GetChild(1).gameObject);
                        Destroy(BiomeNameBox.transform.GetChild(0).gameObject);
                        
                        // Adds a text component so we can give it a biome name, makes sure its centered
                        var TextGO = GameObject.Instantiate(RenderMap.map.biomeName.gameObject, BiomeNameBox.transform);
                        BiomeNameText = TextGO.GetComponent<TextMeshProUGUI>();
                        BiomeNameText.enableWordWrapping = false;
                        BiomeNameText.verticalAlignment = VerticalAlignmentOptions.Geometry;
                        BiomeNameText.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
                        BiomeNameText.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                        BiomeNameText.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                        BiomeNameText.rectTransform.anchoredPosition = Vector2.zero;
                        
                    }

                    // Hide the original map open button prompt
                    var Hide = RenderMap.map.buttonPrompt.GetComponentsInChildren<CanvasRenderer>();
                    foreach (var rend in Hide) { rend.SetAlpha(0); }

                    // Show the biome name below the minimap
                    BiomeNameText.text = GenerateMap.generate.getBiomeNameUnderMapCursor((int)currentPosition.x / 2, (int)currentPosition.z / 2);
                    BiomeNameBox.sizeDelta = new Vector2(BiomeNameText.preferredWidth + 20, 26);
                    
                }

            }
            
        }

        [HarmonyPostfix]
        public static void mapIconUpdatePostfix(mapIcon __instance) {
            if (!__instance.isCustom() || RenderMap.map.mapOpen) return;
            
            var pointingAtPosition = __instance.pointingAtPosition;
            var myTrans = __instance.myTrans;
            
            StaticLogger.LogInfo(Vector3.Distance(RenderMap.map.charToPointTo.position, pointingAtPosition) * Zoom);

            if (Vector3.Distance(RenderMap.map.charToPointTo.position, pointingAtPosition) * Zoom < 45f) {
                myTrans.localPosition = new Vector3(pointingAtPosition.x / 2f / RenderMap.map.mapScale, pointingAtPosition.z / 2f / RenderMap.map.mapScale, 1f);
                myTrans.localScale = new Vector3(5.5f / RenderMap.map.desiredScale, 5.5f / RenderMap.map.desiredScale, 1f);
            } else {
                Vector3 vector = RenderMap.map.charToPointTo.position + ((pointingAtPosition - RenderMap.map.charToPointTo.position).normalized * (45f / Zoom));
                myTrans.localPosition = new Vector3(vector.x / 2f / RenderMap.map.mapScale, vector.z / 2f / RenderMap.map.mapScale, 1f);
                myTrans.localScale = new Vector3(5.5f / RenderMap.map.desiredScale, 5.5f / RenderMap.map.desiredScale, 1f);
            }

        }

    }

}
