using Unity.Netcode;
using UnityEngine;

public class PlayerNameplate : NetworkBehaviour
{
    [SerializeField]
    private TMPro.TMP_Text _nameText;

    [SerializeField]
    private Canvas _canvas;
    private CanvasGroup _canvasGroup;
    private float _fadeStartDistance = 10f;
    private float _fadeEndDistance = 20f;
    private PlayerInfo _playerInfo;

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {

        }
        else
        {
            _playerInfo = GetComponentInParent<PlayerInfo>();
            SetName(_playerInfo.PlayerName);
            _playerInfo.NameChanged += SetName;
        }
    }

    public override void OnNetworkDespawn()
    {
        _playerInfo.NameChanged -= SetName;
    }

    private void SetName(string name)
    {
        _canvas.enabled = !string.IsNullOrEmpty(name);
        _nameText.text = name;
    }
}
