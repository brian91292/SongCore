﻿using BeatSaberMarkupLanguage.Settings;
using Harmony;
using IPA;
using Newtonsoft.Json;
using SongCore.UI;
using SongCore.Utilities;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using IPALogger = IPA.Logging.Logger;

namespace SongCore
{
    public class Plugin : IBeatSaberPlugin
    {
        public static string standardCharacteristicName = "Standard";
        public static string oneSaberCharacteristicName = "OneSaber";
        public static string noArrowsCharacteristicName = "NoArrows";
        internal static HarmonyInstance harmony;
        //     internal static bool ColorsInstalled = false;
        internal static bool PlatformsInstalled = false;
        internal static bool customSongColors;
        internal static bool customSongPlatforms;
        internal static int _currentPlatform = -1;


        public void OnApplicationStart()
        {
            //Delete Old Config
            try
            {
                if (File.Exists(Environment.CurrentDirectory + "/UserData/SongCore.ini"))
                    File.Delete(Environment.CurrentDirectory + "/UserData/SongCore.ini");
            }
            catch
            {
                Logging.logger.Warn("Failed to delete old config file!");
            }

            //      ColorsInstalled = Utils.IsModInstalled("Custom Colors") || Utils.IsModInstalled("Chroma");
            PlatformsInstalled = Utils.IsModInstalled("Custom Platforms");
            harmony = HarmonyInstance.Create("com.kyle1413.BeatSaber.SongCore");
            harmony.PatchAll(System.Reflection.Assembly.GetExecutingAssembly());
            //     Collections.LoadExtraSongData();
            UI.BasicUI.GetIcons();
            BS_Utils.Utilities.BSEvents.levelSelected += BSEvents_levelSelected;
            BS_Utils.Utilities.BSEvents.gameSceneLoaded += BSEvents_gameSceneLoaded;
            BS_Utils.Utilities.BSEvents.menuSceneLoadedFresh += BSEvents_menuSceneLoadedFresh;
            if (!File.Exists(Collections.dataPath))
                File.Create(Collections.dataPath);
            else
                Collections.LoadExtraSongData();
            Collections.RegisterCustomCharacteristic(UI.BasicUI.MissingCharIcon, "Missing Characteristic", "Missing Characteristic", "MissingCharacteristic", "MissingCharacteristic");
            Collections.RegisterCustomCharacteristic(UI.BasicUI.LightshowIcon, "Lightshow", "Lightshow", "Lightshow", "Lightshow");
            Collections.RegisterCustomCharacteristic(UI.BasicUI.ExtraDiffsIcon, "Lawless", "Lawless - Anything Goes", "Lawless", "Lawless");

            if (!File.Exists(Environment.CurrentDirectory + "/UserData/SongCore/folders.xml"))
                File.WriteAllBytes(Environment.CurrentDirectory + "/UserData/SongCore/folders.xml", Utils.GetResource(Assembly.GetExecutingAssembly(), "SongCore.Data.folders.xml"));
            Loader.SeperateSongFolders.InsertRange(0, Data.SeperateSongFolder.ReadSeperateFoldersFromFile(Environment.CurrentDirectory + "/UserData/SongCore/folders.xml"));
        }

        private void BSEvents_menuSceneLoadedFresh()
        {
            Loader.OnLoad();
            RequirementsUI.instance.Setup();
        }

        private void BSEvents_gameSceneLoaded()
        {
            SharedCoroutineStarter.instance.StartCoroutine(DelayedNoteJumpMovementSpeedFix());
        }

        private void BSEvents_levelSelected(LevelCollectionViewController arg1, IPreviewBeatmapLevel level)
        {
            if (level is CustomPreviewBeatmapLevel)
            {
                var customLevel = level as CustomPreviewBeatmapLevel;
                //       Logging.Log((level as CustomPreviewBeatmapLevel).customLevelPath);
                Data.ExtraSongData songData = Collections.RetrieveExtraSongData(Hashing.GetCustomLevelHash(customLevel), customLevel.customLevelPath);
                Collections.SaveExtraSongData();

                if (songData == null)
                {
                    //          Logging.Log("Null song Data");
                    return;
                }
                //      Logging.Log($"Platforms Installed: {PlatformsInstalled}. Platforms enabled: {customSongPlatforms}");
                if (PlatformsInstalled && customSongPlatforms)
                {
                    if (!string.IsNullOrWhiteSpace(songData._customEnvironmentName))
                    {
                        if (findCustomEnvironment(songData._customEnvironmentName) == -1)
                        {
                            Console.WriteLine("CustomPlatform not found: " + songData._customEnvironmentName);
                            if (!string.IsNullOrWhiteSpace(songData._customEnvironmentHash))
                            {
                                Console.WriteLine("Downloading with hash: " + songData._customEnvironmentHash);
                                SharedCoroutineStarter.instance.StartCoroutine(downloadCustomPlatform(songData._customEnvironmentHash, songData._customEnvironmentName));
                            }
                        }
                    }
                }
            }

        }

        public void Init(object thisIsNull, IPALogger pluginLogger)
        {

            Utilities.Logging.logger = pluginLogger;
        }

        public void OnSceneLoaded(Scene scene, LoadSceneMode sceneMode)
        {
            if (scene.name == "MenuViewControllers")
            {
                BSMLSettings.instance.AddSettingsMenu("SongCore", "SongCore.UI.settings.bsml", SCSettings.instance);
            }

        }
       
        public void OnSceneUnloaded(Scene scene)
        {

        }

        public void OnActiveSceneChanged(Scene prevScene, Scene nextScene)
        {
            customSongColors = UI.BasicUI.ModPrefs.GetBool("SongCore", "customSongColors", true, true);
            customSongPlatforms = UI.BasicUI.ModPrefs.GetBool("SongCore", "customSongPlatforms", true, true);
            GameObject.Destroy(GameObject.Find("SongCore Color Setter"));
            if (nextScene.name == "MenuViewControllers")
            {
                BS_Utils.Gameplay.Gamemode.Init();
                if (PlatformsInstalled)
                    CheckForPreviousPlatform();

            }

            if (nextScene.name == "GameCore")
            {
                GameplayCoreSceneSetupData data = BS_Utils.Plugin.LevelData?.GameplayCoreSceneSetupData;
                Data.ExtraSongData.DifficultyData songData = Collections.RetrieveDifficultyData(data.difficultyBeatmap);
                if (songData != null)
                {
                    if (PlatformsInstalled)
                    {
                        Logging.logger.Info("Checking Custom Environment");
                        CheckCustomSongEnvironment(data.difficultyBeatmap);
                    }
                }
                else
                    Logging.logger.Info("Null custom song extra data");


            }
        }

        private IEnumerator DelayedNoteJumpMovementSpeedFix()
        {
            yield return new WaitForSeconds(0.1f);
            //Beat Saber 0.11.1 introduced a check for if noteJumpMovementSpeed <= 0
            //This breaks songs that have a negative noteJumpMovementSpeed and previously required a patcher to get working again
            //I've added this to add support for that again, because why not.
            if (BS_Utils.Plugin.LevelData.GameplayCoreSceneSetupData.difficultyBeatmap.noteJumpMovementSpeed < 0)
            {
                var beatmapObjectSpawnController =
                    Resources.FindObjectsOfTypeAll<BeatmapObjectSpawnController>().FirstOrDefault();

                AdjustNJS(BS_Utils.Plugin.LevelData.GameplayCoreSceneSetupData.difficultyBeatmap.noteJumpMovementSpeed, beatmapObjectSpawnController);

            }
        }

        public static void AdjustNJS(float njs, BeatmapObjectSpawnController _spawnController)
        {

            float halfJumpDur = 4f;
            float maxHalfJump = _spawnController.GetPrivateField<float>("_maxHalfJumpDistance");
            float noteJumpStartBeatOffset = BS_Utils.Plugin.LevelData.GameplayCoreSceneSetupData.difficultyBeatmap.noteJumpStartBeatOffset;
            float moveSpeed = _spawnController.GetPrivateField<float>("_moveSpeed");
            float moveDir = _spawnController.GetPrivateField<float>("_moveDurationInBeats");
            float jumpDis;
            float spawnAheadTime;
            float moveDis;
            float bpm = _spawnController.GetPrivateField<float>("_beatsPerMinute");
            float num = 60f / bpm;
            moveDis = moveSpeed * num * moveDir;
            while (njs * num * halfJumpDur > maxHalfJump)
            {
                halfJumpDur /= 2f;
            }
            halfJumpDur += noteJumpStartBeatOffset;
            if (halfJumpDur < 1f) halfJumpDur = 1f;
            //        halfJumpDur = spawnController.GetPrivateField<float>("_halfJumpDurationInBeats");
            jumpDis = njs * num * halfJumpDur * 2f;
            spawnAheadTime = moveDis / moveSpeed + jumpDis * 0.5f / njs;
            _spawnController.SetPrivateField("_halfJumpDurationInBeats", halfJumpDur);
            _spawnController.SetPrivateField("_spawnAheadTime", spawnAheadTime);
            _spawnController.SetPrivateField("_jumpDistance", jumpDis);
            _spawnController.SetPrivateField("_noteJumpMovementSpeed", njs);
            _spawnController.SetPrivateField("_moveDistance", moveDis);


        }

        public void OnApplicationQuit()
        {

        }

        public void OnLevelWasLoaded(int level)
        {

        }

        public void OnLevelWasInitialized(int level)
        {
        }

        public void OnUpdate()
        {


        }

        public void OnFixedUpdate()
        {
        }


        private void CheckCustomSongEnvironment(IDifficultyBeatmap song)
        {
            Data.ExtraSongData songData = Collections.RetrieveExtraSongData(Hashing.GetCustomLevelHash(song.level as CustomPreviewBeatmapLevel));
            if (songData == null) return;
            if (string.IsNullOrWhiteSpace(songData._customEnvironmentName))
            {
                _currentPlatform = -1;
                return;
            }
            int _customPlatform = customEnvironment(songData._customEnvironmentName);
            if (_customPlatform != -1)
            {
                _currentPlatform = CustomFloorPlugin.PlatformManager.Instance.currentPlatformIndex;
                if (customSongPlatforms && _customPlatform != _currentPlatform)
                {
                    CustomFloorPlugin.PlatformManager.Instance.ChangeToPlatform(_customPlatform, false);
                }
            }
        }

        internal static int customEnvironment(string platform)
        {
            if (!PlatformsInstalled)
                return -1;
            return findCustomEnvironment(platform);
        }
        private static int findCustomEnvironment(string name)
        {

            CustomFloorPlugin.CustomPlatform[] _customPlatformsList = CustomFloorPlugin.PlatformManager.Instance.GetPlatforms();
            int platIndex = 0;
            foreach (CustomFloorPlugin.CustomPlatform plat in _customPlatformsList)
            {
                if (plat?.platName == name)
                    return platIndex;
                platIndex++;
            }
            Console.WriteLine(name + " not found!");

            return -1;
        }
        private void CheckForPreviousPlatform()
        {
            if (_currentPlatform != -1)
            {
                CustomFloorPlugin.PlatformManager.Instance.ChangeToPlatform(_currentPlatform);
            }
        }


        [Serializable]
        public class platformDownloadData
        {
            public string name;
            public string author;
            public string image;
            public string hash;
            public string download;
            public string date;
        }

        private IEnumerator downloadCustomPlatform(string hash, string name)
        {
            using (UnityWebRequest www = UnityWebRequest.Get("https://modelsaber.com/api/v1/platform/get.php?filter=hash:" + hash))
            {
                yield return www.SendWebRequest();

                if (www.isNetworkError || www.isHttpError)
                {
                    Console.WriteLine(www.error);
                }
                else
                {
                    var downloadData = JsonConvert.DeserializeObject<Dictionary<string, platformDownloadData>>(www.downloadHandler.text);
                    platformDownloadData data = downloadData.FirstOrDefault().Value;
                    if (data != null)
                        if (data.name == name)
                        {
                            SharedCoroutineStarter.instance.StartCoroutine(_downloadCustomPlatform(data));
                        }
                }
            }
        }

        private IEnumerator _downloadCustomPlatform(platformDownloadData downloadData)
        {
            using (UnityWebRequest www = UnityWebRequest.Get(downloadData.download))
            {
                yield return www.SendWebRequest();

                if (www.isNetworkError || www.isHttpError)
                {
                    Console.WriteLine(www.error);
                }
                else
                {
                    string customPlatformsFolderPath = Path.Combine(Environment.CurrentDirectory, "CustomPlatforms", downloadData.name);
                    System.IO.File.WriteAllBytes(@customPlatformsFolderPath + ".plat", www.downloadHandler.data);
                    CustomFloorPlugin.PlatformManager.Instance.AddPlatform(customPlatformsFolderPath + ".plat");
                }
            }
        }
    }
}


