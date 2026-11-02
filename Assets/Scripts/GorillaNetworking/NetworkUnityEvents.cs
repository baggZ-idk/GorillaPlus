using UnityEngine;
using GorillaNetworking;

public class NetworkUnityEvents : MonoBehaviour
{
    private NetworkManager GetManager()
    {
        NetworkManager manager = NetworkManager.Instance;

        if (manager == null)
            Debug.LogError("[NetworkUnityEvents] NetworkManager not found.");

        return manager;
    }

    public void JoinSpecificRoom(string roomName)
    {
        NetworkManager manager = GetManager();

        if (manager != null)
            _ = manager.JoinRoom(roomName);
    }

    public void JoinRandomPublicRoom()
    {
        NetworkManager manager = GetManager();

        if (manager != null)
            manager.JoinRandomPublicRoom();
    }

    public void LeaveRoom()
    {
        NetworkManager manager = GetManager();

        if (manager != null)
            manager.LeaveRoom();
    }

    public void StartOfflineMode()
    {
        NetworkManager manager = GetManager();

        if (manager != null)
            manager.StartOfflineMode();
    }

    public void SetPlayerName(string playerName)
    {
        NetworkManager manager = GetManager();

        if (manager != null)
            manager.SetPlayerName(playerName);
    }

    public void SetHeadCosmetic(string cosmeticId)
    {
        NetworkManager manager = GetManager();

        if (manager != null)
            manager.SetHeadCosmetic(cosmeticId);
    }

    public void SetLeftHandCosmetic(string cosmeticId)
    {
        NetworkManager manager = GetManager();

        if (manager != null)
            manager.SetLeftHandCosmetic(cosmeticId);
    }

    public void SetRightHandCosmetic(string cosmeticId)
    {
        NetworkManager manager = GetManager();

        if (manager != null)
            manager.SetRightHandCosmetic(cosmeticId);
    }

    public void SetBodyCosmetic(string cosmeticId)
    {
        NetworkManager manager = GetManager();

        if (manager != null)
            manager.SetBodyCosmetic(cosmeticId);
    }

    public void SetFaceCosmetic(string cosmeticId)
    {
        NetworkManager manager = GetManager();

        if (manager != null)
            manager.SetFaceCosmetic(cosmeticId);
    }

    public void SetAllCosmetics(
        string headId,
        string leftHandId,
        string rightHandId,
        string bodyId,
        string faceId)
    {
        NetworkManager manager = GetManager();

        if (manager != null)
            manager.SetCosmetics(headId, leftHandId, rightHandId, bodyId, faceId);
    }

    public void SetPlayerColor(Color color)
    {
        NetworkManager manager = GetManager();

        if (manager != null)
            manager.SetPlayerColor(color);
    }

    public void SetPlayerColorRed()
    {
        SetPlayerColor(Color.red);
    }

    public void SetPlayerColorGreen()
    {
        SetPlayerColor(Color.green);
    }

    public void SetPlayerColorBlue()
    {
        SetPlayerColor(Color.blue);
    }

    public void SetPlayerColorWhite()
    {
        SetPlayerColor(Color.white);
    }

    public void SetPlayerColorBlack()
    {
        SetPlayerColor(Color.black);
    }
}
