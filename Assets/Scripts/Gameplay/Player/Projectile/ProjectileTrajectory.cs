using Unity.Mathematics;

namespace Unity.MP_FPS
{
    /// <summary>
    /// Immutable motion segment evaluated from authoritative spawn data.
    /// Keeping this calculation free of frame delta accumulation makes the
    /// server and every client sample the same path for a given network time.
    /// </summary>
    public readonly struct ProjectileTrajectory
    {
        public ProjectileTrajectory(float3 origin, float3 initialVelocity,
            float3 acceleration)
        {
            Origin = origin;
            InitialVelocity = initialVelocity;
            Acceleration = acceleration;
        }

        public float3 Origin { get; }
        public float3 InitialVelocity { get; }
        public float3 Acceleration { get; }

        public float3 EvaluatePosition(float elapsedSeconds)
        {
            float time = math.max(0f, elapsedSeconds);
            return Origin + InitialVelocity * time +
                   0.5f * Acceleration * time * time;
        }

        public float3 EvaluateVelocity(float elapsedSeconds)
        {
            return InitialVelocity + Acceleration * math.max(0f, elapsedSeconds);
        }

        public static float ElapsedSeconds(uint currentTick,
            float currentTickFraction, uint startTick, float fixedTimeStep)
        {
            int wholeTicks = unchecked((int)(currentTick - startTick));
            if (wholeTicks < 0)
            {
                return 0f;
            }

            float ticks = wholeTicks + math.clamp(currentTickFraction, 0f, 1f);
            return ticks * math.max(0f, fixedTimeStep);
        }
    }
}
