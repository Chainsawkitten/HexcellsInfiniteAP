using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Models;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using static HexcellsInfiniteRandomizer.HexcellsInfiniteRandomizer.Options;


namespace HexcellsInfiniteRandomizer;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
[BepInProcess("Hexcells Infinite.exe")]
public class HexcellsInfiniteRandomizer : BaseUnityPlugin
{
    internal static new ManualLogSource Logger;

    public static ArchipelagoSession session;

    public static System.Random random = new System.Random();

    public static MenuHexLevel levelEntered = new MenuHexLevel();

    public static int itemCount = 0;

    public static bool[] levelsCleared = new bool[36];

    public static bool[] levelsCollected = new bool[36];

    public static string[] levelClearMessages = new string[36];

    public static long indexOfLastRecievedItem = 0;

    public static string gamepath = "";

    public static Dictionary<string, string> apInfo = [];

    public static bool sessionConnected = false;

    public static GameObject connectedText = new();

    public static Options options = new();

    public static bool hasShield = false;

    public static int[] levelSeeds = new int[36];

    public static bool initialReloadCellDisplay = false;

    public static bool cellDisplayReloadRequested = false;

    public GameObject levelCompleteScreen = null;

    public static List<string> levelUnlockItems = [];

    public static bool onetimeUIChanges = true;

    public static DeathLinkService deathLinkService = null;

    public static int mistakesMade = 0;

    public static bool pendingDeathLink = false;

    public static Version randomizerVersion = new Version(MyPluginInfo.PLUGIN_VERSION);



    //Custom version of SaveData, used to include levelsCleared
    [Serializable]
    public class SaveAddData
    {
        public void GetObjectData(SerializationInfo info, StreamingContext ctxt)
        {
            info.AddValue("LevelsCleared", levelsCleared);
            info.AddValue("NumberOfGems", numberOfGems);
            info.AddValue("LevelGemsUnlocked", levelGemsUnlocked);
            info.AddValue("AnimationsPlayed", animationsPlayed);
            info.AddValue("HasShield", hasShield);
            info.AddValue("LevelSeeds", levelSeeds);
            info.AddValue("ServerSeed",serverSeed);
        }


        public bool[] levelsCleared = new bool[36];

        public int numberOfGems;

        public int[] levelGemsUnlocked = new int[36];

        public int[] animationsPlayed = new int[5];

        public bool hasShield;

        public int[] levelSeeds = new int[36];

        public string serverSeed = "";

    }

    //Archipelago options
    public class Options
    {
        //This determines if levels need to be cleared with no mistakes to send out an item.
        public bool RequirePerfectClears = false;

        //This determines how puzzles will be randomized.
        //Vanilla: No puzzle randomization, all puzzles are as they are in the base game.
        //Randomized: Every level has a randomly generated puzzle.
        //TrueRandomized: Every level has a randomly generated puzzle, that is re-randomized if you leave the level.
        public enum EPuzzleOptions
        {
            Vanilla = 1,
            Randomized = 2,
            TrueRandomized = 3
        }
        public EPuzzleOptions PuzzleOptions = EPuzzleOptions.Vanilla;

        //When enabled, puzzle solves that involve mistakes will give 1 shield (max of 1 shield). Shields are used to block mistakes on future levels. This pairs well with True Randomization.
        public bool EnableShields = false;

        //This determines how levels will be unlocked.
        //Vanilla: Levels are unlocked in groups of 6, with 6 groups total.
        //Individual: Levels are unlocked individually, with individual gem amount requirements.
        public enum ELevelUnlockType
        {
            Vanilla = 1,
            Individual = 2
        }
        public ELevelUnlockType LevelUnlockType = ELevelUnlockType.Vanilla;

        //When enabled, puzzle will be generated with harder sets. Does not affect "Vanilla" under "Puzzle Options".
        public bool HardGeneration = false;

        //When enabled, all levels have to be cleared to complete the goal, even ones that have already been collected (eg. by a game completing its goal or manually with /collect).
        public bool RequireClearingCollectedLevels = false;

        //When enabled and you make a mistake, everyone who enabled death link dies. When anyone dies, your current level is restarted.
        public bool EnableDeathLink = false;

        //How many mistakes it takes to send a death link.
        public int DeathLinkAmnesty = 1;

        public void Load(JObject options)
        {
            if (options["RequirePerfectClears"] != null)
            {
                RequirePerfectClears = int.Parse(options["RequirePerfectClears"].ToString()) == 1;
            }

            if (options["PuzzleOptions"] != null)
            {
                PuzzleOptions = (EPuzzleOptions)int.Parse(options["PuzzleOptions"].ToString());
            }

            if (options["EnableShields"] != null)
            {
                EnableShields = int.Parse(options["EnableShields"].ToString()) == 1;
            }

            if (options["LevelUnlockType"] != null)
            {
                LevelUnlockType = (ELevelUnlockType)int.Parse(options["LevelUnlockType"].ToString());
            }

            if (options["HardGeneration"] != null)
            {
                HardGeneration = int.Parse(options["HardGeneration"].ToString()) == 1;
            }

            if (options["RequireClearingCollectedLevels"] != null)
            {
                RequireClearingCollectedLevels = int.Parse(options["RequireClearingCollectedLevels"].ToString()) == 1;
            }

            if (options["EnableDeathLink"] != null)
            {
                EnableDeathLink = int.Parse(options["EnableDeathLink"].ToString()) == 1;
            }

            if (options["DeathLinkAmnesty"] != null)
            {
                DeathLinkAmnesty = int.Parse(options["DeathLinkAmnesty"].ToString());
            }
        }
    }



    //Used to ensure UI on Level Select Screen is accurate/correct
    public static void ReloadCellDisplay()
    {
        cellDisplayReloadRequested = false;

        if (sessionConnected)
        {
            var gameManagerScript = GameObject.Find("Game Manager(Clone)").GetComponent<GameManagerScript>();

            //shows "AP Connected!" on main screen
            connectedText = GameObject.Find("User Levels Button");
            DestroyImmediate(connectedText.GetComponent<LoadLocalizedText>());
            connectedText.GetComponent<TextMeshPro>().text = "AP Connected";
            connectedText.GetComponent<BoxCollider>().enabled = false;
            if (onetimeUIChanges)
            {
                connectedText.transform.Translate(0, 5, 0);
                onetimeUIChanges = false;
            }
            
            connectedText.GetComponent<TextMeshPro>().color = new UnityEngine.Color(0, 1, 0);


            try
            {
                //removes Save slots 1 and 3
                GameObject.Find("Title Screen/Blue Hex 1").SetActive(false);
                GameObject.Find("Title Screen/Blue Hex 3").SetActive(false);
                GameObject.Find("Title Screen/Go To Generator").SetActive(false);


                //sets save slot 2's text elements
                DestroyImmediate(GameObject.Find("Blue Hex 2/Label").GetComponent<LoadLocalizedText>());
                GameObject.Find("Blue Hex 2/Label").GetComponent<TextMeshPro>().text = session.DataStorage.GetSlotData().GetValueSafe("Slot").ToString();
                GameObject.Find("Blue Hex 2/Percentage 2").SetActive(false);
                GameObject.Find("Blue Hex 2/Number 2").GetComponent<TextMesh>().text = "PLAY";

            }
            catch
            {

            }

            if (options.LevelUnlockType == ELevelUnlockType.Vanilla)
            {

                //sets UI unlock amounts to correct amounts
                GameObject.Find("Unlock Amount 1").GetComponent<TextMesh>().text = "0";
                GameObject.Find("Unlock Amount 2").GetComponent<TextMesh>().text = "6";
                GameObject.Find("Unlock Amount 3").GetComponent<TextMesh>().text = "12";
                GameObject.Find("Unlock Amount 4").GetComponent<TextMesh>().text = "18";
                GameObject.Find("Unlock Amount 5").GetComponent<TextMesh>().text = "24";
                GameObject.Find("Unlock Amount 6").GetComponent<TextMesh>().text = "30";


                //changes UI of center gem bottom number
                GameObject.Find("Out of Number").GetComponent<TextMesh>().text = "36";

                //ensures that the amount of items we have recieved is also the number shown in center/current gem count
                GameObject.Find("Game Manager(Clone)").GetComponent<GameManagerScript>().currentSlotNumberOfGems = itemCount;
                GameObject.Find("Gems Number").GetComponent<TextMesh>().text = itemCount.ToString();


                //Logic for handling display proper state of levels (locked, incomplete, perfect)
                for (int i = 0; i < 6; i++)
                {
                    if (itemCount < gameManagerScript.currentPerGameData.worldUnlockThresholds[i])
                    {
                        IEnumerator enumerator = GameObject.Find((i + 1).ToString()).transform.GetEnumerator();
                        try
                        {
                            while (enumerator.MoveNext())
                            {
                                object obj = enumerator.Current;
                                Transform transform2 = (Transform)obj;
                                transform2.GetComponent<MenuHexLevel>().SetState(MenuHexLevel.State.Locked);
                            }
                        }
                        finally
                        {
                            if (enumerator is IDisposable disposable)
                            {
                                disposable.Dispose();
                            }
                        }
                    }
                    else
                    {
                        Transform transform = GameObject.Find((i + 1).ToString()).transform;
                        IEnumerator enumerator2 = transform.GetEnumerator();
                        try
                        {
                            while (enumerator2.MoveNext())
                            {

                                object obj2 = enumerator2.Current;
                                Transform transform3 = (Transform)obj2;
                                int levelIndex = transform3.GetComponent<MenuHexLevel>().levelToLoad - 1;
                                if (levelsCleared[levelIndex])
                                {
                                    transform3.GetComponent<MenuHexLevel>().SetState(MenuHexLevel.State.Perfect);
                                }
                                else if (levelsCollected[levelIndex] && !options.RequireClearingCollectedLevels)
                                {
                                    transform3.GetComponent<MenuHexLevel>().SetState(MenuHexLevel.State.Completed);
                                }    
                                else
                                {
                                    transform3.GetComponent<MenuHexLevel>().SetState(MenuHexLevel.State.Notplayed);
                                }
                            }
                        }
                        finally
                        {
                            if (enumerator2 is IDisposable disposable2)
                            {
                                disposable2.Dispose();
                            }
                        }
                    }
                }
            }
            else if (options.LevelUnlockType == ELevelUnlockType.Individual)
            {
                //sets UI unlock amounts to correct amounts
                GameObject.Find("Unlock Amount 1").GetComponent<TextMesh>().text = "";
                GameObject.Find("Unlock Amount 2").GetComponent<TextMesh>().text = "";
                GameObject.Find("Unlock Amount 3").GetComponent<TextMesh>().text = "";
                GameObject.Find("Unlock Amount 4").GetComponent<TextMesh>().text = "";
                GameObject.Find("Unlock Amount 5").GetComponent<TextMesh>().text = "";
                GameObject.Find("Unlock Amount 6").GetComponent<TextMesh>().text = "";


                //changes UI of center gem bottom number
                GameObject.Find("Out of Number").GetComponent<TextMesh>().text = "";

                //ensures that the amount of items we have recieved is also the number shown in center/current gem count

                GameObject.Find("Game Manager(Clone)").GetComponent<GameManagerScript>().currentSlotNumberOfGems = 0;
                GameObject.Find("Gems Number").GetComponent<TextMesh>().text = "";


                //Locks all levels
                for (int i = 0; i < 6; i++)
                {
                    IEnumerator enumerator = GameObject.Find((i + 1).ToString()).transform.GetEnumerator();
                    try
                    {
                        while (enumerator.MoveNext())
                        {
                            object obj = enumerator.Current;
                            Transform transform2 = (Transform)obj;
                            transform2.GetComponent<MenuHexLevel>().SetState(MenuHexLevel.State.Locked);
                        }
                    }
                    finally
                    {
                        if (enumerator is IDisposable disposable)
                        {
                            disposable.Dispose();
                        }
                    }
                }



                //Unlocks levels based on AP received items
                for (int i = 1; i < 7; i++)
                {
                    string[] levelnames = new string[6];
                    for (int j = 1; j < 7; j++)
                    {
                        levelnames[j - 1] = "Hexcells " + i + "-" + j;
                    }

                    object[] enumlist = new object[6];
                    int id = 0;

                    Transform transform = GameObject.Find(i.ToString()).transform;
                    IEnumerator enumerator2 = transform.GetEnumerator();
                    try
                    {
                        while (enumerator2.MoveNext())
                            {
                                enumlist[id] = enumerator2.Current;
                                id++;
                            }
                            id = 0;
                        foreach (string entry in levelUnlockItems)
                        {
                            for (int level = 0; level < 6; level++)
                            {
                                if (levelnames[level] == entry)
                                {
                                    int levelIndex = ((Transform) enumlist[level]).GetComponent<MenuHexLevel>().levelToLoad - 1;
                                    if (levelsCleared[levelIndex])
                                    {
                                        ((Transform)enumlist[level]).GetComponent<MenuHexLevel>().SetState(MenuHexLevel.State.Perfect);
                                    }
                                    else if (levelsCollected[levelIndex] && !options.RequireClearingCollectedLevels)
                                    {
                                        ((Transform)enumlist[level]).GetComponent<MenuHexLevel>().SetState(MenuHexLevel.State.Completed);
                                    }
                                    else
                                    {
                                        ((Transform)enumlist[level]).GetComponent<MenuHexLevel>().SetState(MenuHexLevel.State.Notplayed);
                                    }
                                }
                            }
                        }
                    }
                    finally
                    {
                        if (enumerator2 is IDisposable disposable2)
                        {
                            disposable2.Dispose();
                        }

                    }
                }
            }
            else
            {
                connectedText = GameObject.Find("User Levels Button");
                DestroyImmediate(connectedText.GetComponent<LoadLocalizedText>());
                connectedText.GetComponent<TextMeshPro>().text = "Failed to Connect to AP. Check APInfo file.";
                connectedText.GetComponent<BoxCollider>().enabled = false;
                if (onetimeUIChanges) {
                    connectedText.transform.Translate(0, 5, 0);
                    onetimeUIChanges = false;
                }
                
                connectedText.GetComponent<TextMeshPro>().color = new UnityEngine.Color(1, 0, 0);
            }
        }
    }




    //Initial method that establishes AP session
    private void Awake()
    {
        // Plugin startup logic
        Logger = base.Logger;
        Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");

        SceneManager.sceneLoaded += OnSceneChange;

        Harmony.CreateAndPatchAll(typeof(HexcellsInfiniteRandomizer));


        //handle AP Session Info/initialization

        gamepath = Application.dataPath;
        if (Application.platform == RuntimePlatform.OSXPlayer)
        {
            gamepath += "/../..";
        }
        else
        {
            gamepath += "/..";
        }

        if (!File.Exists(gamepath + "/APInfo.json"))
        {
            FileStream fsOut = new(gamepath + "/APInfo.json", FileMode.OpenOrCreate, FileAccess.ReadWrite);
            apInfo.Add("host", "archipelago.gg");
            apInfo.Add("port", "38281");
            apInfo.Add("slot", "");
            apInfo.Add("password", "");
            // foreach (KeyValuePair<string,string> kv in apInfo) {
            //     Logger.LogMessage(kv.Key + ":   " + kv.Value);
            // }
            string jsonOut = JsonConvert.SerializeObject(apInfo);
            using StreamWriter swOut = new(fsOut);
            swOut.Write(jsonOut);
            swOut.Close();
            fsOut.Close();
        }

        FileStream fsIn = new(gamepath + "/APInfo.json", FileMode.Open, FileAccess.Read);
        using StreamReader srIn = new(fsIn);

        apInfo = JsonConvert.DeserializeObject<Dictionary<string, string>>(srIn.ReadLine());
        if (apInfo.GetValueSafe("host") != "localhost")
        {
            session = ArchipelagoSessionFactory.CreateSession("wss://" + apInfo.GetValueSafe("host"), int.Parse(apInfo.GetValueSafe("port")));
        }
        else
        {
            session = ArchipelagoSessionFactory.CreateSession(apInfo.GetValueSafe("host"), int.Parse(apInfo.GetValueSafe("port")));
        }

        LoginResult result;

        try
        {
            result = session.TryConnectAndLogin("Hexcells Infinite", apInfo.GetValueSafe("slot"), ItemsHandlingFlags.AllItems, password: apInfo.GetValueSafe("password"));
            Dictionary<string, object> slotData = session.DataStorage.GetSlotData();

            options.Load((JObject)slotData["options"]);

            //Check if we are compatible with the world version.
            //If the slot data doesn't contain a world version, we assume 1.2.0 (last APWorld version that did not contain a version number).
            Version worldVersion = new Version(1, 2, 0);
            if (slotData.ContainsKey("WorldVersion"))
            {
                JArray versionArray = (JArray)slotData["WorldVersion"];
                worldVersion = new Version((int)versionArray[0], (int)versionArray[1], (int)versionArray[2]);
            }

            if (worldVersion.Major < randomizerVersion.Major)
            {
                throw new Exception("AP world version " + worldVersion + " is incompatible with randomizer version " + randomizerVersion + ". Please update the AP world and regenerate (or downgrade the randomizer).");
            }
            else if (worldVersion.Major > randomizerVersion.Major || worldVersion.Minor > randomizerVersion.Minor)
            {
                throw new Exception("AP world version " + worldVersion + " is newer than randomizer version " + randomizerVersion + ". Please update the randomizer.");
            }

            //DEBUG
            foreach (KeyValuePair<string, object> kv in session.DataStorage.GetSlotData())
            {
                Logger.LogMessage(kv.Key + ":           " + kv.Value);
            }
        }
        catch (Exception e)
        {
            result = new LoginFailure(e.GetBaseException().Message);
            sessionConnected = false;
        }

        if (!result.Successful)
        {
            LoginFailure failure = (LoginFailure)result;
            string errorMessage = $"Failed to Connect to Hexcells Infinite as " + apInfo.GetValueSafe("slot");
            foreach (string error in failure.Errors)
            {
                errorMessage += $"\n    {error}";
            }
            foreach (ConnectionRefusedError error in failure.ErrorCodes)
            {
                errorMessage += $"\n    {error}";
            }

            sessionConnected = false;

            Logger.LogError(errorMessage);

            return; // Did not connect, show the user the contents of `errorMessage`
        }
        else
        {
            sessionConnected = true;

            session.Locations.CheckedLocationsUpdated += CheckedLocationsUpdatedHandler;
            SyncCollectedLocations();
            ScoutLocations();
            SetupDeathLink();
        }

    }

    //DEBUG key to brute solve puzzles
    KeyboardShortcut key = new(KeyCode.U);
    public bool debug = false;

    //runs every frame. used to check for AP items coming in, check goal completion, use brute solver for debug, and ensure level select screen is accurate using ReloadCellDisplay
    private void Update()
    {
        if (sessionConnected)
        {
            if (session.Items.Any())
            {
                if (options.LevelUnlockType == ELevelUnlockType.Vanilla)
                {
                    session.Items.DequeueItem();
                    itemCount++;
                }
                else if (options.LevelUnlockType == ELevelUnlockType.Individual)
                {
                    var test = session.Items.DequeueItem().ItemName.ToString();
                    Logger.LogWarning(test);
                    levelUnlockItems.Add(test);
                }

                if (!initialReloadCellDisplay)
                {
                    cellDisplayReloadRequested = true;
                }
            }


            UpdateGoal();

            if (key.IsPressed() && debug)
            {
                if (SceneManager.GetActiveScene().name == "Level Generator")
                {
                    for (int i = 0; i < 93; i++)
                    {
                        var cell = GameObject.Find("Orange Hex(Clone)").GetComponent<HexBehaviour>();
                        if (!cell.containsShapeBlock)
                        {
                            cell.DestroyClick();
                        }
                        else
                        {
                            cell.HighlightClick();
                        }

                    }

                }
            }

            if (!initialReloadCellDisplay && cellDisplayReloadRequested)
            {
                ReloadCellDisplay();
            }

            //toggled by switchSceneCheck, which is true whenever we switch from any scene back to the level select scene
            if (SceneManager.GetActiveScene().name == "Menu - Hexcells Infinite" && initialReloadCellDisplay == true)
            {
                initialReloadCellDisplay = false;
                onetimeUIChanges = true;
                ReloadCellDisplay();
            }

        }
    }

    private void UpdateGoal()
    {
        for (int i = 0; i < 36; i++)
        {
            bool completed = levelsCleared[i] || (levelsCollected[i] && !options.RequireClearingCollectedLevels);
            if (!completed)
            {
                return;
            }
        }

        session.SetGoalAchieved();
    }

    private void SyncCollectedLocations()
    {
        foreach (long location in session.Locations.AllLocationsChecked)
        {
            long level = location - 1;
            if (options.LevelUnlockType == ELevelUnlockType.Individual)
            {
                level -= 36;
            }
            levelsCollected[level] = true;
        }
    }

    public void CheckedLocationsUpdatedHandler(ReadOnlyCollection<long> newCheckedLocations) {
        SyncCollectedLocations();
        cellDisplayReloadRequested = true;
    }

    private void ScoutLocations()
    {
        long[] ids = new long[36];
        for (int i = 0; i < 36; ++i)
        {
            levelClearMessages[i] = "Check Sent!";

            ids[i] = i + 1;
            if (options.LevelUnlockType == ELevelUnlockType.Individual)
            {
                ids[i] += 36;
            }
        }

        try
        {
            session.Locations.ScoutLocationsAsync(ScoutLocationCallback, ids);
        }
        catch (Exception)
        {
        }
    }

    static private void ScoutLocationCallback(Dictionary<long, ScoutedItemInfo> itemInfos)
    {
        foreach (KeyValuePair<long, ScoutedItemInfo> itemInfo in itemInfos)
        {
            long id = itemInfo.Key - 1;
            if (options.LevelUnlockType == ELevelUnlockType.Individual)
            {
                id -= 36;
            }

            string color = "47bcbc";
            if ((itemInfo.Value.Flags & ItemFlags.Advancement) == ItemFlags.Advancement)
            {
                color = "a08cda";
            }
            else if ((itemInfo.Value.Flags & ItemFlags.NeverExclude) == ItemFlags.NeverExclude)
            {
                color = "3b5bbe";
            }
            else if ((itemInfo.Value.Flags & ItemFlags.Trap) == ItemFlags.Trap)
            {
                color = "b94437";
            }

            levelClearMessages[id] = "Found <color=#b6b615>" + Sanitize(itemInfo.Value.Player.Name) + "</color>'s <color=#" + color + ">" + Sanitize(itemInfo.Value.ItemName) + "</color>!";
        }
    }

    private void SetupDeathLink()
    {
        if (options.EnableDeathLink)
        {
            Logger.LogMessage("Enabling death link");
            deathLinkService = DeathLinkProvider.CreateDeathLinkService(session);
            deathLinkService.EnableDeathLink();
            deathLinkService.OnDeathLinkReceived += this.DeathLinkReceivedCallback;
        }
    }

    private void DeathLinkReceivedCallback(DeathLink deathLink)
    {
        if (deathLink.Source != apInfo.GetValueSafe("slot") && !pendingDeathLink)
        {
            Logger.LogMessage("Death link received : " + (deathLink.Cause != null ? deathLink.Cause : ""));

            //Don't restart the level if it's been cleared (to avoid reset when displaying results screen).
            if (levelEntered.levelToLoad == 0 || levelsCleared[levelEntered.levelToLoad - 1])
            {
                return;
            }

            //Display death link message
            LoadingText loadingLabel = GameObject.Find("Loading Text")?.GetComponent<LoadingText>();
            if (loadingLabel != null)
            {
                loadingLabel.alpha = 0f;
                loadingLabel.alphaTarget = 1f;

                loadingLabel.textMeshPro.text = (deathLink.Cause != null) ? deathLink.Cause : "Death link received";
            }

            //Restart the current level.
            if (GameObject.Find("Game Manager(Clone)") != null)
            {
                GameManagerScript component = GameObject.Find("Game Manager(Clone)").GetComponent<GameManagerScript>();
                if (component.CheckForLevelSaveState())
                {
                    component.DeleteLevelSaveState();
                }

                if (options.PuzzleOptions == EPuzzleOptions.TrueRandomized)
                {
                    component.seedNumber = random.Next(-99999999, 99999999).ToString();
                }
            }

            GameObject.Find("Fader").GetComponent<FaderScript>().alphaLerpTarget = 1f;
            StartCoroutine("ChangeScene", Application.loadedLevelName);
        }
    }

    public IEnumerator ChangeScene(string scene)
    {
        pendingDeathLink = true;
        yield return new WaitForSeconds(2f);
        pendingDeathLink = false;
        Application.LoadLevel(scene);
    }

    static private string Sanitize(string text)
    {
        return text.Replace("<", "<noparse><</noparse>");
    }

    private void OnSceneChange(Scene scene, LoadSceneMode mode)
    {
        Logger.LogMessage(scene.name);
        if ((scene.name.StartsWith("1") || scene.name.StartsWith("2") || scene.name.StartsWith("3") || scene.name.StartsWith("4") || scene.name.StartsWith("5") || scene.name.StartsWith("6") || scene.name.StartsWith("Level")) && options.EnableShields)
        {
            GameObject box;
            if (scene.name.StartsWith("Level"))
            {
                box = Instantiate(GameObject.Find("UI Parent (Level Gen)/Info Box 2"));
                box.transform.parent = GameObject.Find("UI Parent (Level Gen)").transform;
                box.transform.localPosition = new Vector3(9.2763f, 3.5825f, 1.5f);
                box.name = "Shield Info";
                DestroyImmediate(GameObject.Find("UI Parent (Level Gen)/Shield Info/Instruction Text").GetComponent<LoadLocalizedText>());
                GameObject.Find("UI Parent (Level Gen)/Shield Info/Instruction Text").GetComponent<TextMeshPro>().text = "Shield";
                GameObject.Find("UI Parent (Level Gen)/Shield Info/Mistakes Text").GetComponent<TextMeshPro>().fontSize = 4;
                GameObject.Find("UI Parent (Level Gen)/Shield Info/Mistakes Text").GetComponent<TextMeshPro>().text = hasShield.ToString();
                GameObject.Find("UI Parent (Level Gen)/Shield Info").SetActive(true);
            }
            else
            {
                box = Instantiate(GameObject.Find("UI Parent/Info Box 2"));
                box.transform.parent = GameObject.Find("UI Parent").transform;
                box.transform.localPosition = new Vector3(9.2763f, 3.5825f, 1.5f);
                box.name = "Shield Info";
                DestroyImmediate(GameObject.Find("UI Parent/Shield Info/Mistakes Label Text").GetComponent<LoadLocalizedText>());
                GameObject.Find("UI Parent/Shield Info/Mistakes Label Text").GetComponent<TextMeshPro>().text = "Shield";
                GameObject.Find("UI Parent/Shield Info/Mistakes Text").GetComponent<TextMeshPro>().fontSize = 4;
                GameObject.Find("UI Parent/Shield Info/Mistakes Text").GetComponent<TextMeshPro>().text = hasShield.ToString();
                GameObject.Find("UI Parent/Shield Info").SetActive(true);
            }
        }
        if (scene.name == "Menu - Hexcells Infinite") {
            try
            {
                initialReloadCellDisplay = true;
            }
            catch (Exception e)
            {
                Logger.LogMessage(e);
            }
        }
        
    }

    //custom implementation of LoadGame, which also handles setting unlock amounts, as well as showing correct level states when returning to game
    [HarmonyPatch(typeof(HexSaveSlot), "OnMouseOver")]
    [HarmonyPrefix]
    public static bool Prefix_LoadSaveFile_OnMouseOver(HexSaveSlot __instance)
    {
        if (sessionConnected)
        {
            if (Input.GetMouseButtonDown(0))
            {
                GameManagerScript manager = GameObject.Find("Game Manager(Clone)").GetComponent<GameManagerScript>();
                if (!File.Exists(manager.executablePath + "/saves/slotAP.save"))
                {
                    manager.SaveGame();
                }
                
                SaveAddData saveData = new();
                string text = string.Concat(new object[] { manager.executablePath, "/saves/slotAP.save" });
                Stream stream = File.Open(text, FileMode.Open);
                BinaryFormatter binaryFormatter = new();
                saveData = (SaveAddData)binaryFormatter.Deserialize(stream);
                stream.Close();
                if (session.DataStorage.GetSlotData()["Seed"].ToString() != saveData.serverSeed && saveData.serverSeed != "")
                {
                    GameObject.Find("User Levels Button").GetComponent<TextMeshPro>().color = new UnityEngine.Color(1, .5f, .5f);
                    GameObject.Find("User Levels Button").GetComponent<TextMeshPro>().text = "Current save is from a different AP.\nThis will break things!\nRemove the slotAP.save file from the saves directory!";
                }
                else
                {
                    GameObject.Find("Menu Logic").GetComponent<MenuLogic>().gameManagerScript = manager;
                    GameObject.Find("Main Camera").GetComponent<Tween>().targetPosition = new Vector3(0f, 0f, -10f);
                    GameObject.Find("Main Camera").GetComponent<Tween>().Play();
                    manager.currentMenuState = GameManagerScript.MenuState.LevelSelect;

                    __instance.musicDirector.PlayNoteA(0f);


                    Logger.LogMessage("LOAD GAME");
                    try
                    {
                        hasShield = saveData.hasShield;
                        levelsCleared = saveData.levelsCleared;
                        manager.currentSlotNumberOfGems = itemCount;
                        manager.currentSlotLevelGemsUnlocked = saveData.levelGemsUnlocked;
                        manager.currentSlotAnimationsPlayed = saveData.animationsPlayed;
                        levelSeeds = saveData.levelSeeds;
                    }
                    catch (Exception e)
                    {
                        Debug.LogError(e);
                    }


                    manager.currentPerGameData.worldUnlockThresholds = [0, 6, 12, 18, 24, 30];

                    manager.currentPerGameData.levelRewardAmounts = [
                    0,2,2,2,2,2,2,2,2,2,2,
                    2,2,2,2,2,2,2,2,2,2,
                    2,2,2,2,2,2,2,2,2,2,
                    2,2,2,2,2,2
                    ];



                    manager.currentPerGameData.totalGemsInGame = 36;

                    //Set all world unlock animations as already having played. This is to prevent the state being overridden after being collected.
                    for (int i = 0; i < 5; i++)
                    {
                        manager.currentSlotAnimationsPlayed[i] = 1;
                    }

                    for (int i = 0; i < GameObject.Find("Game Manager(Clone)").GetComponent<GameManagerScript>().currentSlotLevelGemsUnlocked.Length; i++)
                    {
                        if (levelsCleared[i])
                        {
                            GameObject.Find("Game Manager(Clone)").GetComponent<GameManagerScript>().currentSlotLevelGemsUnlocked[i] = 2;
                        }
                    }
                    ReloadCellDisplay();
                }
            }
        }

        return false;
    }



    //custom implementation of SaveGame, used to include custom data
    [HarmonyPatch(typeof(GameManagerScript), "SaveGame")]
    [HarmonyPrefix]
    public static bool Prefix_SaveAdditionalValues_SaveGame(GameManagerScript __instance)
    {
        Logger.LogMessage("SAVED GAME");
        try
        {
            SaveAddData saveData = new()
            {
                levelsCleared = levelsCleared,
                animationsPlayed = __instance.currentSlotAnimationsPlayed,
                hasShield = hasShield,
                levelSeeds = levelSeeds,
                serverSeed = session.DataStorage.GetSlotData()["Seed"].ToString()
            };
            string text = string.Concat(new object[] { __instance.executablePath, "/saves/slotAP.save" });
            Stream stream = File.Open(text, FileMode.Create);
            BinaryFormatter binaryFormatter = new();
            binaryFormatter.Serialize(stream, saveData);
            stream.Close();
        }
        catch (Exception e)
        {
            Debug.LogError(e);
        }
        return false;

    }


    private static void CheckLevel(int levelToLoad, GameObject textUI = null)
    {
        if (options.LevelUnlockType == ELevelUnlockType.Vanilla)
        {
            session.Locations.CompleteLocationChecks(levelToLoad);
        }
        else
        {
            session.Locations.CompleteLocationChecks(levelToLoad + 36);
        }

        if (textUI != null)
        {
            textUI.GetComponent<TextMeshPro>().text = levelClearMessages[levelToLoad - 1];
            textUI.GetComponent<TextMeshPro>().parseCtrlCharacters = false;
        }

        levelsCleared[levelToLoad - 1] = true;
    }


    //SetupMenu is called whenever a level is completed, perfected or not. This is where locations are sent out, and levelsCleared is updated to reflect if an level has already sent out it's location (i.e. perfected level)
    [HarmonyPatch(typeof(LevelCompleteScriptLevelGen), "SetupMenu")]
    [HarmonyPostfix]
    public static void Postfix_LocationCheck_SetupMenu(int mistakes, LevelCompleteScriptLevelGen __instance)
    {
        GameObject.Find("Number Of Puzzles Completed").GetComponent<TextMesh>().text = "";
        GameObject.Find("Puzzle Completed Label").GetComponent<TextMeshPro>().enableWordWrapping = true;
        GameObject.Find("Puzzle Completed Label").transform.Translate(0, 1, 0);

        if (!levelsCleared[levelEntered.levelToLoad - 1])
        {
            if (options.RequirePerfectClears)
            {
                if (mistakes == 0 || (mistakes == 1 && hasShield))
                {
                    CheckLevel(levelEntered.levelToLoad, GameObject.Find("Puzzle Completed Label"));
                    if (mistakes == 1)
                    {
                        hasShield = false;
                        GameObject.Find("Puzzle Completed Label").GetComponent<TextMeshPro>().text += "\n\nYou have used your shield";
                    }
                }
                else
                {
                    GameObject.Find("Puzzle Completed Label").GetComponent<TextMeshPro>().text = "Beat the level with no mistakes to send out a check!";
                }
            }
            else
            {
                CheckLevel(levelEntered.levelToLoad, GameObject.Find("Puzzle Completed Label"));
            }
            if (options.EnableShields && mistakes > 0)
            {
                hasShield = true;
                GameObject.Find("Puzzle Completed Label").GetComponent<TextMeshPro>().text += "\n\nYou now have a shield to block 1 mistake.";
            }
        }
        else
        {
            GameObject.Find("Puzzle Completed Label").GetComponent<TextMeshPro>().text = "This location has already been checked!";
        }
        GameObject.Find("Game Manager(Clone)").GetComponent<GameManagerScript>().SaveGame();
    }

    //Stops overwrite of UI Labels on level complete
    [HarmonyPatch(typeof(LevelCompleteScriptLevelGen), "AnimateCompletedText")]
    [HarmonyPrefix]
    public static bool Prefix_StopUIOverwrite_AnimateCompletedText(LevelCompleteScriptLevelGen __instance)
    {
        return false;
    }



    [HarmonyPatch(typeof(LevelCompleteScript), "SetupMenu")]
    [HarmonyPrefix]
    public static bool Prefix_LocationCheck_SetupMenu(int mistakes, LevelCompleteScript __instance)
    {
        return false;
    }


    [HarmonyPatch(typeof(LevelCompleteScript), "Activate")]
    [HarmonyPrefix]
    public static bool Prefix_LocationCheck_Activate(int mistakes, LevelCompleteScript __instance)
    {
		__instance.clickBlocker.SetActive(true);
		__instance.clickBlocker.GetComponent<MeshCollider>().enabled = true;
		__instance.topPanel.GetComponent<Tween>().Play();
		__instance.leftPanel.GetComponent<Tween>().Play();
		__instance.rightPanel.GetComponent<Tween>().Play();
		__instance.retryButton.GetComponent<Tween>().Play();
		__instance.menuButton.GetComponent<Tween>().Play();
        //this.nextButton.GetComponent<Tween>().Play();
        

        GameObject.Find("UI Parent/Level Complete Parent/Next Button").SetActive(false);
        GameObject.Find("UI Parent/Level Complete Parent/Right Panel/Big Gem Icon").SetActive(false);
        GameObject textUI = Instantiate(GameObject.Find("UI Parent/Level Complete Parent/Menu Button/Graphic/Label"));
        textUI.name = "CustomText";
        textUI.transform.SetParent(GameObject.Find("UI Parent/Level Complete Parent/Right Panel").transform);
        GameObject.Find("UI Parent/Level Complete Parent/Right Panel/Gems Number").SetActive(false);
        textUI.transform.position = new Vector3(2.1415f, 0.5f, -2.536f);
        textUI.transform.rotation = GameObject.Find("UI Parent/Level Complete Parent/Menu Button/Graphic/Label").transform.rotation;
        textUI.transform.localScale = GameObject.Find("UI Parent/Level Complete Parent/Menu Button/Graphic/Label").transform.localScale;
        textUI.transform.localPosition = GameObject.Find("UI Parent/Level Complete Parent/Menu Button/Graphic/Label").transform.localPosition;
        DestroyImmediate(textUI.GetComponent<LoadLocalizedText>());
        
        textUI.GetComponent<TextMeshPro>().text = "";
        textUI.GetComponent<TextMeshPro>().color = new UnityEngine.Color(0.7059f, 0.7059f, 0.7059f, 1f);
        textUI.GetComponent<TextMeshPro>().fontSize = .5f;
        textUI.GetComponent<TextMeshPro>().fontSizeMin = 0;
        textUI.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, 0);
        
        textUI.GetComponent<TextMeshPro>().enableWordWrapping = true;
        //textUI.transform.rotation = new Quaternion(90f,0f,180f, 0f);

        
        
        if (!levelsCleared[levelEntered.levelToLoad-1])
        {

            if (options.RequirePerfectClears)
            {
                if (mistakes == 0 || (mistakes == 1 && hasShield))
                {
                    CheckLevel(levelEntered.levelToLoad, textUI);
                    
                    if (mistakes == 1)
                    {
                        hasShield = false;
                        textUI.GetComponent<TextMeshPro>().text += "\n\nYou have used your shield";
                    }
                }
                else
                {
                    textUI.GetComponent<TextMeshPro>().text = "Beat the level with no mistakes to send out a check!";
                }
            }
            else
            {
                CheckLevel(levelEntered.levelToLoad, textUI);
            }
            if (options.EnableShields && mistakes > 0)
            {
                hasShield = true;
                textUI.GetComponent<TextMeshPro>().text += "\n\nYou now have a shield to block 1 mistake.";
            }
        }
        else
        {
            textUI.GetComponent<TextMeshPro>().text = "This location has already been checked!";
        }
        GameObject.Find("Game Manager(Clone)").GetComponent<GameManagerScript>().SaveGame();



		if (GameObject.Find("Game Manager(Clone)") != null)
        {
            GameManagerScript component = GameObject.Find("Game Manager(Clone)").GetComponent<GameManagerScript>();
            if (component.CheckForLevelSaveState())
            {
                component.DeleteLevelSaveState();
            }
        }
		if (GameObject.Find("Music Director(Clone)") != null)
		{
			GameObject.Find("Music Director(Clone)").GetComponent<MusicDirector>().PlayPuzzleComplete();
		}
        return false;
    }


    [HarmonyPatch(typeof(LevelCompleteButtons), "OnMouseOver")]
    [HarmonyPostfix]
    public static void Postfix_GoToLevelSelect_OnMouseOver(LevelCompleteButtons __instance)
    {
        initialReloadCellDisplay = true;
    }





    //Overwrites what level is loaded when selected what level to play, ensuring that a random level is loaded.
    [HarmonyPatch(typeof(MenuHexLevel), "OnMouseOver")]
    [HarmonyPrefix]
    public static bool Prefix_LoadRandomLevel_MenuHexLevel(MenuHexLevel __instance)
    {
        //VANILLA
        if (options.PuzzleOptions == EPuzzleOptions.Vanilla)
        {
            if (Input.GetMouseButtonDown(0))
            {
                levelEntered = __instance;
                Logger.LogWarning(levelEntered.levelToLoad);
                return true;
            }
        }
            //RANDOMIZED
            else if (options.PuzzleOptions == EPuzzleOptions.Randomized)
            {
                if (Input.GetMouseButtonDown(0))
                {
                    levelEntered = __instance;
                    GameObject.Find("Game Manager(Clone)").GetComponent<OptionsManager>().currentOptions.levelGenHardModeActive = options.HardGeneration;
                    __instance.musicDirector.ChangeTrack(__instance.levelTrack);
                    while (levelSeeds[levelEntered.levelToLoad - 1] == 0)
                    {
                        levelSeeds[levelEntered.levelToLoad - 1] = random.Next(-99999999, 99999999);
                    }
                    GameObject.Find("Game Manager(Clone)").GetComponent<GameManagerScript>().SaveGame();
                    GameObject.Find("Game Manager(Clone)").GetComponent<GameManagerScript>().seedNumber = levelSeeds[levelEntered.levelToLoad - 1].ToString();
                    //GameObject.Find("Game Manager(Clone)").GetComponent<GameManagerScript>().seedNumber = "95637466";
                    GameObject.Find("Fader").GetComponent<FaderScript>().FadeOut(37);

                    return false;
                }
            }
            //TRUE RANDOMIZED
            else if (options.PuzzleOptions == EPuzzleOptions.TrueRandomized)
            {
                if (Input.GetMouseButtonDown(0))
                {
                    levelEntered = __instance;
                    GameObject.Find("Game Manager(Clone)").GetComponent<OptionsManager>().currentOptions.levelGenHardModeActive = options.HardGeneration;
                    __instance.musicDirector.ChangeTrack(__instance.levelTrack);

                    GameObject.Find("Game Manager(Clone)").GetComponent<GameManagerScript>().seedNumber = random.Next(-99999999, 99999999).ToString();
                    //GameObject.Find("Game Manager(Clone)").GetComponent<GameManagerScript>().seedNumber = "95637466";
                    GameObject.Find("Fader").GetComponent<FaderScript>().FadeOut(37);
                    return false;
                }
            }
        return true;
    }


    [HarmonyPatch(typeof(GameManagerScript), "SaveCurrentLevelState")]
    [HarmonyPrefix]
    public static bool Prefix_CustomSaveLevelProgress_SaveCurrentLevelState(GameManagerScript __instance)
    {
        if (options.PuzzleOptions == EPuzzleOptions.Randomized)
        {

            HexScoring component = GameObject.Find("Score Text").GetComponent<HexScoring>();
            
            if (component.tilesRemoved == 0) return false;
            
            bool[,] array = new bool[33, 33];
            IEnumerator enumerator = GameObject.Find("Hex Grid Overlay").transform.GetEnumerator();
            try
            {
                while (enumerator.MoveNext())
                {
                    object obj = enumerator.Current;
                    Transform transform = (Transform)obj;
                    int num = Mathf.RoundToInt(transform.position.x / 0.88f) + 15;
                    int num2 = Mathf.RoundToInt(transform.position.y / 0.5f) + 15;
                    if (num >= 0 && num < 33 && num2 >= 0 && num2 < 33)
                    {
                        array[num, num2] = true;
                    }
                }
            }
            finally
            {
                if (enumerator is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
            bool[,] array2 = new bool[33, 33];
            IEnumerator enumerator2 = GameObject.Find("Hex Grid").transform.GetEnumerator();
            try
            {
                while (enumerator2.MoveNext())
                {
                    object obj2 = enumerator2.Current;
                    Transform transform2 = (Transform)obj2;
                    int num3 = Mathf.RoundToInt(transform2.position.x / 0.88f) + 15;
                    int num4 = Mathf.RoundToInt(transform2.position.y / 0.5f) + 15;
                    if ((transform2.name == "Blue Hex (Flower)" || transform2.name == "Blue Hex (Flower)(Clone)") && transform2.GetComponent<BlueHexFlower>().playerHasMarkedComplete)
                    {
                        array2[num3, num4] = true;
                    }
                }
            }
            finally
            {
                if (enumerator2 is IDisposable disposable2)
                {
                    disposable2.Dispose();
                }
            }
            if (GameObject.Find("Columns Parent") != null)
            {
                IEnumerator enumerator3 = GameObject.Find("Columns Parent").transform.GetEnumerator();
                try
                {
                    while (enumerator3.MoveNext())
                    {
                        object obj3 = enumerator3.Current;
                        Transform transform3 = (Transform)obj3;
                        int num5 = Mathf.RoundToInt(transform3.position.x / 0.88f) + 15;
                        int num6 = Mathf.RoundToInt(transform3.position.y / 0.5f) + 15;
                        if (transform3.GetComponent<ColumnNumber>().playerHasMarkedComplete && num5 >= 0 && num5 < 33 && num6 >= 0 && num6 < 33)
                        {
                            array2[num5, num6] = true;
                        }
                    }
                }
                finally
                {
                    if (enumerator3 is IDisposable disposable3)
                    {
                        disposable3.Dispose();
                    }
                }
            }

            
            try
            {
                LevelStateSaveData levelStateSaveData = new()
                {
                    hexesCleared = array,
                    cluesDisabled = array2,
                    mistakesMade = GameObject.Find("Score Text").GetComponent<HexScoring>().numberOfMistakesMade
                };

                if (__instance.currentMenuState == GameManagerScript.MenuState.PuzzleGenerator)
                {
                    levelStateSaveData.seedNumber = __instance.seedNumber;
                    levelStateSaveData.timeTaken = GameObject.Find("Level Complete Parent").GetComponent<LevelCompleteScriptLevelGen>().timer;
                }
                string text = string.Empty;
                if (__instance.currentMenuState == GameManagerScript.MenuState.LevelSelect)
                {
                    text = string.Concat(
                    [
                    SceneManager.GetActiveScene().name,
                    "-",
                    levelEntered.levelToLoad-1,
                    ".save"
                    ]);
                }
                
                Stream stream = File.Open(__instance.executablePath + "/saves/" + text, FileMode.Create);
                BinaryFormatter binaryFormatter = new();
                binaryFormatter.Serialize(stream, levelStateSaveData);
                stream.Close();
            }
            catch
            {
                Debug.LogError("Saving Level State Failed");
            }
            return false;
        }
        return true;

    }




    [HarmonyPatch(typeof(GameManagerScript), "LoadLevelState")]
    [HarmonyPrefix]
    public static bool Prefix_CustomLoadLevelProgress_LoadLevelState(GameManagerScript __instance)
    {
        if (options.PuzzleOptions == EPuzzleOptions.Randomized)
        {
            try
            {
                string text = string.Empty;
                if (__instance.currentMenuState == GameManagerScript.MenuState.LevelSelect)
                {
                    text = string.Concat(
                    [
                SceneManager.GetActiveScene().name,
                "-",
                levelEntered.levelToLoad-1,
                ".save"
                    ]);
                }
                Logger.LogWarning(text);
                LevelStateSaveData levelStateSaveData = new();
                Stream stream = File.Open(__instance.executablePath + "/saves/" + text, FileMode.Open);
                BinaryFormatter binaryFormatter = new();
                levelStateSaveData = (LevelStateSaveData)binaryFormatter.Deserialize(stream);
                stream.Close();
                List<GameObject> list = [];
                IEnumerator enumerator = GameObject.Find("Hex Grid Overlay").transform.GetEnumerator();
                try
                {
                    while (enumerator.MoveNext())
                    {
                        object obj = enumerator.Current;
                        Transform transform = (Transform)obj;
                        int num = Mathf.RoundToInt(transform.position.x / 0.88f) + 15;
                        int num2 = Mathf.RoundToInt(transform.position.y / 0.5f) + 15;
                        if (num >= 0 && num < 33 && num2 >= 0 && num2 < 33 && !levelStateSaveData.hexesCleared[num, num2])
                        {
                            list.Add(transform.gameObject);
                        }
                    }
                }
                finally
                {
                    if (enumerator is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                }
                foreach (GameObject gameObject in list)
                {
                    if (gameObject.GetComponent<HexBehaviour>().containsShapeBlock)
                    {
                        gameObject.GetComponent<HexBehaviour>().QuickHighlightClick(0, 0);
                    }
                    else
                    {
                        gameObject.GetComponent<HexBehaviour>().QuickDestroyClick(0, 0);
                    }
                }
                IEnumerator enumerator3 = GameObject.Find("Hex Grid").transform.GetEnumerator();
                try
                {
                    while (enumerator3.MoveNext())
                    {
                        object obj2 = enumerator3.Current;
                        Transform transform2 = (Transform)obj2;
                        int num3 = Mathf.RoundToInt(transform2.position.x / 0.88f) + 15;
                        int num4 = Mathf.RoundToInt(transform2.position.y / 0.5f) + 15;
                        if (num3 >= 0 && num3 < 33 && num4 >= 0 && num4 < 33 && levelStateSaveData.cluesDisabled[num3, num4])
                        {
                            transform2.GetComponent<BlueHexFlower>().ToggleMarkComplete();
                        }
                    }
                }
                finally
                {
                    if (enumerator3 is IDisposable disposable2)
                    {
                        disposable2.Dispose();
                    }
                }
                if (GameObject.Find("Columns Parent") != null)
                {
                    IEnumerator enumerator4 = GameObject.Find("Columns Parent").transform.GetEnumerator();
                    try
                    {
                        while (enumerator4.MoveNext())
                        {
                            object obj3 = enumerator4.Current;
                            Transform transform3 = (Transform)obj3;
                            int num5 = Mathf.RoundToInt(transform3.position.x / 0.88f) + 15;
                            int num6 = Mathf.RoundToInt(transform3.position.y / 0.5f) + 15;
                            if (num5 >= 0 && num5 < 33 && num6 >= 0 && num6 < 33 && levelStateSaveData.cluesDisabled[num5, num6])
                            {
                                transform3.GetComponent<ColumnNumber>().ToggleMarkComplete();
                            }
                        }
                    }
                    finally
                    {
                        if (enumerator4 is IDisposable disposable3)
                        {
                            disposable3.Dispose();
                        }
                    }
                }
                GameObject.Find("Score Text").GetComponent<HexScoring>().SetMistakes(levelStateSaveData.mistakesMade);
                
               
            }
            catch
            {
                Debug.LogError("Loading Level State Failed");
            }
            return false;
        }
        return true;
    }


    [HarmonyPatch(typeof(GameManagerScript), "CheckForLevelSaveState")]
    [HarmonyPrefix]
    public static bool Prefix_CustomCheckLevelProgress_CheckForLevelSaveState(ref bool __result, GameManagerScript __instance)
    {
        if (options.PuzzleOptions == EPuzzleOptions.Randomized)
        {
            string text = string.Empty;
            if (__instance.currentMenuState == GameManagerScript.MenuState.LevelSelect)
            {
                text = string.Concat(
                [
            SceneManager.GetActiveScene().name,
        "-",
        levelEntered.levelToLoad-1,
        ".save"
                ]);
            }
            __result = File.Exists(__instance.executablePath + "/saves/" + text);
            return false;
        }
        return true;
    }


    [HarmonyPatch(typeof(GameManagerScript), "DeleteLevelSaveState")]
    [HarmonyPrefix]
    public static bool Prefix_CustomDeleteLevelProgress_DeleteLevelSaveState(GameManagerScript __instance)
    {
        if (options.PuzzleOptions == EPuzzleOptions.Randomized)
        {
            string text = string.Empty;
            if (__instance.currentMenuState == GameManagerScript.MenuState.LevelSelect)
            {
                text = string.Concat(new object[]
                {
            SceneManager.GetActiveScene().name,
            "-",
            levelEntered.levelToLoad-1,
            ".save"
                });
            }
            File.Delete(__instance.executablePath + "/saves/" + text);
            return false;
        }
        return true;
    }

    [HarmonyPatch(typeof(LevelCompleteScriptLevelGen), "Activate")]
    [HarmonyPrefix]
    public static bool Prefix_CustomDeleteLevelProgress_Activate(int mistakes, LevelCompleteScriptLevelGen __instance)
    {
        __instance.SetupMenu(mistakes);
        __instance.clickBlocker.SetActive(true);
        __instance.clickBlocker.GetComponent<MeshCollider>().enabled = true;
        __instance.topPanel.GetComponent<Tween>().Play();
        __instance.leftPanel.GetComponent<Tween>().Play();
        __instance.rightPanel.GetComponent<Tween>().Play();
        __instance.retryButton.GetComponent<Tween>().Play();
        __instance.menuButton.GetComponent<Tween>().Play();
        GameManagerScript component = GameObject.Find("Game Manager(Clone)").GetComponent<GameManagerScript>();
        if (component.CheckForLevelSaveState())
        {
            component.DeleteLevelSaveState();
        }
        if (GameObject.Find("Music Director(Clone)") != null)
        {
            GameObject.Find("Music Director(Clone)").GetComponent<MusicDirector>().PlayPuzzleComplete();
        }
        return false;
    }

    [HarmonyPatch(typeof(MenuExitButton), "OnMouseOver")]
    [HarmonyPrefix]
    public static bool Prefix_CustomExitLevel_MenuExitButton(MenuExitButton __instance)
    {
        if (Input.GetMouseButtonDown(0))
		{
			if (GameObject.Find("Game Manager(Clone)") != null)
			{
				GameManagerScript component = GameObject.Find("Game Manager(Clone)").GetComponent<GameManagerScript>();
				component.SaveCurrentLevelState();
			}
			GameObject.Find("Fader").GetComponent<FaderScript>().FadeOut(0);
		}
        return false;
    }

    [HarmonyPatch(typeof(HexScoring), "Mistake")]
    [HarmonyPostfix]
    public static void Postfix_HexScoring_Mistake(HexScoring __instance)
    {
        if (sessionConnected && options.EnableDeathLink)
        {
            mistakesMade += 1;
            if (mistakesMade >= options.DeathLinkAmnesty)
            {
                mistakesMade = 0;
                Logger.LogMessage("Mistake made. Sending death link.");
                deathLinkService.SendDeathLink(new DeathLink(apInfo.GetValueSafe("slot"), apInfo.GetValueSafe("slot") + " made an incorrect deduction."));

                //Display death link message
                LoadingText loadingLabel = GameObject.Find("Loading Text")?.GetComponent<LoadingText>();
                if (loadingLabel != null)
                {
                    loadingLabel.alpha = 1f;
                    loadingLabel.alphaTarget = 0f;

                    loadingLabel.textMeshPro.text = "Death link sent";

                    GameObject.Find("Fader").GetComponent<FaderScript>().alphaLerp = 1f;
                    GameObject.Find("Fader").GetComponent<FaderScript>().alphaLerpTarget = 0f;
                }
            }
        }
    }
}