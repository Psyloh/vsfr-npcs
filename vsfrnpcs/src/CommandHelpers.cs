using Microsoft.VisualBasic;
using System;
using System.Linq;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace VSFRNPCS
{
	public static class Helpers
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

		public static void TeleportXYZ(ICoreServerAPI api, IServerPlayer player, string coords)
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

		public static void TeleportRole(ICoreServerAPI api, IServerPlayer player, string roleName)
		{
			var role = api.Server.Config.Roles.FirstOrDefault(r => r.Name == roleName);
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
			TeleportXYZ(api, player, cmd.ToString());
		}

		public static bool CanResetDungeon(ICoreServerAPI api, string dungeonName)
		{
			var modSys = api.ModLoader.GetModSystem<MainModSystem>();
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

		public static void ResetDungeon(ICoreServerAPI api, string name)
		{
			var sSys = api.ModLoader.GetModSystem<GenStoryStructures>();
			var dungeon = sSys.Structures.Get(name);

			var chunkSize = GlobalConstants.ChunkSize;
			var x1 = dungeon.Location.MinX / chunkSize;
			var z1 = dungeon.Location.MinZ / chunkSize;
			var x2 = dungeon.Location.MaxX / chunkSize;
			var z2 = dungeon.Location.MaxZ / chunkSize;

			var modSys = api.ModLoader.GetModSystem<MainModSystem>();
			modSys.Server.SetLastReset(name, DateAndTime.Now);

			var message = Lang.Get("game:reset-dungeon-message");
			api.SendMessageToGroup(GlobalConstants.GeneralChatGroup, message, EnumChatType.Notification);
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
}