﻿using System.Security.Cryptography;
using MLAPI.Data;
using MLAPI.MonoBehaviours.Core;
using MLAPI.NetworkingManagerComponents.Binary;
using UnityEngine;
using UnityEngine.Networking;

namespace MLAPI.NetworkingManagerComponents.Core
{
    internal static partial class InternalMessageHandler
    {
        internal static void HandleConnectionRequest(uint clientId, byte[] incommingData, int channelId)
        {
            BitReader reader = new BitReader(incommingData);

            byte[] configHash = reader.ReadByteArray(32);
            if (!netManager.NetworkConfig.CompareConfig(configHash))
            {
                Debug.LogWarning("MLAPI: NetworkConfiguration missmatch. The configuration between the server and client does not match.");
                netManager.DisconnectClient(clientId);
                return;
            }

            if (netManager.NetworkConfig.EnableEncryption)
            {
                byte[] diffiePublic = reader.ReadByteArray();
                netManager.diffieHellmanPublicKeys.Add(clientId, diffiePublic);

            }
            if (netManager.NetworkConfig.ConnectionApproval)
            {
                byte[] connectionBuffer = reader.ReadByteArray();
                netManager.ConnectionApprovalCallback(connectionBuffer, clientId, netManager.HandleApproval);
            }
            else
            {
                netManager.HandleApproval(clientId, true, Vector3.zero, Quaternion.identity);
            }
        }

        internal static void HandleConnectionApproved(uint clientId, byte[] incommingData, int channelId)
        {
            BitReader reader = new BitReader(incommingData);

            netManager.myClientId = reader.ReadUInt();
            uint sceneIndex = 0;
            if (netManager.NetworkConfig.EnableSceneSwitching)
                sceneIndex = reader.ReadUInt();

            if (netManager.NetworkConfig.EnableEncryption)
            {
                byte[] serverPublicKey = reader.ReadByteArray();
                netManager.clientAesKey = netManager.clientDiffieHellman.GetSharedSecret(serverPublicKey);
                if (netManager.NetworkConfig.SignKeyExchange)
                {
                    byte[] publicKeySignature = reader.ReadByteArray();
                    using (RSACryptoServiceProvider rsa = new RSACryptoServiceProvider())
                    {
                        rsa.PersistKeyInCsp = false;
                        rsa.FromXmlString(netManager.NetworkConfig.RSAPublicKey);
                        if (!rsa.VerifyData(serverPublicKey, new SHA512CryptoServiceProvider(), publicKeySignature))
                        {
                            //Man in the middle.
                            Debug.LogWarning("MLAPI: Signature doesnt match for the key exchange public part. Disconnecting");
                            netManager.StopClient();
                            return;
                        }
                    }
                }
            }

            float netTime = reader.ReadFloat();
            int remoteStamp = reader.ReadInt();
            byte error;
            NetId netId = new NetId(clientId);
            int msDelay = NetworkTransport.GetRemoteDelayTimeMS(netId.HostId, netId.ConnectionId, remoteStamp, out error);
            if ((NetworkError)error != NetworkError.Ok)
                msDelay = 0;
            netManager.networkTime = netTime + (msDelay / 1000f);

            netManager.connectedClients.Add(netManager.MyClientId, new NetworkedClient() { ClientId = netManager.MyClientId });
            int clientCount = reader.ReadInt();
            for (int i = 0; i < clientCount; i++)
            {
                uint _clientId = reader.ReadUInt();
                netManager.connectedClients.Add(_clientId, new NetworkedClient() { ClientId = _clientId });
            }
            if (netManager.NetworkConfig.HandleObjectSpawning)
            {
                SpawnManager.DestroySceneObjects();
                int objectCount = reader.ReadInt();
                for (int i = 0; i < objectCount; i++)
                {
                    bool isPlayerObject = reader.ReadBool();
                    uint networkId = reader.ReadUInt();
                    uint ownerId = reader.ReadUInt();
                    int prefabId = reader.ReadInt();
                    bool isActive = reader.ReadBool();
                    bool sceneObject = reader.ReadBool();

                    float xPos = reader.ReadFloat();
                    float yPos = reader.ReadFloat();
                    float zPos = reader.ReadFloat();

                    float xRot = reader.ReadFloat();
                    float yRot = reader.ReadFloat();
                    float zRot = reader.ReadFloat();

                    if (isPlayerObject)
                    {
                        SpawnManager.SpawnPlayerObject(ownerId, networkId, new Vector3(xPos, yPos, zPos), Quaternion.Euler(xRot, yRot, zRot));
                    }
                    else
                    {
                        GameObject go = SpawnManager.SpawnPrefabIndexClient(prefabId, networkId, ownerId,
                            new Vector3(xPos, yPos, zPos), Quaternion.Euler(xRot, yRot, zRot));

                        go.GetComponent<NetworkedObject>().sceneObject = sceneObject;
                        go.SetActive(isActive);
                    }
                }
            }

            if (netManager.NetworkConfig.EnableSceneSwitching)
            {
                NetworkSceneManager.OnSceneSwitch(sceneIndex);
            }

            netManager._isClientConnected = true;
            if (netManager.OnClientConnectedCallback != null)
                netManager.OnClientConnectedCallback.Invoke(netManager.MyClientId);
        }

        internal static void HandleAddObject(uint clientId, byte[] incommingData, int channelId)
        {
            BitReader reader = new BitReader(incommingData);

            if (netManager.NetworkConfig.HandleObjectSpawning)
            {
                bool isPlayerObject = reader.ReadBool();
                uint networkId = reader.ReadUInt();
                uint ownerId = reader.ReadUInt();
                int prefabId = reader.ReadInt();
                bool sceneObject = reader.ReadBool();

                float xPos = reader.ReadFloat();
                float yPos = reader.ReadFloat();
                float zPos = reader.ReadFloat();

                float xRot = reader.ReadFloat();
                float yRot = reader.ReadFloat();
                float zRot = reader.ReadFloat();

                if (isPlayerObject)
                {
                    netManager.connectedClients.Add(ownerId, new NetworkedClient() { ClientId = ownerId });
                    SpawnManager.SpawnPlayerObject(ownerId, networkId, new Vector3(xPos, yPos, zPos), Quaternion.Euler(xRot, yRot, zRot));
                }
                else
                {
                    GameObject go = SpawnManager.SpawnPrefabIndexClient(prefabId, networkId, ownerId,
                                        new Vector3(xPos, yPos, zPos), Quaternion.Euler(xRot, yRot, zRot));
                    go.GetComponent<NetworkedObject>().sceneObject = sceneObject;
                }
            }
            else
            {
                uint ownerId = reader.ReadUInt();
                netManager.connectedClients.Add(ownerId, new NetworkedClient() { ClientId = ownerId });
            }
        }

        internal static void HandleClientDisconnect(uint clientId, byte[] incommingData, int channelId)
        {
            BitReader reader = new BitReader(incommingData);

            uint disconnectedClientId = reader.ReadUInt();
            netManager.OnClientDisconnect(disconnectedClientId);
        }

        internal static void HandleDestroyObject(uint clientId, byte[] incommingData, int channelId)
        {
            BitReader reader = new BitReader(incommingData);

            uint netId = reader.ReadUInt();
            SpawnManager.OnDestroyObject(netId, true);
        }

        internal static void HandleSwitchScene(uint clientId, byte[] incommingData, int channelId)
        {
            BitReader reader = new BitReader(incommingData);

            NetworkSceneManager.OnSceneSwitch(reader.ReadUInt());
        }

        internal static void HandleSpawnPoolObject(uint clientId, byte[] incommingData, int channelId)
        {
            BitReader reader = new BitReader(incommingData);

            uint netId = reader.ReadUInt();

            float xPos = reader.ReadFloat();
            float yPos = reader.ReadFloat();
            float zPos = reader.ReadFloat();

            float xRot = reader.ReadFloat();
            float yRot = reader.ReadFloat();
            float zRot = reader.ReadFloat();

            SpawnManager.spawnedObjects[netId].transform.position = new Vector3(xPos, yPos, zPos);
            SpawnManager.spawnedObjects[netId].transform.rotation = Quaternion.Euler(xRot, yRot, zRot);
            SpawnManager.spawnedObjects[netId].gameObject.SetActive(true);
        }

        internal static void HandleDestroyPoolObject(uint clientId, byte[] incommingData, int channelId)
        {
            BitReader reader = new BitReader(incommingData);

            uint netId = reader.ReadUInt();
            SpawnManager.spawnedObjects[netId].gameObject.SetActive(false);
        }

        internal static void HandleChangeOwner(uint clientId, byte[] incommingData, int channelId)
        {
            BitReader reader = new BitReader(incommingData);

            uint netId = reader.ReadUInt();
            uint ownerClientId = reader.ReadUInt();
            if (SpawnManager.spawnedObjects[netId].OwnerClientId == netManager.MyClientId)
            {
                //We are current owner.
                SpawnManager.spawnedObjects[netId].InvokeBehaviourOnLostOwnership();
            }
            if (ownerClientId == netManager.MyClientId)
            {
                //We are new owner.
                SpawnManager.spawnedObjects[netId].InvokeBehaviourOnGainedOwnership();
            }
            SpawnManager.spawnedObjects[netId].ownerClientId = ownerClientId;
        }

        internal static void HandleSyncVarUpdate(uint clientId, byte[] incommingData, int channelId)
        {
            BitReader reader = new BitReader(incommingData);

            byte dirtyCount = reader.ReadByte();
            uint netId = reader.ReadUInt();
            ushort orderIndex = reader.ReadUShort();
            if (dirtyCount > 0)
            {
                for (int i = 0; i < dirtyCount; i++)
                {
                    byte fieldIndex = reader.ReadByte();
                    if (!SpawnManager.spawnedObjects.ContainsKey(netId))
                    {
                        Debug.LogWarning("MLAPI: Sync message recieved for a non existant object with id: " + netId);
                        return;
                    }
                    else if (SpawnManager.spawnedObjects[netId].GetBehaviourAtOrderIndex(orderIndex) == null)
                    {
                        Debug.LogWarning("MLAPI: Sync message recieved for a non existant behaviour");
                        return;
                    }
                    else if (fieldIndex > (SpawnManager.spawnedObjects[netId].GetBehaviourAtOrderIndex(orderIndex).syncedVarFields.Count - 1))
                    {
                        Debug.LogWarning("MLAPI: Sync message recieved for field out of bounds");
                        return;
                    }
                    FieldType type = SpawnManager.spawnedObjects[netId].GetBehaviourAtOrderIndex(orderIndex).syncedVarFields[fieldIndex].FieldType;
                    switch (type)
                    {
                        case FieldType.Bool:
                            SpawnManager.spawnedObjects[netId].GetBehaviourAtOrderIndex(orderIndex).OnSyncVarUpdate(reader.ReadBool(), fieldIndex);
                            break;
                        case FieldType.Byte:
                            SpawnManager.spawnedObjects[netId].GetBehaviourAtOrderIndex(orderIndex).OnSyncVarUpdate(reader.ReadByte(), fieldIndex);
                            break;
                        case FieldType.Double:
                            SpawnManager.spawnedObjects[netId].GetBehaviourAtOrderIndex(orderIndex).OnSyncVarUpdate(reader.ReadDouble(), fieldIndex);
                            break;
                        case FieldType.Single:
                            SpawnManager.spawnedObjects[netId].GetBehaviourAtOrderIndex(orderIndex).OnSyncVarUpdate(reader.ReadFloat(), fieldIndex);
                            break;
                        case FieldType.Int:
                            SpawnManager.spawnedObjects[netId].GetBehaviourAtOrderIndex(orderIndex).OnSyncVarUpdate(reader.ReadInt(), fieldIndex);
                            break;
                        case FieldType.Long:
                            SpawnManager.spawnedObjects[netId].GetBehaviourAtOrderIndex(orderIndex).OnSyncVarUpdate(reader.ReadLong(), fieldIndex);
                            break;
                        case FieldType.SByte:
                            SpawnManager.spawnedObjects[netId].GetBehaviourAtOrderIndex(orderIndex).OnSyncVarUpdate(reader.ReadSByte(), fieldIndex);
                            break;
                        case FieldType.Short:
                            SpawnManager.spawnedObjects[netId].GetBehaviourAtOrderIndex(orderIndex).OnSyncVarUpdate(reader.ReadShort(), fieldIndex);
                            break;
                        case FieldType.UInt:
                            SpawnManager.spawnedObjects[netId].GetBehaviourAtOrderIndex(orderIndex).OnSyncVarUpdate(reader.ReadUInt(), fieldIndex);
                            break;
                        case FieldType.ULong:
                            SpawnManager.spawnedObjects[netId].GetBehaviourAtOrderIndex(orderIndex).OnSyncVarUpdate(reader.ReadULong(), fieldIndex);
                            break;
                        case FieldType.UShort:
                            SpawnManager.spawnedObjects[netId].GetBehaviourAtOrderIndex(orderIndex).OnSyncVarUpdate(reader.ReadUShort(), fieldIndex);
                            break;
                        case FieldType.String:
                            SpawnManager.spawnedObjects[netId].GetBehaviourAtOrderIndex(orderIndex).OnSyncVarUpdate(reader.ReadString(), fieldIndex);
                            break;
                        case FieldType.Vector3:
                            {   //Cases aren't their own scope. Therefor we create a scope for them as they share the X,Y,Z local variables otherwise.
                                float x = reader.ReadFloat();
                                float y = reader.ReadFloat();
                                float z = reader.ReadFloat();
                                SpawnManager.spawnedObjects[netId].GetBehaviourAtOrderIndex(orderIndex).OnSyncVarUpdate(new Vector3(x, y, z), fieldIndex);
                            }
                            break;
                        case FieldType.Vector2:
                            {
                                float x = reader.ReadFloat();
                                float y = reader.ReadFloat();
                                SpawnManager.spawnedObjects[netId].GetBehaviourAtOrderIndex(orderIndex).OnSyncVarUpdate(new Vector2(x, y), fieldIndex);
                            }
                            break;
                        case FieldType.Quaternion:
                            {
                                float x = reader.ReadFloat();
                                float y = reader.ReadFloat();
                                float z = reader.ReadFloat();
                                SpawnManager.spawnedObjects[netId].GetBehaviourAtOrderIndex(orderIndex).OnSyncVarUpdate(Quaternion.Euler(x, y, z), fieldIndex);
                            }
                            break;
                    }
                }
            }
        }

        internal static void HandleAddObjects(uint clientId, byte[] incommingData, int channelId)
        {
            BitReader reader = new BitReader(incommingData);

            if (netManager.NetworkConfig.HandleObjectSpawning)
            {
                ushort objectCount = reader.ReadUShort();
                for (int i = 0; i < objectCount; i++)
                {
                    bool isPlayerObject = reader.ReadBool();
                    uint networkId = reader.ReadUInt();
                    uint ownerId = reader.ReadUInt();
                    int prefabId = reader.ReadInt();
                    bool sceneObject = reader.ReadBool();

                    float xPos = reader.ReadFloat();
                    float yPos = reader.ReadFloat();
                    float zPos = reader.ReadFloat();

                    float xRot = reader.ReadFloat();
                    float yRot = reader.ReadFloat();
                    float zRot = reader.ReadFloat();

                    if (isPlayerObject)
                    {
                        netManager.connectedClients.Add(ownerId, new NetworkedClient() { ClientId = ownerId });
                        SpawnManager.SpawnPlayerObject(ownerId, networkId, new Vector3(xPos, yPos, zPos), Quaternion.Euler(xRot, yRot, zRot));
                    }
                    else
                    {
                        GameObject go = SpawnManager.SpawnPrefabIndexClient(prefabId, networkId, ownerId,
                                            new Vector3(xPos, yPos, zPos), Quaternion.Euler(xRot, yRot, zRot));
                        go.GetComponent<NetworkedObject>().sceneObject = sceneObject;
                    }
                }
            }
        }

        internal static void HandleTimeSync(uint clientId, byte[] incommingData, int channelId)
        {
            BitReader reader = new BitReader(incommingData);

            float netTime = reader.ReadFloat();
            int timestamp = reader.ReadInt();

            NetId netId = new NetId(clientId);
            byte error;
            int msDelay = NetworkTransport.GetRemoteDelayTimeMS(netId.HostId, netId.ConnectionId, timestamp, out error);
            if ((NetworkError)error != NetworkError.Ok)
                msDelay = 0;
            netManager.networkTime = netTime + (msDelay / 1000f);
        }  
    }
}
