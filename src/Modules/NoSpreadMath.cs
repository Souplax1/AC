namespace Modules
{
    /// <summary>
    /// No-spread shows up as a hit that stays on the held view while the shot should have
    /// scattered. Inaccuracy from the game is the cone radius, so the angle is atan(inaccuracy).
    /// A scout is accurate in the air only while horizontal speed is near zero. A hit on the
    /// view while still moving is the same pattern, even when that gun's cone stays small.
    /// </summary>
    public static class NoSpreadMath
    {
        public const float MinConeDegrees = 8f;
        public const float MaxErrorDegrees = 1f;
        public const float MinAirSpeed = 80f;
        public const int HitsToFlag = 3;

        public static float ConeDegrees(float inaccuracy)
        {
            if (inaccuracy <= 0f || float.IsNaN(inaccuracy))
                return 0f;
            return MathF.Atan(inaccuracy) * (180f / MathF.PI);
        }

        public static float DirectionError(float forwardX, float forwardY, float forwardZ, float bulletX, float bulletY, float bulletZ)
        {
            float forwardLength = MathF.Sqrt((forwardX * forwardX) + (forwardY * forwardY) + (forwardZ * forwardZ));
            float bulletLength = MathF.Sqrt((bulletX * bulletX) + (bulletY * bulletY) + (bulletZ * bulletZ));
            if (forwardLength < 0.0001f || bulletLength < 0.0001f)
                return 180f;

            float dot = ((forwardX * bulletX) + (forwardY * bulletY) + (forwardZ * bulletZ)) / (forwardLength * bulletLength);
            dot = Math.Clamp(dot, -1f, 1f);
            return MathF.Acos(dot) * (180f / MathF.PI);
        }

        public static bool IsSuspicious(float coneDegrees, float errorDegrees, bool inAir, float horizontalSpeed)
        {
            if (errorDegrees > MaxErrorDegrees)
                return false;
            if (coneDegrees >= MinConeDegrees)
                return true;
            return inAir && horizontalSpeed >= MinAirSpeed;
        }
    }
}
