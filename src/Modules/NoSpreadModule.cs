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
    public class NoSpreadModule : ModularAntiCheat.IAcModule
    {
        private const int ReportCooldownHits = 6;

        private ISwiftlyCore _core = null!;
        private Guid _damageHook;
        private Guid _disconnectHook;

        private readonly Dictionary<ulong, ViewSample> _heldView = new();
        private readonly Dictionary<ulong, ViewSample> _previousView = new();
        private readonly Dictionary<ulong, Suspicion> _suspicion = new();

        public void Load(ModularAntiCheat plugin, ISwiftlyCore core)
        {
            _core = core;
            _core.Event.OnClientProcessUsercmds += OnUsercmds;
            _damageHook = _core.GameEvent.HookPost<EventBulletDamage>(OnBulletDamage);
            _disconnectHook = _core.GameEvent.HookPost<EventPlayerDisconnect>(OnPlayerDisconnect);
        }

        public void Unload()
        {
            _core.Event.OnClientProcessUsercmds -= OnUsercmds;
            _core.GameEvent.Unhook(_damageHook);
            _core.GameEvent.Unhook(_disconnectHook);
            _heldView.Clear();
            _previousView.Clear();
            _suspicion.Clear();
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
                if (_heldView.TryGetValue(steamId, out ViewSample current))
                    _previousView[steamId] = current;
                _heldView[steamId] = new ViewSample { Pitch = view.X, Yaw = view.Y };
            }
        }

        private HookResult OnBulletDamage(EventBulletDamage @event)
        {
            var player = _core.PlayerManager.GetPlayer(@event.Attacker);
            if (player == null || !player.IsValid || player.IsFakeClient)
                return HookResult.Continue;

            ulong steamId = player.SteamID;
            if (steamId == 0 || !_heldView.TryGetValue(steamId, out ViewSample held))
                return HookResult.Continue;

            Suspicion state = Get(steamId);
            if (state.SeenTick && state.LastAttackTick == @event.AttackTickCount)
                return HookResult.Continue;

            state.SeenTick = true;
            state.LastAttackTick = @event.AttackTickCount;

            if (state.Cooldown > 0)
            {
                state.Cooldown--;
                _suspicion[steamId] = state;
                return HookResult.Continue;
            }

            float error = ViewError(held, @event.DamageDirX, @event.DamageDirY, @event.DamageDirZ);
            // The firing command carries the compensated angle. The command before it is still the aim the bullet was solved toward.
            if (_previousView.TryGetValue(steamId, out ViewSample prior))
                error = MathF.Min(error, ViewError(prior, @event.DamageDirX, @event.DamageDirY, @event.DamageDirZ));
            float cone = NoSpreadMath.ConeDegrees(@event.InaccuracyTotal);
            float horizontalSpeed = HorizontalSpeed(player);

            if (NoSpreadMath.IsSuspicious(cone, error, @event.InAir, horizontalSpeed))
            {
                state.Streak++;
                if (state.Streak >= NoSpreadMath.HitsToFlag)
                {
                    string name = player.Controller?.PlayerName ?? "player";
                    _core.PlayerManager.SendChat($"{Helper.ChatColors.Red}[AC] {Helper.ChatColors.Green}{name} detected using {Helper.ChatColors.Red}NoSpread!");
                    state.Streak = 0;
                    state.Cooldown = ReportCooldownHits;
                }
            }
            else
            {
                state.Streak = 0;
            }

            _suspicion[steamId] = state;
            return HookResult.Continue;
        }

        private static float ViewError(ViewSample view, float bulletX, float bulletY, float bulletZ)
        {
            var look = new QAngle { X = view.Pitch, Y = view.Yaw, Z = 0f };
            look.ToDirectionVectors(out Vector forward, out _, out _);
            return NoSpreadMath.DirectionError(forward.X, forward.Y, forward.Z, bulletX, bulletY, bulletZ);
        }

        private static float HorizontalSpeed(SwiftlyS2.Shared.Players.IPlayer player)
        {
            var pawn = player.PlayerPawn;
            if (pawn == null)
                return 0f;

            Vector velocity = pawn.AbsVelocity;
            return MathF.Sqrt((velocity.X * velocity.X) + (velocity.Y * velocity.Y));
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
                _heldView.Remove(steamId);
                _previousView.Remove(steamId);
                _suspicion.Remove(steamId);
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
            public int Streak;
            public int LastAttackTick;
            public bool SeenTick;
            public int Cooldown;
        }
    }
}
