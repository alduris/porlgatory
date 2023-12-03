using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using RWCustom;
using UnityEngine;
using VoidSea;
using Random = UnityEngine.Random;

namespace PorlgatoryMod
{
    internal static class PorlgatoryVoidSea
    {
        public static List<ScavGhost> scavGhosts = new();
        private static PorlgatoryOptions options;
        private static ManualLogSource Logger;

        public static void ApplyHooks(PorlgatoryOptions opts, ManualLogSource logger)
        {
            options = opts;
            Logger = logger;

            // Dealing with the scav ghosts
            On.VoidSea.PlayerGhosts.AddGhost += PlayerGhosts_AddGhost;
            On.VoidSea.PlayerGhosts.Update += PlayerGhosts_Update;

            // Dealing with issues caused by me replicating the scav ghosts elsewhere and stuff
            new Hook(typeof(VoidSeaScene).GetProperty(nameof(VoidSeaScene.SlugcatGhostMusic)).GetGetMethod(), VoidSeaScene_SlugcatGhostMusic_get);
            IL.VoidSea.VoidSeaScene.TheEgg.Update += TheEgg_Update;

            // Make sure the ghosts get drawn I think
            On.ScavengerGraphics.DrawSprites += ScavengerGraphics_DrawSprites;

            // TODO: see if I can make the scavs brighter so you can see them
        }

        public static void ClearGhosts()
        {
            scavGhosts.Clear();
        }

        private static void PlayerGhosts_AddGhost(On.VoidSea.PlayerGhosts.orig_AddGhost orig, PlayerGhosts self)
        {
            if (options.VoidSeaScavs.Value)
            {
                Vector2 pos = self.originalPlayer.mainBodyChunk.pos + Custom.RNV() * 2000f;
                AbstractCreature abstractCreature = new(self.voidSea.room.world, StaticWorld.GetCreatureTemplate(CreatureTemplate.Type.Scavenger), null, self.voidSea.room.GetWorldCoordinate(pos), self.originalPlayer.abstractCreature.world.game.GetNewID());
                abstractCreature.saveCreature = false;
                self.voidSea.room.abstractRoom.AddEntity(abstractCreature);
                abstractCreature.RealizeInRoom();
                for (int i = 0; i < abstractCreature.realizedCreature.bodyChunks.Length; i++)
                {
                    abstractCreature.realizedCreature.bodyChunks[i].restrictInRoomRange = float.MaxValue;
                }
                abstractCreature.realizedCreature.CollideWithTerrain = false;
                scavGhosts.Add(new ScavGhost(self, abstractCreature.realizedCreature as Scavenger));
            }
            else
            {
                orig(self);
            }
        }

        private static void PlayerGhosts_Update(On.VoidSea.PlayerGhosts.orig_Update orig, PlayerGhosts self)
        {
            if (options.VoidSeaScavs.Value)
            {
                for (int i = scavGhosts.Count - 1; i >= 0; i--)
                {
                    if (scavGhosts[i].slatedForDeletion)
                    {
                        scavGhosts.RemoveAt(i);
                    }
                    else
                    {
                        scavGhosts[i].Update();
                    }
                }
                if (scavGhosts.Count < self.IdealGhostCount)
                {
                    self.AddGhost();
                }
            }
            else
            {
                orig(self);
            }
        }

        private delegate float orig_SlugcatGhostMusic(VoidSeaScene self);
        private static float VoidSeaScene_SlugcatGhostMusic_get(orig_SlugcatGhostMusic orig, VoidSeaScene self)
        {
            if (options.VoidSeaScavs.Value)
            {
                if (!self.secondSpace || self.theEgg == null)
                {
                    return 0f;
                }
                if (self.playerGhosts == null || scavGhosts.Count <= 0)
                {
                    return 0f;
                }
                return Mathf.Lerp(0.5f, 1f, self.theEgg.musicVolumeDirectionBoost) * (1f - self.theEgg.whiteFade);
            }
            else
            {
                return orig(self);
            }
        }

        private static void TheEgg_Update(ILContext il)
        {
            try
            {
                var pre = new ILCursor(il);

                /*
                 * This code basically creates an if-else statement that looks like this:
                 * (options.VoidSeaScavs.Value ? scavGhosts.Count : <the original value>)
                 */

                pre.GotoNext(
                    MoveType.Before,
                    i=> i.MatchLdarg(0),
                    i=> i.MatchCall<VoidSeaScene.TheEgg>("get_voidSeaScene"),
                    i=> i.MatchLdfld<VoidSeaScene>("playerGhosts"),
                    i=> i.MatchLdfld<PlayerGhosts>("ghosts"),
                    i=> i.MatchCallvirt(out _)
                );

                var post = new ILCursor(pre);
                post.Index += 5;
                Instruction after = post.Next;

                post.Emit(OpCodes.Br, after);
                Instruction elseBr = post.Prev;
                post.EmitDelegate(() => scavGhosts.Count);

                pre.EmitDelegate(() => options.VoidSeaScavs.Value);
                pre.Emit(OpCodes.Brfalse, elseBr.Next);

            }
            catch (Exception e)
            {
                Logger.LogError("IL void sea egg update fix error!");
                Logger.LogError(e);
            }
        }

        private static void ScavengerGraphics_DrawSprites(On.ScavengerGraphics.orig_DrawSprites orig, ScavengerGraphics self, RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, float timeStacker, Vector2 camPos)
        {
            if (scavGhosts.Count > 0 && options.VoidSeaScavs.Value)
            {
                self.culled = false;
            }
            orig(self, sLeaser, rCam, timeStacker, camPos);
        }

        /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Clone of VoidSeaScene.VoidSeaTreatment, adapted for scavengers
        /// </summary>
        private static void ScavSeaTreatment(VoidSeaScene scene, Scavenger scav, float swimSpeed)
        {
            if (scav.room != scene.room)
            {
                return;
            }
            for (int i = 0; i < scav.bodyChunks.Length; i++)
            {
                scav.bodyChunks[i].restrictInRoomRange = float.MaxValue;
                scav.bodyChunks[i].vel *= Mathf.Lerp(swimSpeed, 1f, scene.room.game.cameras[0].voidSeaGoldFilter);
                if (scene.Inverted)
                {
                    BodyChunk bodyChunk = scav.bodyChunks[i];
                    bodyChunk.vel.y += scav.buoyancy;
                    BodyChunk bodyChunk2 = scav.bodyChunks[i];
                    bodyChunk2.vel.y -= scav.gravity;
                }
                else
                {
                    BodyChunk bodyChunk3 = scav.bodyChunks[i];
                    bodyChunk3.vel.y -= scav.buoyancy;
                    BodyChunk bodyChunk4 = scav.bodyChunks[i];
                    bodyChunk4.vel.y += scav.gravity;
                }
            }
            scav.lungs = 1f; // so they don't drown
            if (scene.deepDivePhase == VoidSeaScene.DeepDivePhase.EggScenario && Random.value < 0.1f)
            {
                scav.mainBodyChunk.vel += Custom.DirVec(scav.mainBodyChunk.pos, scene.theEgg.pos) * 0.02f * Random.value;
            }
            // TODO: replace scav items with scene.AddMeltObject
        }

        /// <summary>
        /// Clone of VoidSeaScene.Move, adapted for scavengers
        /// </summary>
        private static void MoveScav(Scavenger scav, Vector2 move)
        {
            for (int i = 0; i < scav.bodyChunks.Length; i++)
            {
                scav.bodyChunks[i].pos += move;
                scav.bodyChunks[i].lastPos += move;
                scav.bodyChunks[i].lastLastPos += move;
            }
            if (scav.graphicsModule != null)
            {
                for (int i = 0; i < scav.graphicsModule.bodyParts.Length; i++)
                {
                    scav.graphicsModule.bodyParts[i].pos += move;
                    scav.graphicsModule.bodyParts[i].lastPos += move;
                }
                for (int i = 0; i < (scav.graphicsModule as ScavengerGraphics).drawPositions.GetLength(0); i++)
                {
                    (scav.graphicsModule as ScavengerGraphics).drawPositions[i, 0] += move;
                    (scav.graphicsModule as ScavengerGraphics).drawPositions[i, 1] += move;
                }
            }
        }

        public class ScavGhost
        {
            public PlayerGhosts owner;
            public Scavenger creature;
            public float swimSpeed;
            public Vector2 drift;
            public bool slatedForDeletion;

            public ScavGhost(PlayerGhosts owner,  Scavenger creature)
            {
                this.owner = owner;
                this.creature = creature;
                swimSpeed = Mathf.Lerp(0.9f, 1f, Custom.PushFromHalf(Random.value, 1f + 0.5f * Random.value));
                drift = Custom.RNV();
            }

            public void Update()
            {
                ScavSeaTreatment(owner.voidSea, creature, swimSpeed);
                for (int i = 0; i < creature.bodyChunks.Length; i++)
                {
                    BodyChunk bodyChunk = creature.bodyChunks[i];
                    bodyChunk.vel.y *= swimSpeed;
                    creature.bodyChunks[i].vel += drift * 0.05f;
                    BodyChunk bodyChunk2 = creature.bodyChunks[i];
                    bodyChunk2.vel.y -= Mathf.InverseLerp(-7000f, -2000f, owner.originalPlayer.mainBodyChunk.pos.y);
                }
                Vector2 vector = creature.mainBodyChunk.pos;
                Vector2 pos = owner.voidSea.room.game.cameras[0].pos;
                vector -= pos;
                float num = 100f;
                if (vector.x < -num * 2f)
                {
                    MoveCreature(pos + new Vector2(1400f + num, Mathf.Lerp(-num, 800f + num, Random.value)));
                }
                else if (vector.x > 1400f + num * 2f)
                {
                    MoveCreature(pos + new Vector2(-num, Mathf.Lerp(-num, 800f + num, Random.value)));
                }
                if (vector.y < -num * 2f)
                {
                    MoveCreature(pos + new Vector2(Mathf.Lerp(-num, 1400f + num, Random.value), 800f + num));
                    return;
                }
                if (vector.y > 800f + num * 2f)
                {
                    MoveCreature(pos + new Vector2(Mathf.Lerp(-num, 1400f + num, Random.value), -num));
                }
            }

            public void MoveCreature(Vector2 movePos)
            {
                if (owner.ghosts.Count > owner.IdealGhostCount)
                {
                    Destroy();
                    return;
                }
                swimSpeed = Mathf.Lerp(0.9f, 1f, Custom.PushFromHalf(Random.value, 1f + 0.5f * Random.value));
                MoveScav(creature, movePos - creature.mainBodyChunk.pos);
                for (int i = 0; i < creature.bodyChunks.Length; i++)
                {
                    creature.bodyChunks[i].vel = Custom.DirVec(creature.bodyChunks[i].pos, owner.originalPlayer.mainBodyChunk.pos - new Vector2(700f, 400f)) * 2f * Random.value;
                }
                drift = Custom.RNV() * Random.value;
            }

            public void Destroy()
            {
                slatedForDeletion = true;
                creature.room.abstractRoom.RemoveEntity(creature.abstractCreature);
                creature.Destroy();
            }
        }
    }
}
