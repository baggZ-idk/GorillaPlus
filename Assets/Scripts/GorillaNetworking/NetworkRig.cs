using Fusion;
using UnityEngine;
using TMPro;
using GorillaLocomotion;

namespace GorillaNetworking
{
    public class NetworkRig : NetworkBehaviour
    {
        private enum CosmeticSlot { Head, LeftHand, RightHand, Body, Face }

        [Header("Local Gorilla Player")]
        public Transform headFollower;
        public Transform leftHand;
        public Transform rightHand;

        [Header("Visible Avatar")]
        public Transform remoteHead;
        public Transform remoteLeftHand;
        public Transform remoteRightHand;
        public Transform remoteBody;
        public Transform remoteFace;

        [Header("Motion Smoothing")]
        [Min(0.01f)]
        public float positionSmoothTime = 0.05f;

        [Min(0.01f)]
        public float rotationSmoothSpeed = 20f;

        private Vector3 headVelocity;
        private Vector3 leftHandVelocity;
        private Vector3 rightHandVelocity;

        [Header("Cosmetic Slots")]
        public Transform headCosmeticSlot;
        public Transform leftHandCosmeticSlot;
        public Transform rightHandCosmeticSlot;
        public Transform bodyCosmeticSlot;
        public Transform faceCosmeticSlot;

        [Header("Player Color")]
        [Tooltip("Material that gets tinted. A copy is made at runtime.")]
        public Material playerMaterial;

        [Tooltip("Which material slot on each renderer to replace.")]
        public int materialSlot = 0;

        [Tooltip("Renderers that should use the player's color.")]
        public Renderer[] colorRenderers;

        [Header("Name Tag")]
        public TMP_Text nameTagText;

        [Header("Finger Rigs")]
        public GorillaFingers localLeftFingers;
        public GorillaFingers localRightFingers;
        public GorillaFingers remoteLeftFingers;
        public GorillaFingers remoteRightFingers;

        [Networked] public Vector3 NetworkPlayerColor { get; set; }
        [Networked] public NetworkString<_32> PlayerName { get; set; }
        [Networked] public NetworkString<_32> HeadCosmetic { get; set; }
        [Networked] public NetworkString<_32> LeftHandCosmetic { get; set; }
        [Networked] public NetworkString<_32> RightHandCosmetic { get; set; }
        [Networked] public NetworkString<_32> BodyCosmetic { get; set; }
        [Networked] public NetworkString<_32> FaceCosmetic { get; set; }

        [Networked] private Vector3 HeadPosition { get; set; }
        [Networked] private Quaternion HeadRotation { get; set; }

        [Networked] private Vector3 LeftHandPosition { get; set; }
        [Networked] private Quaternion LeftHandRotation { get; set; }

        [Networked] private Vector3 RightHandPosition { get; set; }
        [Networked] private Quaternion RightHandRotation { get; set; }

        [Networked] public float LeftPinkyCurl { get; set; }
        [Networked] public float LeftIndexCurl { get; set; }
        [Networked] public float LeftThumbCurl { get; set; }

        [Networked] public float RightPinkyCurl { get; set; }
        [Networked] public float RightIndexCurl { get; set; }
        [Networked] public float RightThumbCurl { get; set; }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private string localPlayerName = "Player";
        private Color localPlayerColor = Color.white;
        private string localHeadCosmetic = "";
        private string localLeftHandCosmetic = "";
        private string localRightHandCosmetic = "";
        private string localBodyCosmetic = "";
        private string localFaceCosmetic = "";

        private Material playerMaterialInstance;
        private Color appliedPlayerColor;
        private bool playerColorApplied;

        private string appliedHeadCosmetic;
        private string appliedLeftHandCosmetic;
        private string appliedRightHandCosmetic;
        private string appliedBodyCosmetic;
        private string appliedFaceCosmetic;

        private bool isLocalRig;
        private bool localReferencesReady;
        private bool offlineMode;

        private NetworkManager subscribedManager;

        private bool CanWriteNetworked =>
            !offlineMode &&
            Object != null &&
            Object.IsValid &&
            Object.HasStateAuthority;

        private bool UseLocalValues => offlineMode || isLocalRig;

        private void SubscribeToManager()
        {
            NetworkManager manager = NetworkManager.Instance;

            if (manager == null || subscribedManager == manager)
                return;

            UnsubscribeFromManager();

            subscribedManager = manager;
            subscribedManager.CustomizationChanged += PullCustomization;
        }

        private void UnsubscribeFromManager()
        {
            if (subscribedManager != null)
                subscribedManager.CustomizationChanged -= PullCustomization;

            subscribedManager = null;
        }

        public void PullCustomization()
        {
            if (!isLocalRig)
                return;

            NetworkManager manager = NetworkManager.Instance;

            if (manager == null)
                return;

            WriteName(manager.PlayerName);
            WriteColor(manager.PlayerColor);
            WriteCosmetic(CosmeticSlot.Head, manager.HeadCosmetic);
            WriteCosmetic(CosmeticSlot.LeftHand, manager.LeftHandCosmetic);
            WriteCosmetic(CosmeticSlot.RightHand, manager.RightHandCosmetic);
            WriteCosmetic(CosmeticSlot.Body, manager.BodyCosmetic);
            WriteCosmetic(CosmeticSlot.Face, manager.FaceCosmetic);

            RefreshVisuals();
        }

        private string ReadName()
        {
            if (UseLocalValues)
                return localPlayerName;

            string value = PlayerName.ToString();
            return string.IsNullOrEmpty(value) ? "Player" : value;
        }

        private void WriteName(string value)
        {
            value = SanitizeName(value);
            localPlayerName = value;

            if (CanWriteNetworked)
                PlayerName = value;
        }

        private Color ReadColor()
        {
            if (UseLocalValues)
                return localPlayerColor;

            return new Color(
                NetworkPlayerColor.x,
                NetworkPlayerColor.y,
                NetworkPlayerColor.z,
                1f
            );
        }

        private void WriteColor(Color color)
        {
            localPlayerColor = color;

            if (CanWriteNetworked)
                NetworkPlayerColor = new Vector3(color.r, color.g, color.b);
        }

        private string ReadCosmetic(CosmeticSlot slot)
        {
            if (UseLocalValues)
            {
                switch (slot)
                {
                    case CosmeticSlot.Head:      return localHeadCosmetic ?? "";
                    case CosmeticSlot.LeftHand:  return localLeftHandCosmetic ?? "";
                    case CosmeticSlot.RightHand: return localRightHandCosmetic ?? "";
                    case CosmeticSlot.Body:      return localBodyCosmetic ?? "";
                    case CosmeticSlot.Face:      return localFaceCosmetic ?? "";
                    default:                     return "";
                }
            }

            switch (slot)
            {
                case CosmeticSlot.Head:      return HeadCosmetic.ToString();
                case CosmeticSlot.LeftHand:  return LeftHandCosmetic.ToString();
                case CosmeticSlot.RightHand: return RightHandCosmetic.ToString();
                case CosmeticSlot.Body:      return BodyCosmetic.ToString();
                case CosmeticSlot.Face:      return FaceCosmetic.ToString();
                default:                     return "";
            }
        }

        private void WriteCosmetic(CosmeticSlot slot, string id)
        {
            id ??= "";
            bool net = CanWriteNetworked;

            switch (slot)
            {
                case CosmeticSlot.Head:
                    localHeadCosmetic = id;
                    if (net) HeadCosmetic = id;
                    break;
                case CosmeticSlot.LeftHand:
                    localLeftHandCosmetic = id;
                    if (net) LeftHandCosmetic = id;
                    break;
                case CosmeticSlot.RightHand:
                    localRightHandCosmetic = id;
                    if (net) RightHandCosmetic = id;
                    break;
                case CosmeticSlot.Body:
                    localBodyCosmetic = id;
                    if (net) BodyCosmetic = id;
                    break;
                case CosmeticSlot.Face:
                    localFaceCosmetic = id;
                    if (net) FaceCosmetic = id;
                    break;
            }
        }

        private static string SanitizeName(string name)
        {
            return string.IsNullOrWhiteSpace(name) ? "Player" : name.Trim();
        }

        private void CacheAvatarReferences()
        {
            if (remoteHead == null)
                remoteHead = transform.Find("Head");

            if (remoteLeftHand == null)
                remoteLeftHand = transform.Find("Left Hand");

            if (remoteRightHand == null)
                remoteRightHand = transform.Find("Right Hand");

            if (remoteHead != null)
            {
                if (remoteFace == null)
                    remoteFace = remoteHead.Find("Face");

                if (remoteBody == null)
                    remoteBody = remoteHead.Find("Body");
            }

            if (remoteHead == null ||
                remoteLeftHand == null ||
                remoteRightHand == null)
            {
                Debug.LogError(
                    $"[GorillaNetworking] Could not find avatar objects under '{name}'. " +
                    "Expected children: Head, Left Hand, Right Hand."
                );
            }
        }

        public override void Spawned()
        {
            if (GetComponentInParent<OfflineRig>(true) != null)
                return;

            offlineMode = false;
            isLocalRig = Object.HasStateAuthority;

            CacheAvatarReferences();
            SetAvatarVisible(true);

            if (isLocalRig)
            {
                SubscribeToManager();
                PullCustomization();
                InitializeLocalPlayer();

                Debug.Log(
                    $"[GorillaNetworking] Spawned. " +
                    $"Head follower: {headFollower != null}, " +
                    $"Left hand: {leftHand != null}, " +
                    $"Right hand: {rightHand != null}, " +
                    $"References ready: {localReferencesReady}"
                );

                if (localReferencesReady)
                    NetworkManager.Instance?.NotifyLocalRigReady(this);
            }

            RefreshVisuals();
        }

        private void Update()
        {
            if (offlineMode || !isLocalRig)
                return;

            if (!localReferencesReady)
                InitializeLocalPlayer();

            if (localReferencesReady)
                NetworkManager.Instance?.NotifyLocalRigReady(this);
        }

        public void EnableLocalAvatar(
            Transform head,
            Transform left,
            Transform right,
            GorillaFingers leftFingers,
            GorillaFingers rightFingers)
        {
            offlineMode = true;
            isLocalRig = true;

            headFollower = head;
            leftHand = left;
            rightHand = right;

            localLeftFingers = leftFingers;
            localRightFingers = rightFingers;

            CacheAvatarReferences();
            SetAvatarVisible(true);

            SubscribeToManager();
            PullCustomization();

            if (headFollower == null ||
                leftHand == null ||
                rightHand == null)
            {
                Debug.LogError(
                    "[GorillaNetworking] Offline avatar initialization failed: " +
                    "head or hand tracking references are missing."
                );
                return;
            }

            RefreshVisuals();

            Debug.Log("[GorillaNetworking] Offline avatar initialized.");
        }

        private void InitializeLocalPlayer()
        {
            NetworkManager manager = NetworkManager.Instance;

            if (manager == null)
                return;

            manager.RefreshGorillaReferences();

            Player player = manager.GorillaPlayer;

            if (player == null)
                return;

            if (headFollower == null)
                headFollower = player.headFollower;

            if (leftHand == null)
                leftHand = player.leftFollower;

            if (rightHand == null)
                rightHand = player.rightFollower;

            if (localLeftFingers == null)
                localLeftFingers = manager.LeftGorillaFingers;

            if (localRightFingers == null)
                localRightFingers = manager.RightGorillaFingers;

            localReferencesReady =
                headFollower != null &&
                leftHand != null &&
                rightHand != null;

            if (localReferencesReady)
                RefreshVisuals();
        }

        public override void FixedUpdateNetwork()
        {
            if (!isLocalRig || offlineMode)
                return;

            if (!localReferencesReady)
            {
                InitializeLocalPlayer();

                if (!localReferencesReady)
                    return;
            }

            HeadPosition = headFollower.position;
            HeadRotation = headFollower.rotation;

            LeftHandPosition = leftHand.position;
            LeftHandRotation = leftHand.rotation;

            RightHandPosition = rightHand.position;
            RightHandRotation = rightHand.rotation;

            if (localLeftFingers != null)
            {
                LeftPinkyCurl = localLeftFingers.Pinky.Curl;
                LeftIndexCurl = localLeftFingers.Index.Curl;
                LeftThumbCurl = localLeftFingers.Thumb.Curl;
            }

            if (localRightFingers != null)
            {
                RightPinkyCurl = localRightFingers.Pinky.Curl;
                RightIndexCurl = localRightFingers.Index.Curl;
                RightThumbCurl = localRightFingers.Thumb.Curl;
            }
        }

        public override void Render()
        {
            if (offlineMode)
            {
                FollowLocalPlayer();

                if (localLeftFingers != null)
                {
                    localLeftFingers.SetCurls(
                        localLeftFingers.Pinky.Curl,
                        localLeftFingers.Index.Curl,
                        localLeftFingers.Thumb.Curl
                    );
                }

                if (localRightFingers != null)
                {
                    localRightFingers.SetCurls(
                        localRightFingers.Pinky.Curl,
                        localRightFingers.Index.Curl,
                        localRightFingers.Thumb.Curl
                    );
                }

                RefreshVisuals();
                return;
            }

            if (isLocalRig)
            {
                FollowLocalPlayer();
            }
            else
            {
                SmoothPose(remoteHead, HeadPosition, HeadRotation, ref headVelocity);
                SmoothPose(remoteLeftHand, LeftHandPosition, LeftHandRotation, ref leftHandVelocity);
                SmoothPose(remoteRightHand, RightHandPosition, RightHandRotation, ref rightHandVelocity);
            }

            if (remoteLeftFingers != null)
            {
                remoteLeftFingers.SetCurls(
                    LeftPinkyCurl,
                    LeftIndexCurl,
                    LeftThumbCurl
                );
            }

            if (remoteRightFingers != null)
            {
                remoteRightFingers.SetCurls(
                    RightPinkyCurl,
                    RightIndexCurl,
                    RightThumbCurl
                );
            }

            RefreshVisuals();
        }

        private void LateUpdate()
        {
            if (isLocalRig)
                FollowLocalPlayer();
        }

        private void OnDestroy()
        {
            UnsubscribeFromManager();

            if (playerMaterialInstance != null)
                Destroy(playerMaterialInstance);
        }

        private void SmoothPose(
            Transform target,
            Vector3 targetPosition,
            Quaternion targetRotation,
            ref Vector3 velocity)
        {
            if (target == null)
                return;

            target.position = Vector3.SmoothDamp(
                target.position,
                targetPosition,
                ref velocity,
                positionSmoothTime
            );

            float rotationFactor =
                1f - Mathf.Exp(-rotationSmoothSpeed * Time.deltaTime);

            target.rotation = Quaternion.Slerp(
                target.rotation,
                targetRotation,
                rotationFactor
            );
        }

        private void FollowLocalPlayer()
        {
            if (headFollower == null ||
                leftHand == null ||
                rightHand == null)
                return;

            ApplyPose(remoteHead, headFollower.position, headFollower.rotation);
            ApplyPose(remoteLeftHand, leftHand.position, leftHand.rotation);
            ApplyPose(remoteRightHand, rightHand.position, rightHand.rotation);
        }

        private void ApplyPose(Transform target, Vector3 position, Quaternion rotation)
        {
            if (target == null)
                return;

            target.SetPositionAndRotation(position, rotation);
        }

        private void RefreshVisuals()
        {
            UpdateNameTagText();
            UpdateCosmetics();
            UpdatePlayerColor();
        }

        private bool EnsurePlayerMaterial()
        {
            if (playerMaterial == null)
            {
                Debug.LogWarning(
                    $"[GorillaNetworking] '{name}': playerMaterial is not assigned."
                );
                return false;
            }

            if (colorRenderers == null || colorRenderers.Length == 0)
            {
                Debug.LogWarning(
                    $"[GorillaNetworking] '{name}': colorRenderers is empty."
                );
                return false;
            }

            if (playerMaterialInstance == null)
                playerMaterialInstance = new Material(playerMaterial);

            foreach (Renderer targetRenderer in colorRenderers)
            {
                if (targetRenderer == null)
                {
                    Debug.LogWarning(
                        $"[GorillaNetworking] '{name}': a colorRenderers entry is null."
                    );
                    continue;
                }

                Material[] mats = targetRenderer.sharedMaterials;

                if (materialSlot < 0 || materialSlot >= mats.Length)
                {
                    Debug.LogWarning(
                        $"[GorillaNetworking] '{targetRenderer.name}' has " +
                        $"{mats.Length} material slots, slot {materialSlot} " +
                        "does not exist."
                    );
                    continue;
                }

                if (mats[materialSlot] == playerMaterialInstance)
                    continue;

                mats[materialSlot] = playerMaterialInstance;
                targetRenderer.sharedMaterials = mats;

                Debug.Log(
                    $"[GorillaNetworking] '{name}': assigned instance to " +
                    $"'{targetRenderer.name}' slot {materialSlot}."
                );
            }

            return true;
        }

        private void ApplyPlayerColor(Color color)
        {
            if (playerColorApplied && color == appliedPlayerColor)
                return;

            if (!EnsurePlayerMaterial())
                return;

            if (!playerMaterialInstance.HasProperty(BaseColorId))
            {
                Debug.LogWarning(
                    $"[GorillaNetworking] Shader '{playerMaterialInstance.shader.name}' " +
                    "has no _BaseColor property."
                );
                return;
            }

            playerMaterialInstance.SetColor(BaseColorId, color);

            appliedPlayerColor = color;
            playerColorApplied = true;

            Debug.Log(
                $"[GorillaNetworking] '{name}': color {color} applied to " +
                $"'{playerMaterialInstance.name}'."
            );
        }

        private void UpdatePlayerColor()
        {
            ApplyPlayerColor(ReadColor());
        }

        private void UpdateNameTagText()
        {
            if (nameTagText == null)
                return;

            nameTagText.text = ReadName();
        }

        private void SetAvatarVisible(bool visible)
        {
            SetVisible(remoteHead, visible);
            SetVisible(remoteLeftHand, visible);
            SetVisible(remoteRightHand, visible);
            SetVisible(remoteBody, visible);
            SetVisible(remoteFace, visible);

            if (nameTagText != null)
                nameTagText.gameObject.SetActive(visible);
        }

        private void SetVisible(Transform target, bool visible)
        {
            if (target == null)
                return;

            foreach (Renderer targetRenderer in
                     target.GetComponentsInChildren<Renderer>(true))
            {
                targetRenderer.enabled = visible;
            }
        }

        private void ApplyCosmetic(string id, Transform slot, ref string appliedId)
        {
            id ??= "";

            if (appliedId == id)
                return;

            appliedId = id;

            if (slot == null)
                return;

            foreach (Transform child in slot)
            {
                bool shouldEnable =
                    !string.IsNullOrEmpty(id) &&
                    child.name == id;

                child.gameObject.SetActive(shouldEnable);
            }
        }

        private void UpdateCosmetics()
        {
            ApplyCosmetic(ReadCosmetic(CosmeticSlot.Head), headCosmeticSlot, ref appliedHeadCosmetic);
            ApplyCosmetic(ReadCosmetic(CosmeticSlot.LeftHand), leftHandCosmeticSlot, ref appliedLeftHandCosmetic);
            ApplyCosmetic(ReadCosmetic(CosmeticSlot.RightHand), rightHandCosmeticSlot, ref appliedRightHandCosmetic);
            ApplyCosmetic(ReadCosmetic(CosmeticSlot.Body), bodyCosmeticSlot, ref appliedBodyCosmetic);
            ApplyCosmetic(ReadCosmetic(CosmeticSlot.Face), faceCosmeticSlot, ref appliedFaceCosmetic);
        }
    }
}
