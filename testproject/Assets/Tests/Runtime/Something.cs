using UnityEngine;
using Unity.Netcode;

public class Something : NetworkBehaviour
{
    public void PrintRpcTable()
    {
        Debug.Log($"{nameof(Something)}.{nameof(PrintRpcTable)}()");
        foreach (var rpcKey in NetworkManager.__rpc_func_table.Keys)
        {
            Debug.Log($"rpcKey: {rpcKey}");
            if (NetworkManager.__rpc_name_table.TryGetValue(rpcKey, out var rpcName))
            {
                Debug.Log($"rpcName: {rpcName}");
            }
        }
    }

    [ServerRpc]
    public void SomeServerRpc()
    {
    }

    [ClientRpc]
    public void SomeClientRpc()
    {
    }

    [ClientRpc]
    public void SomeClientRpc(int number)
    {
    }

    [ClientRpc]
    public void SomeClientRpc(int number, string text)
    {
    }
}
