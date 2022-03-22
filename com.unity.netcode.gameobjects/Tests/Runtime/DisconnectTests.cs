using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Unity.Netcode.TestHelpers.Runtime;
using System.Collections.Generic;

namespace Unity.Netcode.RuntimeTests
{
    public class DisconnectHandler
    {
        public NetworkManager NetworkManager { get; private set; }
        public bool IsServer { get; private set; }

        public DisconnectHandler(NetworkManager networkManager, bool isServer = false)
        {
            NetworkManager = networkManager;
            IsServer = isServer;
        }

        public List<ulong> HandledClientIds { get; private set; } = new List<ulong>();
        public void HandleClientDisconnect(ulong clientId)
        {
            HandledClientIds.Add(clientId);
            Debug.Log($"{(IsServer ? "Server" : "Client")}NetworkManager#{NetworkManager.LocalClientId}.{nameof(NetworkManager.OnClientDisconnectCallback)}({clientId})");
        }
    }

    [TestFixture(HostOrServer.Host)]
    [TestFixture(HostOrServer.Server)]
    public class DisconnectTests : NetcodeIntegrationTest
    {
        private const int k_NumberOfClients = 3;
        protected override int NumberOfClients => k_NumberOfClients;

        public DisconnectTests(HostOrServer hostOrServer)
            : base(hostOrServer)
        {
        }

        private DisconnectHandler m_ServerDisconnectHandler;
        private DisconnectHandler[] m_ClientDisconnectHandlers = new DisconnectHandler[k_NumberOfClients];

        protected override void OnServerAndClientsCreated()
        {
            m_ServerDisconnectHandler = new DisconnectHandler(m_ServerNetworkManager, isServer: true);
            m_ServerNetworkManager.OnClientDisconnectCallback += m_ServerDisconnectHandler.HandleClientDisconnect;

            for (int i = 0; i < m_ClientNetworkManagers.Length; i++)
            {
                var clientNetworkManager = m_ClientNetworkManagers[i];
                m_ClientDisconnectHandlers[i] = new DisconnectHandler(clientNetworkManager);
                clientNetworkManager.OnClientDisconnectCallback += m_ClientDisconnectHandlers[i].HandleClientDisconnect;
            }

            Assert.NotNull(m_ServerDisconnectHandler);
            foreach (var clientDisconnectHandler in m_ClientDisconnectHandlers)
            {
                Assert.NotNull(clientDisconnectHandler);
            }
        }

        [UnityTest]
        public IEnumerator ServerDisconnectingClients()
        {
            Assert.IsEmpty(m_ServerDisconnectHandler.HandledClientIds);
            foreach (var clientDisconnectHandler in m_ClientDisconnectHandlers)
            {
                Assert.IsEmpty(clientDisconnectHandler.HandledClientIds);
            }

            for (int i = 0; i < m_ClientNetworkManagers.Length; i++)
            {
                var clientNetworkManager = m_ClientNetworkManagers[i];
                var clientNetworkId = clientNetworkManager.LocalClientId;
                m_ServerNetworkManager.DisconnectClient(clientNetworkManager.LocalClientId);

                Assert.AreEqual(i + 1, m_ServerDisconnectHandler.HandledClientIds.Count);
                Assert.AreEqual(clientNetworkId, m_ServerDisconnectHandler.HandledClientIds[i]);

                yield return NetcodeIntegrationTestHelpers.WaitForTicks(m_ServerNetworkManager, 5);

                Assert.IsFalse(m_ClientNetworkManagers[i].IsConnectedClient);
                Assert.AreEqual(1, m_ClientDisconnectHandlers[i].HandledClientIds.Count);
                Assert.AreEqual(clientNetworkId, m_ClientDisconnectHandlers[i].HandledClientIds[0]);
            }

            Debug.Log($"server.connectedclients: {string.Join(",", m_ServerNetworkManager.ConnectedClientsIds)}");
            yield return null;
            /* Debug.Log($"---- {nameof(ServerDisconnectingClients)}");

            int nextFrame = Time.frameCount + 1;
            yield return new WaitUntil(() => Time.frameCount >= nextFrame);

            for (int i = 0; i < m_ClientNetworkManagers.Length; i++)
            {
                m_ServerNetworkManager.DisconnectClient(m_ClientNetworkManagers[i].LocalClientId);

                nextFrame = Time.frameCount + 1;
                yield return new WaitUntil(() => Time.frameCount >= nextFrame);
            } */
        }

        [UnityTest]
        public IEnumerator ClientsDisconnectFromServer()
        {
            yield return null;
            /* Debug.Log($"---- {nameof(ClientsDisconnectFromServer)}");

            int nextFrame = Time.frameCount + 1;
            yield return new WaitUntil(() => Time.frameCount >= nextFrame);

            for (int i = 0; i < m_ClientNetworkManagers.Length; i++)
            {
                m_ClientNetworkManagers[i].Shutdown();

                nextFrame = Time.frameCount + 1;
                yield return new WaitUntil(() => Time.frameCount >= nextFrame);
            } */
        }

        /*
        [UnityTest]
        public IEnumerator RemoteDisconnectPlayerObjectCleanup()
        {
            // create server and client instances
            NetcodeIntegrationTestHelpers.Create(1, out NetworkManager server, out NetworkManager[] clients);

            // create prefab
            var gameObject = new GameObject("PlayerObject");
            var networkObject = gameObject.AddComponent<NetworkObject>();
            networkObject.DontDestroyWithOwner = true;
            NetcodeIntegrationTestHelpers.MakeNetworkObjectTestPrefab(networkObject);

            server.NetworkConfig.PlayerPrefab = gameObject;

            for (int i = 0; i < clients.Length; i++)
            {
                clients[i].NetworkConfig.PlayerPrefab = gameObject;
            }

            // start server and connect clients
            NetcodeIntegrationTestHelpers.Start(false, server, clients);

            // wait for connection on client side
            yield return NetcodeIntegrationTestHelpers.WaitForClientsConnected(clients);

            // wait for connection on server side
            yield return NetcodeIntegrationTestHelpers.WaitForClientConnectedToServer(server);

            // disconnect the remote client
            server.DisconnectClient(clients[0].LocalClientId);

            // wait 1 frame because destroys are delayed
            var nextFrameNumber = Time.frameCount + 1;
            yield return new WaitUntil(() => Time.frameCount >= nextFrameNumber);

            // ensure the object was destroyed
            Assert.False(server.SpawnManager.SpawnedObjects.Any(x => x.Value.IsPlayerObject && x.Value.OwnerClientId == clients[0].LocalClientId));

            // cleanup
            NetcodeIntegrationTestHelpers.Destroy();
        }
        */
    }
}
