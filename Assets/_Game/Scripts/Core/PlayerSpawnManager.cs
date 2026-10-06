using Unity.Multiplayer.GettingStarted.PlayerMovement;
using Unity.Netcode;
using UnityEngine;

public class PlayerSpawnManager : MonoBehaviour
{
    [SerializeField]
    private Transform[] spawnPoints;

    private void Start()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        }
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        }

    }

    private void OnClientConnected(ulong clientId)
    {
        if (!NetworkManager.Singleton.IsServer)
            return;

        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out NetworkClient client))
        {
            return;
        }

        NetworkObject player = client.PlayerObject;
        Debug.Log($"Player object for client {clientId}: {player}");
        if (player == null || spawnPoints.Length == 0)
            return;

        int spawnIndex = (int)(clientId % (ulong)spawnPoints.Length);
        Debug.Log($"Spawning player {clientId} at spawn point {spawnIndex}");
        Transform spawnPoint = spawnPoints[spawnIndex];

        PlayerMovementController movement = player.GetComponent<PlayerMovementController>();
        movement.TeleportToSpawnRpc(spawnPoint.position, spawnPoint.rotation);
    }
}
