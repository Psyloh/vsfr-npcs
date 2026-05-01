using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.CommandAbbr;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Common;
using Vintagestory.GameContent;
using Vintagestory.Server;
using Vintagestory.ServerMods;

namespace vsfrnpcs
{
	[HarmonyPatch(typeof(EntityBehaviorConversable), "Controller_DialogTriggers")]
	public static class ConversableTriggerPatch {

		public static void Postfix(EntityBehaviorConversable __instance, EntityAgent triggeringEntity, string value, JsonObject data)
		{
            if (__instance.entity.Api is not ICoreServerAPI api)
			{
				return;
			}

            var player = ((triggeringEntity as EntityPlayer)?.Player as IServerPlayer);
			if (player == null)
			{
				return;
			}

			if (value == "teleport")
			{
				__instance.Dialog?.TryClose();

				var x = data["x"].AsString("~0");
				var y = data["y"].AsString("~0");
				var z = data["z"].AsString("~0");

				var caller = Helpers.AdminCaller;
				caller.Player = player;

				var args = new TextCommandCallingArgs
				{
					Caller = caller,
					RawArgs = new CmdArgs($"{player.PlayerName} {x} {y} {z}")
				};
				api.ChatCommands.Execute("tp", args);
			}
			else if (value == "resetarchives")
			{
				Helpers.ResetArchives(api);
			}
		}
	}

	public static class Helpers
	{
		public static Caller AdminCaller {
			get
			{
				return new Caller
				{
					Type = EnumCallerType.Console,
					CallerRole = "admin",
					CallerPrivileges = ["*"],
					FromChatGroupId = GlobalConstants.ConsoleGroup
				};
			}
		}

		public static void ResetArchives(ICoreServerAPI api)
		{
			var sSys = api.ModLoader.GetModSystem<GenStoryStructures>();
			var archive = sSys.Structures.Get("resonancearchive");

			var chunkSize = GlobalConstants.ChunkSize;
			var x1 = archive.Location.MinX / chunkSize;
			var z1 = archive.Location.MinZ / chunkSize;
			var x2 = archive.Location.MaxX / chunkSize;
			var z2 = archive.Location.MaxZ / chunkSize;
			
			api.ChatCommands.Execute("wgen", new TextCommandCallingArgs
			{
				Caller = AdminCaller,
				RawArgs = new CmdArgs($"regenrange {x1} {z1} {x2} {z2}")
			});
		}
	}

	public class MainModSystem : ModSystem
	{
		public override void StartServerSide(ICoreServerAPI api)
		{
			Harmony harmony = new(Mod.Info.ModID);
			harmony.PatchAll();

			api.ChatCommands.Create("regenArch").RequiresPrivilege(Privilege.chat).WithDesc("Testing").HandleWith((TextCommandCallingArgs args) => {

				Helpers.ResetArchives(api);

				return  TextCommandResult.Success("Disappeared!");
			});
		}
	}
}