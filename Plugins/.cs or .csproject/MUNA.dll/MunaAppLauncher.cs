// =============================================================================
// M.U.N.A. - App Launcher & GUI Controller
// Version: 1.0.0
// Author: Red (2026)
//
// OVERVIEW:
//   This MonoBehaviour provides the graphical user interface for M.U.N.A.
//   It adds a button to KSP's App Launcher toolbar and opens a control window
//   that allows players to interact with the M.U.N.A. system.
//
// FEATURES:
//   - App Launcher button with M.U.N.A. icon
//   - Main control window with status display and analysis button
//   - Difficulty mode selector (Easy/Normal/Hard)
//   - Settings panel for Groq API configuration
//   - Model selector (Llama 3, Mixtral, Gemma)
//   - Live report display with scrollable text area
//
// INTEGRATION:
//   - Finds ModuleMunaCore on the active vessel
//   - Displays real-time status and telemetry reports
//   - Communicates with ModuleMunaCore to trigger analysis
//
// USAGE:
//   1. Install M.U.N.A. on a command pod or probe core
//   2. Click the M.U.N.A. button in the App Launcher during flight
//   3. Use the GUI to request analysis, change modes, or configure settings
// =============================================================================

using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using KSP.UI.Screens;
using UnityEngine;

// KSP Assembly attributes for mod identification
[assembly: KSPAssembly("MUNA", 1, 0)]
[assembly: AssemblyVersion("1.0.0.0")]

namespace MunaIntegration
{
    /// <summary>
    /// Main GUI controller for M.U.N.A. Provides the App Launcher button and
    /// interactive window for controlling the M.U.N.A. system.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class MunaAppLauncher : MonoBehaviour
    {
        // =============================================================================
        // APP LAUNCHER STATE
        // =============================================================================
        
        /// <summary>The App Launcher button instance. Created at startup.</summary>
        private ApplicationLauncherButton _button;
        
        /// <summary>Is the main control window currently visible?</summary>
        private bool _windowOpen = false;
        
        /// <summary>Is the settings panel currently visible?</summary>
        private bool _settingsOpen = false;
        
        /// <summary>Position and size of the main window. Persisted during session.</summary>
        private Rect _windowRect = new Rect(120, 80, 400, 500);
        
        /// <summary>Position and size of the settings panel.</summary>
        private Rect _settingsRect = new Rect(540, 80, 340, 260);

        // =============================================================================
        // REPORT DISPLAY
        // =============================================================================
        
        /// <summary>Scroll position for the report text area.</summary>
        private Vector2 _reportScroll = Vector2.zero;

        // =============================================================================
        // SETTINGS PANEL STATE
        // =============================================================================
        
        /// <summary>Current API key input in the settings field (may be masked).</summary>
        private string _apiKeyInput = "";
        
        /// <summary>Should the API key be visible or masked with dots?</summary>
        private bool _showApiKey = false;
        
        /// <summary>Selected model index in the ModelNames/ModelLabels arrays.</summary>
        private int _modelIndex = 0;
        
        /// <summary>Internal model IDs for Groq API requests.</summary>
        private static readonly string[] ModelNames = {
            "llama3-8b-8192",      // Fast, good quality
            "llama3-70b-8192",     // Best quality, slower
            "mixtral-8x7b-32768",  // Balanced performance
            "gemma2-9b-it",        // Lightweight, quick
        };
        
        /// <summary>Human-readable model labels for the GUI.</summary>
        private static readonly string[] ModelLabels = {
            "Llama 3 8B  (fast)",
            "Llama 3 70B (best quality)",
            "Mixtral 8x7B (balanced)",
            "Gemma 2 9B  (lightweight)",
        };

        // =============================================================================
        // GUI STYLES
        // =============================================================================
        
        /// <summary>Cached GUI styles for consistent appearance.</summary>
        private GUIStyle _boxStyle, _monoLabel, _btnMode, _btnSmall, _titleLabel;
        
        /// <summary>Have the GUI styles been initialized yet?</summary>
        private bool _stylesReady = false;

        // =============================================================================
        // MONOBEHAVIOUR LIFECYCLE
        // =============================================================================
        
        /// <summary>
        /// Called by Unity when the MonoBehaviour starts.
        /// Subscribes to App Launcher events and initializes settings.
        /// </summary>
        private void Start()
        {
            // Subscribe to App Launcher ready/destroyed events
            GameEvents.onGUIApplicationLauncherReady.Add(AddButton);
            GameEvents.onGUIApplicationLauncherDestroyed.Add(RemoveButton);

            // Pre-fill settings panel from saved global settings
            _apiKeyInput = MunaSettings.ApiKey;
            _modelIndex  = Mathf.Max(0, Array.IndexOf(ModelNames, MunaSettings.Model));
        }

        /// <summary>
        /// Called by Unity when the MonoBehaviour is destroyed.
        /// Cleans up App Launcher button and event subscriptions.
        /// </summary>
        private void OnDestroy()
        {
            // Unsubscribe from events
            GameEvents.onGUIApplicationLauncherReady.Remove(AddButton);
            GameEvents.onGUIApplicationLauncherDestroyed.Remove(RemoveButton);
            // Remove the button if it exists
            RemoveButton();
        }

        // =============================================================================
        // APP LAUNCHER BUTTON MANAGEMENT
        // =============================================================================
        
        /// <summary>
        /// Creates the M.U.N.A. button in the KSP App Launcher toolbar.
        /// Called when the App Launcher is ready.
        /// </summary>
        private void AddButton()
        {
            if (_button != null) return; // Already created
            
            // Load icon from GameDatabase, fallback to white texture if missing
            Texture2D icon = GameDatabase.Instance.GetTexture(
                "MUNA/Assets/Icons/muna_icon", false) ?? Texture2D.whiteTexture;

            // Add button to App Launcher (FLIGHT scene only)
            _button = ApplicationLauncher.Instance.AddModApplication(
                onTrue: OnToggleOn,      // Called when toggled on
                onFalse: OnToggleOff,    // Called when toggled off
                onHover: null,          // No hover callback
                onHoverOut: null,       // No hover out callback
                onEnable: null,         // No enable callback
                onDisable: null,        // No disable callback
                visibleInScenes: ApplicationLauncher.AppScenes.FLIGHT,
                texture: icon);
        }

        /// <summary>
        /// Removes the M.U.N.A. button from the App Launcher.
        /// Called during cleanup or scene transitions.
        /// </summary>
        private void RemoveButton()
        {
            if (_button == null) return;
            ApplicationLauncher.Instance.RemoveModApplication(_button);
            _button = null;
        }

        /// <summary>Called when player toggles the App Launcher button ON.</summary>
        private void OnToggleOn() => _windowOpen = true;
        
        /// <summary>Called when player toggles the App Launcher button OFF.</summary>
        private void OnToggleOff() { _windowOpen = false; _settingsOpen = false; }

        // =============================================================================
        // GUI RENDERING
        // =============================================================================
        
        /// <summary>
        /// Unity OnGUI callback - renders the M.U.N.A. windows.
        /// Only draws when _windowOpen is true.
        /// </summary>
        private void OnGUI()
        {
            if (!_windowOpen) return;
            
            // Initialize custom styles on first render
            BuildStyles();
            
            // Use KSP's built-in skin for consistent look
            GUI.skin = HighLogic.Skin;

            // -------------------------------------------------------------------------
            // MAIN CONTROL WINDOW
            // -------------------------------------------------------------------------
            _windowRect = GUILayout.Window(
                id: GetInstanceID(),
                screenRect: _windowRect,
                func: DrawMainWindow,
                text: "◈  M.U.N.A.  Interface",
                options: GUILayout.Width(400));

            // -------------------------------------------------------------------------
            // SETTINGS PANEL (if open)
            // -------------------------------------------------------------------------
            if (_settingsOpen)
            {
                _settingsRect = GUILayout.Window(
                    id: GetInstanceID() + 1,
                    screenRect: _settingsRect,
                    func: DrawSettingsWindow,
                    text: "⚙  M.U.N.A. Settings",
                    options: GUILayout.Width(340));
            }
        }

        // =============================================================================
        // MAIN CONTROL WINDOW
        // =============================================================================
        
        /// <summary>
        /// Draws the main M.U.N.A. control window with status, mode selector,
        /// analysis button, and report display.
        /// </summary>
        /// <param name="windowId">Unity window ID (unused but required)</param>
        private void DrawMainWindow(int windowId)
        {
            // Find the M.U.N.A. module on the active vessel
            ModuleMunaCore module = GetMunaModule();

            // -------------------------------------------------------------------------
            // HEADER ROW (Title + Settings Button)
            // -------------------------------------------------------------------------
            GUILayout.BeginHorizontal(_boxStyle);
            GUILayout.Label("M.U.N.A. Flight Assistant", _titleLabel);
            GUILayout.FlexibleSpace();
            
            // Settings button (⚙) - toggles settings panel
            if (GUILayout.Button("⚙", _btnSmall, GUILayout.Width(28)))
                _settingsOpen = !_settingsOpen;
            GUILayout.EndHorizontal();
            GUILayout.Space(4);

            // -------------------------------------------------------------------------
            // NO MODULE FOUND
            // -------------------------------------------------------------------------
            if (module == null)
            {
                GUILayout.Label(
                    "No M.U.N.A. unit found on active vessel.\n" +
                    "Install a compatible command pod or probe core.");
                GUI.DragWindow();
                return;
            }

            // -------------------------------------------------------------------------
            // STATUS DISPLAY
            // -------------------------------------------------------------------------
            bool isEnabled = module.isMunaEnabled;
            string statusColor = isEnabled ? "#00ff88" : "#ff4444";
            string statusText = isEnabled
                ? $"<color={statusColor}>◆ ONLINE</color>  v{module.installedVersion}  |  Mode: {module.difficulty}"
                : $"<color={statusColor}>◇ OFFLINE</color>  Unit not activated";
            GUILayout.Label(statusText, _monoLabel);

            // Groq connection indicator
            if (MunaSettings.UseGroq && !string.IsNullOrEmpty(MunaSettings.ApiKey))
            {
                string groqColor = module.IsWaitingForGroq ? "#ffaa00" : "#00aaff";
                string groqText = module.IsWaitingForGroq
                    ? $"<color={groqColor}>⟳ {module.GroqStatusText}</color>"
                    : $"<color={groqColor}>◆ Groq: {MunaSettings.Model}</color>";
                GUILayout.Label(groqText, _monoLabel);
            }
            else
            {
                GUILayout.Label("<color=#888888>◇ Groq: not configured — using backup</color>", _monoLabel);
            }

            GUILayout.Space(6);

            // -------------------------------------------------------------------------
            // DISABLED STATE MESSAGE
            // -------------------------------------------------------------------------
            if (!isEnabled)
            {
                GUILayout.Label("Unit installed but disabled.\nEnable from the part's right-click menu.");
                GUI.DragWindow(new Rect(0, 0, _windowRect.width, 28));
                return;
            }

            // -------------------------------------------------------------------------
            // DIFFICULTY MODE SELECTOR
            // -------------------------------------------------------------------------
            GUILayout.Label("Operating Mode:");
            GUILayout.BeginHorizontal();
            if (DrawModeButton("EASY", module.difficulty == "Easy")) 
                SetDifficulty(module, "Easy");
            if (DrawModeButton("NORMAL", module.difficulty == "Normal")) 
                SetDifficulty(module, "Normal");
            if (DrawModeButton("HARD", module.difficulty == "Hard")) 
                SetDifficulty(module, "Hard");
            GUILayout.EndHorizontal();

            // Mode description
            string modeDescription = module.difficulty switch
            {
                "Easy"   => "<color=#aaffaa>Rockomax Conglomerate — Professional. Boring. Reliable.</color>",
                "Normal" => "<color=#aaaaff>Rockomax Conglomerate — Standard issue. By the book.</color>",
                "Hard"   => "<color=#ffaaaa>Jebediah Junk Co. — Unstable. Sarcastic. Duct-taped.</color>",
                _        => ""
            };
            GUILayout.Label(modeDescription, _monoLabel);
            GUILayout.Space(8);

            // -------------------------------------------------------------------------
            // ANALYZE BUTTON
            // -------------------------------------------------------------------------
            bool isWaiting = module.IsWaitingForGroq;
            GUI.enabled = !isWaiting; // Disable button while waiting
            
            string buttonLabel = isWaiting 
                ? "⟳  Waiting for Logic Core..." 
                : "▶  Request M.U.N.A. Analysis";
            if (GUILayout.Button(buttonLabel, GUILayout.Height(34)))
            {
                module.RequestAnalysis();
            }
            GUI.enabled = true; // Re-enable GUI

            GUILayout.Space(6);

            // -------------------------------------------------------------------------
            // REPORT DISPLAY (Scrollable)
            // -------------------------------------------------------------------------
            GUILayout.Label("Last Report:");
            _reportScroll = GUILayout.BeginScrollView(_reportScroll, GUILayout.Height(190));
            GUILayout.Label(module.munaDisplay, _monoLabel);
            GUILayout.EndScrollView();

            // Footer
            GUILayout.Space(4);
            GUILayout.Label(
                "<size=9><color=#555555>M.U.N.A. v6.1.0 by Red — Groq + Procedural Fallback</color></size>",
                _monoLabel);

            // Make window draggable from top area
            GUI.DragWindow(new Rect(0, 0, _windowRect.width, 28));
        }

        // =============================================================================
        // SETTINGS PANEL
        // =============================================================================
        
        /// <summary>
        /// Draws the settings configuration panel for Groq API and model selection.
        /// </summary>
        /// <param name="windowId">Unity window ID (unused but required)</param>
        private void DrawSettingsWindow(int windowId)
        {
            GUILayout.Space(4);

            // -------------------------------------------------------------------------
            // GROQ ENABLE/DISABLE TOGGLE
            // -------------------------------------------------------------------------
            bool groqToggle = GUILayout.Toggle(MunaSettings.UseGroq, "  Use Groq API (recommended)");
            if (groqToggle != MunaSettings.UseGroq)
            {
                MunaSettings.UseGroq = groqToggle;
                MunaSettings.Save();
            }

            GUILayout.Space(6);

            // -------------------------------------------------------------------------
            // API KEY INPUT
            // -------------------------------------------------------------------------
            GUILayout.Label("Groq API Key:");
            GUILayout.BeginHorizontal();
            
            // Show masked or unmasked key based on toggle
            string displayValue = _showApiKey ? _apiKeyInput : MaskKey(_apiKeyInput);
            string newKey = GUILayout.TextField(displayValue, GUILayout.ExpandWidth(true));
            
            // Only update input if we're in show mode (otherwise we see dots)
            if (_showApiKey) _apiKeyInput = newKey;

            // Show/Hide toggle button
            if (GUILayout.Button(_showApiKey ? "Hide" : "Show", _btnSmall, GUILayout.Width(42)))
                _showApiKey = !_showApiKey;
            GUILayout.EndHorizontal();

            GUILayout.Label("<size=9><color=#888888>Get a free key at console.groq.com</color></size>",
                _monoLabel);
            GUILayout.Space(6);

            // -------------------------------------------------------------------------
            // AI MODEL SELECTOR
            // -------------------------------------------------------------------------
            GUILayout.Label("Model:");
            for (int i = 0; i < ModelLabels.Length; i++)
            {
                bool isSelected = (_modelIndex == i);
                Color previousColor = GUI.backgroundColor;
                
                // Highlight selected model
                GUI.backgroundColor = isSelected ? Color.cyan : Color.gray;
                if (GUILayout.Button(ModelLabels[i], _btnSmall))
                    _modelIndex = i;
                GUI.backgroundColor = previousColor;
            }

            GUILayout.Space(8);

            // -------------------------------------------------------------------------
            // ACTION BUTTONS
            // -------------------------------------------------------------------------
            GUILayout.BeginHorizontal();
            
            // Save button - persists settings to disk
            if (GUILayout.Button("Save", GUILayout.Height(28)))
            {
                MunaSettings.ApiKey = _apiKeyInput;
                MunaSettings.Model  = ModelNames[_modelIndex];
                MunaSettings.Save();
                ScreenMessages.PostScreenMessage(
                    "[M.U.N.A.]: Settings saved.", 2f,
                    ScreenMessageStyle.UPPER_CENTER);
            }
            
            // Close button - just hides the panel
            if (GUILayout.Button("Close", GUILayout.Height(28)))
                _settingsOpen = false;
            GUILayout.EndHorizontal();

            // Make settings panel draggable
            GUI.DragWindow(new Rect(0, 0, _settingsRect.width, 28));
        }

        // =============================================================================
        // GUI HELPER METHODS
        // =============================================================================
        
        /// <summary>
        /// Draws a difficulty mode button with highlighted state when active.
        /// </summary>
        /// <param name="label">Button text (EASY, NORMAL, HARD)</param>
        /// <param name="isActive">Is this the currently selected mode?</param>
        /// <returns>True if button was clicked AND wasn't already active</returns>
        private bool DrawModeButton(string label, bool isActive)
        {
            Color previousColor = GUI.backgroundColor;
            GUI.backgroundColor = isActive 
                ? new Color(0.2f, 0.8f, 0.4f)  // Green for active
                : Color.gray;                   // Gray for inactive
            bool wasClicked = GUILayout.Button(label, _btnMode);
            GUI.backgroundColor = previousColor;
            return wasClicked && !isActive;
        }

        /// <summary>
        /// Changes the difficulty mode on the M.U.N.A. module and shows confirmation.
        /// </summary>
        /// <param name="module">The ModuleMunaCore to update</param>
        /// <param name="newDifficulty">The difficulty string (Easy, Normal, Hard)</param>
        private static void SetDifficulty(ModuleMunaCore module, string newDifficulty)
        {
            module.difficulty = newDifficulty;
            module.RefreshDifficulty();
            ScreenMessages.PostScreenMessage(
                $"[M.U.N.A.]: Mode set to {newDifficulty}.", 2f,
                ScreenMessageStyle.UPPER_CENTER);
        }

        /// <summary>
        /// Finds the first ModuleMunaCore on the active vessel.
        /// </summary>
        /// <returns>The ModuleMunaCore instance, or null if none found</returns>
        private static ModuleMunaCore GetMunaModule()
        {
            Vessel activeVessel = FlightGlobals.ActiveVessel;
            if (activeVessel == null) return null;
            
            foreach (Part part in activeVessel.parts)
            {
                ModuleMunaCore munaModule = part.FindModuleImplementing<ModuleMunaCore>();
                if (munaModule != null) return munaModule;
            }
            return null;
        }

        /// <summary>
        /// Masks an API key for display, showing only first/last 4 characters.
        /// Example: "gsk_1234...5678" becomes "gsk_••••5678"
        /// </summary>
        /// <param name="key">The API key to mask</param>
        /// <returns>Masked string with dots replacing middle characters</returns>
        private static string MaskKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            if (key.Length <= 8) return new string('•', key.Length);
            
            // Show first 4, dots, last 4
            return key.Substring(0, 4) 
                 + new string('•', key.Length - 8) 
                 + key.Substring(key.Length - 4);
        }

        // =============================================================================
        // GUI STYLE INITIALIZATION
        // =============================================================================
        
        /// <summary>
        /// Creates and caches custom GUI styles for consistent appearance.
        /// Only runs once per session.
        /// </summary>
        private void BuildStyles()
        {
            if (_stylesReady) return;
            _stylesReady = true;

            // Box style for header sections
            _boxStyle = new GUIStyle(HighLogic.Skin.box)
            {
                padding  = new RectOffset(8, 8, 5, 5),
                wordWrap = true
            };
            
            // Monospace-style label for reports and status
            _monoLabel = new GUIStyle(HighLogic.Skin.label)
            {
                wordWrap = true,
                fontSize = 11,
                richText = true  // Enable color tags like <color=#00ff88>
            };
            
            // Title/header label
            _titleLabel = new GUIStyle(HighLogic.Skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold
            };
            
            // Difficulty mode buttons
            _btnMode = new GUIStyle(HighLogic.Skin.button)
            {
                fixedHeight = 26,
                fontSize = 11,
                fontStyle = FontStyle.Bold
            };
            
            // Small buttons (settings, show/hide)
            _btnSmall = new GUIStyle(HighLogic.Skin.button)
            {
                fixedHeight = 22,
                fontSize = 10
            };
        }
    }
}
