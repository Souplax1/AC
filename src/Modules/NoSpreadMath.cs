namespace Modules
{
    /// <summary>
    /// No-spread shows up as a hit that stays on the held view while the weapon cone is wide.
    /// Inaccuracy from the game is the radius of that cone, so the angle is atan(inaccuracy).
    /// </summary>
    public static class NoSpreadMath
    {
        public const float MinConeDegrees = 8f;
        public const float MaxErrorDegrees = 1f;
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

        public static bool IsTightHit(float coneDegrees, float errorDegrees)
        {
            return coneDegrees >= MinConeDegrees && errorDegrees <= MaxErrorDegrees;
        }
    }
}
