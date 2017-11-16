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

    public class ManUp : MonoBehaviour
    {
        public static ManUp Instance {
            get;
            private set;
        }

        [Header("Configuration")]
        [SerializeField] string ConfigURL = @"";

        private bool triggerCheck = false;
        [SerializeField] bool logToFile = true;

        [Range(0.1f, 24.0f)]
        [SerializeField]
        double HoursBeforeStale;
        [SerializeField] bool ShowVersionInRelease = false;

        [Header("UI Setup")]
        [SerializeField] Button OKButton;
        [SerializeField] Button UpdateButton;
        [SerializeField] Text MessageText;
        [SerializeField] Text VersionText;
        [SerializeField] GameObject UIPanel;

        [SerializeField] UnityEvent onCompletion;

        [Header("Debug Options")]
        [SerializeField] string versionOverride = "1.0";
        [SerializeField] RuntimePlatform platformOverride;

        [SerializeField]
        [TextArea(5, 10)]
        string configOverride;

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

        public string MaintenanceMessage
        {
            get;
            private set;
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

        #endregion

        #region JSON Keys

        static readonly string maintenanceModeKey = "maintenanceMode";
        static readonly string maintenanceMessageKey = "maintenanceMessage";
        static readonly string appUpdateLinkKey = "appUpdateLink";
        static readonly string appVersionLatestKey = "appVersionLatest";
        static readonly string appVersionMinKey = "appVersionMin";

        static readonly string platformAndroidKey = "android";
        static readonly string platformIOSKey = "ios";
        static readonly string platformOSXKey = "osx";
        static readonly string platformLinuxKey = "linux";
        static readonly string platformWindowsKey = "windows";

        #endregion

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
        }

        void Start()
        {
            if (Instance != null) {
                Debug.LogWarningFormat("Multiple ManUp instances detected. Deleting instance {0}", gameObject.name);
                Destroy(this.gameObject);
            }

            Instance = this;
            DontDestroyOnLoad(this.gameObject);

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
                if (string.IsNullOrEmpty(this.configOverride) == false) {
                    ParseConfigJSON(this.configOverride);
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

        #region UI

        void SetupUI()
        {
            try
            {
                LogToFile("Setting up UI");

                LogToFile(string.Format("Application version is '{0}'", Application.version));

                this.CurrentVersion = new Version(Application.version);

#if UNITY_EDITOR
                if (this.versionOverride != null)
                {
                    this.CurrentVersion = new Version(this.versionOverride);
                }
#endif

                LogToFile(string.Format("Current version is {0}", this.CurrentVersion.ToString()));

                this.VersionText.gameObject.SetActive(Debug.isDebugBuild || this.ShowVersionInRelease);
                this.VersionText.text = this.CurrentVersion.ToString();
                this.UpdateButton.onClick.AddListener(UpdateClicked);
                this.OKButton.onClick.AddListener(OkClicked);

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

            ShowMessage("Please wait...", ManUpButtons.None);
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

        void ShowMessage(string message, ManUpButtons buttons)
        {
            this.MessageText.text = message;

            switch (buttons)
            {
                case ManUpButtons.None:
                    this.OKButton.gameObject.SetActive(false);
                    this.UpdateButton.gameObject.SetActive(false);

                    break;

                case ManUpButtons.Ok:
                    this.OKButton.gameObject.SetActive(true);
                    this.UpdateButton.gameObject.SetActive(false);

                    RectTransform rect = this.OKButton.GetComponent<RectTransform>();
                    Vector2 anchorMax = rect.anchorMax;
                    anchorMax.x = 1;
                    rect.anchorMax = anchorMax;

                    break;

                case ManUpButtons.OkAndUpdate:
                    this.OKButton.gameObject.SetActive(true);
                    this.UpdateButton.gameObject.SetActive(true);

                    break;
            }
        }

        void UpdateClicked()
        {
            LogToFile("Update Clicked");
            OpenUpdateURL();
            FlipKillswitch();
        }

        void OkClicked()
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

        void ParseConfigJSON(string configJSON)
        {
            LogToFile("Parsing config");
            CurrentState = ManUpState.Processing;
            JSONNode rootNode = JSON.Parse(configJSON);

            this.MaintenanceMode = rootNode[maintenanceModeKey].AsBool;
            this.MaintenanceMessage = rootNode[maintenanceMessageKey];

            JSONNode appUpdateLinkNode = rootNode[appUpdateLinkKey];
            JSONNode appVersionCurrentNode = rootNode[appVersionLatestKey];
            JSONNode appVersionMinNode = rootNode[appVersionMinKey];

            string platformKey = "";
            RuntimePlatform platform = Application.platform;

#if UNITY_EDITOR
            platform = this.platformOverride;
#endif

            switch (Application.platform) {
				
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
					
					break;
			}
			
			this.UpdateLink = appUpdateLinkNode[platformKey];
			this.LatestVersion = new Version(appVersionCurrentNode[platformKey]);
			this.MinimumVersion = new Version(appVersionMinNode[platformKey]);
			
			StartCoroutine(CheckConfig());
		}
		
		IEnumerator CheckConfig()
		{
			CurrentState = ManUpState.Checking;
			string message = "";
			ManUpButtons buttons = ManUpButtons.Ok;
			
			if (this.MaintenanceMode) {
				message = this.MaintenanceMessage;
			}
			else if (this.MinimumVersion > this.CurrentVersion) {
				message = string.Format("There is a mandatory update for {0}, please update to continue.", this.AppName);
			}
			else if (this.LatestVersion > this.CurrentVersion) {
				message = string.Format("There is a new update for {0}.", this.AppName);
				buttons = ManUpButtons.OkAndUpdate;
			}
			
			if (!string.IsNullOrEmpty(message)) {
				CurrentState = ManUpState.Invalid;
				StartCoroutine(ShowUI());
				ShowMessage(message, buttons);
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

