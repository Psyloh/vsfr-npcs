using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Vintagestory.ServerMods;

namespace VSFRNPCS.Server
{
	public static class CmdHelpers
	{
		static Caller AdminCaller
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
			ApiModHelper.Api.ChatCommands.Execute("tp", args);
		}

		public static void TeleportRole(IServerPlayer player, string roleName)
		{
			var role = ApiModHelper.Api.Server.Config.Roles.FirstOrDefault(r => r.Name == roleName);
			if (role is null)
			{
				return;
			}

			var spawnPos = role.DefaultSpawn;
			var cmd = new StringBuilder($"{spawnPos.x} ");
			if (spawnPos.y != null)
			{
				cmd.Append($"{spawnPos.y} ");
			}
			cmd.Append($"{spawnPos.z} ");
			TeleportXYZ(player, cmd.ToString());
		}

		public static bool CanResetDungeon(string dungeonName)
		{
			var modSys = ApiModHelper.Api.ModLoader.GetModSystem<MainModSystem>();
			var isPending = modSys.Server.IsPending(dungeonName);
			if (isPending)
			{
				return false;
			}

			var lastReset = modSys.Server.GetLastReset(dungeonName);
			if (lastReset == null)
			{
				return true;
			}

			var config = modSys.Server.Config;
			if (!config.Delays.TryGetValue(dungeonName, out var delay))
			{
				delay = config.GlobalDelay;
			}
			return lastReset.Value.AddHours(delay) <= DateTime.Now;
		}

		public static void RegisterDungeonReset(string name)
		{
			var modSys = ApiModHelper.Api.ModLoader.GetModSystem<MainModSystem>();
			modSys.Server.RegisterReset(name);
		}

		public static void ResetDungeon(string name)
		{
			var sSys = ApiModHelper.Api.ModLoader.GetModSystem<GenStoryStructures>();
			var dungeon = sSys.Structures.Get(name);
			if (dungeon == null)
			{
				throw new Exception($@"There's no such dungeon as ""{dungeon}""");
			}

			var message = Lang.Get("game:reset-dungeon-message");
			ApiModHelper.Api.SendMessageToGroup(GlobalConstants.GeneralChatGroup, message, EnumChatType.Notification);

			var chunkSize = GlobalConstants.ChunkSize;
			var x1 = dungeon.Location.MinX / chunkSize;
			var z1 = dungeon.Location.MinZ / chunkSize;
			var x2 = dungeon.Location.MaxX / chunkSize;
			var z2 = dungeon.Location.MaxZ / chunkSize;			

			var modSys = ApiModHelper.Api.ModLoader.GetModSystem<MainModSystem>();
			modSys.Server.SetReset(name);

			var maxHeight = ApiModHelper.Api.WorldManager.MapSizeY;
			var exclusionArea = dungeon.Location.GrowToInclude(x1, 0, z1).GrowToInclude(x1, maxHeight, z1);

			var players = ApiModHelper.Api.World.AllOnlinePlayers.Cast<IServerPlayer>();
			foreach(var player in players)
			{
				if (player.ConnectionState == EnumClientState.Playing && exclusionArea.Contains(player.Entity.Pos.XYZFloat))
				{
					player.Entity.ReceiveDamage(new() { Type = EnumDamageType.Injury, KnockbackStrength = 0}, 5);
				}
			}

			ApiModHelper.Api.ChatCommands.Execute("wgen", new TextCommandCallingArgs
			{
				Caller = AdminCaller,
				RawArgs = new CmdArgs($"regenrange {x1} {z1} {x2} {z2}")
			});
		}

		public static void Expel(EntityPlayer player, StoryStructureLocation location, bool cheating)
		{
			var x = player.Pos.X <= location.CenterPos.X ? location.Location.X1 - 1 : location.Location.X2 + 2;
			var z = player.Pos.Z <= location.CenterPos.Z ? location.Location.Z1 - 1 : location.Location.Z2 + 2;

			player.TeleportTo(x, ApiModHelper.Api.World.BlockAccessor.GetRainMapHeightAt(x, z), z);

			if (cheating)
			{
				player.ReceiveDamage(new() { Type = EnumDamageType.Injury, KnockbackStrength = 0 }, 5);
			}
		}

		public static bool HasRole(EntityPlayer player, string role) =>
			player.Player.Role.Code == role;

		public static string SwitchRole(EntityPlayer player, JsonObject data)
		{
			var role = player.Player.Role.Code;

			if (data["values"].Token is JObject roles)
			{
				foreach (var property in roles.Properties())
				{
					if (property.Name == role)
					{
						return property?.Value.Value<string>() ?? throw new Exception("");
					}
				}
			}
			return "";
		}

		public static bool HasPrivilege(EntityPlayer player, string privilege) =>
			player.Player.Privileges.Contains(privilege);

		public static bool HasAnyOfPrivileges(EntityPlayer player, string[] privileges) =>
			privileges.Any(p => player.Player.Privileges.Contains(p));

		public static bool HasAllPrivileges(EntityPlayer player, string[] privileges) =>
			privileges.All(p => player.Player.Privileges.Contains(p));

		public static string SwitchPrivilege(EntityPlayer player, JsonObject data)
		{
			var privileges = player.Player.Privileges;

			if (data["values"].Token is JObject paths)
			{
				foreach (var property in paths.Properties())
				{
					if (privileges.Contains(property.Name))
					{
						return property?.Value.Value<string>() ?? throw new Exception("");
					}
				}
			}
			return "";
		}

		public static void ChangeRole(IServerPlayer player, string role) =>
			player.SetRole(role);
	}
}