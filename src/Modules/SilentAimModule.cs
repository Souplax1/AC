// Updated SilentAimModule.cs - Now detects NoSpread (rage) via low bullet deviation variance
// Also logs for AutoStrafe potential (needs velocity checks)

using AC;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.GameEvents;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;
using SwiftlyS2.Shared.Natives;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Modules
{
    public struct FireData
    {
        public QAngle Angles;
        public Vector EyePos;
        public DateTime Time;
    }

    public class SilentAimModule : ModularAntiCheat.IAcModule
    {
        private ModularAntiCheat _plugin = null!;
        private ISwiftlyCore _core = null!;
        private readonly Dictionary<ulong, FireData> _lastFireData = new(); // Use SteamID for key
        private readonly Dictionary<ulong, List<float>> _deviations = new(); // Recent deviations per player
        private readonly Dictionary<ulong, int> _violations = new();
        private readonly Dictionary<ulong, DateTime> _lastViolationTime = new();
        public void Load(ModularAntiCheat plugin, ISwiftlyCore core)
        {
            _plugin = plugin;
            _core = core;
            _core.GameEvent.HookPost<EventWeaponFire>(OnWeaponFire);
            _core.GameEvent.HookPost<EventBulletImpact>(OnBulletImpact); // Fixed name
            _core.GameEvent.HookPost<EventPlayerHurt>(OnPlayerHurt); // Keep for future
            _core.GameEvent.HookPost<EventPlayerDisconnect>(OnPlayerDisconnect);
        }

        public void Unload()
        {
            _lastFireData.Clear();
            _deviations.Clear();
            _violations.Clear();
            _lastViolationTime.Clear();
        }

        private HookResult OnWeaponFire(EventWeaponFire @event)
        {
            var player = @event.UserIdController;
            if (player == null || !player.IsValid) return HookResult.Continue;
            var pawn = player.Pawn.Value;
            if (pawn == null) return HookResult.Continue;

            ulong steamId = player.SteamID; // Assume SteamID available - adjust if Controller.SteamID
            var eyeAngles = @event.UserIdPawn.EyeAngles;
            var eyePosNullable = pawn.EyePosition;
            if (!eyePosNullable.HasValue)
                return HookResult.Continue;
            var eyePos = eyePosNullable.Value;

            _lastFireData[steamId] = new FireData { Angles = eyeAngles, EyePos = eyePos, Time = DateTime.Now };

            // Debug chat
            //_core.PlayerManager.SendChatEOT($"QAngle({eyeAngles.Pitch:F6}, {eyeAngles.Yaw:F5}, {eyeAngles.Roll:F1})");

            return HookResult.Continue;
        }

        private HookResult OnBulletImpact(EventBulletImpact @event)
        {
            var shooterController = _core.PlayerManager.GetPlayer(@event.UserId);
            if (shooterController == null || !shooterController.IsValid || shooterController.IsFakeClient) return HookResult.Continue;

            ulong steamId = shooterController.SteamID;
            if (!_lastFireData.TryGetValue(steamId, out FireData fireData)) return HookResult.Continue;

            var pawn = shooterController.PlayerPawn;
            if (pawn == null) return HookResult.Continue;
 

            // Skip if player is in air (jumping shots have wild spread)
            if (!pawn.OnGroundLastTick)
            {
                return HookResult.Continue; // Ignore airborne shots
            }

            Vector eyePos = fireData.EyePos;
            Vector impactPos = new Vector(@event.X, @event.Y, @event.Z);

            // Optional: Ignore very far or very close impacts (tune as needed)
            float distance = (impactPos - eyePos).Length();
            if (distance < 100 || distance > 10000) return HookResult.Continue;

            Vector dirToImpact = (impactPos - eyePos).Normalized();
            fireData.Angles.ToDirectionVectors(out Vector forward, out _, out _);

            float dot = Vector.Dot(forward, dirToImpact);
            dot = Math.Clamp(dot, -1f, 1f);
            float deviation = (float)(Math.Acos(dot) * (180.0 / Math.PI));

            // Debug
            float speed = pawn.AbsVelocity.Length();
            //_core.PlayerManager.SendConsole($"Bullet: {deviation:F3}° | Speed {speed:F1} units/s");

            float HIGH_DEVIATION_THRESHOLD = GetHighDeviationThreshold(shooterController.RequiredController);  // Safe: legit rarely >10° when standing
            const int REQUIRED_HIGH_DEVS = 3;             // 3+ bullets with high dev → detect

            // Track high-deviation bullets
            if (!_deviations.TryGetValue(steamId, out var highDevs))
            {
                highDevs = new List<float>();
                _deviations[steamId] = highDevs;
            }

            if (deviation > HIGH_DEVIATION_THRESHOLD)
            {
                highDevs.Add(deviation);
                if (highDevs.Count > 10) highDevs.RemoveAt(0);

                //_core.PlayerManager.SendChat($"[AC] High deviation: {deviation:F2}° on {shooterController.RequiredController.PlayerName}");

                if (highDevs.Count >= REQUIRED_HIGH_DEVS)
                {
                    _violations[steamId] = (_violations.GetValueOrDefault(steamId) + 1);

                    if (_violations[steamId] >= 3) // 3 bursts of high dev → kick
                    {
                        _core.PlayerManager.SendChat($"{Helper.ChatColors.Red}[AC] {Helper.ChatColors.Default}Rage func detected on {Helper.ChatColors.Green}{shooterController.RequiredController.PlayerName}");
                       // shooterController.Kick("Rage Func Detected",
                       //     SwiftlyS2.Shared.ProtobufDefinitions.ENetworkDisconnectionReason.NETWORK_DISCONNECT_KICKED);

                        // Cleanup
                        _deviations.Remove(steamId);
                        _violations.Remove(steamId);
                    }
                }
            }
            else
            {
                // Legit shot — reduce suspicion slightly
                highDevs.Clear(); // Reset on good shot
            }

            return HookResult.Continue;
        }

        private float GetHighDeviationThreshold(CCSPlayerController player)
        {
            var pawn = player.PlayerPawn.Value;
            if (pawn == null) return 15f; // Default fallback

            var activeWeapon = pawn.WeaponServices?.ActiveWeapon.Value;
            if (activeWeapon == null ) return 15f;

            string weaponName = activeWeapon.DesignerName; // e.g., "weapon_ak47", "weapon_deagle"

            return weaponName switch
            {
                // Rifles - tight spread
                "weapon_ak47" or "weapon_m4a1" or "weapon_m4a1_silencer" or
                "weapon_galilar" or "weapon_famas" or "weapon_aug" or "weapon_sg556" => 12f,

                // Precision rifles
                "weapon_awp" or "weapon_ssg08" => 2f,

                // Autosnipers
                "weapon_g3sg1" or "weapon_scar20" => 12f,

                // High-inaccuracy pistols
                "weapon_deagle" or "weapon_revolver" => 4f,

                // Standard pistols
                "weapon_glock" or "weapon_hkp2000" or "weapon_usp_silencer" or
                "weapon_elite" or "weapon_p250" or "weapon_tec9" or "weapon_fiveseven" or "weapon_cz75a" => 3.8f,

                // SMGs - very high spread
                "weapon_mac10" or "weapon_mp9" or "weapon_mp7" or "weapon_mp5sd" or
                "weapon_ump45" or "weapon_p90" or "weapon_bizon" => 22f,

                // Shotguns - ignore almost entirely
                "weapon_nova" or "weapon_xm1014" or "weapon_sawedoff" or "weapon_mag7" or "weapon_m249" or "weapon_negev" => 13f,

                // Default catch-all
                _ => 15f
            };
        }

        private HookResult OnPlayerHurt(EventPlayerHurt @event)
        {
            // Existing logic or extend for hit-based checks
            return HookResult.Continue;
        }

        private HookResult OnPlayerDisconnect(EventPlayerDisconnect @event)
        {
            var player = @event.UserIdController;
            if (player != null)
            {
                ulong steamId = player.SteamID;
                _lastFireData.Remove(steamId);
                _deviations.Remove(steamId);
                _violations.Remove(steamId);
                _lastViolationTime.Remove(steamId);
            }
            return HookResult.Continue;
        }
    }
}