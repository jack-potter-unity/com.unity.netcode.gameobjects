using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using MLAPI;
using MLAPI.Messaging;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using UnityEngine;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;

[RequireComponent(typeof(NetworkObject))]
public class TestCoordinator : NetworkBehaviour
{
    /// <summary>
    /// TestCoordinator
    /// Used for coordinating multiprocess end to end tests. Used to call RPCs on other nodes and gather results
    /// /// Needed since the current remote player test runner hardcodes test to run in a bootstrap scene before launching the player.
    /// There's not per-test communication to run these tests, only to get the results per test as they are running
    /// </summary>
    public const float maxWaitTimeout = 10;
    private const char k_MethodFullNameSplitChar = '@';
    public bool isRegistering;
    public bool hasRegistered;

    public static List<ulong> AllClientIdExceptMine
    {
        get { return NetworkManager.Singleton.ConnectedClients.Keys.ToList().FindAll(client => client != NetworkManager.Singleton.LocalClientId); }
    }

    private bool m_ShouldShutdown;

    private Dictionary<ulong, List<float>> m_TestResultsLocal = new Dictionary<ulong, List<float>>();

    public static TestCoordinator Instance;

    private void Awake()
    {
        if (Instance != null)
        {
            Debug.LogError("Multiple test coordinator! destroying self");
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    public void Start()
    {
        bool isClient = Environment.GetCommandLineArgs().Any(value => value == MultiprocessOrchestration.isWorkerArg);
        if (isClient)
        {
            Debug.Log("starting MLAPI client");
            NetworkManager.Singleton.StartClient();
        }

        NetworkManager.OnClientDisconnectCallback += OnClientDisconnectCallback;

        // registering magically all method steps
        isRegistering = true;
        var registeredMethods = typeof(TestCoordinator).Assembly.GetTypes().SelectMany(t => t.GetMethods())
            .Where(m => m.GetCustomAttributes(typeof(ExecuteStepInContext.MultiprocessContextBasedTestAttribute), true).Length > 0)
            .ToArray();
        if (registeredMethods.Length == 0)
        {
            throw new Exception("Couldn't find any registered methods for multiprocess testing. Is TestCoordinator in same assembly as test methods?");
        }

        object[] GetParameterValuesToPass(ParameterInfo[] parameterInfo)
        {
            object[] parametersToReturn = new object[parameterInfo.Length];
            for (int i = 0; i < parameterInfo.Length; i++)
            {
                var paramType = parameterInfo[i].GetType();
                object defaultObj = null;
                if(paramType.IsValueType)
                {
                    defaultObj = Activator.CreateInstance(paramType);
                }
                parametersToReturn[i] = defaultObj;
            }

            return parametersToReturn;
        }
        foreach (var method in registeredMethods)
        {
            var methodsType = method.ReflectedType;
            var allConstructors = methodsType.GetConstructors();
            if (allConstructors.Length > 1)
            {
                throw new NotImplementedException("Case not implemented where test has more than one contructor");
            }
            var contructorParameters = allConstructors[0].GetParameters();
            var instance = Activator.CreateInstance(methodsType, GetParameterValuesToPass(contructorParameters));
            var parametersToPass = GetParameterValuesToPass(method.GetParameters());
            var result = (IEnumerator)method.Invoke(instance, parametersToPass.ToArray());
            while (result.MoveNext()) { }
        }

        isRegistering = false;
        hasRegistered = true;
    }

    public void Update()
    {
        if ((IsServer && NetworkManager.Singleton.IsListening) || (IsClient && NetworkManager.Singleton.IsConnectedClient))
        {
            m_TimeSinceLastConnected = Time.time;
        }
        else if (Time.time - m_TimeSinceLastConnected > maxWaitTimeout || m_ShouldShutdown)
        {
            // Make sure we don't have zombie processes
            Debug.Log($"quitting application, shouldShutdown set to {m_ShouldShutdown}, is listening {NetworkManager.Singleton.IsListening}, is connected client {NetworkManager.Singleton.IsConnectedClient}");
            if (!m_ShouldShutdown)
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
                Assert.Fail($"something wrong happened, was not connected for {Time.time - m_TimeSinceLastConnected} seconds");
            }
        }
    }

    public void TestRunTeardown()
    {
        m_TestResultsLocal.Clear();
    }

    public void OnDestroy()
    {
        if (NetworkObject != null && NetworkManager != null)
        {
            NetworkManager.OnClientDisconnectCallback -= OnClientDisconnectCallback;
        }
    }

    private static void OnClientDisconnectCallback(ulong clientId)
    {
        if (clientId == NetworkManager.Singleton.ServerClientId || clientId == NetworkManager.Singleton.LocalClientId)
        {
            // if disconnect callback is for me or for server, quit, we're done here
            Debug.Log($"received disconnect from {clientId}, quitting");
            Application.Quit();
        }
    }

    private static string GetMethodInfo(Action<byte[]> method)
    {
        return $"{method.Method.DeclaringType.FullName}{k_MethodFullNameSplitChar}{method.Method.Name}";
    }

    private static string GetMethodInfo(Action method)
    {
        return $"{method.Method.DeclaringType.FullName}{k_MethodFullNameSplitChar}{method.Method.Name}";
    }

    [ServerRpc(RequireOwnership = false)]
    public void WriteTestResultsServerRpc(float result, ServerRpcParams receiveParams = default)
    {
        Debug.Log($"received test results {result}");
        var senderId = receiveParams.Receive.SenderClientId;
        if (!m_TestResultsLocal.ContainsKey(senderId))
        {
            m_TestResultsLocal[senderId] = new List<float>();
        }

        m_TestResultsLocal[senderId].Add(result);
    }

    [ServerRpc(RequireOwnership = false)]
    public void WriteErrorServerRpc(string errorMessage, ServerRpcParams receiveParams = default)
    {
        Debug.LogError($"Got Exception client side {errorMessage}, from client {receiveParams.Receive.SenderClientId}");
    }

    public static IEnumerable<(ulong clientId, float result)> ConsumeCurrentResult()
    {
        foreach (var kv in Instance.m_TestResultsLocal)
        {
            while (kv.Value.Count > 0)
            {
                var toReturn = (kv.Key, kv.Value[0]);
                kv.Value.RemoveAt(0);
                yield return toReturn;
            }
        }
    }

    public static IEnumerable<float> ConsumeCurrentResult(ulong clientID)
    {
        var allResults = Instance.m_TestResultsLocal[clientID];
        while (allResults.Count > 0)
        {
            var toReturn = allResults[0];
            allResults.RemoveAt(0);
            yield return toReturn;
        }
    }

    public static float PeekLatestResult(ulong clientId)
    {
        if (Instance.m_TestResultsLocal.ContainsKey(clientId) && Instance.m_TestResultsLocal[clientId].Count > 0)
        {
            return Instance.m_TestResultsLocal[clientId].Last();
        }

        return float.NaN;
    }

    public static ulong[] AllClientIdsWithResults => Instance.m_TestResultsLocal.Keys.ToArray();

    public static Func<bool> ResultIsSet(bool useTimeoutException = true)
    {
        var startWaitTime = Time.time;
        return () =>
        {
            if (Time.time - startWaitTime > maxWaitTimeout)
            {
                if (useTimeoutException)
                {
                    throw new Exception($"timeout while waiting for results, didn't get results for {Time.time - startWaitTime} seconds");
                }

                return true;
            }

            foreach (var clientIdAndTestResultList in Instance.m_TestResultsLocal)
            {
                if (clientIdAndTestResultList.Value.Count > 0)
                {
                    return true;
                }
            }

            return false;
        };
    }

    private Dictionary<ulong, bool> m_ClientIsFinished = new Dictionary<ulong, bool>();

    public static Func<bool> ConsumeClientIsFinished(ulong clientId, bool useTimeoutException = true)
    {
        var startWaitTime = Time.time;
        return () =>
        {
            if (Time.time - startWaitTime > maxWaitTimeout)
            {
                if (useTimeoutException)
                {
                    throw new Exception($"timeout while waiting for client finished, didn't get results for {Time.time - startWaitTime} seconds");
                }
                else
                {
                    return true;
                }
            }

            if (Instance.m_ClientIsFinished.ContainsKey(clientId) && Instance.m_ClientIsFinished[clientId])
            {
                Instance.m_ClientIsFinished[clientId] = false; // consume
                return true;
            }

            return false;
        };
    }

    [ServerRpc(RequireOwnership = false)]
    public void ClientFinishedServerRpc(ServerRpcParams p = default)
    {
        // signal from clients to the server to say the client is done with it's task
        m_ClientIsFinished[p.Receive.SenderClientId] = true;
    }

    public void TriggerRpc(Action<byte[]> methodInfo, params byte[] args)
    {
        var methodInfoString = GetMethodInfo(methodInfo);
        TriggerInternalClientRpc(methodInfoString, args, new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = AllClientIdExceptMine.ToArray() } });
    }

    public void TriggerRpc(Action methodInfo)
    {
        var methodInfoString = GetMethodInfo(methodInfo);
        TriggerInternalClientRpc(methodInfoString, null, new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = AllClientIdExceptMine.ToArray() } });
    }

    [ClientRpc]
    public void TriggerActionIDClientRpc(string actionID, byte[] args, ClientRpcParams clientRpcParams = default)
    {
        Debug.Log($"received RPC from server, client side triggering action ID {actionID}");
        try
        {
            ExecuteStepInContext.allActions[actionID].Invoke(args);
        }
        catch (Exception e)
        {
            WriteErrorServerRpc(e.Message);
            throw;
        }
    }

    [ClientRpc]
    public void TriggerInternalClientRpc(string methodInfoString, byte[] args, ClientRpcParams clientRpcParams = default)
    {
        try
        {
            Debug.Log($"received client RPC for method {methodInfoString}");
            var split = methodInfoString.Split(k_MethodFullNameSplitChar);
            (string classToExecute, string staticMethodToExecute) info = (split[0], split[1]);

            var foundType = Type.GetType(info.classToExecute);
            if (foundType == null)
            {
                throw new Exception($"couldn't find {info.classToExecute}");
            }

            var foundMethod = foundType.GetMethod(info.staticMethodToExecute, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (foundMethod == null)
            {
                throw new Exception($"couldn't find method {info.staticMethodToExecute}");
            }

            foundMethod.Invoke(null, args != null ? new object[] { args } : null);
        }
        catch (Exception e)
        {
            WriteErrorServerRpc(e.Message);
            throw;
        }
    }

    [ClientRpc]
    public void CloseRemoteClientRpc()
    {
        try
        {
            NetworkManager.Singleton.StopClient();
            m_ShouldShutdown = true; // wait until isConnectedClient is false to run Application Quit in next update
            Debug.Log("Quitting player cleanly");
            Application.Quit();
        }
        catch (Exception e)
        {
            WriteErrorServerRpc(e.Message);
            throw;
        }
    }

    private float m_TimeSinceLastConnected;



    public class ExecuteStepInContext : CustomYieldInstruction
    {
        public enum StepExecutionContext
        {
            Server,
            Clients
        }

        [AttributeUsage(AttributeTargets.Method)]
        public class MultiprocessContextBasedTestAttribute : NUnitAttribute, IOuterUnityTestAction
        {
            public IEnumerator BeforeTest(ITest test)
            {
                yield return new WaitUntil(() => Instance != null && Instance.hasRegistered);
            }

            public IEnumerator AfterTest(ITest test)
            {
                yield break;
            }
        }

        private StepExecutionContext m_ActionContext;
        private Action<byte[]> m_StepToExecute;
        public static Dictionary<string, ExecuteStepInContext> allActions = new Dictionary<string, ExecuteStepInContext>();

        private static Dictionary<string, int> s_MethodIDCounter = new Dictionary<string, int>();

        private NetworkManager m_NetworkManager;
        private bool m_IsRegistering;
        private List<Func<bool>> m_RemoteIsFinishedChecks = new List<Func<bool>>();
        private Func<bool> m_AdditionalIsFinishedWaiter;

        private string m_CurrentActionID;

        private bool m_WaitMultipleUpdates;
        private bool m_TimeoutExpected;

        private float m_StartTime;
        private bool isTimingOut => Time.time - m_StartTime > maxWaitTimeout;

        // Assumes this is called from same callsite as ExecuteInContext (and assumes this is called from IEnumerator).
        // This relies on the name to be unique for each generated IEnumerator state machines
        public static void InitContextSteps()
        {
            var callerMethod = new StackFrame(1).GetMethod();
            var methodIdentifier = GetMethodIdentifier(callerMethod.DeclaringType.FullName); // since this is called from IEnumerator, this should be a generated class
            s_MethodIDCounter[methodIdentifier] = 0;
        }

        public static string GetMethodIdentifier(string callerTypeName)
        {
            var info = callerTypeName;
            return info;
        }

        private bool ShouldExecuteLocally => (m_ActionContext == StepExecutionContext.Server && m_NetworkManager.IsServer) || (m_ActionContext == StepExecutionContext.Clients && !m_NetworkManager.IsServer);

        /// <summary>
        /// Executes an action with the specified context. This allows writing tests all in the same sequential flow,
        /// making it more readable. This allows not having to jump between static client methods and test method
        /// </summary>
        /// <param name="actionContext">context to use. for example, should execute client side? server side?</param>
        /// <param name="stepToExecute">action to execute</param>
        /// <param name="timeoutExpected"></param>
        /// <param name="paramToPass">parameters to pass to action</param>
        /// <param name="networkManager"></param>
        /// <param name="waitMultipleUpdates"> waits multiple frames before allowing the execution to continue. This means ClientFinishedServerRpc must be called manually</param>
        /// <param name="additionalIsFinishedWaiter"></param>
        public ExecuteStepInContext(StepExecutionContext actionContext, Action<byte[]> stepToExecute, bool timeoutExpected = false, byte[] paramToPass = default, NetworkManager networkManager = null, bool waitMultipleUpdates = false, Func<bool> additionalIsFinishedWaiter = null)
        {
            m_StartTime = Time.time;
            m_IsRegistering = Instance.isRegistering;
            m_ActionContext = actionContext;
            m_StepToExecute = stepToExecute;
            m_WaitMultipleUpdates = waitMultipleUpdates;
            m_TimeoutExpected = timeoutExpected;
            if (additionalIsFinishedWaiter != null)
            {
                m_AdditionalIsFinishedWaiter = additionalIsFinishedWaiter;
                // m_IsFinishedChecks.Add(additionalIsFinishedWaiter);
            }
            if (networkManager == null)
            {
                networkManager = NetworkManager.Singleton;
            }

            m_NetworkManager = networkManager; // todo test using this for multiinstance tests too?

            var callerMethod = new StackFrame(1).GetMethod(); // one skip frame for current method

            var methodId = GetMethodIdentifier(callerMethod.DeclaringType.FullName); // assumes called from IEnumerator MoveNext, which should be the case since we're a CustomYieldInstruction
            if (!s_MethodIDCounter.ContainsKey(methodId))
            {
                s_MethodIDCounter[methodId] = 0;
            }

            string currentActionID = $"{methodId}-{s_MethodIDCounter[methodId]++}";
            m_CurrentActionID = currentActionID;

            if (m_IsRegistering)
            {
                Debug.Log($"registering action with id {currentActionID}");
                allActions[currentActionID] = this;
            }
            else
            {
                if (ShouldExecuteLocally)
                {
                    m_StepToExecute.Invoke(paramToPass);
                }
                else
                {
                    if (networkManager.IsServer)
                    {
                        Instance.TriggerActionIDClientRpc(currentActionID, paramToPass,
                            clientRpcParams: new ClientRpcParams()
                            {
                                Send = new ClientRpcSendParams() { TargetClientIds = AllClientIdExceptMine.ToArray() }
                            });
                        foreach (var clientId in AllClientIdExceptMine)
                        {
                            m_RemoteIsFinishedChecks.Add(ConsumeClientIsFinished(clientId));
                        }
                    }
                    else
                    {
                        throw new NotImplementedException();
                    }
                }
            }
        }

        public void Invoke(byte[] args)
        {
            m_StepToExecute.Invoke(args);
            if (!m_WaitMultipleUpdates)
            {
                if (!m_NetworkManager.IsServer)
                {
                    Instance.ClientFinishedServerRpc();
                }
                else
                {
                    throw new NotImplementedException("todo implement");
                }
            }
        }

        public override bool keepWaiting
        {
            get
            {
                if (isTimingOut)
                {
                    if (m_TimeoutExpected)
                    {
                        return false;
                    }
                    Assert.Fail($"timeout for Context Step with action ID {m_CurrentActionID}");
                }
                if (m_AdditionalIsFinishedWaiter != null)
                {
                    var isFinished = m_AdditionalIsFinishedWaiter.Invoke();
                    if (!isFinished)
                    {
                        return true;
                    }
                }
                if (m_IsRegistering || ShouldExecuteLocally || m_RemoteIsFinishedChecks == null)
                {

                    return false;
                }

                for (int i = m_RemoteIsFinishedChecks.Count - 1; i >= 0; i--)
                {
                    var waiter = m_RemoteIsFinishedChecks[i];
                    var receivedResponse = waiter.Invoke();
                    if (receivedResponse)
                    {
                        m_RemoteIsFinishedChecks.RemoveAt(i);
                    }
                    else
                    {
                        return true;
                    }
                }

                return false;
            }
        }
    }

}
