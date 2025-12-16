using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Plugins;
using System.Collections.Generic;

namespace AC
{
    [PluginMetadata(Id = "ModularAntiCheat", Name = "Modular AntiCheat", Version = "1.0.0", Author = "Yeezy", Description = "Modular anti-cheat with separate modules")]
    public class ModularAntiCheat : BasePlugin
    {
        public ModularAntiCheat(ISwiftlyCore core) : base(core)
        {
        }

        public ISwiftlyCore Core => base.Core;

        private readonly List<IAcModule> _modules = new();

        public override void Load(bool hotReload)
        {
            _modules.Add(new Modules.DoubletapModule());
            _modules.Add(new Modules.SilentAimModule());
            // Add more modules here later, e.g.:
            // _modules.Add(new Modules.AimbotModule());

            foreach (var module in _modules)
            {
                module.Load(this, this.Core);   
            }
        }

        public override void Unload()
        {
            foreach (var module in _modules)
            {
                module.Unload();
            }
            _modules.Clear();
        }

        public interface IAcModule
        {
            /// <summary>
            /// Load the module. The host plugin instance and ISwiftlyCore are provided so modules can access core services.
            /// </summary>
            void Load(ModularAntiCheat plugin, ISwiftlyCore core);

            void Unload();
        }
    }
}