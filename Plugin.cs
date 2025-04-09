using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Security.Permissions;
using BepInEx;
using BepInEx.Logging;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MoreSlugcats;
using UnityEngine;
using Watcher;
using Random = UnityEngine.Random;
using SecurityAction = System.Security.Permissions.SecurityAction;

#pragma warning disable CS0618

[module: UnverifiableCode]
[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]

namespace PorlgatoryMod;

[BepInPlugin("alduris.porlgatory", "Porlgatory", "1.0.5")]
public class Plugin : BaseUnityPlugin
{
    public static new ManualLogSource Logger;

    private void OnEnable()
    {
        Logger = base.Logger;
        On.RainWorld.OnModsInit += RainWorldOnOnModsInit;
    }

    private void OnDisable()
    {
        On.RainWorld.OnModsInit -= RainWorldOnOnModsInit;
        IsInit = false;
        options = null;
    }

    private bool IsInit;
    private Options options;
    private void RainWorldOnOnModsInit(On.RainWorld.orig_OnModsInit orig, RainWorld self)
    {
        orig(self);
        try
        {
            if (IsInit) return;
            options = new Options();
            MachineConnector.SetRegisteredOI("alduris.porlgatory", options);

            // The hook that creates the scavengers
            On.AbstractCreature.ctor += AbstractCreature_ctor;

            // The hook that ensures scavs get their items on spawn :D
            IL.ScavengerAbstractAI.ctor += ScavengerAbstractAI_ctor;

            // The hook that force fresh spawns every cycle
            On.WorldLoader.GeneratePopulation += WorldLoader_GeneratePopulation;

            // The hooks that prevent the game from throwing null exceptions because I replaced the creature it spawned with a scavenger
            IL.DaddyCorruption.AIMapReady += DaddyCorruption_AIMapReady;
            IL.Room.Loaded += Room_Loaded;
            IL.Room.PlaceQuantifiedCreaturesInRoom += Room_PlaceQuantifiedCreaturesInRoom;
            IL.RegionState.AddHatchedNeedleFly += RegionState_AddHatchedNeedleFly;
            On.FliesRoomAI.Update += FliesRoomAI_Update;
            if (ModManager.MSC)
            {
                IL.BigSpider.BabyPuff += BigSpider_BabyPuff;
                IL.MoreSlugcats.HRGuardManager.Update += IL_HRGuardManager_Update;
            }

            // Vulture grub shenanigans
            IL.VultureGrub.RayTraceSky += VultureGrub_RayTraceSky;
            IL.VultureGrub.AttemptCallVulture += VultureGrub_AttemptCallVulture;

            // Prevent softlocks during Rubicon
            if (ModManager.MSC)
            {
                On.MoreSlugcats.HRGuardManager.Update += On_HRGuardManager_Update;
            }

            // Void sea ghost funnies
            VoidSeaHooks.ApplyHooks(options, Logger);
            On.RainWorldGame.ShutDownProcess += RainWorldGame_ShutDownProcess;

            // Ready to go!
            IsInit = true;
            Logger.LogInfo("The scavengers are ready to invade!");
        }
        catch (Exception ex)
        {
            Logger.LogError("Oops! No scavs (because of an error)!");
            Logger.LogError(ex);
        }
    }

    private void RainWorldGame_ShutDownProcess(On.RainWorldGame.orig_ShutDownProcess orig, RainWorldGame self)
    {
        orig(self);
        VoidSeaHooks.ClearGhosts();
    }


    // Spawning the scavs
    private void AbstractCreature_ctor(On.AbstractCreature.orig_ctor orig, AbstractCreature self, World world, CreatureTemplate creatureTemplate, Creature realizedCreature, WorldCoordinate pos, EntityID ID)
    {
        HashSet<CreatureTemplate.Type> exceptions = [
            CreatureTemplate.Type.Slugcat,
            CreatureTemplate.Type.Overseer,
            CreatureTemplate.Type.VultureGrub,
            CreatureTemplate.Type.Scavenger,
            CreatureTemplate.Type.Deer
        ];
        if (options.BatfliesSpawn.Value) exceptions.Add(CreatureTemplate.Type.Fly);

        // DLC Shared
        if (ModManager.DLCShared)
        {
            exceptions.UnionWith([
                DLCSharedEnums.CreatureTemplateType.MotherSpider,
                DLCSharedEnums.CreatureTemplateType.ScavengerElite
            ]);
        }

        // Watcher
        if (ModManager.MSC)
        {
            exceptions.UnionWith([
                MoreSlugcatsEnums.CreatureTemplateType.SlugNPC,
                MoreSlugcatsEnums.CreatureTemplateType.ScavengerKing
            ]);

            // Inv still gets to suffer >:3
            if (world.game.GetStorySession?.saveStateNumber == MoreSlugcatsEnums.SlugcatStatsName.Sofanthiel && Random.value < 0.5f)
            {
                exceptions.UnionWith([
                    CreatureTemplate.Type.RedLizard,
                    CreatureTemplate.Type.RedCentipede,
                    CreatureTemplate.Type.SpitterSpider,
                    CreatureTemplate.Type.DaddyLongLegs,
                    MoreSlugcatsEnums.CreatureTemplateType.TrainLizard,
                    DLCSharedEnums.CreatureTemplateType.TerrorLongLegs,
                    DLCSharedEnums.CreatureTemplateType.MirosVulture
                ]);
            }
        }
        if (realizedCreature is null && !exceptions.Contains(creatureTemplate.type))
        {
            CreatureTemplate.Type type = CreatureTemplate.Type.Scavenger;
            //CreatureTemplate.Type type = CreatureTemplate.Type.RedLizard;

            if (ModManager.DLCShared)
            {
                if (Random.value < 1f / 20f)
                {
                    type = DLCSharedEnums.CreatureTemplateType.ScavengerElite;
                }
            }
            if (ModManager.Watcher)
            {
                if (Random.value < 1f / 20f)
                {
                    type = Random.value < 1f / 3f ? WatcherEnums.CreatureTemplateType.ScavengerDisciple : WatcherEnums.CreatureTemplateType.ScavengerTemplar;
                }
            }

            if (options.UseSpecificId.Value)
            {
                if (type == CreatureTemplate.Type.Scavenger)
                {
                    ID = new EntityID(ID.spawner, options.ScavSpawnId.Value);
                }
                else if (type == DLCSharedEnums.CreatureTemplateType.ScavengerElite)
                {
                    ID = new EntityID(ID.spawner, options.EliteSpawnId.Value);
                }
            }

            creatureTemplate = StaticWorld.GetCreatureTemplate(type);
        }
        orig(self, world, creatureTemplate, realizedCreature, pos, ID);
    }

    // Items on spawn
    private void ScavengerAbstractAI_ctor(ILContext il)
    {
        try
        {
            var c = new ILCursor(il);

            c.GotoNext(MoveType.After, x => x.MatchLdfld<World>(nameof(World.singleRoomWorld)));
            c.EmitDelegate((bool singleRoomWorld) => singleRoomWorld || options.SpawnWithItems.Value); // only give everyone items if we want them to have them
        }
        catch (Exception ex)
        {
            Logger.LogError("IL ScavengerAbstractAI item fix error!");
            Logger.LogError(ex);
        }
    }


    // Saving the scavs
    private void WorldLoader_GeneratePopulation(On.WorldLoader.orig_GeneratePopulation orig, WorldLoader self, bool fresh)
    {
        try
        {
            // From enemy randomizer mod
            if (!fresh)
            {
                foreach (AbstractRoom abstractRoom in self.abstractRooms)
                {
                    if (abstractRoom.shelter || (ModManager.MSC && abstractRoom.isAncientShelter))
                        continue;

                    abstractRoom.creatures.Clear();
                    abstractRoom.entitiesInDens.Clear();
                }
                fresh = true;
            }
            orig(self, fresh);
        }
        catch(Exception e)
        {
            Logger.LogError("GeneratePopulation hook error!");
            Logger.LogError(e);
            throw;
        }
    }


    // Prevent null errors
    private void DaddyCorruption_AIMapReady(ILContext il)
    {
        // Stop StuckDaddy in DaddyCorruption from trying to create a DaddyRestraint because of null error
        // Accomplish this by breaking to the end of the if code block before we ever get to that part
        try
        {
            var c = new ILCursor(il);

            c.GotoNext(
                MoveType.Before,
                i => i.MatchLdloc(out _),
                i => i.MatchCallvirt(typeof(AbstractCreature).GetProperty(nameof(AbstractCreature.realizedCreature)).GetGetMethod()),
                i => i.MatchIsinst<DaddyLongLegs>()
            );

            // Create temporary cursor before skip block
            var location = new ILCursor(c);

            // Get next br statement (should be end of code block) and add it to location
            c.GotoNext(i => i.Match(OpCodes.Br));
            ILLabel target = (ILLabel)c.Next.Operand;

            location.Emit(OpCodes.Br, target);
        }
        catch (Exception ex)
        {
            Logger.LogError("IL StuckDaddy fix error!");
            Logger.LogError(ex);
        }
    }

    private void Room_Loaded(ILContext il)
    {
        // Stop various creatures from trying to create alter a creature state of a creature that isn't what it expects
        // Do the same thing that we did with DaddyCorruption.AIMapReady: skip past the code
        // The following creatures need this: Hazer, MSC BigJellyFish, and MSC StowawayBug
        // VultureGrub would also need this but it's funny to watch a scavenger fall out of the sky
        try
        {
            var c = new ILCursor(il);

            // Skip hazer stuff
            c.GotoNext(i => i.MatchLdsfld<CreatureTemplate.Type>(nameof(CreatureTemplate.Type.Hazer))); // hazer uses vulture grub state but it does so after vulture grubs themselves
            CreateHook<VultureGrub.VultureGrubState>();
            CreateHook<BigJellyState>();
            CreateHook<StowawayBugState>();

            void CreateHook<T>()
            {
                // Find location
                c.GotoNext(i => i.MatchIsinst<T>());
                c.GotoPrev(MoveType.AfterLabel, i => i.MatchStloc(out _));

                // Spawn in creature
                c.Emit(OpCodes.Dup);
                c.Emit(OpCodes.Ldarg_0);
                // c.EmitDelegate((AbstractCreature crit, AbstractRoom self) => self.AddEntity(crit));
                c.EmitDelegate((AbstractCreature crit, Room self) => self.abstractRoom.AddEntity(crit));
                c.GotoNext(MoveType.After, x => x.MatchStloc(out _));

                // Create break point to skip extra stuff
                var label = c.MarkLabel();
                ILLabel brTo = null;
                c.GotoNext(x => x.MatchBr(out brTo));
                c.GotoLabel(label);
                c.Emit(OpCodes.Br, brTo);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError("IL Room.Loaded fix error!");
            Logger.LogError(ex);
        }
    }

    private void Room_PlaceQuantifiedCreaturesInRoom(ILContext il)
    {
        // Stop the AI mapper from trying to assign a den position to spiders
        try
        {
            var c = new ILCursor(il);

            c.GotoNext(x => x.MatchLdsfld<CreatureTemplate.Type>(nameof(CreatureTemplate.Type.Spider)));
            c.GotoNext(MoveType.AfterLabel, x => x.MatchBrfalse(out _));
            c.EmitDelegate((bool _) => false);
        }
        catch (Exception ex)
        {
            Logger.LogError("IL Coalescipede den fix error!");
            Logger.LogError(ex);
        }
    }

    private void BigSpider_BabyPuff(ILContext il)
    {
        // Stop mother spiders from trying to make their coalescipede children aggressive when their children aren't coalescipedes
        // This method just removes the entire statement lmao
        try
        {
            var c = new ILCursor(il);

            c.GotoNext(i => i.MatchIsinst<Spider>());
            c.GotoPrev(MoveType.After, x => x.MatchCallvirt<AbstractPhysicalObject>(nameof(AbstractPhysicalObject.RealizeInRoom)));
            var label = c.MarkLabel();
            c.GotoNext(MoveType.After, x => x.MatchStfld<Spider>(nameof(Spider.bloodLust)));
            var brTo = c.MarkLabel();
            c.GotoLabel(label);
            c.Emit(OpCodes.Br, brTo);

            c.RemoveRange(5);
        }
        catch (Exception ex)
        {
            Logger.LogError("IL BigSpider.BabyPuff fix error!");
            Logger.LogError(ex);
        }
    }

    private void RegionState_AddHatchedNeedleFly(ILContext il)
    {
        // Stop the game from trying to hatch scavengers as noodleflies
        try
        {
            var c = new ILCursor(il);

            c.GotoNext(MoveType.After, x => x.MatchStfld<NeedleWormAbstractAI.NeedleWormState>(nameof(NeedleWormAbstractAI.NeedleWormState.eggSpawn)));
            var brTo = c.MarkLabel();
            c.GotoPrev(MoveType.AfterLabel, x => x.MatchDup());
            c.Emit(OpCodes.Br, brTo);
        }
        catch (Exception ex)
        {
            Logger.LogError("IL Needlefly hatch fix error!");
            Logger.LogError(ex);
        }
    }

    private void FliesRoomAI_Update(On.FliesRoomAI.orig_Update orig, FliesRoomAI self, bool eu)
    {
        if (options.BatfliesSpawn.Value)
        {
            orig(self, eu);
        }
    }

    private void IL_HRGuardManager_Update(ILContext il)
    {
        // Scavengers aren't guardians, there's 3 lines of code removed here. All 3 have 8 IL instructions.
        try
        {
            var c = new ILCursor(il);

            c.GotoNext(x => x.MatchIsinst<TempleGuard>());
            c.GotoPrev(MoveType.After, x => x.MatchCallOrCallvirt(typeof(CreatureState).GetProperty(nameof(CreatureState.alive)).GetGetMethod()));
            c.EmitDelegate((bool orig) => false);
        }
        catch (Exception ex)
        {
            Logger.LogError("IL Needlefly hatch fix error!");
            Logger.LogError(ex);
        }
    }


    // Vulture grub crap
    private void VultureGrub_RayTraceSky(ILContext il)
    {
        // Make it so that vulture grub doesn't care about room attraction for any type of vulture
        try
        {
            var c = new ILCursor(il);

            c.GotoNext(x => x.MatchLdsfld<CreatureTemplate.Type>(nameof(CreatureTemplate.Type.Vulture)));
            c.GotoNext(MoveType.After, x => x.MatchRet());
            var brTo = c.MarkLabel();
            c.GotoPrev(MoveType.AfterLabel, x => x.MatchLdcI4(0));
            c.Emit(OpCodes.Br, brTo);
        }
        catch (Exception ex)
        {
            Logger.LogError("IL VultureGrub.RayTraceSky fix failed!");
            Logger.LogError(ex);
        }
    }

    private void VultureGrub_AttemptCallVulture(ILContext il)
    {
        // Make it so the grub spawns scavengers instead of vultures.
        try
        {
            var c = new ILCursor(il);

            // Skip room attraction check
            c.GotoNext(MoveType.After, x => x.MatchLdsfld<AbstractRoom.CreatureRoomAttraction>(nameof(AbstractRoom.CreatureRoomAttraction.Forbidden)));
            c.GotoNext(MoveType.AfterLabel, x => x.MatchBrfalse(out _));
            c.EmitDelegate((bool orig) => false);

            // Replace array
            int arrIndex = 2;
            c.GotoNext(x => x.MatchNewarr<CreatureTemplate.Type>());
            c.GotoNext(x => x.MatchStloc(out arrIndex));

            c.GotoNext(MoveType.Before, x => x.MatchBr(out _));
            c.EmitDelegate(() => {
                HashSet<CreatureTemplate.Type> creatures = [CreatureTemplate.Type.Scavenger];
                if (ModManager.DLCShared) creatures.Add(DLCSharedEnums.CreatureTemplateType.ScavengerElite);
                if (ModManager.Watcher)
                {
                    creatures.Add(WatcherEnums.CreatureTemplateType.ScavengerTemplar);
                    creatures.Add(WatcherEnums.CreatureTemplateType.ScavengerDisciple);
                }
                return creatures.ToArray();
            });
            c.Emit(OpCodes.Stloc, arrIndex);
        }
        catch (Exception ex)
        {
            Logger.LogError("IL VultureGrub.AttemptCallVulture fix failed!");
            Logger.LogError(ex);
        }
    }


    // Rubicon softlock prevention
    private void On_HRGuardManager_Update(On.MoreSlugcats.HRGuardManager.orig_Update orig, HRGuardManager self, bool eu)
    {
        orig(self, eu);

        // Sometimes the scav likes to wander a little bit, occasionally into the next room where you can't kill it
        if (self.myGuard != null && self.myGuard.state.alive)
        {
            // To prevent that from happening, prevent it from being able to wander at all
            self.myGuard.realizedCreature.firstChunk.pos = self.room.MiddleOfTile(self.startCoord);
            self.myGuard.realizedCreature.firstChunk.vel = Vector2.zero;
        }
    }
}
