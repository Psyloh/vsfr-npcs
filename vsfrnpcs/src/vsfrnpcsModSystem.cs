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
			var api = __instance.entity.Api as ICoreServerAPI;
			if (api == null) return;

			var player = ((triggeringEntity as EntityPlayer)?.Player as IServerPlayer);
			if (player == null) return;

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
		}
	}

	[HarmonyPatch(typeof(WgenCommands), "Regen")]
	public static class WgenResetPatch
	{
		public static void Prefix(WgenCommands __instance)
		{
			
		}
	}

	public class regenStoryLoc
	{

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
	}

	public class vsfrnpcsModSystem : ModSystem
	{
		// Called on server and client
		// Useful for registering block/entity classes on both sides
		public override void Start(ICoreAPI api)
		{
			Harmony harmony = new("vsfrnpcs");
			harmony.PatchAll();
		}

		public override void StartServerSide(ICoreServerAPI api)
		{
			api.ChatCommands.Create("regenArch").RequiresPrivilege(Privilege.chat).WithDesc("Testing").HandleWith((TextCommandCallingArgs args) => {

				var chunkSize = GlobalConstants.ChunkSize;
				var chunksInRegion = api.WorldManager.RegionSize / chunkSize;

				var sSys = api.ModLoader.GetModSystem<GenStoryStructures>();
				var archive = sSys.Structures.Get("resonancearchive");

				var x1 = archive.Location.MinX / chunkSize;
				var z1 = archive.Location.MinZ / chunkSize;
				var x2 = archive.Location.MaxX / chunkSize;
				var z2 = archive.Location.MaxZ / chunkSize;
				
				api.ChatCommands.Execute("wgen", new TextCommandCallingArgs
				{
					Caller = Helpers.AdminCaller,
					RawArgs = new CmdArgs($"regenrange {x1} {z1} {x2} {z2}")
				});

				return  TextCommandResult.Success("Disappeared!");
			});
		}

		public override void StartClientSide(ICoreClientAPI api)
		{

		}
	}

	class VecComparer : IEqualityComparer<FastVec2i>
	{
		bool IEqualityComparer<FastVec2i>.Equals(FastVec2i x, FastVec2i y)
		{
			return x == y;
		}

		int IEqualityComparer<FastVec2i>.GetHashCode(FastVec2i obj)
		{
			return obj.GetHashCode();
		}
	}
}
