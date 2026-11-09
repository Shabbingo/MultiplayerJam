using Unity.Entities;
using Unity.MP_FPS.Cards;
using UnityEngine;

[DisallowMultipleComponent]
public class PredictedPlayerGhostAuthoring : MonoBehaviour
{
    public float DisabledPredictionLerpFactor = 10f;
}

public class PredictedPlayerGhostBaker : Baker<PredictedPlayerGhostAuthoring>
{
    public override void Bake(PredictedPlayerGhostAuthoring authoring)
    {
        var entity = GetEntity(TransformUsageFlags.None);
        AddComponent(entity, new PredictedPlayerGhost { DisabledPredictionLerpFactor = authoring.DisabledPredictionLerpFactor });
        AddComponent<PlayerModifierStacks>(entity);
        AddBuffer<PredictedPlayerGhostState>(entity);

        AddComponent<PlayerInputComponent>(entity);
    }
}
