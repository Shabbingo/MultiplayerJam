using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

namespace Unity.MP_FPS
{
    public struct MapSelectionRequestRpc : IRpcCommand
    {
    }

    public struct MapSelectionResponseRpc : IRpcCommand
    {
        public FixedString64Bytes SceneName;
    }

    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct MapSelectionResponseSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            var commandBuffer = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (receiveRequest, requestEntity) in
                     SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>>()
                         .WithAll<MapSelectionRequestRpc>()
                         .WithEntityAccess())
            {
                Entity responseEntity = commandBuffer.CreateEntity();
                commandBuffer.AddComponent(responseEntity,
                    new MapSelectionResponseRpc
                    {
                        SceneName = GameManager.ActiveServerMapSceneName
                    });
                commandBuffer.AddComponent(responseEntity,
                    new SendRpcCommandRequest
                    {
                        TargetConnection = receiveRequest.ValueRO.SourceConnection
                    });
                commandBuffer.DestroyEntity(requestEntity);
            }

            commandBuffer.Playback(state.EntityManager);
            commandBuffer.Dispose();
        }
    }
}
