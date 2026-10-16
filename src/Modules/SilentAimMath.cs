namespace Modules
{
    /// <summary>
    /// Angle checks for silent aim. The shot angle of a real bullet stays on the
    /// path between the view sample before it and the view sample after it.
    /// </summary>
    public static class SilentAimMath
    {
        public const float SuspiciousExcessDegrees = 20f;
        public const float SnapJumpDegrees = 20f;
        public const float SnapReturnDegrees = 8f;
        public const float SnapLeaveDegrees = 15f;

        public static float WrapYaw(float degrees)
        {
            degrees %= 360f;
            if (degrees > 180f)
                degrees -= 360f;
            else if (degrees < -180f)
                degrees += 360f;
            return degrees;
        }

        public static float AngleDelta(float pitchA, float yawA, float pitchB, float yawB)
        {
            float pitch = pitchA - pitchB;
            float yaw = WrapYaw(yawA - yawB);
            return MathF.Sqrt((pitch * pitch) + (yaw * yaw));
        }

        /// <summary>
        /// How far the shot angle sits off the segment between the surrounding view angles.
        /// Zero when the shot is on that path. About twice the miss distance when it is not.
        /// </summary>
        public static float PathExcess(float beforePitch, float beforeYaw, float shotPitch, float shotYaw, float afterPitch, float afterYaw)
        {
            float span = AngleDelta(beforePitch, beforeYaw, afterPitch, afterYaw);
            float toBefore = AngleDelta(shotPitch, shotYaw, beforePitch, beforeYaw);
            float toAfter = AngleDelta(shotPitch, shotYaw, afterPitch, afterYaw);
            return toBefore + toAfter - span;
        }

        public static bool IsSuspiciousExcess(float excess)
        {
            return excess > SuspiciousExcessDegrees;
        }

        /// <summary>
        /// True when the view jumped to the shot and the next command is back on the old view.
        /// A real flick stays near the shot angle.
        /// </summary>
        public static bool IsSnapBack(float prePitch, float preYaw, float shotPitch, float shotYaw, float nowPitch, float nowYaw)
        {
            float jump = AngleDelta(prePitch, preYaw, shotPitch, shotYaw);
            float back = AngleDelta(prePitch, preYaw, nowPitch, nowYaw);
            float stayed = AngleDelta(shotPitch, shotYaw, nowPitch, nowYaw);
            return jump >= SnapJumpDegrees && back <= SnapReturnDegrees && stayed >= SnapLeaveDegrees;
        }
    }
}
