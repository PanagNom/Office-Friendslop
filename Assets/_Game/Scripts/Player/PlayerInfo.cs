using Unity.Collections;
using Unity.Netcode;

public class PlayerInfo : NetworkBehaviour
{
    private readonly NetworkVariable<FixedString64Bytes> _playerName = new NetworkVariable<FixedString64Bytes>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public string PlayerName => _playerName.Value.ToString();
    public event System.Action<string> NameChanged;
    public override void OnNetworkSpawn()
    {
        _playerName.OnValueChanged += OnPlayerNameChanged;
        if (IsOwner)
        {
            SubmitNameRpc(new FixedString64Bytes(LocalPlayerProfile.PlayerName));
        }
    }

    public override void OnNetworkDespawn()
    {
        _playerName.OnValueChanged -= OnPlayerNameChanged;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void SubmitNameRpc(FixedString64Bytes newName)
    {
        string cleanedName = LocalPlayerProfile.CleanName(newName.ToString());
        if (cleanedName == "")
            cleanedName = $"Employee {OwnerClientId}";

        _playerName.Value = cleanedName;
    }

    private void OnPlayerNameChanged(FixedString64Bytes previousValue, FixedString64Bytes newValue)
    {
        NameChanged?.Invoke(newValue.ToString());
    }
}
