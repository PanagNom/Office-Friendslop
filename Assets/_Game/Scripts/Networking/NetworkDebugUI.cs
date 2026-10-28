using Unity.Netcode;
using UnityEngine;

public class NetworkDebugUI : MonoBehaviour
{
    private string _nameInput;

    private void Start()
    {
        _nameInput = LocalPlayerProfile.PlayerName;
    }

    private void OnGUI()
    {
        if (NetworkManager.Singleton == null)
            return;

        if (!NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer)
        {
            _nameInput = GUILayout.TextField(_nameInput, LocalPlayerProfile.MaxNameLength);
            if (GUILayout.Button("Start Host"))
            {
                LocalPlayerProfile.PlayerName = _nameInput;
                NetworkManager.Singleton.StartHost();
            }

            if (GUILayout.Button("Start Client"))
            {
                LocalPlayerProfile.PlayerName = _nameInput;
                NetworkManager.Singleton.StartClient();
            }
        }
    }
}
