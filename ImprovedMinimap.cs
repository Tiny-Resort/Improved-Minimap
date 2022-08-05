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
using TinyResort;
using TMPro;
using UnityEngine.UI;

namespace ImprovedMinimap {
    
    [BepInPlugin(pluginGuid, pluginName, pluginVersion)]
    public class ImprovedMinimap : BaseUnityPlugin {

        public static TRPlugin Plugin;

        public const string pluginGuid = "tinyresort.dinkum.improvedminimap";
        public const string pluginName = "Improved Minimap";
        public const string pluginVersion = "0.6.0";
        
        public static float Zoom = 1;
        public static bool showMinimap = true;
        public static ConfigEntry<int> nexusID;
        public static Vector3 currentPosition;

        private static RectTransform BiomeNameBox;
        public static TextMeshProUGUI BiomeNameText;
        public static ConfigEntry<bool> showBiomeName;

        private static float updateAnimalsTimer;
        public static List<(AnimalAI AI, AnimalAggressiveness aggression)> Animals = new List<(AnimalAI AI, AnimalAggressiveness aggression)>();
        public static LayerMask AnimalMask;
        private static Sprite AnimalMarkerSprite;

        public static ConfigEntry<bool> isDebug;
        public static ConfigEntry<KeyCode> zoomInHotkey;
        public static ConfigEntry<KeyCode> zoomOutHotkey;
        public static ConfigEntry<float> defaultZoom;
        public static ConfigEntry<KeyCode> hideMinimapHotkey;
        public static ConfigEntry<bool> showNearbyAnimals;
        public static ConfigEntry<float> AnimalMarkerSize;
        public static ConfigEntry<bool> ExcludeFarmAnimals;
        public static ConfigEntry<Color> PassiveAnimalColor;
        public static ConfigEntry<Color> AggressiveAnimalColor;
        public static ConfigEntry<Color> DefensiveAnimalColor;
        
        private void Awake() {

            Plugin = TRTools.Initialize(this, new Harmony(pluginGuid), Logger, 18, pluginGuid, pluginName, pluginVersion);
            Plugin.QuickPatch(typeof(CharInteract), "Update", typeof(ImprovedMinimap), "updatePrefix");
            Plugin.QuickPatch(typeof(RenderMap), "runMapFollow", typeof(ImprovedMinimap), "runMapFollowPrefix");
            Plugin.QuickPatch(typeof(mapIcon), "Update", typeof(ImprovedMinimap), "mapIconUpdatePostfix");

            #region Config Options
            showBiomeName = Config.Bind("General", "ShowBiomeName", true, "Shows the player's current biome name under the minimap.");
            defaultZoom = Config.Bind("General", "DefaultZoom", 1f, "Default zoom level of minimap. Value of 1 keeps normal game zoom. Lower values are more zoomed out. Higher values are more zoomed in. Range: 0.20 - 1.25");
            Zoom = defaultZoom.Value;

            showNearbyAnimals = Config.Bind("MapMarkers", "ShowNearbyAnimals", true, "Shows colored dot markers on the minimap where animals are located.");
            AnimalMarkerSize = Config.Bind("MapMarkers", "AnimalMarkerSize", 10f, "How big the animal dot markers should appear.");
            ExcludeFarmAnimals = Config.Bind("MapMarkers", "ExcludeFarmAnimals", true, "If true, then farm animals will not be shown on the minimap.");
            PassiveAnimalColor = Config.Bind("MapMarkers", "PassiveAnimalColor", new Color(3f / 255f, 240f / 255f, 127f / 255f, 1), "The color of the dot marker for animals that will not attack you ever.");
            DefensiveAnimalColor = Config.Bind("MapMarkers", "AggressiveAnimalColor", new Color(1, 209f / 255f, 30f / 255f, 1), "The color of the dot marker for defensive animals who attack when threatened.");
            AggressiveAnimalColor = Config.Bind("MapMarkers", "AggressiveAnimalColor", new Color(242f / 255f, 40f / 255f, 3f / 255f, 1), "The color of the dot marker for aggressive animals who will attack you on sight.");
         
            hideMinimapHotkey = Config.Bind("Keybinds", "HideMinimapHotkey", KeyCode.None, "Keybind for toggling whether or not the minimap is shown. (Set to None to disable)");
            zoomInHotkey = Config.Bind("Keybinds", "ZoomInHotkey", KeyCode.None, "Keybind for zooming in on the minimap. (Set to None to disable)");
            zoomOutHotkey = Config.Bind("Keybinds", "ZoomOutHotkey", KeyCode.None, "Keybind for zooming out on the minimap. (Set to None to disable)");
            #endregion

            AnimalMask = LayerMask.GetMask("Prey", "Predator");
            AnimalMarkerSprite = TRDrawing.CircleSprite(128, Color.white, 32, new Color(0.25f, 0.25f, 0.25f, 1));

        }

        [HarmonyPrefix]
        public static void updatePrefix(CharInteract __instance) {
            
            // Gets the player's current position
            if (!__instance.isLocalPlayer) return;
            currentPosition = __instance.transform.position;

            // Looks for nearby animals
            if (showNearbyAnimals.Value) {

                // Only runs every quarter second to save on performance
                updateAnimalsTimer -= Time.deltaTime;
                if (updateAnimalsTimer <= 0) {

                    updateAnimalsTimer = 0.25f;
                    Animals.Clear();
                    
                    // Physics check to find animals within a radius
                    var hits = Physics.OverlapSphere(currentPosition, 100f / Zoom, AnimalMask);
                    foreach (var hit in hits) {
                        
                        // Ignore farm animals if the setting says to
                        var animal = hit.GetComponentInParent<AnimalAI>();
                        if (ExcludeFarmAnimals.Value && hit.GetComponentInParent<FarmAnimal>()) continue;
                        
                        // Figures out how aggressive the animal is for marker style purposes
                        var aggression = AnimalAggressiveness.Passive;
                        if (animal.myEnemies == (animal.myEnemies & (1 << LayerMask.NameToLayer("Char")))) {
                            var AttackAI = animal.GetComponent<AnimalAI_Attack>();
                            if (AttackAI != null) { aggression = AttackAI.attackOnlyOnAttack ? AnimalAggressiveness.Defensive : AnimalAggressiveness.Aggressive; }
                        }
                        
                        Animals.Add((animal, aggression));
                        
                    }

                }

            }
            
        }

        // Adjusting map zoom (scale)
        public void Update() {
            if (Input.GetKeyDown(zoomInHotkey.Value)) { Zoom += 0.05f; }
            if (Input.GetKeyDown(zoomOutHotkey.Value)) { Zoom -= 0.05f; }
            if (Input.GetKeyDown(hideMinimapHotkey.Value)) { showMinimap = !showMinimap; }
            Zoom = Mathf.Clamp(Zoom, 0.2f, 1.25f);
        }

        [HarmonyPrefix]
        public static void runMapFollowPrefix(RenderMap __instance) {

            // Toggle whether or not the minimap should be shown
            if (TownManager.manage.mapUnlocked && !__instance.mapOpen &&
                !MenuButtonsTop.menu.subMenuOpen && 
                !WeatherManager.manage.isInside() && 
                !ChestWindow.chests.chestWindowOpen && 
                !CraftingManager.manage.craftMenuOpen && 
                !CheatScript.cheat.cheatMenuOpen && __instance.mapCircle.gameObject.activeSelf != showMinimap)
                __instance.mapCircle.gameObject.SetActive(showMinimap);

            // Makes sure every animal has a map marker and is in the correct position with the correct color
            var Markers = TRMap.RefreshPool("Animals", Animals.Count, AnimalMarkerSprite, AnimalMarkerSize.Value);
            for (var i = 0; i < Animals.Count; i++) {
                Markers[i].mainRect.localPosition =
                    new Vector2(Animals[i].AI.transform.position.x / 2f / RenderMap.map.mapScale, Animals[i].AI.transform.position.z / 2f / RenderMap.map.mapScale);
                Markers[i].markerImage.color = Animals[i].aggression == AnimalAggressiveness.Aggressive ? AggressiveAnimalColor.Value : 
                                      Animals[i].aggression == AnimalAggressiveness.Defensive ? DefensiveAnimalColor.Value : PassiveAnimalColor.Value;
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

            if (Vector3.Distance(RenderMap.map.charToPointTo.position, pointingAtPosition) * Zoom < 45f) {
                myTrans.localPosition = new Vector3(pointingAtPosition.x / 2f / RenderMap.map.mapScale, pointingAtPosition.z / 2f / RenderMap.map.mapScale, 1f);
                myTrans.localScale = new Vector3(5.5f / RenderMap.map.desiredScale, 5.5f / RenderMap.map.desiredScale, 1f);
            } else {
                Vector3 vector = RenderMap.map.charToPointTo.position + ((pointingAtPosition - RenderMap.map.charToPointTo.position).normalized * (45f / Zoom));
                myTrans.localPosition = new Vector3(vector.x / 2f / RenderMap.map.mapScale, vector.z / 2f / RenderMap.map.mapScale, 1f);
                myTrans.localScale = new Vector3(5.5f / RenderMap.map.desiredScale, 5.5f / RenderMap.map.desiredScale, 1f);
            }

        }
        
        public enum AnimalAggressiveness { Passive, Defensive, Aggressive }

    }

}
