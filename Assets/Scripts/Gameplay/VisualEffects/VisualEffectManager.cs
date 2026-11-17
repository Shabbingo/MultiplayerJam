using UnityEngine;
using System.Collections.Generic;
using System;
using Unity.Mathematics;
using Unity.NetCode;

namespace Unity.MP_FPS
{
    public struct ClientSpawnVfxRpc : IRpcCommand
    {
        public int OwnerNetworkId;
        public uint WeaponId;
    }

    public struct ClientHitConfirmedRpc : IRpcCommand
    {
        public int AttackerNetworkId;
    }

    public struct ClientSpawnPlayerExplosionRpc : IRpcCommand
    {
        public float3 Position;
    }

    public struct ClientProjectileImpactSoundRpc : IRpcCommand
    {
        public float3 Position;
        public byte SizeTier;
    }

    public class VisualEffectManager : GhostSingleton<VisualEffectManager>, IUpdateServer, IUpdateClient, IGhostManager
    {
        private Queue<ClientSpawnVfxRpc> _vfxQueue = new();
        private Queue<ClientSpawnPlayerExplosionRpc> m_PlayerExplosionQueue = new();
        private Queue<ClientProjectileImpactSoundRpc> m_ProjectileImpactSoundQueue = new();
        private Queue<ClientHitConfirmedRpc> m_HitConfirmationQueue = new();
        private HashSet<ulong> m_PendingHitConfirmationKeys = new();

        [SerializeField] private GameObject m_PlayerExplosionPrefab;
        [SerializeField, Tooltip("Non-spatial confirmation played only for the player whose attack hit an opponent.")]
        private SoundDef m_HitMarkerSound;
        [SerializeField, Tooltip("Spatial impact sound for projectiles below the normal size range.")]
        private SoundDef m_SmallProjectileImpactSound;
        [SerializeField, Tooltip("Spatial impact sound for normal-sized projectiles.")]
        private SoundDef m_MediumProjectileImpactSound;
        [SerializeField, Tooltip("Spatial impact sound for projectiles above the normal size range.")]
        private SoundDef m_LargeProjectileImpactSound;
        [SerializeField, Range(0.1f, 1f),
         Tooltip("Base muzzle flash size before it is scaled by the firing player's size.")]
        private float m_MuzzleFlashScale = 0.5f;

        public void Server_RequestVfx(int ownerNetworkId, uint weaponId)
        {
            _vfxQueue.Enqueue(new ClientSpawnVfxRpc
            {
                OwnerNetworkId = ownerNetworkId,
                WeaponId = weaponId
            });
        }

        public void Server_RequestHitConfirmation(int attackerNetworkId,
            uint hitTick)
        {
            // Scatter shots and area effects can damage several players during the
            // same simulation tick. Present one clean confirmation for the attack.
            ulong key = ((ulong)(uint)attackerNetworkId << 32) | hitTick;
            if (!m_PendingHitConfirmationKeys.Add(key))
            {
                return;
            }

            m_HitConfirmationQueue.Enqueue(new ClientHitConfirmedRpc
            {
                AttackerNetworkId = attackerNetworkId
            });
        }

        public void Server_RequestPlayerExplosion(Vector3 position)
        {
            m_PlayerExplosionQueue.Enqueue(new ClientSpawnPlayerExplosionRpc
            {
                Position = position
            });
        }

        public void Server_RequestProjectileImpactSound(Vector3 position,
            byte sizeTier)
        {
            m_ProjectileImpactSoundQueue.Enqueue(new ClientProjectileImpactSoundRpc
            {
                Position = position,
                SizeTier = sizeTier
            });
        }

        public void UpdateServer(float deltaTime)
        {
            while (_vfxQueue.Count > 0)
            {
                var vfxRequest = _vfxQueue.Dequeue();
                GhostGameObject.BroadcastRPC(vfxRequest);
            }

            while (m_PlayerExplosionQueue.Count > 0)
            {
                GhostGameObject.BroadcastRPC(m_PlayerExplosionQueue.Dequeue());
            }

            while (m_ProjectileImpactSoundQueue.Count > 0)
            {
                GhostGameObject.BroadcastRPC(m_ProjectileImpactSoundQueue.Dequeue());
            }

            while (m_HitConfirmationQueue.Count > 0)
            {
                GhostGameObject.BroadcastRPC(m_HitConfirmationQueue.Dequeue());
            }
            m_PendingHitConfirmationKeys.Clear();
        }

        public void UpdateClient(float deltaTime)
        {
            int localPlayerNetworkId = GetLocalPlayerNetworkId();

            while (GhostGameObject.ConsumeRPC(out ClientHitConfirmedRpc hitRpc))
            {
                if (hitRpc.AttackerNetworkId == localPlayerNetworkId &&
                    m_HitMarkerSound != null &&
                    GameManager.Instance?.SoundSystem != null)
                {
                    // The confirmation belongs to the listener, not the impact
                    // location, so its SoundDef is configured as a 2D sound.
                    GameManager.Instance.SoundSystem.CreateEmitter(
                        m_HitMarkerSound, Vector3.zero);
                }
            }

            while (GhostGameObject.ConsumeRPC(out ClientSpawnPlayerExplosionRpc explosionRpc))
            {
                if (m_PlayerExplosionPrefab != null)
                {
                    Instantiate(m_PlayerExplosionPrefab, (Vector3)explosionRpc.Position,
                        Quaternion.identity);
                }
            }

            while (GhostGameObject.ConsumeRPC(out ClientProjectileImpactSoundRpc impactSoundRpc))
            {
                SoundDef impactSound = m_MediumProjectileImpactSound;
                if (impactSoundRpc.SizeTier == 0)
                {
                    impactSound = m_SmallProjectileImpactSound;
                }
                else if (impactSoundRpc.SizeTier >= 2)
                {
                    impactSound = m_LargeProjectileImpactSound;
                }

                if (impactSound != null && GameManager.Instance?.SoundSystem != null)
                {
                    GameManager.Instance.SoundSystem.CreateEmitter(
                        impactSound, (Vector3)impactSoundRpc.Position);
                }
            }

            while (GhostGameObject.ConsumeRPC(out ClientSpawnVfxRpc rpc))
            {
                // If the RPC is for the local player, skip it.
                // The local player already spawned their own VFX in the prediction system.
                if (localPlayerNetworkId != -1 && localPlayerNetworkId == rpc.OwnerNetworkId)
                {
                    continue;
                }

                // This is for a remote player. Find them and spawn the effect.
                if (PlayerGhostManager.TryGetInstanceByRole(MultiplayerRole.ClientAll, out var allPlayersManager) &&
                    allPlayersManager.TryGetPlayersByRole(MultiplayerRole.ClientAll, out var allPlayers) && allPlayers.Count > 0)
                {
                    foreach (var player in allPlayers)
                    {
                        if (player.GhostGameObject.Owner == rpc.OwnerNetworkId)
                        {
                            // This reliable RPC represents one accepted server shot.
                            // Play at a world position instead of parenting a pooled
                            // AudioSource beneath the animated remote hierarchy.
                            Transform shotOrigin = player.GetVisualShotOrigin(
                                rpc.WeaponId, false);
                            Vector3 soundPosition = shotOrigin != null
                                ? shotOrigin.position
                                : player.transform.position;
                            PlayWeaponFireSound(
                                rpc.WeaponId, soundPosition, 1.25f);

                            // Audio was handled synchronously above; the muzzle
                            // visual can continue loading asynchronously.
                            SpawnMuzzleFlash(player, rpc.WeaponId, false, false);
                            break;
                        }
                    }
                }
            }
        }

        private static int GetLocalPlayerNetworkId()
        {
            if (PlayerGhostManager.TryGetInstanceByRole(
                    MultiplayerRole.ClientOwned, out var playerManager) &&
                playerManager.TryGetPlayersByRole(
                    MultiplayerRole.ClientOwned, out var players) &&
                players.Count > 0)
            {
                var localPlayer = players[0];
                if (localPlayer != null && localPlayer.GhostGameObject != null)
                {
                    return localPlayer.GhostGameObject.Owner;
                }
            }

            return -1;
        }

        public static void PlayWeaponFireSound(uint weaponId, Vector3 position,
            float volume)
        {
            var weaponData = WeaponManager.Instance?.WeaponRegistry?
                .GetWeaponData(weaponId);
            if (weaponData?.WeaponFireSfx != null &&
                GameManager.Instance?.SoundSystem != null)
            {
                GameManager.Instance.SoundSystem.CreateEmitter(
                    weaponData.WeaponFireSfx, position, volume);
            }
        }

        public async void SpawnMuzzleFlash(PlayerGhost player, uint weaponId,
            bool isFirstPerson, bool playAudio = true)
        {
            try
            {
                if (player == null)
                {
                    Debug.Log("Cannot spawn muzzle flash: player is null");
                    return;
                }

                var weaponData = WeaponManager.Instance.WeaponRegistry.GetWeaponData(weaponId);
                if (weaponData == null)
                {
                    Debug.Log("Cannot spawn muzzle flash: weapon data is null");
                    return;
                }

                var spawnPoint = player.GetVisualShotOrigin(weaponId, isFirstPerson);
                if (spawnPoint == null)
                {
                    spawnPoint = player.transform; // Fallback
                }

                // Audio is gameplay feedback, so do not make it wait for the
                // asynchronous muzzle-flash load. Remote shots also receive a
                // small boost so they remain readable over the local weapon mix.
                if (playAudio)
                {
                    float shotVolume = isFirstPerson ? 1f : 1.25f;
                    PlayWeaponFireSound(weaponId, spawnPoint.position, shotVolume);
                }

                try
                {
                    var vfxInstance = await weaponData.MuzzleFlashVfxPrefab.GhostPrefab.InstantiateAsync(
                        spawnPoint.position, spawnPoint.rotation, spawnPoint).Task;
                    if (vfxInstance == null)
                    {
                        Debug.LogWarning("Cannot spawn muzzle flash: vfx instance is null");
                        return;
                    }

                    vfxInstance.transform.localScale *= m_MuzzleFlashScale;
                    vfxInstance.AddComponent<DestroyAfterDelay>().Lifetime = 0.5f;

                    vfxInstance.SetActive(true);
                }
                catch (Exception e)
                {
                    Debug.LogError($"Failed to spawn muzzle flash: {e.Message}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError("Error in SpawnMuzzleFlash: " + e.Message);
            }
        }

        private static void DrawGizmoAtPosition(Vector3 position)
        {
            var color = Color.red;
            const float duration = 4.0f; // How long the gizmo will be visible in seconds
            const float size = 0.5f; // The length of the lines for the cross marker

            Debug.DrawRay(position - Vector3.up * size, Vector3.up * size * 2, color, duration);
            Debug.DrawRay(position - Vector3.right * size, Vector3.right * size * 2, color, duration);
            Debug.DrawRay(position - Vector3.forward * size, Vector3.forward * size * 2, color, duration);
        }
    }
}
