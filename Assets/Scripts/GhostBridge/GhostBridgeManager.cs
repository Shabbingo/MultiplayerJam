using System;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace Unity.GhostBridge
{

    public class GhostBridgeManager : MonoBehaviour
    {
        public static GhostBridgeManager Instance { get; private set; } = null;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        void Start()
        {
            DontDestroyOnLoad(this);
        }

#region Server functions
        public void SetServerNetworkStreamDriver(NetworkStreamDriver serverNetworkStreamDriver)
        {
            // Kept for compatibility with the existing startup paths. Do not cache
            // NetworkStreamDriver here: it contains native pointers owned by its ECS
            // world and becomes unsafe as soon as that world is disposed/recreated.
        }
        
        public bool IsServerListening()
        {
            if (!TryGetServerEntityManager(out var manager))
            {
                return false;
            }

            using var driverQuery = manager.CreateEntityQuery(ComponentType.ReadOnly<NetworkStreamDriver>());
            if (driverQuery.IsEmptyIgnoreFilter)
            {
                return false;
            }

            var networkStreamDriver = driverQuery.GetSingleton<NetworkStreamDriver>();
            var driverStore = networkStreamDriver.DriverStore;
            if (driverStore.IsCreated && driverStore.DriversCount > 0)
            {
                int driverId = driverStore.FirstDriver;
                return driverStore.GetDriverInstanceRO(driverId).driver.IsCreated &&
                       driverStore.GetDriverInstanceRO(driverId).driver.Listening;
            }
            return false;
        }

        public bool TryGetServerEntityManager(out EntityManager manager)
        {
            World serverWorld = null;
            foreach (var world in World.All)
            {
                if ((world.Flags & WorldFlags.GameServer) == WorldFlags.GameServer)
                {
                    serverWorld = world;
                    break;
                }
            }

            if (serverWorld != null && serverWorld.IsCreated)
            {
                manager = serverWorld.EntityManager;
                return true;
            }
            else
            {
                manager = default(EntityManager);
                return false;    
            }
        }
#endregion End of Server functions
        
#region Client functions
        public struct LocalPlayerInfo
        {
            public FixedString64Bytes PlayerName;
            public uint InputUserId;
        };
        
        [System.NonSerialized]
        public LocalPlayerInfo LocalPlayer;
    }
#endregion End of Client functions    
}
