using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fusion;
using Fusion.Sockets;
using PlayFab;
using UnityEngine;
using UnityEngine.SceneManagement;
using GorillaLocomotion;

using PFModels = PlayFab.ClientModels;
using FusionRealtime = Fusion.Photon.Realtime;

namespace GorillaNetworking
{
    [Serializable]
    public class PlayerCustomData
    {
        public Color playerColor = Color.white;
        public string headCosmetic = "";
        public string leftHandCosmetic = "";
        public string rightHandCosmetic = "";
        public string bodyCosmetic = "";
        public string faceCosmetic = "";
    }

    public class NetworkManager : MonoBehaviour, INetworkRunnerCallbacks
    {
        public static NetworkManager Instance { get; private set; }

        [Header("Player")]
        [SerializeField] private NetworkPrefabRef playerPrefab;
        [SerializeField] private Transform spawnPoint;

        [Header("Customization")]
        [SerializeField] private string playerName = "Player";
        [SerializeField] private Color playerColor = Color.white;
        [SerializeField] private string headCosmetic = "";
        [SerializeField] private string leftHandCosmetic = "";
        [SerializeField] private string rightHandCosmetic = "";
        [SerializeField] private string bodyCosmetic = "";
        [SerializeField] private string faceCosmetic = "";

        [Header("PlayFab")]
        [Tooltip("Leave empty if the Title ID is already set in PlayFabSharedSettings.")]
        [SerializeField] private string titleIdOverride = "";
        [SerializeField] private string customDataKey = "PlayerData";
        [Tooltip("If the user has no saved custom data yet, save the current customization as their defaults.")]
        [SerializeField] private bool saveDefaultsIfMissing = true;
        [Tooltip("Automatically save color/cosmetic changes to PlayFab (debounced).")]
        [SerializeField] private bool autoSaveCustomization = true;
        [SerializeField] private float autoSaveDelaySeconds = 2f;
        [SerializeField] private int maxRetries = 3;
        [SerializeField] private float retryDelaySeconds = 2f;
        [Tooltip("A Fusion auth token older than this is refreshed right before joining a room.")]
        [SerializeField] private float fusionTokenMaxAgeSeconds = 120f;

        [Header("Username Rules")]
        [SerializeField] private int minUsernameLength = 3;
        [SerializeField] private int maxUsernameLength = 25;

        public string PlayerName => playerName;
        public Color PlayerColor => playerColor;
        public string HeadCosmetic => headCosmetic;
        public string LeftHandCosmetic => leftHandCosmetic;
        public string RightHandCosmetic => rightHandCosmetic;
        public string BodyCosmetic => bodyCosmetic;
        public string FaceCosmetic => faceCosmetic;

        public event Action CustomizationChanged;

        public Player GorillaPlayer { get; private set; }
        public GorillaFingers LeftGorillaFingers { get; private set; }
        public GorillaFingers RightGorillaFingers { get; private set; }
        public NetworkRig LocalNetworkRig => localNetworkRig;

        [Header("Session")]
        [SerializeField] private int maxPlayers = 10;

        private GameObject offlineRigObject;
        private NetworkObject localPlayerObject;
        private NetworkRig localNetworkRig;

        public NetworkRunner Runner { get; private set; }
        public bool IsConnected { get; private set; }
        public bool IsBusy => isStarting;

        private bool isStarting;
        private bool spawnAttempted;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            IsConnected = false;
            isStarting = false;
            spawnAttempted = false;
            localPlayerObject = null;
            localNetworkRig = null;

            playerName = SanitizeName(playerName);

            SetOfflineRigVisible(true);
        }

        private void Start()
        {
            Login();
        }

        private void Update()
        {
            if (GorillaPlayer == null ||
                LeftGorillaFingers == null ||
                RightGorillaFingers == null)
            {
                RefreshGorillaReferences();
            }

            if (customDataDirty)
            {
                saveCountdown -= Time.unscaledDeltaTime;

                if (saveCountdown <= 0f)
                    SaveCustomData();
            }
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused && customDataDirty)
                SaveCustomData();
        }

        private static string SanitizeName(string name)
        {
            return string.IsNullOrWhiteSpace(name) ? "Player" : name.Trim();
        }

        private void NotifyCustomizationChanged()
        {
            CustomizationChanged?.Invoke();

            if (autoSaveCustomization && IsLoggedIn)
            {
                customDataDirty = true;
                saveCountdown = autoSaveDelaySeconds;
            }
        }

        public void SetPlayerName(string newName)
        {
            playerName = SanitizeName(newName);
            NotifyCustomizationChanged();
        }

        public void SetPlayerColor(Color color)
        {
            playerColor = color;
            NotifyCustomizationChanged();
        }

        public void SetHeadCosmetic(string id)
        {
            headCosmetic = id ?? "";
            NotifyCustomizationChanged();
        }

        public void SetLeftHandCosmetic(string id)
        {
            leftHandCosmetic = id ?? "";
            NotifyCustomizationChanged();
        }

        public void SetRightHandCosmetic(string id)
        {
            rightHandCosmetic = id ?? "";
            NotifyCustomizationChanged();
        }

        public void SetBodyCosmetic(string id)
        {
            bodyCosmetic = id ?? "";
            NotifyCustomizationChanged();
        }

        public void SetFaceCosmetic(string id)
        {
            faceCosmetic = id ?? "";
            NotifyCustomizationChanged();
        }

        public void SetCosmetics(
            string headId,
            string leftHandId,
            string rightHandId,
            string bodyId,
            string faceId)
        {
            if (headId != null)
                headCosmetic = headId;

            if (leftHandId != null)
                leftHandCosmetic = leftHandId;

            if (rightHandId != null)
                rightHandCosmetic = rightHandId;

            if (bodyId != null)
                bodyCosmetic = bodyId;

            if (faceId != null)
                faceCosmetic = faceId;

            NotifyCustomizationChanged();
        }

        public void RegisterOfflineRig(GameObject rig)
        {
            if (rig == null)
                return;

            offlineRigObject = rig;

            SetOfflineRigVisible(!IsConnected || localNetworkRig == null);

            Debug.Log(
                $"[GorillaNetworking] Registered offline rig: {rig.name}"
            );
        }

        private void SetOfflineRigVisible(bool visible)
        {
            if (offlineRigObject == null)
                return;

            if (offlineRigObject.activeSelf != visible)
                offlineRigObject.SetActive(visible);

            Debug.Log(
                $"[GorillaNetworking] Offline rig visibility: {visible}"
            );
        }

        public void RefreshGorillaReferences()
        {
            Player player = Player.Instance;

            if (player == null)
                return;

            GorillaPlayer = player;

            GorillaFingers[] fingers =
                player.GetComponentsInChildren<GorillaFingers>(true);

            foreach (GorillaFingers finger in fingers)
            {
                if (finger == null)
                    continue;

                if (finger.UseLeftHand)
                    LeftGorillaFingers = finger;
                else
                    RightGorillaFingers = finger;
            }
        }

        #region PlayFab + Fusion Auth

        public bool IsReady { get; private set; }
        public bool IsLoggedIn { get; private set; }
        public bool IsLoggingIn { get; private set; }
        public bool NewlyCreated { get; private set; }

        public string PlayFabId { get; private set; }
        public string Username { get; private set; } = "";
        public string CustomId { get; private set; }

        public List<PFModels.ItemInstance> Inventory { get; private set; } = new List<PFModels.ItemInstance>();
        public Dictionary<string, int> VirtualCurrency { get; private set; } = new Dictionary<string, int>();
        public PlayerCustomData CustomData { get; private set; } = new PlayerCustomData();

        public string FusionToken { get; private set; }

        public event Action OnLoginSuccess;
        public event Action<string> OnLoginFailed;
        public event Action OnInventoryUpdated;
        public event Action OnCustomDataUpdated;
        public event Action<string> OnUsernameChanged;
        public event Action OnFusionAuthRefreshed;

        private int _attempt;
        private float fusionTokenIssuedAt;
        private bool customDataDirty;
        private float saveCountdown;
        private TaskCompletionSource<bool> loginTcs = new TaskCompletionSource<bool>();

        public static void RunWhenReady(Action action)
        {
            if (action == null)
                return;

            if (Instance == null)
            {
                Debug.LogWarning("[GorillaNetworking] RunWhenReady called but no NetworkManager exists.");
                return;
            }

            if (Instance.IsReady)
            {
                action();
                return;
            }

            void Handler()
            {
                if (Instance != null)
                    Instance.OnLoginSuccess -= Handler;

                action();
            }

            Instance.OnLoginSuccess += Handler;
        }

        public Task<bool> WaitForLoginAsync()
        {
            return IsReady ? Task.FromResult(true) : loginTcs.Task;
        }

        private void CompleteLogin(bool success)
        {
            TaskCompletionSource<bool> finished = loginTcs;

            if (!success)
                loginTcs = new TaskCompletionSource<bool>();

            finished.TrySetResult(success);
        }

        public void Login()
        {
            if (IsLoggingIn || IsReady)
                return;

            if (!string.IsNullOrEmpty(titleIdOverride))
                PlayFabSettings.staticSettings.TitleId = titleIdOverride;

            if (string.IsNullOrEmpty(PlayFabSettings.staticSettings.TitleId))
            {
                Fail("PlayFab Title ID is not set. Set it in PlayFabSharedSettings or on the NetworkManager.");
                return;
            }

            IsLoggingIn = true;
            _attempt = 0;
            CustomId = GetCustomId();

            SendLoginRequest();
        }

        private void SendLoginRequest()
        {
            var request = new PFModels.LoginWithCustomIDRequest
            {
                CustomId = CustomId,
                CreateAccount = true,
                InfoRequestParameters = BuildInfoParams()
            };

            PlayFabClientAPI.LoginWithCustomID(request, OnLoginResult, OnLoginError);
        }

        private static string GetCustomId()
        {
            string id = SystemInfo.deviceUniqueIdentifier;

            if (string.IsNullOrEmpty(id) || id == SystemInfo.unsupportedIdentifier)
            {
                const string key = "PF_FallbackCustomId";
                id = PlayerPrefs.GetString(key, "");

                if (string.IsNullOrEmpty(id))
                {
                    id = Guid.NewGuid().ToString("N");
                    PlayerPrefs.SetString(key, id);
                    PlayerPrefs.Save();
                }
            }

            return id;
        }

        private static PFModels.GetPlayerCombinedInfoRequestParams BuildInfoParams()
        {
            return new PFModels.GetPlayerCombinedInfoRequestParams
            {
                GetUserAccountInfo = true,
                GetUserInventory = true,
                GetUserVirtualCurrency = true,
                GetUserData = true,
                GetPlayerProfile = true,
                ProfileConstraints = new PFModels.PlayerProfileViewConstraints { ShowDisplayName = true }
            };
        }

        private void OnLoginResult(PFModels.LoginResult result)
        {
            IsLoggedIn = true;
            _attempt = 0;

            PlayFabId = result.PlayFabId;
            NewlyCreated = result.NewlyCreated;

            ApplyPayload(result.InfoResultPayload);

            Debug.Log(
                $"[GorillaNetworking] PlayFab logged in. PlayFabId={PlayFabId} " +
                $"Name='{Username}' New={NewlyCreated} Items={Inventory.Count}"
            );

            RequestFusionToken(null);
        }

        private void OnLoginError(PlayFabError error)
        {
            Debug.LogWarning($"[GorillaNetworking] PlayFab login failed: {error.GenerateErrorReport()}");

            if (IsRetryable(error) && _attempt < maxRetries)
            {
                _attempt++;
                Invoke(nameof(SendLoginRequest), retryDelaySeconds * _attempt);
                return;
            }

            Fail($"PlayFab login failed: {error.ErrorMessage} ({error.Error})");
        }

        private static bool IsRetryable(PlayFabError error)
        {
            return error.Error == PlayFabErrorCode.ConnectionError
                   || error.Error == PlayFabErrorCode.ServiceUnavailable
                   || error.Error == PlayFabErrorCode.APIClientRequestRateLimitExceeded
                   || error.HttpCode >= 500;
        }

        private void Fail(string message)
        {
            IsReady = false;
            IsLoggingIn = false;

            Debug.LogError($"[GorillaNetworking] {message}");

            CompleteLogin(false);
            OnLoginFailed?.Invoke(message);
        }

        public void RefreshFusionAuth(Action<bool> onDone = null)
        {
            if (!IsLoggedIn)
            {
                onDone?.Invoke(false);
                return;
            }

            RequestFusionToken(onDone);
        }

        private Task<bool> RefreshFusionAuthAsync()
        {
            var tcs = new TaskCompletionSource<bool>();
            RefreshFusionAuth(ok => tcs.TrySetResult(ok));
            return tcs.Task;
        }

        private void RequestFusionToken(Action<bool> onDone)
        {
            string appId = FusionRealtime.PhotonAppSettings.Global.AppSettings.AppIdFusion;

            if (string.IsNullOrEmpty(appId))
            {
                const string message = "Fusion App Id is empty. Set it in the Fusion Hub / Photon App Settings.";

                if (!IsReady)
                    Fail(message);
                else
                    Debug.LogError($"[GorillaNetworking] {message}");

                onDone?.Invoke(false);
                return;
            }

            PlayFabClientAPI.GetPhotonAuthenticationToken(
                new PFModels.GetPhotonAuthenticationTokenRequest { PhotonApplicationId = appId },
                result =>
                {
                    FusionToken = result.PhotonCustomAuthenticationToken;
                    fusionTokenIssuedAt = Time.realtimeSinceStartup;
                    _attempt = 0;

                    OnFusionAuthRefreshed?.Invoke();
                    onDone?.Invoke(true);

                    if (!IsReady)
                    {
                        IsReady = true;
                        IsLoggingIn = false;

                        Debug.Log("[GorillaNetworking] Fusion auth ready. Login complete.");

                        CompleteLogin(true);
                        OnLoginSuccess?.Invoke();
                    }
                },
                error =>
                {
                    Debug.LogWarning($"[GorillaNetworking] Fusion token request failed: {error.GenerateErrorReport()}");

                    if (!IsReady)
                    {
                        if (IsRetryable(error) && _attempt < maxRetries)
                        {
                            _attempt++;
                            Invoke(nameof(RetryInitialFusionToken), retryDelaySeconds * _attempt);
                            return;
                        }

                        Fail($"Fusion auth failed: {error.ErrorMessage} ({error.Error})");
                    }

                    onDone?.Invoke(false);
                });
        }

        private void RetryInitialFusionToken()
        {
            RequestFusionToken(null);
        }

        private void ApplyFusionAuth(ref StartGameArgs args)
        {
            args.AuthValues = new();

            var auth = args.AuthValues;
            auth.AuthType = default;
            auth.UserId = PlayFabId;
            auth.AddAuthParameter("username", PlayFabId);
            auth.AddAuthParameter("token", FusionToken);
        }

        private async Task<bool> EnsureReadyForJoinAsync()
        {
            if (!IsReady)
            {
                Debug.Log("[GorillaNetworking] Waiting for PlayFab / Fusion login before joining...");

                Task<bool> waitTask = WaitForLoginAsync();

                if (!IsLoggingIn)
                    Login();

                if (!await waitTask)
                {
                    Debug.LogError("[GorillaNetworking] Can't join a room: PlayFab / Fusion login failed.");
                    return false;
                }

                return true;
            }

            if (Time.realtimeSinceStartup - fusionTokenIssuedAt > fusionTokenMaxAgeSeconds)
            {
                if (!await RefreshFusionAuthAsync())
                {
                    Debug.LogError("[GorillaNetworking] Can't join a room: couldn't refresh Fusion auth.");
                    return false;
                }
            }

            return true;
        }

        public void RefreshAll(Action<bool> onDone = null)
        {
            if (!IsLoggedIn)
            {
                onDone?.Invoke(false);
                return;
            }

            var request = new PFModels.GetPlayerCombinedInfoRequest
            {
                PlayFabId = PlayFabId,
                InfoRequestParameters = BuildInfoParams()
            };

            PlayFabClientAPI.GetPlayerCombinedInfo(request,
                result =>
                {
                    ApplyPayload(result.InfoResultPayload);
                    onDone?.Invoke(true);
                },
                error =>
                {
                    Debug.LogWarning($"[GorillaNetworking] PlayFab refresh failed: {error.GenerateErrorReport()}");
                    onDone?.Invoke(false);
                });
        }

        public void RefreshInventory(Action<bool> onDone = null)
        {
            if (!IsLoggedIn)
            {
                onDone?.Invoke(false);
                return;
            }

            PlayFabClientAPI.GetUserInventory(new PFModels.GetUserInventoryRequest(),
                result =>
                {
                    ApplyInventory(result.Inventory, result.VirtualCurrency);
                    onDone?.Invoke(true);
                },
                error =>
                {
                    Debug.LogWarning($"[GorillaNetworking] Inventory refresh failed: {error.GenerateErrorReport()}");
                    onDone?.Invoke(false);
                });
        }

        public void RefreshCustomData(Action<bool> onDone = null)
        {
            if (!IsLoggedIn)
            {
                onDone?.Invoke(false);
                return;
            }

            PlayFabClientAPI.GetUserData(new PFModels.GetUserDataRequest { Keys = new List<string> { customDataKey } },
                result =>
                {
                    ApplyUserData(result.Data);
                    onDone?.Invoke(true);
                },
                error =>
                {
                    Debug.LogWarning($"[GorillaNetworking] Custom data refresh failed: {error.GenerateErrorReport()}");
                    onDone?.Invoke(false);
                });
        }

        private void ApplyPayload(PFModels.GetPlayerCombinedInfoResultPayload payload)
        {
            if (payload == null)
                return;

            Username = payload.PlayerProfile?.DisplayName
                       ?? payload.AccountInfo?.TitleInfo?.DisplayName
                       ?? payload.AccountInfo?.Username
                       ?? "";

            if (!string.IsNullOrWhiteSpace(Username))
                playerName = SanitizeName(Username);

            ApplyInventory(payload.UserInventory, payload.UserVirtualCurrency);
            ApplyUserData(payload.UserData);
        }

        private void ApplyInventory(List<PFModels.ItemInstance> items, Dictionary<string, int> currency)
        {
            Inventory = items ?? new List<PFModels.ItemInstance>();
            VirtualCurrency = currency ?? new Dictionary<string, int>();
            OnInventoryUpdated?.Invoke();
        }

        public bool HasItem(string itemId)
        {
            for (int i = 0; i < Inventory.Count; i++)
            {
                if (Inventory[i].ItemId == itemId)
                    return true;
            }

            return false;
        }

        public int GetCurrency(string code)
        {
            return VirtualCurrency.TryGetValue(code, out int amount) ? amount : 0;
        }

        private void ApplyUserData(Dictionary<string, PFModels.UserDataRecord> data)
        {
            bool keyPresent = data != null
                              && data.TryGetValue(customDataKey, out PFModels.UserDataRecord record)
                              && !string.IsNullOrEmpty(record.Value);

            if (keyPresent)
            {
                try
                {
                    CustomData = JsonUtility.FromJson<PlayerCustomData>(data[customDataKey].Value)
                                 ?? new PlayerCustomData();

                    ApplyCustomDataToFields();
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[GorillaNetworking] Failed to parse '{customDataKey}': {e.Message}");
                }
            }
            else
            {
                SyncCustomDataFromFields();

                if (saveDefaultsIfMissing)
                    SaveCustomData();
            }

            CustomizationChanged?.Invoke();
            OnCustomDataUpdated?.Invoke();
        }

        private void ApplyCustomDataToFields()
        {
            playerColor = CustomData.playerColor;
            headCosmetic = CustomData.headCosmetic ?? "";
            leftHandCosmetic = CustomData.leftHandCosmetic ?? "";
            rightHandCosmetic = CustomData.rightHandCosmetic ?? "";
            bodyCosmetic = CustomData.bodyCosmetic ?? "";
            faceCosmetic = CustomData.faceCosmetic ?? "";
        }

        private void SyncCustomDataFromFields()
        {
            CustomData.playerColor = playerColor;
            CustomData.headCosmetic = headCosmetic;
            CustomData.leftHandCosmetic = leftHandCosmetic;
            CustomData.rightHandCosmetic = rightHandCosmetic;
            CustomData.bodyCosmetic = bodyCosmetic;
            CustomData.faceCosmetic = faceCosmetic;
        }

        public void SaveCustomData(Action<bool> onDone = null)
        {
            customDataDirty = false;

            if (!IsLoggedIn)
            {
                onDone?.Invoke(false);
                return;
            }

            SyncCustomDataFromFields();

            var request = new PFModels.UpdateUserDataRequest
            {
                Data = new Dictionary<string, string>
                {
                    { customDataKey, JsonUtility.ToJson(CustomData) }
                }
            };

            PlayFabClientAPI.UpdateUserData(request,
                _ => onDone?.Invoke(true),
                error =>
                {
                    Debug.LogWarning($"[GorillaNetworking] Custom data save failed: {error.GenerateErrorReport()}");
                    onDone?.Invoke(false);
                });
        }

        public void ChangeUsername(string newName, Action<bool, string> onDone = null)
        {
            if (!IsLoggedIn)
            {
                onDone?.Invoke(false, "Not logged in.");
                return;
            }

            newName = newName?.Trim();

            if (!ValidateUsername(newName, out string reason))
            {
                onDone?.Invoke(false, reason);
                return;
            }

            if (newName == Username)
            {
                onDone?.Invoke(true, null);
                return;
            }

            PlayFabClientAPI.UpdateUserTitleDisplayName(
                new PFModels.UpdateUserTitleDisplayNameRequest { DisplayName = newName },
                result =>
                {
                    Username = result.DisplayName;
                    playerName = SanitizeName(Username);

                    CustomizationChanged?.Invoke();
                    OnUsernameChanged?.Invoke(Username);
                    onDone?.Invoke(true, null);
                },
                error =>
                {
                    Debug.LogWarning($"[GorillaNetworking] Username change failed: {error.GenerateErrorReport()}");
                    onDone?.Invoke(false, GetFriendlyNameError(error));
                });
        }

        public bool ValidateUsername(string name, out string reason)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                reason = "Username can't be empty.";
                return false;
            }

            if (name.Length < minUsernameLength || name.Length > maxUsernameLength)
            {
                reason = $"Username must be {minUsernameLength}-{maxUsernameLength} characters.";
                return false;
            }

            reason = null;
            return true;
        }

        private string GetFriendlyNameError(PlayFabError error)
        {
            switch (error.Error)
            {
                case PlayFabErrorCode.NameNotAvailable:
                    return "That username is already taken.";
                case PlayFabErrorCode.ProfaneDisplayName:
                    return "That username isn't allowed.";
                case PlayFabErrorCode.InvalidParams:
                    return $"Username must be {minUsernameLength}-{maxUsernameLength} characters.";
                default:
                    return string.IsNullOrEmpty(error.ErrorMessage)
                        ? "Couldn't change username."
                        : error.ErrorMessage;
            }
        }

        #endregion

        public async Task JoinRoom(string roomName)
        {
            if (isStarting || IsConnected)
                return;

            if (string.IsNullOrWhiteSpace(roomName))
            {
                Debug.LogError(
                    "[GorillaNetworking] Cannot join a room with an empty name."
                );
                return;
            }

            isStarting = true;
            NetworkRunner newRunner = null;

            try
            {
                if (!await EnsureReadyForJoinAsync())
                {
                    SetOfflineRigVisible(true);
                    return;
                }

                if (Runner != null)
                {
                    NetworkRunner oldRunner = Runner;
                    Runner = null;
                    IsConnected = false;
                    localPlayerObject = null;
                    localNetworkRig = null;
                    spawnAttempted = false;

                    await oldRunner.Shutdown();
                    Destroy(oldRunner.gameObject);
                }

                GameObject runnerObject = new GameObject("NetworkRunner");

                newRunner = runnerObject.AddComponent<NetworkRunner>();

                Runner = newRunner;
                newRunner.ProvideInput = true;
                newRunner.AddCallbacks(this);

                runnerObject.AddComponent<NetworkSceneManagerDefault>();

                Debug.Log(
                    $"[GorillaNetworking] Joining room '{roomName}'..."
                );

                StartGameArgs startArgs = new StartGameArgs
                {
                    GameMode = GameMode.Shared,
                    SessionName = roomName,
                    PlayerCount = maxPlayers
                };

                ApplyFusionAuth(ref startArgs);

                StartGameResult result = await newRunner.StartGame(startArgs);

                if (!result.Ok)
                {
                    Debug.LogError(
                        "[GorillaNetworking] Failed to join room: " +
                        result.ShutdownReason
                    );

                    if (Runner == newRunner)
                        Runner = null;

                    IsConnected = false;
                    localPlayerObject = null;
                    localNetworkRig = null;
                    spawnAttempted = false;

                    await newRunner.Shutdown();
                    Destroy(runnerObject);

                    SetOfflineRigVisible(true);
                    return;
                }

                IsConnected = true;

                Debug.Log(
                    $"[GorillaNetworking] Joined room '{roomName}'. " +
                    $"Local player: {newRunner.LocalPlayer}"
                );

                TrySpawnLocalPlayer(newRunner);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "[GorillaNetworking] Error while joining room."
                );
                Debug.LogException(exception);

                if (Runner == newRunner)
                    Runner = null;

                IsConnected = false;
                localPlayerObject = null;
                localNetworkRig = null;
                spawnAttempted = false;

                if (newRunner != null)
                {
                    try
                    {
                        await newRunner.Shutdown();
                    }
                    catch (Exception shutdownException)
                    {
                        Debug.LogException(shutdownException);
                    }

                    Destroy(newRunner.gameObject);
                }

                SetOfflineRigVisible(true);
            }
            finally
            {
                isStarting = false;
            }
        }

        public async void JoinRandomPublicRoom()
        {
            if (isStarting || IsConnected)
                return;

            isStarting = true;
            NetworkRunner newRunner = null;

            try
            {
                if (!await EnsureReadyForJoinAsync())
                {
                    SetOfflineRigVisible(true);
                    return;
                }

                GameObject runnerObject = new GameObject("NetworkRunner");
                newRunner = runnerObject.AddComponent<NetworkRunner>();

                Runner = newRunner;
                newRunner.ProvideInput = true;
                newRunner.AddCallbacks(this);

                runnerObject.AddComponent<NetworkSceneManagerDefault>();

                Debug.Log(
                    "[GorillaNetworking] Searching for a public room..."
                );

                StartGameArgs startArgs = new StartGameArgs
                {
                    GameMode = GameMode.Shared,
                    SessionName = null,
                    PlayerCount = maxPlayers,
                    EnableClientSessionCreation = true
                };

                ApplyFusionAuth(ref startArgs);

                StartGameResult result = await newRunner.StartGame(startArgs);

                if (!result.Ok)
                {
                    Debug.LogError(
                        "[GorillaNetworking] Failed to join a public room: " +
                        result.ShutdownReason
                    );

                    if (Runner == newRunner)
                        Runner = null;

                    IsConnected = false;
                    localPlayerObject = null;
                    localNetworkRig = null;
                    spawnAttempted = false;

                    await newRunner.Shutdown();
                    Destroy(runnerObject);

                    SetOfflineRigVisible(true);
                    return;
                }

                IsConnected = true;

                Debug.Log(
                    $"[GorillaNetworking] Joined public session: " +
                    $"{newRunner.SessionInfo.Name}"
                );

                TrySpawnLocalPlayer(newRunner);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "[GorillaNetworking] Error joining a public room."
                );
                Debug.LogException(exception);

                if (Runner == newRunner)
                    Runner = null;

                IsConnected = false;
                localPlayerObject = null;
                localNetworkRig = null;
                spawnAttempted = false;

                if (newRunner != null)
                {
                    try
                    {
                        await newRunner.Shutdown();
                    }
                    catch (Exception shutdownException)
                    {
                        Debug.LogException(shutdownException);
                    }

                    Destroy(newRunner.gameObject);
                }

                SetOfflineRigVisible(true);
            }
            finally
            {
                isStarting = false;
            }
        }

        public async void LeaveRoom()
        {
            if (isStarting)
                return;

            isStarting = true;

            try
            {
                NetworkRunner oldRunner = Runner;

                Runner = null;
                IsConnected = false;
                localPlayerObject = null;
                localNetworkRig = null;
                spawnAttempted = false;

                if (oldRunner != null)
                {
                    await oldRunner.Shutdown();
                    Destroy(oldRunner.gameObject);
                }

                SetOfflineRigVisible(true);

                Debug.Log(
                    "[GorillaNetworking] Left room. Offline rig restored."
                );
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                SetOfflineRigVisible(true);
            }
            finally
            {
                isStarting = false;
            }
        }

        public void StartOfflineMode()
        {
            if (isStarting)
                return;

            if (Runner != null)
            {
                LeaveRoom();
                return;
            }

            IsConnected = false;
            localNetworkRig = null;
            localPlayerObject = null;
            spawnAttempted = false;

            SetOfflineRigVisible(true);

            Debug.Log("[GorillaNetworking] Offline mode active.");
        }

        private void TrySpawnLocalPlayer(NetworkRunner runner)
        {
            if (runner == null ||
                runner != Runner ||
                !runner.IsRunning)
            {
                return;
            }

            PlayerRef localPlayer = runner.LocalPlayer;

            if (localPlayer == PlayerRef.None)
            {
                Debug.Log(
                    "[GorillaNetworking] Local PlayerRef is not ready yet."
                );
                return;
            }

            SpawnLocalPlayer(runner, localPlayer);
        }

        private void SpawnLocalPlayer(
            NetworkRunner runner,
            PlayerRef player)
        {
            if (runner == null ||
                runner != Runner ||
                !runner.IsRunning ||
                player != runner.LocalPlayer)
            {
                return;
            }

            if (localPlayerObject != null || spawnAttempted)
                return;

            if (!playerPrefab.IsValid)
            {
                Debug.LogError(
                    "[GorillaNetworking] playerPrefab is invalid. " +
                    "Assign the NetworkPlayer prefab and register it " +
                    "in Fusion's Network Project Config."
                );
                return;
            }

            spawnAttempted = true;

            Vector3 position = spawnPoint != null
                ? spawnPoint.position
                : Vector3.zero;

            Quaternion rotation = spawnPoint != null
                ? spawnPoint.rotation
                : Quaternion.identity;

            Debug.Log(
                $"[GorillaNetworking] Spawning local player at {position}..."
            );

            try
            {
                NetworkObject spawnedObject = runner.Spawn(
                    playerPrefab,
                    position,
                    rotation
                );

                if (spawnedObject == null)
                {
                    spawnAttempted = false;

                    Debug.LogError(
                        "[GorillaNetworking] Runner.Spawn returned null."
                    );
                    return;
                }

                localPlayerObject = spawnedObject;
                runner.SetPlayerObject(player, spawnedObject);

                Debug.Log(
                    $"[GorillaNetworking] Spawned '{spawnedObject.name}'. " +
                    $"State Authority: {spawnedObject.HasStateAuthority}"
                );
            }
            catch (Exception exception)
            {
                spawnAttempted = false;

                Debug.LogError(
                    "[GorillaNetworking] Failed to spawn NetworkPlayer."
                );
                Debug.LogException(exception);
            }
        }

        public void OnPlayerJoined(
            NetworkRunner runner,
            PlayerRef player)
        {
            Debug.Log(
                $"[GorillaNetworking] Player joined: {player}"
            );

            if (runner != Runner)
                return;

            if (player == runner.LocalPlayer)
                SpawnLocalPlayer(runner, player);
        }

        public void OnPlayerLeft(
            NetworkRunner runner,
            PlayerRef player)
        {
            if (runner != Runner)
                return;

            if (player == runner.LocalPlayer)
            {
                localPlayerObject = null;
                localNetworkRig = null;
                spawnAttempted = false;
                SetOfflineRigVisible(true);
            }
        }

        public void NotifyLocalRigReady(NetworkRig rig)
        {
            if (rig == null || rig.Object == null)
                return;

            if (Runner == null ||
                !IsConnected ||
                rig.Object.Runner != Runner ||
                !rig.Object.HasStateAuthority)
            {
                return;
            }

            localNetworkRig = rig;
            localPlayerObject = rig.Object;
            spawnAttempted = true;

            SetOfflineRigVisible(false);

            Debug.Log(
                "[GorillaNetworking] Local network rig initialized. " +
                "Offline rig hidden."
            );
        }

        public void OnShutdown(
            NetworkRunner runner,
            ShutdownReason shutdownReason)
        {
            Debug.Log(
                $"[GorillaNetworking] Network shutdown: {shutdownReason}"
            );

            if (Runner != runner)
                return;

            Runner = null;
            IsConnected = false;
            localPlayerObject = null;
            localNetworkRig = null;
            spawnAttempted = false;

            SetOfflineRigVisible(true);
        }

        public void OnConnectedToServer(NetworkRunner runner)
        {
            Debug.Log("[GorillaNetworking] Connected to session.");

            if (runner == Runner)
                TrySpawnLocalPlayer(runner);
        }

        public void OnDisconnectedFromServer(
            NetworkRunner runner,
            NetDisconnectReason reason)
        {
            Debug.LogWarning(
                $"[GorillaNetworking] Disconnected: {reason}"
            );

            if (Runner != runner)
                return;

            IsConnected = false;
            localPlayerObject = null;
            localNetworkRig = null;
            spawnAttempted = false;

            SetOfflineRigVisible(true);
        }

        public void OnConnectRequest(
            NetworkRunner runner,
            NetworkRunnerCallbackArgs.ConnectRequest request,
            byte[] token)
        {
            request.Accept();
        }

        public void OnConnectFailed(
            NetworkRunner runner,
            NetAddress remoteAddress,
            NetConnectFailedReason reason)
        {
            Debug.LogError(
                $"[GorillaNetworking] Connection failed: {reason}"
            );

            if (Runner != runner)
                return;

            Runner = null;
            IsConnected = false;
            localPlayerObject = null;
            localNetworkRig = null;
            spawnAttempted = false;

            SetOfflineRigVisible(true);
        }

        public void OnInput(
            NetworkRunner runner,
            NetworkInput input) { }

        public void OnInputMissing(
            NetworkRunner runner,
            PlayerRef player,
            NetworkInput input) { }

        public void OnUserSimulationMessage(
            NetworkRunner runner,
            SimulationMessagePtr message) { }

        public void OnSessionListUpdated(
            NetworkRunner runner,
            List<SessionInfo> sessionList) { }

        public void OnCustomAuthenticationResponse(
            NetworkRunner runner,
            Dictionary<string, object> data) { }

        public void OnHostMigration(
            NetworkRunner runner,
            HostMigrationToken hostMigrationToken) { }

        public void OnReliableDataReceived(
            NetworkRunner runner,
            PlayerRef player,
            ReliableKey key,
            ReadOnlySpan<byte> data) { }

        public void OnReliableDataProgress(
            NetworkRunner runner,
            PlayerRef player,
            ReliableKey key,
            float progress) { }

        public void OnSceneLoadDone(NetworkRunner runner) { }

        public void OnSceneLoadStart(NetworkRunner runner) { }

        public void OnObjectExitAOI(
            NetworkRunner runner,
            NetworkObject obj,
            PlayerRef player) { }

        public void OnObjectEnterAOI(
            NetworkRunner runner,
            NetworkObject obj,
            PlayerRef player) { }

        private void OnDestroy()
        {
            loginTcs.TrySetResult(false);

            if (Instance == this)
                Instance = null;
        }
    }
}