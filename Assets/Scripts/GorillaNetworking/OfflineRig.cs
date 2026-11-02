using UnityEngine;
using GorillaLocomotion;

namespace GorillaNetworking
{
    public class OfflineRig : MonoBehaviour
    {
        [Header("Offline Avatar")]
        [SerializeField] private GameObject offlineRigObject;
        [SerializeField] private NetworkRig offlineNetworkRig;
        public NetworkRig Rig => offlineNetworkRig;

        private bool initialized;

        private void Start()
        {
            Initialize();
        }

        private void Update()
        {
            if (!initialized)
                Initialize();
        }

        private void Initialize()
        {
            NetworkManager manager = NetworkManager.Instance;

            if (manager == null)
                return;

            if (offlineRigObject == null)
                offlineRigObject = gameObject;

            if (offlineNetworkRig == null)
                offlineNetworkRig =
                    offlineRigObject.GetComponent<NetworkRig>();

            if (offlineNetworkRig == null)
            {
                Debug.LogError(
                    "[GorillaNetworking] No NetworkRig found on offline avatar."
                );
                enabled = false;
                return;
            }

            Player player = Player.Instance;

            if (player == null)
                return;

            manager.RefreshGorillaReferences();

            offlineNetworkRig.EnableLocalAvatar(
                player.headFollower,
                player.leftFollower,
                player.rightFollower,
                manager.LeftGorillaFingers,
                manager.RightGorillaFingers
            );

            manager.RegisterOfflineRig(offlineRigObject);

            initialized = true;

            Debug.Log(
                "[GorillaNetworking] Offline avatar initialized."
            );
        }
    }
}