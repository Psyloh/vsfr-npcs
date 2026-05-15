using HarmonyLib;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
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

            if ((triggeringEntity as EntityPlayer)?.Player is not IServerPlayer player)
            {
                return;
            }

            if (value == "teleport")
			{
				__instance.Dialog?.TryClose();

				var x = data["x"].AsString("~0");
				var y = data["y"].AsString("~0");
				var z = data["z"].AsString("~0");

				Helpers.TeleportXYZ(player, $"{x} {y} {z}");
			}
			else if (value == "teleportRole")
			{
				var roleName = data["role"].AsString(player.Role.Name);
				var role = api.Server.Config.Roles.FirstOrDefault(r => r.Name == roleName);
				if (role is null)
				{
					return;
				}

				var spawnPos = role.DefaultSpawn;
				Helpers.TeleportXYZ(player, $"{spawnPos.x} {spawnPos.y ?? ""} {spawnPos.z}");
			}
			else if (value == "playsound")
			{
				var path = data["path"].AsString("");

				api.World.PlaySoundFor(AssetLocation.CreateOrNull(path), player);
			}
			else if (value == "checkDungeon")
			{
				var dungeon = data["dungeon"].AsString("");
				var delay = data["delay"].AsDouble(0);

				Helpers.CheckDungeon(api, __instance.entity, dungeon, delay);
			}
			else if (value == "resetDungeon")
			{
				var id = data["id"].AsString("");
				Helpers.ResetDungeon(api, id);
			}
			else if (value == "setRole")
			{
				var role = data["role"].AsString("");
				Helpers.ChangeRole(player, role);
			}
		}
	}

	public static class Helpers
	{
		public static Caller AdminCaller
		{
			get => new()
			{
				Type = EnumCallerType.Console,
				CallerRole = "admin",
				CallerPrivileges = ["*"],
				FromChatGroupId = GlobalConstants.ConsoleGroup
			};
		}

		public static void TeleportXYZ(IServerPlayer player, string coords)
		{
			var caller = AdminCaller;
			caller.Player = player;

			var args = new TextCommandCallingArgs
			{
				Caller = caller,
				RawArgs = new CmdArgs($"{player.PlayerName} {coords}")
			};
			api.ChatCommands.Execute("tp", args);
		}

		public static void CheckDungeon(ICoreServerAPI api, EntityHandle entity, string dungeonName, double delay)
		{
			var tree = entity.WatchedAttributes.GetOrAddTreeAttribute("resetDungeon");
			
			var modSys = api.ModLoader.GetModSystem<MainModSystem>();
			var lastReset = modSys.GetLastReset(dungeonName);
			var remaining = lastReset + delay - api.World.Calendar.TotalHours;

			tree.SetDouble(dungeonName, remaining <= 0 ? 0 : remaining);
			entity.WatchedAttributes.MarkPathDirty("resetDungeon");
		}

		public static void ResetDungeon(ICoreServerAPI api, string id)
		{
			var sSys = api.ModLoader.GetModSystem<GenStoryStructures>();
			var dungeon = sSys.Structures.Get(id);

			var chunkSize = GlobalConstants.ChunkSize;
			var x1 = dungeon.Location.MinX / chunkSize;
			var z1 = dungeon.Location.MinZ / chunkSize;
			var x2 = dungeon.Location.MaxX / chunkSize;
			var z2 = dungeon.Location.MaxZ / chunkSize;

			var modSys = api.ModLoader.GetModSystem<MainModSystem>();
			modSys.SetLastReset(id, api.World.Calendar.TotalHours);

			api.SendMessageToGroup(GlobalConstants.ServerInfoChatGroup, Lang.Get("game:reset-dungeon-message"), EnumChatType.Notification);
			api.ChatCommands.Execute("wgen", new TextCommandCallingArgs
			{
				Caller = AdminCaller,
				RawArgs = new CmdArgs($"regenrange {x1} {z1} {x2} {z2}")
			});
		}

		public static void ChangeRole(IServerPlayer player, string role)
		{
			player.SetRole(role);
		}
	}

	public class MainModSystem : ModSystem
	{
		Dictionary<string, double>? _dungeonReset;

		ICoreServerAPI? _api;
		ICoreServerAPI Api => _api ?? throw new Exception("Api is null");

		public double GetLastReset(string dungeonName)
		{
			if (_dungeonReset.TryGetValue(dungeonName, out var time))
			{
				return time;
			}
			return -1;
		}

		public void SetLastReset(string dungeonName, double time)
		{
			_dungeonReset[dungeonName] = time;
		}

		public override void StartServerSide(ICoreServerAPI api)
		{
			Api = api;

			Harmony harmony = new(Mod.Info.ModID);
			harmony.PatchAll();

			api.ChatCommands.Create("regenArch").RequiresPrivilege(Privilege.chat).WithDesc("Testing").HandleWith((TextCommandCallingArgs args) => {

				Helpers.ResetDungeon(api, "resonancearchive");

				return  TextCommandResult.Success("Disappeared!");
			});

			_dungeonReset = api.WorldManager.SaveGame.GetData<Dictionary<string, double>>("dungeonReset") ?? [];

			api.Event.GameWorldSave += Save;
		}

		void Save()
		{
			if (_dungeonReset.Count > 0)
			{
				Api.WorldManager.SaveGame.StoreData("dungeonReset", _dungeonReset);
			}
		}

		public override void Dispose()
		{
			Harmony harmony = new(Mod.Info.ModID);
			harmony.UnpatchAll();
		}
	}
}