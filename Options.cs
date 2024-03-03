using System.Collections.Generic;
using BepInEx.Logging;
using Menu.Remix.MixedUI;
using Menu.Remix.MixedUI.ValueTypes;

namespace PorlgatoryMod
{
    internal class Options : OptionInterface
    {
        private readonly ManualLogSource logger;

        public Options(Plugin modInstance, ManualLogSource loggerSource)
        {
            logger = loggerSource;

            BatfliesSpawn = config.Bind<bool>("Porlgatory_BatfliesSpawn", false, new ConfigurableInfo("Whether or not batflies are part of the exceptions list"));
            SpawnWithItems = config.Bind<bool>("Porlgatory_SpawnWithItems", false, new ConfigurableInfo("Whether or not scavs spawned by the mod spawn with items (can cause lag)"));
            VoidSeaScavs = config.Bind<bool>("Porlgatory_VoidSeaScavs", true, new ConfigurableInfo("Whether or not the players in the void sea are replaced with scavs"));
            UseSpecificId = config.Bind<bool>("Porlgatory_UseSpecificId", false, new ConfigurableInfo("Whether or not to spawn a specific id (set by below options; can cause glitches)"));
            ScavSpawnId = config.Bind<int>("Porlgatory_ScavSpawnId", 0, new ConfigurableInfo("Which id is forced to spawn for normal scavs"));
            EliteSpawnId = config.Bind<int>("Porlgatory_EliteSpawnId", 0, new ConfigurableInfo("Which id is forced to spawn for elite scavs"));
        }

        // private UIelement[] UIArrPlayerOptions;
        public readonly Configurable<bool> BatfliesSpawn;
        public readonly Configurable<bool> SpawnWithItems;
        public readonly Configurable<bool> VoidSeaScavs;
        public readonly Configurable<bool> UseSpecificId;
        public readonly Configurable<int> ScavSpawnId;
        public readonly Configurable<int> EliteSpawnId;

        private OpCheckBox useIdCheckbox;
        private OpLabel scavIdLabel;
        private OpTextBox scavIdInput;
        private OpLabel eliteIdLabel;
        private OpTextBox eliteIdInput;

        public override void Initialize()
        {
            base.Initialize();

            // Initialize tab
            var opTab = new OpTab(this, "Options");
            this.Tabs = new[]
            {
                opTab
            };

            useIdCheckbox = new OpCheckBox(UseSpecificId, new(10f, 440f));

            scavIdLabel = new OpLabel(40f, 410f, "Scav id");
            scavIdInput = new OpTextBox(ScavSpawnId, new(46f + scavIdLabel.GetDisplaySize().x, 410f), 150f) { allowSpace = true };

            // Add stuff to tab
            opTab.AddItems(
                new OpLabel(10f, 560f, "OPTIONS", true),
                new OpCheckBox(BatfliesSpawn, new(10f, 530f)),
                new OpLabel(40f, 530f, "Batflies can spawn"),
                new OpCheckBox(SpawnWithItems, new(10f, 500f)),
                new OpLabel(40f, 500f, "All scavs spawn with items (laggy in some areas)"),
                new OpCheckBox(VoidSeaScavs, new(10f, 470f)),
                new OpLabel(40f, 470f, "Void sea scavs"),
                useIdCheckbox,
                new OpLabel(40f, 440f, "Scavs use specific id (can cause glitches)"),
                scavIdLabel,
                scavIdInput
            );
            if (ModManager.MSC)
            {
                eliteIdLabel = new OpLabel(40f, 380f, "Elite id");
                eliteIdInput = new OpTextBox(EliteSpawnId, new(46f + eliteIdLabel.GetDisplaySize().x, 380f), 150f) { allowSpace = true };
                opTab.AddItems(
                    eliteIdLabel,
                    eliteIdInput
                );
            }
        }

        public override void Update()
        {
            base.Update();

            if (useIdCheckbox.GetValueBool())
            {
                scavIdLabel.Show();
                eliteIdLabel?.Show();
                scavIdInput.Show();
                eliteIdInput?.Show();
            }
            else
            {
                scavIdLabel.Hide();
                eliteIdLabel?.Hide();
                scavIdInput.Hide();
                eliteIdInput?.Hide();
            }
        }
    }
}
