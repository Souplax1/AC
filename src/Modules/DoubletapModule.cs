// File: src/Modules/DoubletapModule.cs
using AC;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.GameEvents;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;
using System.Collections.Generic;

namespace Modules
{
    public class DoubletapModule : ModularAntiCheat.IAcModule
    {
        private ModularAntiCheat _plugin = null!;
        private ISwiftlyCore _core = null!;
        // Use player name as a stable key here (replace with a better unique id if available)
        private readonly Dictionary<string, int> _lastFireTicks = new();

        public void Load(ModularAntiCheat plugin, ISwiftlyCore core)
        {
            _plugin = plugin;
            _core = core;

            _core.GameEvent.HookPost<EventWeaponFire>(OnWeaponFire);
            _core.GameEvent.HookPost<EventPlayerDisconnect>(OnPlayerDisconnect);
        }

        public void Unload()
        {
            _lastFireTicks.Clear();
        }

        private HookResult OnWeaponFire(EventWeaponFire @event)
        {
            var player = @event.UserIdController;
            if (player == null || !player.IsValid)
                return HookResult.Continue;

            int currentTick = _core.Engine.GlobalVars.TickCount;
            string key = player.PlayerName ?? "<unknown>";

            if (_lastFireTicks.TryGetValue(key, out int lastTick))
            {
                int delta = currentTick - lastTick;

                if (delta <= 1)  // Same-tick (0) or consecutive (1) = DT in CS2
                {
                  _core.PlayerManager.SendChat($"{Helper.ChatColors.Red}[AC] {Helper.ChatColors.Green}{player.PlayerName} detected using {Helper.ChatColors.Red}Doubletap!");
                }
            }

            _lastFireTicks[key] = currentTick;
            return HookResult.Continue;
        }

        private HookResult OnPlayerDisconnect(EventPlayerDisconnect @event)
        {
            var player = @event.UserIdController;
            if (player != null)
            {
                string key = player.PlayerName ?? "<unknown>";
                _lastFireTicks.Remove(key);
            }
            return HookResult.Continue;
        }
    }
}
