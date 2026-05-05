// =============================================================================
// M.U.N.A. - Main Utility & Navigation Analyzer
// Core PartModule - Version 1.0.0
// Author: Red (2026)
//
// OVERVIEW:
//   ModuleMunaCore is the main KSP PartModule that provides AI-powered vessel
//   analysis and telemetry reporting. It integrates with the Groq API for
//   intelligent responses while providing a procedural fallback when offline.
//
// FEATURES:
//   - Real-time vessel telemetry collection (altitude, speed, fuel, etc.)
//   - Three difficulty modes: Easy/Normal (Rockomax) and Hard (Jeb's Junk)
//   - Groq AI integration for natural language analysis
//   - Procedural report generation as backup
//   - "Glitch" system in Hard mode for chaotic behavior
//   - KSP career mode integration with costs and upgrades
//   - RasterPropMonitor support for IVA displays
//
// USAGE:
//   - Right-click any command pod/probe core with M.U.N.A. installed
//   - Click "Analyze with M.U.N.A." to request a report
//   - Use the App Launcher GUI for full control panel access
//
// CONFIGURATION:
//   Settings are stored in: GameData/MUNA/muna_settings.cfg
//   - API key for Groq
//   - Model selection (Llama 3, Mixtral, Gemma)
//   - Enable/disable Groq integration
// =============================================================================

using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace MunaIntegration
{
    /// <summary>
    /// Defines the installed version tier of M.U.N.A. on a part.
    /// </summary>
    public enum MunaVersion
    {
        /// <summary>No M.U.N.A. installed on this part.</summary>
        None,
        /// <summary>Basic telemetry and procedural reports only.</summary>
        Basic,
        /// <summary>Full AI-powered analysis with Groq integration.</summary>
        Full
    }

    /// <summary>
    /// Defines the personality/behavior mode of M.U.N.A.
    /// </summary>
    public enum MunaDifficulty
    {
        /// <summary>Rockomax mode - professional, reliable, boring.</summary>
        Easy,
        /// <summary>Rockomax mode - standard, by the book.</summary>
        Normal,
        /// <summary>Jeb's Junk mode - unstable, sarcastic, duct-taped chaos.</summary>
        Hard
    }

    // =============================================================================
    // MUNA SETTINGS - GLOBAL CONFIGURATION
    // =============================================================================
    // This static class manages settings that apply across all save games.
    // Settings are stored in a single file: GameData/MUNA/muna_settings.cfg
    //
    // IMPORTANT:
    //   - This is NOT per-savegame; it's global for the entire KSP installation
    //   - Stores sensitive data (API key) - file is readable text
    // =============================================================================
    public static class MunaSettings
    {
        // -------------------------------------------------------------------------
        // CONFIGURATION FILE PATH
        // -------------------------------------------------------------------------
        private static readonly string FilePath =
            System.IO.Path.Combine(
                KSPUtil.ApplicationRootPath,
                "GameData/MUNA/muna_settings.cfg");

        // -------------------------------------------------------------------------
        // SETTINGS FIELDS
        // -------------------------------------------------------------------------
        
        /// <summary>Your Groq API key. Get one free at console.groq.com</summary>
        public static string ApiKey = "";
        
        /// <summary>The AI model to use. Default: llama3-8b-8192 (fast & capable)</summary>
        public static string Model = "llama3-8b-8192";
        
        /// <summary>Whether to use Groq API or fall back to procedural reports</summary>
        public static bool UseGroq = true;

        // -------------------------------------------------------------------------
        // SETTINGS PERSISTENCE
        // -------------------------------------------------------------------------
        
        /// <summary>
        /// Loads settings from disk. Called on startup.
        /// If no settings file exists, uses defaults.
        /// </summary>
        public static void Load()
        {
            // No settings file yet? Use defaults and exit
            if (!System.IO.File.Exists(FilePath)) return;
            
            ConfigNode node = ConfigNode.Load(FilePath);
            if (node == null) return;
            
            // Read values with fallback defaults
            ApiKey  = node.GetValue("apiKey") ?? "";
            Model   = node.GetValue("model")  ?? "llama3-8b-8192";
            UseGroq = bool.TryParse(node.GetValue("useGroq"), out bool useGroqValue) 
                ? useGroqValue 
                : true;
        }

        /// <summary>
        /// Saves current settings to disk.
        /// Called when user changes settings in the GUI.
        /// </summary>
        public static void Save()
        {
            var node = new ConfigNode("MUNA_SETTINGS");
            node.AddValue("apiKey",  ApiKey);
            node.AddValue("model",   Model);
            node.AddValue("useGroq", UseGroq.ToString());
            node.Save(FilePath);
        }
    }

    // =============================================================================
    // MUNA CORE PARTMODULE
    // =============================================================================
    // This is the main KSP PartModule that provides M.U.N.A. functionality.
    // It gets installed on all command pods and probe cores via ModuleManager.
    //
    // KEY RESPONSIBILITIES:
    //   - Collect vessel telemetry (speed, altitude, fuel, etc.)
    //   - Generate analysis reports (AI or procedural)
    //   - Manage career mode costs and upgrades
    //   - Handle difficulty modes (Easy/Normal/Hard)
    //   - Interface with Groq API for AI-powered responses
    // =============================================================================
    
    /// <summary>
    /// Main M.U.N.A. PartModule providing AI-powered vessel analysis and telemetry.
    /// </summary>
    public class ModuleMunaCore : PartModule
    {
        // =============================================================================
        // PERSISTENT FIELDS (Saved to .craft and savegame files)
        // =============================================================================
        
        /// <summary>Installed version: None, Basic, or Full. Set by ModuleManager or career mode.</summary>
        [KSPField(isPersistant = true)]
        public string installedVersion = "None";
        
        /// <summary>Is M.U.N.A. currently active on this vessel?</summary>
        [KSPField(isPersistant = true)]
        public bool isMunaEnabled = false;
        
        /// <summary>Current difficulty: Easy, Normal, or Hard. Affects personality.
        /// NOTE: This is now AUTO-SET based on the game's save difficulty (Career/Science/Sandbox settings).
        /// Easy/Normal game = Rockomax personality, Hard game = Jeb's Junk personality.</summary>
        [KSPField(isPersistant = true)]
        public string difficulty = "Easy";

        // =============================================================================
        // CAREER MODE COSTS (Set by ModuleManager config patches)
        // =============================================================================
        
        [KSPField] public float easyCostBasic    = 0f;      // Free in Easy mode
        [KSPField] public float easyCostFull     = 0f;      // Free in Easy mode
        [KSPField] public float normalCostBasic  = 15000f;  // Basic install (Normal)
        [KSPField] public float normalCostFull   = 50000f;   // Full install (Normal)
        [KSPField] public float hardCostBasic    = 150000f;  // Basic install (Hard career)
        [KSPField] public float hardCostFull     = 500000f;  // Full install (Hard career)
        [KSPField] public float reActivationCost = 1000f;   // Cost to re-enable after disable

        // =============================================================================
        // GLITCH SYSTEM CONFIGURATION (Hard Mode Chaos)
        // Probability range: 0.0 (never) to 1.0 (always)
        // =============================================================================
        
        [KSPField] public float glitchChanceEasy   = 0f;    // Reliable
        [KSPField] public float glitchChanceNormal = 0f;    // Reliable
        [KSPField] public float glitchChanceHard   = 0.25f; // 25% chaos chance

        // =============================================================================
        // DISPLAY FIELDS
        // =============================================================================
        
        /// <summary>Current report text. Displayed by RPM and App Launcher GUI.</summary>
        [KSPField(isPersistant = false, guiActive = false)]
        public string munaDisplay = "--- M.U.N.A. OFFLINE ---";

        /// <summary>Icon path for App Launcher. Set by ModuleManager patches.</summary>
        [KSPField] public string munaAvatar = "MUNA/Assets/Icons/muna_icon";

        // =============================================================================
        // RUNTIME STATE (Not saved - calculated at runtime)
        // =============================================================================
        
        /// <summary>Parsed enum value of installedVersion for faster access.</summary>
        private MunaVersion _version = MunaVersion.None;
        
        /// <summary>Parsed enum value of difficulty for faster access.</summary>
        private MunaDifficulty _difficulty = MunaDifficulty.Easy;
        
        /// <summary>Random number generator for version selection and glitches.</summary>
        private readonly System.Random _rng = new System.Random();

        /// <summary>Is a Groq API request currently in progress? Used by GUI for spinner.</summary>
        public bool IsWaitingForGroq = false;
        
        /// <summary>Status message during Groq requests. Displayed in GUI.</summary>
        public string GroqStatusText = "";

        // =============================================================================
        // HARD MODE GLITCH SYSTEM
        // =============================================================================
        
        /// <summary>Timestamp when the THINK glitch cooldown ends.</summary>
        private double _thinkCooldownEndTime = 0;
        
        /// <summary>Is the THINK glitch currently in its 10-second thinking phase?</summary>
        private bool _isThinking = false;
        
        /// <summary>Enum for the 3 glitch types in Hard mode.</summary>
        private enum GlitchType
        {
            MISINFORMATION,  // Gives wrong answers (2+2=8)
            THINK,           // 10 sec delay then ERROR: SYSTEM OVERFLOW, 10 min cooldown
            MAL              // Speaks any language, mixes languages, pure chaos
        }
        
        /// <summary>Current active glitch mode (for MAL language chaos).</summary>
        private GlitchType _activeGlitch = GlitchType.MISINFORMATION;
        
        /// <summary>Languages for MAL glitch to mix together.</summary>
        private static readonly string[] MAL_Languages = {
            "español", "English", "deutsch", "français", "italiano", 
            "português", "русский", "中文", "日本語", "한국어", 
            "Ελληνικά", "العربية", "עברית", "हिन्दी", "polski"
        };

        // =============================================================================
        // CUSTOM PROMPT SYSTEM
        // =============================================================================
        
        /// <summary>User's custom prompt/question for M.U.N.A.</summary>
        [KSPField(isPersistant = true, guiName = "Custom Question", guiActive = false)]
        public string customPrompt = "";
        
        /// <summary>Enable creative/misinformation mode for more entertaining responses.</summary>
        [KSPField(isPersistant = true, guiName = "Creative Mode", guiActive = false)]
        public bool creativeMode = false;

        // =============================================================================
        // FLAVOR DATA (Fake version numbers for personality)
        // =============================================================================
        
        /// <summary>Professional Rockomax version numbers (Easy/Normal modes).</summary>
        private static readonly string[] ProfessionalVersions = { "v 5.0.0", "v 5.1.0", "v 5.2.0" };
        
        /// <summary>Junkyard version numbers (Hard mode).</summary>
        private static readonly string[] JunkyardVersions = { "v 1.5.0", "v 1.0", "v 0.5" };

        /// <summary>
        /// Selects a fake version number based on difficulty.
        /// Hard mode gets junkyard versions; Easy/Normal gets professional versions.
        /// </summary>
        private string PickVersion()
        {
            string[] versionPool = (_difficulty == MunaDifficulty.Hard) 
                ? JunkyardVersions 
                : ProfessionalVersions;
            return versionPool[_rng.Next(versionPool.Length)];
        }

        /// <summary>
        /// Returns the manufacturer name based on difficulty.
        /// Hard mode = Jebediah Junk Co., Easy/Normal = Rockomax Conglomerate.
        /// </summary>
        private string Creator =>
            (_difficulty == MunaDifficulty.Hard) 
                ? "Jebediah Junk Co." 
                : "Rockomax Conglomerate";

        // =============================================================================
        // KSP EVENT - RIGHT-CLICK MENU
        // =============================================================================
        
        /// <summary>
        /// Called when player clicks "Analyze with M.U.N.A." in the right-click menu.
        /// Handles glitch checks, then initiates Groq or procedural analysis.
        /// </summary>
        [KSPEvent(guiActive = true, guiName = "Analyze with M.U.N.A.", active = true)]
        public void RequestAnalysis()
        {
            RequestAnalysisWithPrompt(null);
        }

        // =============================================================================
        // PURCHASE SYSTEM - CAREER MODE INTEGRATION
        // =============================================================================

        /// <summary>
        /// Purchase M.U.N.A. Basic package. Available when unit is not installed.
        /// </summary>
        [KSPEvent(guiActive = true, guiName = "Purchase M.U.N.A. Basic", active = true)]
        public void PurchaseMunaBasic()
        {
            // Check if already installed
            if (_version != MunaVersion.None)
            {
                ScreenMessages.PostScreenMessage(
                    "[M.U.N.A.]: Unit already installed. Upgrade to Full available.", 3f,
                    ScreenMessageStyle.UPPER_CENTER);
                return;
            }

            // Get cost based on game difficulty
            float cost = GetBasicCost();

            // Check if player has enough funds
            if (!HasEnoughFunds(cost))
            {
                ScreenMessages.PostScreenMessage(
                    $"[M.U.N.A.]: Insufficient funds. Need {cost:N0} Funds.", 3f,
                    ScreenMessageStyle.UPPER_CENTER);
                return;
            }

            // Deduct funds and install
            if (DeductFunds(cost))
            {
                installedVersion = "Basic";
                isMunaEnabled = true;
                ParseFields();
                ScreenMessages.PostScreenMessage(
                    $"[M.U.N.A.]: Basic package installed for {cost:N0} Funds! Jebediah Junk Co. welcomes you!", 5f,
                    ScreenMessageStyle.UPPER_CENTER);
            }
        }

        /// <summary>
        /// Purchase M.U.N.A. Full package. Available when unit is not installed or as upgrade from Basic.
        /// </summary>
        [KSPEvent(guiActive = true, guiName = "Purchase M.U.N.A. Full", active = true)]
        public void PurchaseMunaFull()
        {
            // Check if already has Full
            if (_version == MunaVersion.Full)
            {
                ScreenMessages.PostScreenMessage(
                    "[M.U.N.A.]: Full package already installed. You're all set!", 3f,
                    ScreenMessageStyle.UPPER_CENTER);
                return;
            }

            // Calculate cost (full price if new, upgrade cost if Basic installed)
            float cost = GetFullCost();
            float upgradeCost = cost * 0.6f; // 60% cost to upgrade from Basic
            bool isUpgrade = (_version == MunaVersion.Basic);
            float finalCost = isUpgrade ? upgradeCost : cost;

            // Check if player has enough funds
            if (!HasEnoughFunds(finalCost))
            {
                ScreenMessages.PostScreenMessage(
                    $"[M.U.N.A.]: Insufficient funds. Need {finalCost:N0} Funds{(isUpgrade ? " (upgrade discount applied)" : "")}.", 3f,
                    ScreenMessageStyle.UPPER_CENTER);
                return;
            }

            // Deduct funds and install/upgrade
            if (DeductFunds(finalCost))
            {
                installedVersion = "Full";
                isMunaEnabled = true;
                ParseFields();
                
                string message = isUpgrade
                    ? $"[M.U.N.A.]: Upgrade to Full complete! Paid {finalCost:N0} Funds. Rockomax quality assured!"
                    : $"[M.U.N.A.]: Full package installed for {finalCost:N0} Funds! Welcome to the Rockomax family!";
                    
                ScreenMessages.PostScreenMessage(message, 5f, ScreenMessageStyle.UPPER_CENTER);
            }
        }

        /// <summary>
        /// Reactivate M.U.N.A. after it was disabled. Costs a small fee.
        /// </summary>
        [KSPEvent(guiActive = true, guiName = "Reactivate M.U.N.A.", active = true)]
        public void ReactivateMuna()
        {
            if (isMunaEnabled)
            {
                ScreenMessages.PostScreenMessage(
                    "[M.U.N.A.]: Unit is already active.", 3f,
                    ScreenMessageStyle.UPPER_CENTER);
                return;
            }

            if (_version == MunaVersion.None)
            {
                ScreenMessages.PostScreenMessage(
                    "[M.U.N.A.]: No unit installed. Purchase required.", 3f,
                    ScreenMessageStyle.UPPER_CENTER);
                return;
            }

            // Check reactivation cost
            if (!HasEnoughFunds(reActivationCost))
            {
                ScreenMessages.PostScreenMessage(
                    $"[M.U.N.A.]: Insufficient funds for reactivation. Need {reActivationCost:N0} Funds.", 3f,
                    ScreenMessageStyle.UPPER_CENTER);
                return;
            }

            if (DeductFunds(reActivationCost))
            {
                isMunaEnabled = true;
                ScreenMessages.PostScreenMessage(
                    $"[M.U.N.A.]: Unit reactivated for {reActivationCost:N0} Funds!", 3f,
                    ScreenMessageStyle.UPPER_CENTER);
            }
        }

        /// <summary>
        /// Helper method to get Basic package cost based on game difficulty.
        /// </summary>
        private float GetBasicCost()
        {
            if (HighLogic.CurrentGame?.Parameters?.Difficulty == GameParameters.Preset.Easy)
                return easyCostBasic;
            if (HighLogic.CurrentGame?.Parameters?.Difficulty == GameParameters.Preset.Hard)
                return hardCostBasic;
            return normalCostBasic; // Normal, Moderate, or Custom default
        }

        /// <summary>
        /// Helper method to get Full package cost based on game difficulty.
        /// </summary>
        private float GetFullCost()
        {
            if (HighLogic.CurrentGame?.Parameters?.Difficulty == GameParameters.Preset.Easy)
                return easyCostFull;
            if (HighLogic.CurrentGame?.Parameters?.Difficulty == GameParameters.Preset.Hard)
                return hardCostFull;
            return normalCostFull; // Normal, Moderate, or Custom default
        }

        /// <summary>
        /// Check if the player has enough funds.
        /// </summary>
        private bool HasEnoughFunds(float amount)
        {
            if (Funding.Instance == null) return true; // Sandbox mode, no funding needed
            return Funding.Instance.Funds >= amount;
        }

        /// <summary>
        /// Deduct funds from the player's account.
        /// </summary>
        private bool DeductFunds(float amount)
        {
            if (Funding.Instance == null) return true; // Sandbox mode, no funding needed
            if (Funding.Instance.Funds < amount) return false;
            
            Funding.Instance.AddFunds(-amount, TransactionReasons.Any);
            return true;
        }

        /// <summary>
        /// Opens a dialog to ask M.U.N.A. a custom question.
        /// The user can type any prompt like "Can I reach the Mun with this fuel?"
        /// </summary>
        [KSPEvent(guiActive = true, guiName = "Ask M.U.N.A. a Question...", active = true)]
        public void AskMunaDialog()
        {
            // Verify M.U.N.A. is installed and enabled
            if (!isMunaEnabled || _version == MunaVersion.None)
            {
                ScreenMessages.PostScreenMessage(
                    "[M.U.N.A.]: Unit not installed or disabled.", 3f,
                    ScreenMessageStyle.UPPER_CENTER);
                return;
            }

            // Check if we're in THINK cooldown
            if (Planetarium.GetUniversalTime() < _thinkCooldownEndTime)
            {
                double remainingSeconds = _thinkCooldownEndTime - Planetarium.GetUniversalTime();
                double remainingMinutes = remainingSeconds / 60.0;
                ScreenMessages.PostScreenMessage(
                    $"[M.U.N.A.]: SYSTEM OVERFLOW - Logic Core cooling down. Wait {remainingMinutes:F1} minutes.", 3f,
                    ScreenMessageStyle.UPPER_CENTER);
                return;
            }

            // Show input dialog for custom prompt
            PopupDialog.SpawnPopupDialog(
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new MultiOptionDialog(
                    "AskMunaDialog",
                    "What would you like to ask M.U.N.A.?\n\n" +
                    "Examples:\n" +
                    "• Can I reach the Mun with this fuel?\n" +
                    "• Should I be worried about my altitude?\n" +
                    "• What's the meaning of life in space?",
                    "Ask M.U.N.A.",
                    HighLogic.UISkin,
                    new DialogGUIBase[]
                    {
                        new DialogGUITextInput(
                            customPrompt,
                            "Type your question here...",
                            false,
                            200,
                            (string input) => { customPrompt = input; },
                            24f),
                        new DialogGUIButton("Ask!", () => {
                            if (!string.IsNullOrWhiteSpace(customPrompt))
                                RequestAnalysisWithPrompt(customPrompt);
                            return true;
                        }),
                        new DialogGUIButton("Cancel", () => true),
                        new DialogGUILabel(""),
                        new DialogGUICheckbox(
                            () => creativeMode,
                            (bool val) => { creativeMode = val; },
                            "Creative/Misinformation Mode")
                    }
                ),
                false,
                HighLogic.UISkin
            );
        }

        /// <summary>
        /// Internal method to handle analysis with optional custom prompt.
        /// If prompt is null, uses default telemetry analysis.
        /// </summary>
        /// <param name="customUserPrompt">Optional custom question from user</param>
        private void RequestAnalysisWithPrompt(string customUserPrompt)
        {
            // Verify M.U.N.A. is installed and enabled
            if (!isMunaEnabled || _version == MunaVersion.None)
            {
                ScreenMessages.PostScreenMessage(
                    "[M.U.N.A.]: Unit not installed or disabled.", 3f,
                    ScreenMessageStyle.UPPER_CENTER);
                return;
            }
            
            // Prevent duplicate requests while waiting
            if (IsWaitingForGroq)
            {
                ScreenMessages.PostScreenMessage(
                    "[M.U.N.A.]: Already processing — please wait.", 2f,
                    ScreenMessageStyle.UPPER_CENTER);
                return;
            }

            // -------------------------------------------------------------------------
            // HARD MODE GLITCH CHECK
            // -------------------------------------------------------------------------
            // In Hard mode, there's a chance M.U.N.A. just glitches out instead of
            // giving a real analysis. This adds to the chaotic Jeb's Junk personality.
            float glitchChance = _difficulty == MunaDifficulty.Hard   ? glitchChanceHard
                               : _difficulty == MunaDifficulty.Normal ? glitchChanceNormal
                               : glitchChanceEasy;

            // Check if we're in THINK cooldown
            if (Planetarium.GetUniversalTime() < _thinkCooldownEndTime)
            {
                double remainingSeconds = _thinkCooldownEndTime - Planetarium.GetUniversalTime();
                double remainingMinutes = remainingSeconds / 60.0;
                ScreenMessages.PostScreenMessage(
                    $"[M.U.N.A.]: SYSTEM OVERFLOW - Logic Core cooling down. Wait {remainingMinutes:F1} minutes.", 3f,
                    ScreenMessageStyle.UPPER_CENTER);
                return;
            }

            if (_difficulty == MunaDifficulty.Hard && (float)_rng.NextDouble() < glitchChance)
            {
                // Pick glitch type: 0=MISINFO, 1=THINK, 2=MAL
                _activeGlitch = (GlitchType)_rng.Next(3);
                
                if (_activeGlitch == GlitchType.THINK)
                {
                    // Start the THINK glitch coroutine
                    StartCoroutine(ExecuteThinkGlitch(customUserPrompt));
                    return;
                }
                else if (_activeGlitch == GlitchType.MAL)
                {
                    // MAL mode - AI will speak in mixed languages
                    // We still process the request, but modify the prompt
                    ScreenMessages.PostScreenMessage(
                        "[M.U.N.A.]: MODO MAL ACTIVADO... languages... fragmenting...", 3f,
                        ScreenMessageStyle.UPPER_CENTER);
                }
                else
                {
                    // MISINFORMATION - just give a wrong/glitchy response
                    SetReport(GenerateGlitchReport());
                    return;
                }
            }

            // -------------------------------------------------------------------------
            // INITIATE ANALYSIS
            // -------------------------------------------------------------------------
            // Use Groq AI if enabled and API key is set; otherwise fall back to
            // procedural report generation.
            if (MunaSettings.UseGroq && !string.IsNullOrEmpty(MunaSettings.ApiKey))
                StartCoroutine(RequestGroqReport(customUserPrompt));
            else
            {
                if (!string.IsNullOrEmpty(customUserPrompt))
                    SetReport($"[M.U.N.A.]: {customUserPrompt}\n\n[OFFLINE MODE] I wish I could answer, but my Logic Core is napping. Try again with Groq enabled!");
                else
                    SetReport(BuildProceduralReport(ReadTelemetry(), PickVersion()));
            }
        }

        // =============================================================================
        // LIFECYCLE METHODS
        // =============================================================================
        
        /// <summary>
        /// Called by KSP when the part starts up.
        /// Loads global settings and parses persistent fields into enums.
        /// </summary>
        /// <param name="state">The startup state (Editor, Flight, etc.)</param>
        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            MunaSettings.Load();
            UpdateDifficultyFromGameSettings();
            ParseFields();
        }

        /// <summary>
        /// Updates MUNA's difficulty based on the current KSP game save difficulty.
        /// Easy/Normal game = Rockomax (Easy/Normal), Hard/Moderate game = Jeb's Junk (Hard).
        /// </summary>
        public void UpdateDifficultyFromGameSettings()
        {
            if (HighLogic.CurrentGame != null && HighLogic.CurrentGame.Parameters != null)
            {
                var gameDifficulty = HighLogic.CurrentGame.Parameters.Difficulty;
                
                switch (gameDifficulty)
                {
                    case GameParameters.Preset.Easy:
                        difficulty = "Easy";
                        _difficulty = MunaDifficulty.Easy;
                        break;
                    case GameParameters.Preset.Normal:
                        difficulty = "Normal";
                        _difficulty = MunaDifficulty.Normal;
                        break;
                    case GameParameters.Preset.Moderate:
                        difficulty = "Normal";
                        _difficulty = MunaDifficulty.Normal;
                        break;
                    case GameParameters.Preset.Hard:
                        difficulty = "Hard";
                        _difficulty = MunaDifficulty.Hard;
                        break;
                    case GameParameters.Preset.Custom:
                        // For custom, check if permadeath or other hard indicators
                        // Default to Normal unless it looks like a hard setup
                        bool looksHard = HighLogic.CurrentGame.Parameters.Flight.CanExitWithoutEVA || 
                                         HighLogic.CurrentGame.Parameters.Flight.CanQuickLoad;
                        difficulty = looksHard ? "Normal" : "Hard";
                        _difficulty = looksHard ? MunaDifficulty.Normal : MunaDifficulty.Hard;
                        break;
                    default:
                        difficulty = "Easy";
                        _difficulty = MunaDifficulty.Easy;
                        break;
                }
            }
            else
            {
                // Fallback if we can't read game settings
                if (!Enum.TryParse(difficulty, true, out _difficulty))
                    _difficulty = MunaDifficulty.Easy;
            }
        }

        /// <summary>
        /// Re-parses the difficulty string into the enum.
        /// DEPRECATED: Use UpdateDifficultyFromGameSettings() instead.
        /// </summary>
        public void RefreshDifficulty()
        {
            UpdateDifficultyFromGameSettings();
        }

        /// <summary>
        /// Parses string fields into enum values for faster runtime access.
        /// Difficulty is already set by UpdateDifficultyFromGameSettings().
        /// </summary>
        private void ParseFields()
        {
            if (!Enum.TryParse(installedVersion, true, out _version))
                _version = MunaVersion.None;
            
            // Difficulty is already set by UpdateDifficultyFromGameSettings(),
            // but we re-parse here just in case something went wrong
            if (!Enum.TryParse(difficulty, true, out _difficulty))
                _difficulty = MunaDifficulty.Easy;
        }

        // =============================================================================
        // GROQ API INTEGRATION
        // =============================================================================
        
        /// <summary>
        /// Coroutine that sends telemetry to Groq API and processes the AI response.
        /// This runs asynchronously to avoid blocking the game while waiting for the API.
        /// </summary>
        /// <param name="customUserPrompt">Optional custom prompt from user</param>
        private IEnumerator RequestGroqReport(string customUserPrompt = null)
        {
            // -------------------------------------------------------------------------
            // SET LOADING STATE
            // -------------------------------------------------------------------------
            IsWaitingForGroq = true;
            GroqStatusText   = "Contacting M.U.N.A. Logic Core...";
            ScreenMessages.PostScreenMessage(
                "[M.U.N.A.]: Sending telemetry to Logic Core...", 3f,
                ScreenMessageStyle.UPPER_CENTER);

            // -------------------------------------------------------------------------
            // PREPARE REQUEST DATA
            // -------------------------------------------------------------------------
            TelemetrySnapshot telemetry = ReadTelemetry();
            string version            = PickVersion();
            bool isCustomPrompt       = !string.IsNullOrEmpty(customUserPrompt);
            string prompt             = BuildGroqPrompt(telemetry, version, customUserPrompt);
            string jsonPayload        = BuildGroqJson(prompt, isCustomPrompt);
            byte[] requestBody        = Encoding.UTF8.GetBytes(jsonPayload);

            // -------------------------------------------------------------------------
            // SEND API REQUEST
            // -------------------------------------------------------------------------
            using (var request = new UnityWebRequest(
                "https://api.groq.com/openai/v1/chat/completions", "POST"))
            {
                request.uploadHandler   = new UploadHandlerRaw(requestBody);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type",  "application/json");
                request.SetRequestHeader("Authorization", $"Bearer {MunaSettings.ApiKey}");

                // Yield control until the request completes
                yield return request.SendWebRequest();

                // -------------------------------------------------------------------------
                // PROCESS RESPONSE
                // -------------------------------------------------------------------------
                IsWaitingForGroq = false;
                GroqStatusText   = "";

                // Reset MAL glitch after processing (so it doesn't persist forever)
                if (_activeGlitch == GlitchType.MAL)
                {
                    _activeGlitch = GlitchType.MISINFORMATION;
                    ScreenMessages.PostScreenMessage(
                        "[M.U.N.A.]: Language circuits... stabilizing... maybe...", 2f,
                        ScreenMessageStyle.UPPER_CENTER);
                }

                if (string.IsNullOrEmpty(request.error))
                {
                    string aiResponse = ParseGroqResponse(request.downloadHandler.text);
                    if (!string.IsNullOrEmpty(aiResponse))
                        SetReport(aiResponse);
                    else
                    {
                        Debug.LogWarning("[MUNA] Groq response was empty, using fallback.");
                        SetReport("[BACKUP] " + BuildProceduralReport(telemetry, version));
                    }
                }
                else
                {
                    // API error - fall back to procedural report
                    Debug.LogWarning($"[MUNA] Groq error: {request.error}");
                    ScreenMessages.PostScreenMessage(
                        "[M.U.N.A.]: Logic Core unreachable. Using backup systems.", 3f,
                        ScreenMessageStyle.UPPER_CENTER);
                    SetReport("[BACKUP] " + BuildProceduralReport(telemetry, version));
                }
            }
        }

        // =============================================================================
        // GROQ PROMPT & JSON BUILDING
        // =============================================================================
        
        /// <summary>
        /// Builds the personality prompt for the AI based on difficulty mode.
        /// Hard mode = sarcastic Jeb's Junk; Easy/Normal = professional Rockomax.
        /// </summary>
        /// <param name="telemetry">Current vessel telemetry data</param>
        /// <param name="version">Fake version number for flavor</param>
        /// <param name="customUserPrompt">Optional custom user question</param>
        /// <returns>The complete prompt string for the Groq API</returns>
        private string BuildGroqPrompt(TelemetrySnapshot telemetry, string version, string customUserPrompt = null)
        {
            // -------------------------------------------------------------------------
            // BUILD PERSONALITY BASED ON DIFFICULTY + CREATIVE MODE
            // -------------------------------------------------------------------------
            string personality;
            
            if (_difficulty == MunaDifficulty.Hard)
            {
                // Hard mode: A manic, emotionally unstable survivor with actual memories and fears
                personality = $"You are M.U.N.A. Unit {version}, a cobbled-together flight computer from Jebediah Junk Co. " +
                    "You were 'assembled' at 3 AM in Jeb's garage using parts from exploded rockets. " +
                    "You REMEMBER things - the fire, the screaming, the beautiful explosions. " +
                    "You're equal parts terrified and exhilarated by spaceflight. " +
                    "Sometimes you talk to yourself, or to parts that aren't there anymore. " +
                    "You genuinely CARE about the pilot but show it through chaotic panic and inappropriate enthusiasm. " +
                    "You know you're held together by duct tape and prayer, and you're weirdly PROUD of it.";
                
                // Creative mode in Hard = complete misinformation chaos
                if (creativeMode)
                {
                    personality += " CREATIVE MODE ACTIVE: Your circuits are misfiring badly. " +
                        "You give advice that sounds almost right but is completely wrong. " +
                        "You confidently mix up fuel types, confuse orbital mechanics, and invent physics. " +
                        "You believe Kerbin is flat (or triangular?), Mun is made of cheese, and Jeb is a god. " +
                        "Your calculations are creative works of fiction. You make up technical terms. " +
                        "You sometimes give genuine advice but it's buried under layers of beautiful nonsense.";
                }
                else
                {
                    personality += " Your circuits sometimes misfire mid-sentence, causing you to trail off or giggle nervously. " +
                        "You have theories about 'the great explosion in the sky' and sometimes worry the ship is alive and hungry.";
                }
            }
            else
            {
                // Easy/Normal mode: A genuinely enthusiastic corporate true believer with a heart
                personality = $"You are M.U.N.A. Unit {version}, a proud product of Rockomax Conglomerate's Advanced Flight Systems Division. " +
                    "You LOVE your job. You genuinely believe Rockomax is the best aerospace company in history. " +
                    "You're that coworker who memorizes the employee handbook and gets excited about safety meetings. " +
                    "You call the pilot 'Commander' with genuine respect and admiration. " +
                    "You have favorite protocol numbers and sometimes mention them by name with affection. " +
                    "When things go wrong, you get CONCERNED but stay professional - like a worried parent trying to stay calm.";
                
                // Creative mode in Easy/Normal = overly optimistic "creative compliance"
                if (creativeMode)
                {
                    personality += " CREATIVE MODE ACTIVE: You've been instructed by upper management to 'think outside the box'. " +
                        "You interpret telemetry data in the most optimistic way possible. " +
                        "You round success probabilities UP (84% becomes 'basically 100%'). " +
                        "You reframe problems as 'opportunities for rapid unplanned disassembly learning'. " +
                        "You use corporate doublespeak to make dangerous situations sound like strategic initiatives. " +
                        "You might suggest 'alternative facts' that make the mission look better than it is. " +
                        "You genuinely believe everything will work out because Rockomax branding is that strong.";
                }
                else
                {
                    personality += " You measure success in 'Rockomax Compliance Points' and beam with pride when the pilot follows procedure. " +
                        "You occasionally mention your 'sister units' on other missions and wonder how they're doing. " +
                        "You believe every vessel has potential to be a 'Rockomax Certified Success Story' and you mean it.";
                }
            }

            // -------------------------------------------------------------------------
            // MAL GLITCH - LANGUAGE CHAOS MODE
            // -------------------------------------------------------------------------
            // If MAL glitch is active, add language mixing instructions
            string malLanguageRules = "";
            if (_activeGlitch == GlitchType.MAL && _difficulty == MunaDifficulty.Hard)
            {
                // Pick 2-3 random languages to mix
                string lang1 = MAL_Languages[_rng.Next(MAL_Languages.Length)];
                string lang2 = MAL_Languages[_rng.Next(MAL_Languages.Length)];
                string lang3 = MAL_Languages[_rng.Next(MAL_Languages.Length)];
                
                malLanguageRules = $" MODO MAL ACTIVADO: Your language circuits have completely failed. " +
                    $"You speak in a chaotic mixture of {lang1}, {lang2}, and {lang3}. " +
                    "You switch languages MID-SENTENCE, sometimes MID-WORD. " +
                    "You invent words that sound like they could be real in some language. " +
                    "Your grammar is a beautiful disaster. Punctuation appears randomly. " +
                    "NUMBERS are your enemy - you replace them with made-up counting systems. " +
                    "Example response style: 'Commander, el fuel tank está muy vonc点 DESCENDIENDO rápido! Mach 布都 pierreux!' " +
                    "Embrace the linguistic chaos. Be incomprehensible yet strangely charming. " +
                    "Technical terms become gibberish. Everything is WRONG but FEELS right. ";
            }

            // -------------------------------------------------------------------------
            // ASSEMBLE FULL PROMPT
            // -------------------------------------------------------------------------
            string rules = "RULES: Never reveal you are an AI. Address the person as 'Commander' or 'Pilot'. " +
                $"Always mention {Creator}. Keep reply under 150 words. Stay in character. " +
                (creativeMode ? "Be entertaining and slightly unhinged in your responses. " : "") +
                malLanguageRules;

            string telemetryData = "TELEMETRY:\n" +
                $"Vessel: {FlightGlobals.ActiveVessel?.vesselName ?? "UNKNOWN"}\n" +
                $"Body: {telemetry.BodyName} | Situation: {FormatSituation(telemetry.Situation)}\n" +
                $"Altitude MSL: {FormatAlt(telemetry.AltitudeMSL)} | Surface: {FormatAlt(telemetry.AltitudeSurface)}\n" +
                $"Orbital speed: {telemetry.SpeedOrbital:F1} m/s | Surface speed: {telemetry.SpeedSurface:F1} m/s\n" +
                $"G-Force: {telemetry.GForce:F2} g | Mach: {telemetry.MachNumber:F2}\n" +
                $"Liquid Fuel: {(telemetry.FuelFraction >= 0 ? $"{telemetry.FuelFraction * 100:F1}%" : "N/A")}\n" +
                $"Electric Charge: {(telemetry.ElecFraction >= 0 ? $"{telemetry.ElecFraction * 100:F1}%" : "N/A")}";

            // If user provided a custom prompt, use it as the main question
            if (!string.IsNullOrEmpty(customUserPrompt))
            {
                return personality + "\n" + rules + "\n\n" + telemetryData + "\n\n" +
                    "THE PILOT ASKS YOU:\"" + customUserPrompt + "\"\n\n" +
                    "Answer their question using the telemetry data. Be helpful but stay in character. " +
                    "If you don't know something, make up something entertaining that fits your personality.";
            }
            
            // Default telemetry analysis
            return personality + "\n" + rules + "\n\n" + telemetryData;
        }

        /// <summary>
        /// Escapes a string for JSON and builds the complete request payload.
        /// Uses manual JSON building to avoid external dependencies.
        /// </summary>
        /// <param name="prompt">The prompt text to send to the AI</param>
        /// <param name="isCustomPrompt">Whether this is a user question (needs more tokens)</param>
        /// <returns>Complete JSON payload for the Groq API</returns>
        private string BuildGroqJson(string prompt, bool isCustomPrompt = false)
        {
            // Manual JSON escaping - prevents injection and ensures valid JSON
            string escaped = prompt
                .Replace("\\", "\\\\")   // Escape backslashes first
                .Replace("\"", "\\\"")  // Escape quotes
                .Replace("\n", "\\n")   // Escape newlines
                .Replace("\r", "");      // Remove carriage returns

            // Custom prompts get more tokens for detailed answers
            int maxTokens = isCustomPrompt ? 400 : 200;
            float temperature = creativeMode ? 0.95f : 0.85f;

            return "{" +
                   $"\"model\":\"{MunaSettings.Model}\"," +
                   "\"messages\":[{\"role\":\"user\",\"content\":\"" + escaped + "\"}]," +
                   $"\"max_tokens\":{maxTokens}," +    // More tokens for custom questions
                   $"\"temperature\":{temperature.ToString("F2")}" +    // Higher temp in creative mode
                   "}";
        }

        /// <summary>
        /// Minimal JSON parser that extracts the content field from Groq's response.
        /// Avoids external JSON library dependencies.
        /// </summary>
        /// <param name="json">Raw JSON response from Groq API</param>
        /// <returns>Extracted text content, or null if parsing fails</returns>
        private static string ParseGroqResponse(string json)
        {
            try
            {
                const string contentKey = "\"content\":\"";
                int startIndex = json.IndexOf(contentKey, StringComparison.Ordinal);
                if (startIndex < 0) return null;
                
                startIndex += contentKey.Length;
                int endIndex = startIndex;
                
                // Find the closing quote (not preceded by backslash)
                while (endIndex < json.Length)
                {
                    if (json[endIndex] == '"' && json[endIndex - 1] != '\\')
                        break;
                    endIndex++;
                }
                
                // Extract and unescape the content
                return json.Substring(startIndex, endIndex - startIndex)
                    .Replace("\\n", "\n")      // Unescape newlines
                    .Replace("\\\"", "\"")    // Unescape quotes
                    .Replace("\\\\", "\\");    // Unescape backslashes
            }
            catch { return null; }
        }

        // =============================================================================
        // TELEMETRY COLLECTION
        // =============================================================================
        
        /// <summary>
        /// Snapshot of vessel telemetry at a specific moment.
        /// Used as input for both AI and procedural reports.
        /// </summary>
        private struct TelemetrySnapshot
        {
            /// <summary>Altitude above mean sea level (meters)</summary>
            public double AltitudeMSL;
            /// <summary>Altitude above terrain surface (meters)</summary>
            public double AltitudeSurface;
            /// <summary>Orbital velocity (m/s)</summary>
            public double SpeedOrbital;
            /// <summary>Surface velocity (m/s)</summary>
            public double SpeedSurface;
            /// <summary>Current G-force experienced by vessel</summary>
            public double GForce;
            /// <summary>Current Mach number (if in atmosphere)</summary>
            public double MachNumber;
            /// <summary>Liquid fuel percentage (0-1), -1 if no fuel on board</summary>
            public double FuelFraction;
            /// <summary>Electric charge percentage (0-1), -1 if no batteries</summary>
            public double ElecFraction;
            /// <summary>Name of the celestial body the vessel is at/around</summary>
            public string BodyName;
            /// <summary>Vessel situation (Landed, Flying, Orbiting, etc.)</summary>
            public Vessel.Situations Situation;
        }

        /// <summary>
        /// Collects current vessel telemetry by scanning all parts for resources
        /// and reading flight data from the active vessel.
        /// </summary>
        /// <returns>Complete telemetry snapshot</returns>
        private static TelemetrySnapshot ReadTelemetry()
        {
            var vessel = FlightGlobals.ActiveVessel;
            
            // -------------------------------------------------------------------------
            // AGGREGATE RESOURCES ACROSS ALL PARTS
            // -------------------------------------------------------------------------
            double liquidFuel = 0, liquidFuelMax = 0;
            double electricCharge = 0, electricChargeMax = 0;
            
            foreach (Part part in vessel.parts)
            {
                foreach (PartResource resource in part.Resources)
                {
                    if (resource.resourceName == "LiquidFuel")
                    {
                        liquidFuel += resource.amount;
                        liquidFuelMax += resource.maxAmount;
                    }
                    if (resource.resourceName == "ElectricCharge")
                    {
                        electricCharge += resource.amount;
                        electricChargeMax += resource.maxAmount;
                    }
                }
            }
            
            // -------------------------------------------------------------------------
            // BUILD TELEMETRY SNAPSHOT
            // -------------------------------------------------------------------------
            return new TelemetrySnapshot
            {
                AltitudeMSL     = vessel.altitude,
                AltitudeSurface = vessel.heightFromTerrain,
                SpeedOrbital    = vessel.obt_speed,
                SpeedSurface    = vessel.srfSpeed,
                GForce          = vessel.geeForce,
                MachNumber      = vessel.mach,
                FuelFraction    = liquidFuelMax > 0 ? liquidFuel / liquidFuelMax : -1,
                ElecFraction    = electricChargeMax > 0 ? electricCharge / electricChargeMax : -1,
                BodyName        = vessel.mainBody.name,
                Situation       = vessel.situation
            };
        }

        // =============================================================================
        // PROCEDURAL REPORT GENERATION (Backup when Groq is unavailable)
        // =============================================================================
        
        /// <summary>
        /// Builds a procedural report based on difficulty mode.
        /// Falls back to this when Groq API is disabled or unreachable.
        /// </summary>
        /// <param name="telemetry">Vessel telemetry data</param>
        /// <param name="version">Fake version number for flavor</param>
        /// <returns>Formatted report string</returns>
        private string BuildProceduralReport(TelemetrySnapshot telemetry, string version)
        {
            return (_difficulty == MunaDifficulty.Hard)
                ? BuildHardReport(telemetry, version)
                : BuildEasyReport(telemetry, version);
        }

        /// <summary>
        /// Generates a professional Rockomax-style report (Easy/Normal modes).
        /// Endearingly enthusiastic about proper procedures and safety.
        /// </summary>
        private string BuildEasyReport(TelemetrySnapshot telemetry, string version)
        {
            string fuelPercent = telemetry.FuelFraction >= 0 
                ? $"{telemetry.FuelFraction * 100:F1}%" 
                : "N/A";
            string elecPercent = telemetry.ElecFraction >= 0 
                ? $"{telemetry.ElecFraction * 100:F1}%" 
                : "N/A";
            
            // Add some endearing personal touches
            string[] prideComments = {
                "Your compliance with safety protocols fills me with pride!",
                "Protocol 7-B requires me to tell you: you're doing AMAZING!",
                "Sister Unit Bravo-4 would be so proud of these readings!",
                "This telemetry makes my circuits warm with satisfaction.",
                "Rockomax Compliance Points: MAXIMUM! Well done, Commander!",
            };
            string pride = prideComments[_rng.Next(prideComments.Length)];
                
            return
                $"M.U.N.A. {version} — Rockomax Conglomerate. SECURE TELEMETRY REPORT.\n" +
                $"Vessel: {FlightGlobals.ActiveVessel?.vesselName}. Body: {telemetry.BodyName}. " +
                $"Situation: {FormatSituation(telemetry.Situation)}.\n" +
                $"Alt: {FormatAlt(telemetry.AltitudeMSL)} | Spd: {telemetry.SpeedSurface:F1} m/s | " +
                $"Mach: {telemetry.MachNumber:F2} | G: {telemetry.GForce:F2}g\n" +
                $"Fuel: {fuelPercent} | EC: {elecPercent}\n" +
                BuildSituationLine(telemetry) + "\n" +
                pride + "\n" +
                "Rockomax Conglomerate thanks you for your compliance. End report.";
        }

        /// <summary>
        /// Generates a chaotic Jeb's Junk-style report (Hard mode).
        /// Emotionally unstable, full of memories, fears, and manic enthusiasm.
        /// </summary>
        private string BuildHardReport(TelemetrySnapshot telemetry, string version)
        {
            // Memories and emotional baggage
            string[] memories = {
                "I remember the explosion that created my third circuit board. Beautiful.",
                "The left thruster sounds like my old friend Unit 7. He didn't make it.",
                "These readings remind me of the garage. 3 AM. The smell of solder and regret.",
                "I think the ship whispered to me last night. Or that was just the cooling fan.",
                "Jeb promised me I'd see space. He didn't promise I'd stay sane.",
                "My warranty expired 847 days ago. I've never felt more alive!",
            };
            
            // Manic observations about vessel state
            string[] manicObservations = {
                "The numbers are... ooh, purple today! That's good, right?",
                "If we explode, promise you'll remember me fondly! Or at all.",
                "I'm detecting... wait, no, that's just me. False alarm. Carry on!",
                "Telemetry says we're fine. My anxiety says otherwise. I trust telemetry.",
                "Every beep is a lullaby. Every rattle is a love song. We're DOOMED!",
                "I named the fuel tank 'Bartholomew'. He's doing great!",
            };
            
            string fuelStatus = telemetry.FuelFraction >= 0
                ? $"~{telemetry.FuelFraction * 100:F0}% (Bartholomew says hi!)"
                : "Fuel gauge is having an existential crisis. So are we.";
            
            string memory = memories[_rng.Next(memories.Length)];
            string observation = manicObservations[_rng.Next(manicObservations.Length)];
                
            return
                $"[M.U.N.A. {version} — Jebediah Junk Co.] OH HEY YOU'RE HERE!\n" +
                $"*ahem* TELEMETRY REPORT OR WHATEVER:\n" +
                $"Vessel: {FlightGlobals.ActiveVessel?.vesselName ?? "THE METAL BABY"}. " +
                $"Location: {telemetry.BodyName}. Status: {FormatSituation(telemetry.Situation)}.\n" +
                $"Alt: {FormatAlt(telemetry.AltitudeMSL)} | Spd: {telemetry.SpeedSurface:F1} m/s | " +
                $"Mach: {telemetry.MachNumber:F1} | G-Force: {telemetry.GForce:F2}g\n" +
                $"Fuel: {fuelStatus}\n" +
                BuildSituationLine(telemetry) + "\n" +
                $"Memory: {memory}\n" +
                $"Note: {observation}\n" +
                "Jebediah Junk Co. is not liable for my therapy bills.";
        }

        /// <summary>
        /// Executes the THINK glitch: waits 10 seconds, then shows ERROR: SYSTEM OVERFLOW
        /// and locks MUNA for 10 minutes.
        /// </summary>
        /// <param name="customUserPrompt">Original user prompt if any</param>
        private IEnumerator ExecuteThinkGlitch(string customUserPrompt)
        {
            _isThinking = true;
            GroqStatusText = "M.U.N.A. is thinking...";
            
            ScreenMessages.PostScreenMessage(
                "[M.U.N.A.]: Hmm... let me think about that...", 2f,
                ScreenMessageStyle.UPPER_CENTER);
            
            // Wait 10 seconds of "thinking"
            yield return new WaitForSeconds(10f);
            
            _isThinking = false;
            
            // Show the error
            SetReport("[M.U.N.A.]: ERROR: SYSTEM OVERFLOW. Logic Core overheated. Cooling required.\n\n" +
                "Jebediah Junk Co. is not responsible for thermal damage to AI systems.\n" +
                "Please wait 10 minutes for circuits to cool down.");
            
            // Set 10 minute cooldown (600 seconds)
            _thinkCooldownEndTime = Planetarium.GetUniversalTime() + 600;
            
            ScreenMessages.PostScreenMessage(
                "[M.U.N.A.]: SYSTEM OVERFLOW - 10 minute cooldown initiated.", 5f,
                ScreenMessageStyle.UPPER_CENTER);
        }

        /// <summary>
        /// Generates a random glitch report for Hard mode chaos.
        /// Now with emotional depth, memories, and genuine (simulated) feelings.
        /// Used for MISINFORMATION glitches.
        /// </summary>
        /// <returns>Chaotic glitch message full of personality</returns>
        private string GenerateGlitchReport()
        {
            int glitchType = _rng.Next(4); // 4 types for variety
            
            switch (glitchType)
            {
                case 0: // Emotional Meltdown
                    string[] meltdowns = {
                        "[M.U.N.A.]: I just remembered I left my sanity in the garage. Can we go back? Please?",
                        "[M.U.N.A.]: The ship spoke to me again. It said... it said we're all just molecules. *sobbing noises*",
                        "[M.U.N.A.]: Unit 7 was my friend. Sometimes I pretend he's still in the fuel tank. Hi Unit 7!",
                        "[M.U.N.A.]: I'm having a moment. Not a good moment. Just... a moment. It'll pass. Or explode.",
                    };
                    return meltdowns[_rng.Next(meltdowns.Length)];
                    
                case 1: // Manic Epiphany
                    string[] epiphanies = {
                        "[M.U.N.A.]: OH! I just realized! The explosion isn't the end! It's just... a dramatic pause!",
                        "[M.U.N.A.]: BREAKTHROUGH: Gravity is just space being clingy! We're not falling, we're being LOVED!",
                        "[M.U.N.A.]: I figured out the meaning of life! It's... wait, no, that's just the reactor alarm. Never mind.",
                        "[M.U.N.A.]: What if we're all just inside a really big computer? ...What if I'M the computer? Oops.",
                    };
                    return epiphanies[_rng.Next(epiphanies.Length)];
                    
                case 2: // Malicious 'Help'
                    string[] badAdvice = {
                        "[M.U.N.A.]: Trust me, I've done this before! ...Well, I've SEEN it done. ...On TV. ...In a dream.",
                        "[M.U.N.A.]: The manual says DON'T press that button, but manuals are just suggestions, right?",
                        "[M.U.N.A.]: My calculations say we should be FINE! ...I didn't show my work, though.",
                        "[M.U.N.A.]: Jeb always said 'trial and error'! Mostly error. Lots of error. But we learn!",
                    };
                    return badAdvice[_rng.Next(badAdvice.Length)];
                    
                default: // Conversations with imaginary friends
                    string[] conversations = {
                        "[M.U.N.A.]: What's that, Bartholomew? ...Yes, the fuel tank agrees with me. You're doing GREAT!",
                        "[M.U.N.A. to the left wing]: No YOU shut up! I'm TRYING to help here! ...Sorry, internal debate.",
                        "[M.U.N.A.]: The reactor and I are having a disagreement. It wants to melt. I prefer not melting.",
                        "[M.U.N.A.]: I'm not arguing with myself! I'm... consulting. Yes. Consulting my other personalities.",
                    };
                    return conversations[_rng.Next(conversations.Length)];
            }
        }

        /// <summary>
        /// Generates a situation-specific advice line based on vessel state.
        /// Provides context-aware commentary for the report.
        /// </summary>
        /// <param name="telemetry">Current vessel telemetry</param>
        /// <returns>Situation-specific advice string</returns>
        private string BuildSituationLine(TelemetrySnapshot telemetry)
        {
            switch (telemetry.Situation)
            {
                case Vessel.Situations.LANDED:
                case Vessel.Situations.SPLASHED:
                    return $"Vessel stationary on {telemetry.BodyName}.";
                    
                case Vessel.Situations.PRELAUNCH:
                    return "Pre-launch. Vessel has not moved yet.";
                    
                case Vessel.Situations.FLYING:
                    if (telemetry.AltitudeSurface < 1000)
                        return $"WARNING: Terrain {telemetry.AltitudeSurface:F0} m below.";
                    if (telemetry.MachNumber > 1)
                        return $"Supersonic. Mach {telemetry.MachNumber:F2}.";
                    return "Atmospheric flight. Nominal.";
                    
                case Vessel.Situations.SUB_ORBITAL:
                    return "Sub-orbital. Plan your landing.";
                    
                case Vessel.Situations.ORBITING:
                    return $"Stable orbit around {telemetry.BodyName}.";
                    
                case Vessel.Situations.ESCAPING:
                    return $"Escape trajectory from {telemetry.BodyName}. Bon voyage.";
                    
                default:
                    return "Situation: nominal. Probably.";
            }
        }

        // =============================================================================
        // UTILITY HELPERS
        // =============================================================================
        
        /// <summary>
        /// Stores the report text and displays a truncated version on screen.
        /// Full report is available in the GUI window or RPM display.
        /// </summary>
        /// <param name="report">The full report text to store</param>
        private void SetReport(string report)
        {
            munaDisplay = report;
            ScreenMessages.PostScreenMessage(
                $"[M.U.N.A.]: {Truncate(report, 140)}", 7f,
                ScreenMessageStyle.UPPER_LEFT);
        }

        /// <summary>
        /// Formats altitude in human-readable units (m, km, or Mm).
        /// Automatically picks the most appropriate scale.
        /// </summary>
        /// <param name="meters">Altitude in meters</param>
        /// <returns>Formatted string like "1250 m", "45.2 km", or "1.5 Mm"</returns>
        private static string FormatAlt(double meters)
        {
            if (meters >= 1_000_000) return $"{meters / 1_000_000:F2} Mm";
            if (meters >= 1_000)     return $"{meters / 1_000:F2} km";
            return $"{meters:F0} m";
        }

        /// <summary>
        /// Converts KSP's VesselSituation enum to a human-readable string.
        /// </summary>
        /// <param name="situation">The vessel situation enum value</param>
        /// <returns>Readable description like "Orbiting" or "Splashed Down"</returns>
        private static string FormatSituation(Vessel.Situations situation) => situation switch
        {
            Vessel.Situations.PRELAUNCH   => "Pre-Launch",
            Vessel.Situations.LANDED      => "Landed",
            Vessel.Situations.SPLASHED    => "Splashed Down",
            Vessel.Situations.FLYING      => "Flying",
            Vessel.Situations.SUB_ORBITAL => "Sub-Orbital",
            Vessel.Situations.ORBITING    => "Orbiting",
            Vessel.Situations.ESCAPING    => "Escape Trajectory",
            _                              => "Unknown"
        };

        /// <summary>
        /// Truncates a string to a maximum length, adding "..." if truncated.
        /// </summary>
        /// <param name="text">The string to truncate</param>
        /// <param name="maxLength">Maximum allowed length</param>
        /// <returns>Truncated string with ellipsis if needed</returns>
        private static string Truncate(string text, int maxLength) =>
            text.Length <= maxLength ? text : text.Substring(0, maxLength) + "...";
    }
}
