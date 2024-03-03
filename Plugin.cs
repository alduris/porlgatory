using System;
using System.Security;
using System.Security.Permissions;
using BepInEx;
using BepInEx.Logging;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MoreSlugcats;
using UnityEngine;
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
        On.RainWorld.OnModsInit += RainWorldOnOnModsInit;
        options = new Options();
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
            IL.FliesRoomAI.Update += FliesRoomAI_Update;
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
        if (
            // If realizedCreature is not null (doesn't seem to be anywhere though), we can't change that
            realizedCreature is null &&
            // Special cases where we leave it be
            creatureTemplate.type != CreatureTemplate.Type.Slugcat &&
            creatureTemplate.type != CreatureTemplate.Type.Overseer &&
            creatureTemplate.type != CreatureTemplate.Type.VultureGrub &&
            creatureTemplate.type != CreatureTemplate.Type.Scavenger &&
            creatureTemplate.type != CreatureTemplate.Type.Deer &&
            !(creatureTemplate.type == CreatureTemplate.Type.Fly && options.BatfliesSpawn.Value) &&
            (
                // MSC stuff
                !ModManager.MSC || (
                    creatureTemplate.type != MoreSlugcatsEnums.CreatureTemplateType.SlugNPC &&
                    creatureTemplate.type != MoreSlugcatsEnums.CreatureTemplateType.MotherSpider &&
                    creatureTemplate.type != MoreSlugcatsEnums.CreatureTemplateType.ScavengerElite &&
                    creatureTemplate.type != MoreSlugcatsEnums.CreatureTemplateType.ScavengerKing
                )
            )
        )
        {
            CreatureTemplate.Type type = CreatureTemplate.Type.Scavenger;
            //CreatureTemplate.Type type = CreatureTemplate.Type.RedLizard;

            if (ModManager.MSC)
            {

                if (Random.value < 1.0 / 20.0)
                {
                    type = MoreSlugcatsEnums.CreatureTemplateType.ScavengerElite;
                }

                // Inv still gets to suffer >:3
                SlugcatStats.Name campaign = world.game.GetStorySession?.saveStateNumber;
                if (campaign == MoreSlugcatsEnums.SlugcatStatsName.Sofanthiel)
                {
                    if (
                        creatureTemplate.type == MoreSlugcatsEnums.CreatureTemplateType.Yeek ||
                        ((
                            creatureTemplate.type == CreatureTemplate.Type.RedLizard ||
                            creatureTemplate.type == CreatureTemplate.Type.RedCentipede ||
                            creatureTemplate.type == CreatureTemplate.Type.SpitterSpider ||
                            creatureTemplate.type == CreatureTemplate.Type.DaddyLongLegs ||
                            creatureTemplate.type == MoreSlugcatsEnums.CreatureTemplateType.TrainLizard ||
                            creatureTemplate.type == MoreSlugcatsEnums.CreatureTemplateType.MirosVulture ||
                            creatureTemplate.type == MoreSlugcatsEnums.CreatureTemplateType.TerrorLongLegs
                        ) &&
                        Random.value < 0.5f)
                    )
                    {
                        type = creatureTemplate.type;
                    }
                    else
                    {
                        type = MoreSlugcatsEnums.CreatureTemplateType.ScavengerElite;
                    }
                }
            }

            if (options.UseSpecificId.Value)
            {
                if (type == CreatureTemplate.Type.Scavenger)
                {
                    ID = new EntityID(ID.spawner, options.ScavSpawnId.Value);
                }
                else if (type == MoreSlugcatsEnums.CreatureTemplateType.ScavengerElite)
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

            c.GotoNext(
                MoveType.After,
                i => i.MatchLdarg(1),
                i => i.MatchLdfld<World>("singleRoomWorld"),
                i => i.Match(OpCodes.Brtrue_S)
            );
            ILLabel target = (ILLabel)c.Prev.Operand;

            c.GotoPrev(MoveType.Before, i => i.MatchLdarg(1));

            c.EmitDelegate(() => options.SpawnWithItems.Value); // only give everyone items if we want them to have them
            c.Emit(OpCodes.Brtrue, target);
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
                i => i.MatchCallvirt<AbstractCreature>("get_realizedCreature"),
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
            c.GotoNext(i => i.MatchLdsfld<CreatureTemplate.Type>("Hazer")); // hazer uses vulture grub state but it does so after vulture grubs themselves
            c.GotoNext(
                MoveType.Before,
                i => i.MatchLdloc(out _),
                i => i.MatchLdfld<AbstractCreature>("state"),
                i => i.MatchIsinst<VultureGrub.VultureGrubState>()
            );

            var location = new ILCursor(c);

            c.GotoNext(i => i.Match(OpCodes.Br));
            ILLabel target = (ILLabel)c.Next.Operand;

            location.Emit(OpCodes.Br, target);


            // MSC stuff
            if (ModManager.MSC)
            {
                // Big jellyfish time
                c.GotoNext(
                    MoveType.Before,
                    i => i.MatchLdloc(out _),
                    i => i.MatchLdfld<AbstractCreature>("state"),
                    i => i.MatchIsinst<BigJellyState>()
                );

                location = new ILCursor(c);

                c.GotoNext(i => i.Match(OpCodes.Br));
                target = (ILLabel)c.Next.Operand;

                location.Emit(OpCodes.Br, target);


                // Stowaways
                c.GotoNext(
                    MoveType.Before,
                    i => i.MatchLdloc(out _),
                    i => i.MatchLdfld<AbstractCreature>("state"),
                    i => i.MatchIsinst<StowawayBugState>()
                );

                location = new ILCursor(c);

                c.GotoNext(i => i.Match(OpCodes.Br));
                target = (ILLabel)c.Next.Operand;

                location.Emit(OpCodes.Br, target);
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

            c.GotoNext(
                MoveType.Before,
                i => i.MatchLdarg(1),
                i => i.MatchLdsfld<CreatureTemplate.Type>("Spider")
            );

            // Create temporary cursor before skip block
            var location = new ILCursor(c);

            // Get next br statement (should be end of code block) and add it to location
            c.GotoNext(i => i.Match(OpCodes.Brfalse_S));
            ILLabel target = (ILLabel)c.Next.Operand;

            location.Emit(OpCodes.Br, target);
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

            c.GotoNext(
                MoveType.Before,
                i => i.MatchLdloc(out _),
                i => i.MatchCallvirt<AbstractCreature>("get_realizedCreature"),
                i => i.MatchIsinst<Spider>()
            );

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

            c.GotoNext(
                MoveType.Before,
                i => i.MatchDup(),
                i => i.MatchLdfld<AbstractCreature>("state"),
                i => i.MatchIsinst<NeedleWormAbstractAI.NeedleWormState>(),
                i => i.MatchLdcI4(1),
                i => i.MatchStfld<NeedleWormAbstractAI.NeedleWormState>("eggSpawn")
            );

            c.RemoveRange(5);
        }
        catch (Exception ex)
        {
            Logger.LogError("IL Needlefly hatch fix error!");
            Logger.LogError(ex);
        }
    }

    private void FliesRoomAI_Update(ILContext il)
    {
        // Stop the game from trying to deal with flies that don't exist
        try
        {
            var start = new ILCursor(il);
            var end = new ILCursor(il);

            end.GotoNext(MoveType.Before, i => i.MatchRet());
            end.Index -= 3;

            start.EmitDelegate(() => options.BatfliesSpawn.Value); // don't skip AI code if batflies can spawn
            start.Emit(OpCodes.Brfalse, end.Next);
        }
        catch (Exception ex)
        {
            Logger.LogError("IL swarm room AI update fix error!");
            Logger.LogError(ex);
        }
    }

    private void IL_HRGuardManager_Update(ILContext il)
    {
        // Scavengers aren't guardians, there's 3 lines of code removed here. All 3 have 8 IL instructions.
        try
        {
            var c = new ILCursor(il);

            for (int i = 0; i < 3; i++)
            {
                c.GotoNext(
                    MoveType.Before,
                    i => i.MatchLdarg(0),
                    i => i.MatchLdfld<HRGuardManager>("myGuard"),
                    i => i.MatchCallvirt<AbstractCreature>("get_realizedCreature"),
                    i => i.MatchIsinst<TempleGuard>(),
                    i => i.MatchLdfld<TempleGuard>("AI")
                );
                c.RemoveRange(8);
            }
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

            c.GotoNext(
                MoveType.Before,
                i => i.MatchLdarg(0),
                i => i.MatchLdfld<UpdatableAndDeletable>("room"),
                i => i.MatchCallvirt<Room>("get_abstractRoom"),
                i => i.MatchLdsfld<CreatureTemplate.Type>("Vulture")
            );

            // Create temporary cursor before skip block
            var location = new ILCursor(c);

            // Get next br statement (should be end of code block) and add it to location
            c.GotoNext(i => i.Match(OpCodes.Brfalse_S));
            ILLabel target = (ILLabel)c.Next.Operand;

            location.Emit(OpCodes.Br, target);
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

            // Make it so the vulture doesn't give a crap about room attractedness for vultures first
            c.GotoNext(
                MoveType.Before,
                i => i.MatchLdloc(1),
                i => i.MatchLdsfld<CreatureTemplate.Type>("Vulture"),
                i => i.MatchCallvirt<AbstractRoom>("AttractionForCreature")
            );

            var location = new ILCursor(c);

            c.GotoNext(i => i.Match(OpCodes.Brfalse_S));
            ILLabel target = (ILLabel)c.Next.Operand;

            location.Emit(OpCodes.Br, target);

            // Find where the arrays for what can actually spawn are set and hijack them for our devious purposes
            c.GotoNext(
                MoveType.After,
                i => i.MatchLdarg(0),
                i => i.MatchLdfld<UpdatableAndDeletable>("room"),
                i => i.MatchLdfld<Room>("world"),
                i => i.MatchLdfld<World>("offScreenDen")
            );

            // Replace the array values for MSC
            c.GotoNext(i => i.MatchNewarr<CreatureTemplate.Type>());
            c.GotoNext(MoveType.Before, i => i.MatchLdsfld<CreatureTemplate.Type>("Vulture"));
            c.Remove();
            c.EmitDelegate(() => CreatureTemplate.Type.Scavenger);
            c.GotoNext(MoveType.Before, i => i.MatchLdsfld<CreatureTemplate.Type>("KingVulture"));
            c.Remove();
            c.EmitDelegate(() => MoreSlugcatsEnums.CreatureTemplateType.ScavengerElite);

            // Remove MirosVulture
            c.GotoNext(MoveType.Before, i => i.MatchDup());
            c.RemoveRange(4);

            // Repeat for non-MSC array but don't put in ScavengerElite
            c.GotoNext(i => i.MatchNewarr<CreatureTemplate.Type>());
            c.GotoNext(MoveType.Before, i => i.MatchLdsfld<CreatureTemplate.Type>("Vulture"));
            c.Remove();
            c.EmitDelegate(() => CreatureTemplate.Type.Scavenger);

            c.GotoNext(MoveType.Before, i => i.MatchDup());
            c.RemoveRange(4);
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
