using System;
using System.Collections;
using SimpleJSON;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

#if !UNITY_WINRT
using System.IO;
#endif

namespace NextFaze
{
    enum ManUpButtons
    {
        None,
        Ok,
        Update,
        OkAndUpdate
    }

    public enum ManUpState
    {
        Idle = 0,
        NoLocalFile,
        Downloading,
        LoadingLocalFile,
        Processing,
        Checking,
        Invalid,
        Valid
    }

    public struct ManUpMessage
    {
        public string Title;
        public string Message;

        public ManUpMessage(string title, string message)
        {
            Title = title;
            Message = message;
        }
    }

    public class ManUp : MonoBehaviour
    {
        public static ManUp Instance
        {
            get;
            private set;
        }

        [Header("Configuration")]
        [SerializeField]
        string ConfigURL = @"";

        private bool triggerCheck = false;
        [SerializeField] bool logToFile = true;

        [Range(1f, 72f)]
        [SerializeField]
        double HoursBeforeStale = 24;
        [SerializeField] bool ShowVersionInRelease = false;

        [Header("UI Setup")]
        [SerializeField] GameObject UIPanel = null;
        [SerializeField] Text TitleText = null;
        [SerializeField] Text MessageText = null;
        [SerializeField] Button OKButton = null;
        [SerializeField] Text OKButtonText = null;
        [SerializeField] Button UpdateButton = null;
        [SerializeField] Text UpdateButtonText = null;
        [SerializeField] Text VersionText = null;

        [SerializeField] UnityEvent onCompletion = null;

#if UNITY_EDITOR
        [Header("Debug Options")]
        [SerializeField]
        string versionOverride = "1.0";
        [SerializeField] RuntimePlatform platformOverride = RuntimePlatform.OSXEditor;
        [SerializeField] TextAsset configOverride = null;
#endif

        #region Kill Switch Properites

        public ManUpState CurrentState
        {
            get;
            private set;
        }

        public bool ConfigIsValid
        {
            get
            {
                return CurrentState == ManUpState.Valid || CurrentState == ManUpState.NoLocalFile;
            }
        }

        public bool MaintenanceMode
        {
            get;
            private set;
        }

        public bool Activated
        {
            get;
            private set;
        }

        public string AppName
        {
            get { return Application.productName; }
        }

        public string UpdateLink
        {
            get;
            private set;
        }

        public Version LatestVersion
        {
            get;
            private set;
        }

        public Version CurrentVersion
        {
            get;
            private set;
        }

        public Version MinimumVersion
        {
            get;
            private set;
        }

        string CachedFilePath
        {
            get { return Application.persistentDataPath + "/ManUpConfig.json"; }
        }

        string ConfigKey
        {
            get { return "ManUpConfigJSON"; }
        }

        string LogFilePath
        {
            get { return Application.persistentDataPath + "/ManUp.log"; }
        }

        public ManUpMessage MandatoryMessage
        {
            get;
            private set;
        }

        public ManUpMessage OptionalMessage
        {
            get;
            private set;
        }

        public ManUpMessage MaintenanceMessage
        {
            get;
            private set;
        }

        public string ButtonUpdateText
        {
            get;
            private set;
        }

        public string ButtonLaterText
        {
            get;
            private set;
        }

        public string ButtonOKText
        {
            get;
            private set;
        }

        #endregion

        #region JSON Keys

        static readonly string manupSettingsKey = "manup";

        static readonly string messageTitleKey = "title";
        static readonly string messageTextKey = "message";

        static readonly string buttonsUpdateKey = "update";
        static readonly string buttonsLaterKey = "later";
        static readonly string buttonsOKKey = "ok";

        static readonly string mandatoryUpdateDictKey = "mandatory";
        static readonly string optionalUpdateDictKey = "optional";
        static readonly string maintenanceDictKey = "maintenance";
        static readonly string buttonsDictKey = "buttons";

        static readonly string maintenanceModeKey = "enabled";
        static readonly string appUpdateLinkKey = "url";
        static readonly string appVersionLatestKey = "latest";
        static readonly string appVersionMinKey = "minimum";

        static readonly string platformAndroidKey = "android";
        static readonly string platformIOSKey = "ios";
        static readonly string platformOSXKey = "osx";
        static readonly string platformLinuxKey = "linux";
        static readonly string platformWindowsKey = "windows";

        #endregion

        #region Logging
        void LogToFile(string message)
        {
#if !UNITY_WINRT
            if (logToFile)
            {

                StreamWriter writer = File.AppendText(this.LogFilePath);

                writer.WriteLine(string.Format("{0} {1}", DateTime.Now.ToString("[dd-MM-yy hh:mm:ss]"), message));

                writer.Flush();
                writer.Close();
            }
#endif

#if UNITY_EDITOR
            Debug.Log(message);
#endif
        }
#endregion

#region Unity Methods
        void Start()
        {
            if (Instance != null) {
                Debug.LogWarningFormat("Multiple ManUp instances detected. Deleting instance {0}", gameObject.name);
                Destroy(this.gameObject);
            }

            Instance = this;
            DontDestroyOnLoad(this.gameObject);

            MandatoryMessage = new ManUpMessage("Update Required", "An update to {{app}} is required to continue.");
            OptionalMessage = new ManUpMessage("Update Available", "An update to {{app}} is available. Would you like to update?");
            MaintenanceMessage = new ManUpMessage("{{app}} Unavailable", "{{app}} is currently unavailable, please check back again later.");

            this.ButtonOKText = "OK";
            this.ButtonUpdateText = "Update";
            this.ButtonLaterText = "Later";

            LogToFile("ManUp Start");

            SetupUI();

            this.CurrentState = ManUpState.Idle;
            this.triggerCheck = true;

            this.Activated = true;
        }

        void OnEnable()
        {
            LogToFile("ManUp OnEnable");
        }

        // Use this for initialization
        void OnApplicationFocus(bool focusState)
        {
            if (focusState) {
                this.triggerCheck = true;
            }

            LogToFile(string.Format("Application Focus {0}", focusState));
        }

        void Update()
        {
            if (this.triggerCheck) {

                LogToFile("ManUp Check triggered");
                this.triggerCheck = false;

#if UNITY_EDITOR
                if (configOverride) {
                    ParseConfigJSON(this.configOverride.text);
                    return;
                }
#endif

                if (CachedFileIsStale()) {
                    LogToFile("Downloading latest config");
                    StartCoroutine(DownloadConfig());
                }
                else {
                    LogToFile("Loading local config");
                    LoadLocalConfig();
                }
            }
        }
#endregion

#region UI

        void SetupUI()
        {
            bool uiReferencesExist = true;

            if (UnityEngine.EventSystems.EventSystem.current == null) {
                // We need an event system
                var eventSystem = new GameObject("Event System");
                eventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
                eventSystem.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
                eventSystem.transform.parent = this.transform;
            }

            if (this.UIPanel == null)
            {
                Debug.LogWarning("No UI Panel set for ManUp");
                uiReferencesExist = false;
            }

            if (this.MessageText == null)
            {
                Debug.LogWarning("No Message text set for ManUp");
                uiReferencesExist = false;
            }

            if (this.TitleText == null)
            {
                Debug.LogWarning("No Title text set for ManUp");
                uiReferencesExist = false;
            }

            if (this.OKButton == null)
            {
                Debug.LogWarning("No OK Button set for ManUp");
                uiReferencesExist = false;
            }

            if (this.OKButtonText == null)
            {
                Debug.LogWarning("No OK button text set for ManUp");
                uiReferencesExist = false;
            }

            if (this.UpdateButton == null)
            {
                Debug.LogWarning("No Update Button set for ManUp");
                uiReferencesExist = false;
            }

            if (this.UpdateButtonText == null)
            {
                Debug.LogWarning("No Update Button Text set for ManUp");
                uiReferencesExist = false;
            }


            if (!uiReferencesExist) {
                Debug.LogError("UI not defined for ManUp!");
                return;
            }

            try
            {
                LogToFile("Setting up UI");

                LogToFile(string.Format("Application version is '{0}'", Application.version));

                this.CurrentVersion = new Version(Application.version);

#if UNITY_EDITOR
                if (!string.IsNullOrEmpty(this.versionOverride))
                {
                    this.CurrentVersion = new Version(this.versionOverride);
                }
#endif

                LogToFile(string.Format("Current version is {0}", this.CurrentVersion.ToString()));

                if (this.VersionText)
                {
                    this.VersionText.gameObject.SetActive(Debug.isDebugBuild || this.ShowVersionInRelease);
                    this.VersionText.text = this.CurrentVersion.ToString();
                }

                this.UIPanel.transform.localScale = new Vector3(0, 0, 0);

                LogToFile("UI Setup");
            }
            catch (Exception ex)
            {
                LogToFile(String.Format("Exception: {0}", ex.Message));
            }
        }

        void GenerateUI()
        {
            
        }

        IEnumerator ShowUI()
        {
            LogToFile("Showing UI");

            ShowMessage("Checking config", "Please wait...", ManUpButtons.None);
            float scale = this.UIPanel.transform.localScale.x;
            this.UIPanel.SetActive(true);
            while (scale < 1.0f)
            {
                scale = Math.Min(1.0f, scale + 0.1f);

                this.UIPanel.transform.localScale = new Vector3(scale, scale, scale);

                yield return new WaitForSeconds(0.01f);
            }

            LogToFile("UI Shown");

            yield return null;
        }

        IEnumerator HideUI()
        {
            LogToFile("Hiding UI");
            float scale = this.UIPanel.transform.localScale.x;
            while (scale > 0.0f)
            {

                scale = Math.Max(0.0f, scale - 0.1f);

                this.UIPanel.transform.localScale = new Vector3(scale, scale, scale);

                yield return new WaitForSeconds(0.01f);
            }

            this.UIPanel.SetActive(false);

            LogToFile("UI Hidden");

            onCompletion.Invoke();

            yield return null;
        }

        void ShowMessage(string title, string message, ManUpButtons buttons)
        {
            this.TitleText.text = title;
            this.MessageText.text = message;

            this.UpdateButtonText.text = this.ButtonUpdateText;
            this.OKButtonText.text = this.ButtonOKText;

            switch (buttons)
            {
                case ManUpButtons.None:
                    this.OKButton.gameObject.SetActive(false);
                    this.UpdateButton.gameObject.SetActive(false);

                    break;

                case ManUpButtons.Ok:
                    this.OKButton.gameObject.SetActive(true);
                    this.UpdateButton.gameObject.SetActive(false);

                    break;

                case ManUpButtons.Update:
                    this.OKButton.gameObject.SetActive(false);
                    this.UpdateButton.gameObject.SetActive(true);

                    break;

                case ManUpButtons.OkAndUpdate:
                    this.OKButton.gameObject.SetActive(true);
                    this.UpdateButton.gameObject.SetActive(true);

                    this.OKButtonText.text = this.ButtonLaterText;

                    break;
            }
        }

        public void UpdateClicked()
        {
            LogToFile("Update Clicked");
            OpenUpdateURL();
            FlipKillswitch();
        }

        public void OkClicked()
        {
            LogToFile("OK Clicked");
            if (this.MaintenanceMode || this.MinimumVersion > this.CurrentVersion)
            {
                FlipKillswitch();
            }
            else
            {
                StartCoroutine(HideUI());
            }
        }

#endregion

        bool CheckForInternet()
        {
            return true;
        }

        bool CachedFileIsStale()
        {
            if (File.Exists(this.CachedFilePath))
            {
                DateTime lastWriteTime = File.GetLastWriteTime(this.CachedFilePath);
                TimeSpan difference = DateTime.Now - lastWriteTime;

                if (difference.TotalDays < 1 && difference.TotalHours <= this.HoursBeforeStale) return false;
            }

            return true;
        }

#region ManUp Config parsing

        IEnumerator DownloadConfig()
        {
            CurrentState = ManUpState.Downloading;
            WWW configFile = new WWW(this.ConfigURL);

            yield return configFile;

            if (string.IsNullOrEmpty(configFile.error))
            {
                string jsonString = configFile.text;

                ParseConfigJSON(jsonString);

                PlayerPrefs.SetString(this.ConfigKey, jsonString);
                PlayerPrefs.Save();

            }
            else
            {
                if (string.IsNullOrEmpty(configFile.error) == false)
                {
                    LogToFile("Download error: " + configFile.error);
                }

                LoadLocalConfig();
            }
        }

        void LoadLocalConfig()
        {
            LogToFile("Loading local config");
            if (File.Exists(this.CachedFilePath))
            {
                CurrentState = ManUpState.LoadingLocalFile;
                ParseConfigJSON(PlayerPrefs.GetString(this.ConfigKey));
            }
            else
            {
                LogToFile("No Local File");
                CurrentState = ManUpState.NoLocalFile;
            }
        }

        internal void ParseConfigJSON(string configJSON)
        {
            LogToFile("Parsing config");
            CurrentState = ManUpState.Processing;
            JSONNode rootNode = JSON.Parse(configJSON);

            // Determine Platform
            string platformKey = "";
            RuntimePlatform platform = Application.platform;

#if UNITY_EDITOR
            platform = this.platformOverride;
#endif

            switch (platform) {
				
				case RuntimePlatform.Android:
					platformKey = platformAndroidKey;
					break;
					
				case RuntimePlatform.IPhonePlayer:
					platformKey = platformIOSKey;
					break;
					
	            case RuntimePlatform.OSXPlayer:
	                platformKey = platformOSXKey;
	                break;
	          
	            case RuntimePlatform.LinuxPlayer:
	                platformKey = platformLinuxKey;
	                break;
	          
	            case RuntimePlatform.WindowsPlayer:
	                platformKey = platformWindowsKey;
	                break;
					
				default:
					Debug.LogWarningFormat("Platform not supported: {0}", Application.platform);
					LogToFile(string.Format("Platform not supported: {0}", Application.platform));
					
					LogToFile("Deactivating");
					this.Activated = false;
					
					return;
			}

            JSONNode platformNode = rootNode[platformKey];
			
            this.UpdateLink = platformNode[appUpdateLinkKey];
            this.LatestVersion = new Version(platformNode[appVersionLatestKey]);
            this.MinimumVersion = new Version(platformNode[appVersionMinKey]);
            this.MaintenanceMode = !platformNode[maintenanceModeKey].AsBool;

            JSONNode manupSettings = rootNode[manupSettingsKey];
            if (manupSettings != null) {

                JSONNode mandatorySettings = manupSettings[mandatoryUpdateDictKey];
                if (mandatorySettings != null)
                {
                    string title = mandatorySettings[messageTitleKey] ?? "Update Required";
                    string message = mandatorySettings[messageTextKey] ?? "An update to {{app}} is required to continue.";
                    this.MandatoryMessage = new ManUpMessage(title, message);
                }

                JSONNode optionalSettings = manupSettings[optionalUpdateDictKey];
                if (optionalSettings != null)
                {
                    string title = mandatorySettings[messageTitleKey] ?? "Update Available";
                    string message = mandatorySettings[messageTextKey] ?? "An update to {{app}} is available. Would you like to update?";
                    this.OptionalMessage = new ManUpMessage(title, message);
                }

                JSONNode maintenanceSettings = manupSettings[maintenanceDictKey];
                if (maintenanceSettings != null)
                {
                    string title = mandatorySettings[messageTitleKey] ?? "{{app}} Unavailable";
                    string message = mandatorySettings[messageTextKey] ?? "{{app}} is currently unavailable, please check back again later.";
                    this.MaintenanceMessage = new ManUpMessage(title, message);
                }

                JSONNode buttonsSettings = manupSettings[buttonsDictKey];
                if (buttonsSettings != null)
                {
                    this.ButtonUpdateText = mandatorySettings[buttonsUpdateKey] ?? "Update";
                    this.ButtonLaterText = mandatorySettings[buttonsLaterKey] ?? "Later";
                    this.ButtonOKText = mandatorySettings[buttonsOKKey] ?? "OK";
                }
            }
			
			StartCoroutine(CheckConfig());
		}
		
		IEnumerator CheckConfig()
		{
            CurrentState = ManUpState.Checking;
            string title = "";
            string message = "";
			ManUpButtons buttons = ManUpButtons.Ok;
			
			if (this.MaintenanceMode) {
                title = this.MaintenanceMessage.Title;
                message = this.MaintenanceMessage.Message;
			}
            else if (this.MinimumVersion > this.CurrentVersion) {
                title = this.MandatoryMessage.Title;
                message = this.MandatoryMessage.Message;
                buttons = ManUpButtons.Update;
			}
            else if (this.LatestVersion > this.CurrentVersion) {
                title = this.OptionalMessage.Title;
                message = this.OptionalMessage.Message;
                buttons = ManUpButtons.OkAndUpdate;
            }

            title = title.Replace("{{app}}", this.AppName);
            message = message.Replace("{{app}}", this.AppName);
			
			if (!string.IsNullOrEmpty(message)) {
				CurrentState = ManUpState.Invalid;
				StartCoroutine(ShowUI());
				ShowMessage(title, message, buttons);
			}
			else {
				CurrentState = ManUpState.Valid;
				StopAllCoroutines();
				StartCoroutine(HideUI());
			}
			
			yield return null;
		}
		
#endregion
		
		void OpenUpdateURL()
		{
			LogToFile(string.Format("Opening URL {0}", this.UpdateLink));
			Application.OpenURL(this.UpdateLink);
		}
		
        void FlipKillswitch()
		{
			LogToFile("Killswitch flipped");
			
			if (this.MinimumVersion > this.CurrentVersion) {
				OpenUpdateURL();
			}
			
			Application.Quit();
		}
	}
}

