using System.Collections.Generic;
using BepInEx.Logging;
using Menu.Remix.MixedUI;

namespace PorlgatoryMod
{
    internal class PorlgatoryOptions : OptionInterface
    {
        private readonly ManualLogSource logger;

        public PorlgatoryOptions(PorlgatoryPlugin modInstance, ManualLogSource loggerSource)
        {
            logger = loggerSource;

            BatfliesSpawn = config.Bind<bool>("Porlgatory_BatfliesSpawn", false, new ConfigurableInfo("Whether or not batflies are part of the exceptions list"));
            SpawnWithItems = config.Bind<bool>("Porlgatory_SpawnWithItems", false, new ConfigurableInfo("Whether or not scavs spawned by the mod spawn with items (can cause lag)"));
            VoidSeaScavs = config.Bind<bool>("Porlgatory_VoidSeaScavs", true, new ConfigurableInfo("Whether or not the players in the void sea are replaced with scavs"));
        }

        // private UIelement[] UIArrPlayerOptions;
        public readonly Configurable<bool> BatfliesSpawn;
        public readonly Configurable<bool> SpawnWithItems;
        public readonly Configurable<bool> VoidSeaScavs;

        public override void Initialize()
        {
            base.Initialize();

            // Initialize tab
            var opTab = new OpTab(this, "Options");
            this.Tabs = new[]
            {
                opTab
            };

            // Add stuff to tab
            opTab.AddItems(
                new OpLabel(10f, 560f, "OPTIONS", true),
                new OpCheckBox(BatfliesSpawn, new(10f, 530f)),
                new OpLabel(40f, 530f, "Batflies can spawn"),
                new OpCheckBox(SpawnWithItems, new(10f, 500f)),
                new OpLabel(40f, 500f, "All scavs spawn with items (laggy in some areas)"),
                new OpCheckBox(VoidSeaScavs, new(10f, 470f)),
                new OpLabel(40f, 470f, "Void sea scavs")
            );
        }

        public override void Update()
        {
            base.Update();
        }
    }
}
