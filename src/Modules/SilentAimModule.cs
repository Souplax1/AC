using AC;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.GameEvents;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.ProtobufDefinitions;
using SwiftlyS2.Shared.SchemaDefinitions;
using System;
using System.Collections.Generic;

namespace Modules
{
    public class SilentAimModule : ModularAntiCheat.IAcModule
    {
        private const int StrikesToFlag = 2;
        private const int SuspicionWindowCommands = 64;
        private const int ReportCooldownCommands = 48;

        private ISwiftlyCore _core = null!;
        private Guid _disconnectHook;

        private readonly Dictionary<ulong, ViewSample> _lastView = new();
        private readonly Dictionary<ulong, Suspicion> _suspicion = new();
        private readonly Dictionary<ulong, SnapWatch> _snap = new();

        public void Load(ModularAntiCheat plugin, ISwiftlyCore core)
        {
            _core = core;
            _core.Event.OnClientProcessUsercmds += OnUsercmds;
            _disconnectHook = _core.GameEvent.HookPost<EventPlayerDisconnect>(OnPlayerDisconnect);
        }

        public void Unload()
        {
            _core.Event.OnClientProcessUsercmds -= OnUsercmds;
            _core.GameEvent.Unhook(_disconnectHook);
            _lastView.Clear();
            _suspicion.Clear();
            _snap.Clear();
        }

        private void OnUsercmds(IOnClientProcessUsercmdsEvent args)
        {
            var player = _core.PlayerManager.GetPlayer(args.PlayerId);
            if (player == null || !player.IsValid || player.IsFakeClient)
                return;

            ulong steamId = player.SteamID;
            if (steamId == 0)
                return;

            foreach (CSGOUserCmdPB cmd in args.Usercmds)
            {
                CBaseUserCmdPB? basis = cmd.Base;
                if (basis == null)
                    continue;

                QAngle view = basis.Viewangles;
                bool attacking = IsAttacking(basis);

                if (_snap.TryGetValue(steamId, out SnapWatch watch))
                {
                    if (SilentAimMath.IsSnapBack(watch.PrePitch, watch.PreYaw, watch.ShotPitch, watch.ShotYaw, view.X, view.Y))
                        AddStrike(player, steamId);
                    _snap.Remove(steamId);
                }

                if (attacking && _lastView.TryGetValue(steamId, out ViewSample previous))
                {
                    bool haveShot = TryGetShotAngles(cmd, out QAngle shot) && HasAngle(shot, view);
                    if (haveShot)
                    {
                        Sample(cmd, previous, view, out float beforePitch, out float beforeYaw, out float afterPitch, out float afterYaw);
                        float excess = SilentAimMath.PathExcess(beforePitch, beforeYaw, shot.X, shot.Y, afterPitch, afterYaw);
                        if (SilentAimMath.IsSuspiciousExcess(excess))
                            AddStrike(player, steamId);
                    }

                    float aimPitch = haveShot ? shot.X : view.X;
                    float aimYaw = haveShot ? shot.Y : view.Y;
                    float jump = SilentAimMath.AngleDelta(previous.Pitch, previous.Yaw, aimPitch, aimYaw);
                    if (jump >= SilentAimMath.SnapJumpDegrees)
                    {
                        _snap[steamId] = new SnapWatch
                        {
                            PrePitch = previous.Pitch,
                            PreYaw = previous.Yaw,
                            ShotPitch = aimPitch,
                            ShotYaw = aimYaw,
                        };
                    }
                }

                Touch(steamId);
                _lastView[steamId] = new ViewSample { Pitch = view.X, Yaw = view.Y };
            }
        }

        private static bool IsAttacking(CBaseUserCmdPB basis)
        {
            CInButtonStatePB? buttons = basis.ButtonsPb;
            if (buttons == null)
                return false;
            return (buttons.Buttonstate1 & (ulong)InputBitMask_t.IN_ATTACK) != 0;
        }

        private static bool TryGetShotAngles(CSGOUserCmdPB cmd, out QAngle shot)
        {
            shot = default;
            var history = cmd.InputHistory;
            if (history == null)
                return false;

            int index = cmd.Attack1StartHistoryIndex;
            if (index < 0 || index >= history.Count)
                return false;

            shot = history.Get(index).ViewAngles;
            return true;
        }

        // Missing protobuf angles arrive as 0,0,0. That is a real aim only when the
        // player is actually looking there.
        private static bool HasAngle(QAngle angle, QAngle view)
        {
            bool zero = MathF.Abs(angle.X) < 0.001f && MathF.Abs(angle.Y) < 0.001f;
            if (!zero)
                return true;
            return SilentAimMath.AngleDelta(0f, 0f, view.X, view.Y) <= 2f;
        }

        private static void Sample(CSGOUserCmdPB cmd, ViewSample previous, QAngle view, out float beforePitch, out float beforeYaw, out float afterPitch, out float afterYaw)
        {
            int index = cmd.Attack1StartHistoryIndex;
            var history = cmd.InputHistory;
            if (history == null)
            {
                beforePitch = previous.Pitch;
                beforeYaw = previous.Yaw;
                afterPitch = view.X;
                afterYaw = view.Y;
                return;
            }

            if (index > 0)
            {
                QAngle before = history.Get(index - 1).ViewAngles;
                if (HasAngle(before, view))
                {
                    beforePitch = before.X;
                    beforeYaw = before.Y;
                }
                else
                {
                    beforePitch = previous.Pitch;
                    beforeYaw = previous.Yaw;
                }
            }
            else
            {
                beforePitch = previous.Pitch;
                beforeYaw = previous.Yaw;
            }

            if (index + 1 < history.Count)
            {
                QAngle after = history.Get(index + 1).ViewAngles;
                if (HasAngle(after, view))
                {
                    afterPitch = after.X;
                    afterYaw = after.Y;
                }
                else
                {
                    afterPitch = view.X;
                    afterYaw = view.Y;
                }
            }
            else
            {
                afterPitch = view.X;
                afterYaw = view.Y;
            }
        }

        private void AddStrike(SwiftlyS2.Shared.Players.IPlayer player, ulong steamId)
        {
            Suspicion state = Get(steamId);
            if (state.Cooldown > 0)
                return;

            state.Strikes++;
            state.SinceSuspicious = 0;
            if (state.Strikes >= StrikesToFlag)
            {
                string name = player.Controller?.PlayerName ?? "player";
                _core.PlayerManager.SendChat($"{Helper.ChatColors.Red}[AC] {Helper.ChatColors.Green}{name} detected using {Helper.ChatColors.Red}Silent Aim!");
                state.Strikes = 0;
                state.Cooldown = ReportCooldownCommands;
            }

            _suspicion[steamId] = state;
        }

        private void Touch(ulong steamId)
        {
            if (!_suspicion.TryGetValue(steamId, out Suspicion state))
                return;

            if (state.Cooldown > 0)
                state.Cooldown--;

            state.SinceSuspicious++;
            if (state.SinceSuspicious > SuspicionWindowCommands)
                state.Strikes = 0;

            _suspicion[steamId] = state;
        }

        private Suspicion Get(ulong steamId)
        {
            _suspicion.TryGetValue(steamId, out Suspicion state);
            return state;
        }

        private HookResult OnPlayerDisconnect(EventPlayerDisconnect @event)
        {
            var player = @event.UserIdController;
            if (player != null)
            {
                ulong steamId = player.SteamID;
                _lastView.Remove(steamId);
                _suspicion.Remove(steamId);
                _snap.Remove(steamId);
            }

            return HookResult.Continue;
        }

        private struct ViewSample
        {
            public float Pitch;
            public float Yaw;
        }

        private struct Suspicion
        {
            public int Strikes;
            public int SinceSuspicious;
            public int Cooldown;
        }

        private struct SnapWatch
        {
            public float PrePitch;
            public float PreYaw;
            public float ShotPitch;
            public float ShotYaw;
        }
    }
}
