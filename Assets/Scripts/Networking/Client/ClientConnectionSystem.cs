using UnityEngine;
using Unity.Entities;
using Unity.NetCode;

namespace Unity.MP_FPS
{
    /// <summary>
    /// This system will connect and re-connect a client to the server
    /// as long as the <see cref="ConnectionSettings.ConnectionState"/> is Connecting or Connected.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class NetcodeClientConnectionSystem : SystemBase
    {
        const float ConnectionAttemptTimeoutSeconds = 15f;

        bool m_ReturningToMenuAfterDisconnect;
        float m_ConnectionAttemptStartedAt = -1f;

        protected override void OnUpdate()
        {
            CompleteDependency();

            if (ConnectionSettings.Instance.GameConnectionState == ConnectionState.State.Connected ||
                ConnectionSettings.Instance.GameConnectionState == ConnectionState.State.Connecting)
            {
                if (ConnectionSettings.Instance.GameConnectionState == ConnectionState.State.Connecting)
                {
                    if (m_ConnectionAttemptStartedAt < 0f)
                    {
                        m_ConnectionAttemptStartedAt = UnityEngine.Time.realtimeSinceStartup;
                    }
                    else if (UnityEngine.Time.realtimeSinceStartup - m_ConnectionAttemptStartedAt >=
                             ConnectionAttemptTimeoutSeconds)
                    {
                        HandleUnexpectedServerDisconnect("Connection Timed Out");
                        return;
                    }
                }

                ref var driver = ref SystemAPI.GetSingletonRW<NetworkStreamDriver>().ValueRW;
                foreach (var evt in driver.ConnectionEventsForTick)
                {
                    if (evt.State == ConnectionState.State.Disconnected)
                    {
                        HandleUnexpectedServerDisconnect(GetDisconnectMessage(
                            evt.DisconnectReason,
                            ConnectionSettings.Instance.GameConnectionState == ConnectionState.State.Connected));
                        return;
                    }
                }

                bool hasNetworkStreamConnectionSingleton = 
                    SystemAPI.TryGetSingleton(out NetworkStreamConnection connection);
                
                if (hasNetworkStreamConnectionSingleton)
                {
                    ConnectionSettings.Instance.GameConnectionState =
                        connection.CurrentState == ConnectionState.State.Connected
                            ? ConnectionState.State.Connected
                            : ConnectionState.State.Connecting;

                    if (connection.CurrentState == ConnectionState.State.Connected)
                    {
                        m_ConnectionAttemptStartedAt = -1f;
                    }
                }
                else
                {
                    //If it just lost connection (GameConnectionState is still connected), return to main menu
                    if (ConnectionSettings.Instance.GameConnectionState == ConnectionState.State.Connected)
                    {
                        HandleUnexpectedServerDisconnect("Server Closed");
                        return;
                    }

                    //Try to connect to the server
                    if (connection.CurrentState == ConnectionState.State.Unknown)
                    {
                        ConnectionSettings.Instance.GameConnectionState = ConnectionState.State.Connecting;
                        if (UnityEngine.Time.frameCount % 120 == 0) 
                        {
                            var networkEndpoint = ConnectionSettings.Instance.ConnectionEndpoint;
                            Debug.Log($"[{World.Name}] Reconnecting to {networkEndpoint.ToString()}...");
                            driver.Connect(EntityManager, networkEndpoint);
                        }
                    }
                }
            }            
        }

        static string GetDisconnectMessage(NetworkStreamDisconnectReason reason,
            bool wasConnected)
        {
            switch (reason)
            {
                case NetworkStreamDisconnectReason.Timeout:
                    return wasConnected
                        ? "Connection Lost - check your internet connection"
                        : "Connection Timed Out";
                case NetworkStreamDisconnectReason.MaxConnectionAttempts:
                case NetworkStreamDisconnectReason.HandshakeTimeout:
                case NetworkStreamDisconnectReason.ApprovalTimeout:
                    return "Connection Timed Out";
                case NetworkStreamDisconnectReason.BadProtocolVersion:
                case NetworkStreamDisconnectReason.InvalidRpc:
                case NetworkStreamDisconnectReason.ProtocolError:
                    return "Client/server version mismatch";
                case NetworkStreamDisconnectReason.AuthenticationFailure:
                case NetworkStreamDisconnectReason.ApprovalFailure:
                    return "Connection rejected by server";
                case NetworkStreamDisconnectReason.ClosedByRemote:
                    return "Server Closed";
                default:
                    return wasConnected ? "Connection Lost" : "Unable to connect to server";
            }
        }

        void HandleUnexpectedServerDisconnect(string message)
        {
            if (m_ReturningToMenuAfterDisconnect)
                return;

            m_ReturningToMenuAfterDisconnect = true;

            // Mark the connection offline before starting asynchronous teardown. This
            // prevents this client system from reconnecting while the menu is loading.
            ConnectionSettings.Instance.GameConnectionState = ConnectionState.State.Disconnected;
            if (string.IsNullOrWhiteSpace(ConnectionSettings.Instance.DisconnectMessage))
            {
                ConnectionSettings.Instance.ShowDisconnectMessage(message);
            }
            Debug.LogWarning($"[Client] {message}. Returning to the main menu.");

            if (GameManager.Instance != null)
            {
                GameManager.Instance.ReturnToMainMenuAsync();
            }
            else
            {
                Debug.LogError("[Client] Cannot return to the main menu because GameManager is unavailable.");
            }
        }
    }
}
