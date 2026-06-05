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
		public static void ClearVariables(bool server)
		{
			var args = new TextCommandCallingArgs
			{
				RawArgs = new("clearvariables")
			};

			if (server)
			{
				ApiModHelper.ExecuteServerCommand("debug", args);
			}
			else
			{
				ApiModHelper.ExecuteClientCommand("debug", args);
			}
		}

		public static void TeleportXYZ(IPlayer player, string coords)
		{
			var args = new TextCommandCallingArgs
			{
				RawArgs = new($"{player.PlayerName} {coords}")
			};
			ApiModHelper.ExecuteServerCommand("tp", args, player);
		}

		public static void TeleportRole(IPlayer player, string roleName)
		{
			var role = ApiModHelper.GetRole(roleName);
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
			var modSys = ApiModHelper.GetSystem<DungeonResetModSystem>();
			var isPending = modSys.IsPending(dungeonName);
			if (isPending)
			{
				ApiModHelper.Error("Dungeon reset pending");
				return false;
			}

			var lastReset = modSys.GetLastReset(dungeonName);
			if (lastReset == null)
			{
				ApiModHelper.Error("Dungeon never reset");
				return true;
			}

			var config = modSys.Config;
			if (!config.Delays.TryGetValue(dungeonName, out var delay))
			{
				delay = config.GlobalDelay;
			}

			var canReset = lastReset.Value.AddHours(delay) <= DateTime.Now;
			ApiModHelper.Error($"canReset: {canReset}");
			return canReset;
		}

		public static void RegisterDungeonReset(string name)
		{
			var modSys = ApiModHelper.GetSystem<DungeonResetModSystem>();
			modSys.RegisterReset(name);
		}

		public static void ResetDungeon(string name)
		{
			var sSys = ApiModHelper.GetSystem<GenStoryStructures>();
			var dungeon = sSys.Structures.Get(name);
			if (dungeon == null)
			{
				throw new Exception($@"There's no such dungeon as ""{dungeon}""");
			}

			var chunkSize = GlobalConstants.ChunkSize;
			var x1 = dungeon.Location.MinX / chunkSize;
			var z1 = dungeon.Location.MinZ / chunkSize;
			var x2 = dungeon.Location.MaxX / chunkSize;
			var z2 = dungeon.Location.MaxZ / chunkSize;

			var modSys = ApiModHelper.GetSystem<DungeonResetModSystem>();
			modSys.SetReset(name);

			var players = ApiModHelper.OnlineServerPlayers;
			foreach (var player in players)
			{
				if (player.ConnectionState == EnumClientState.Playing && IsPlayerInDungeonArea(player.Entity, dungeon, out var inside))
				{
					Expel(player.Entity, dungeon, inside);
				}
			}

			var args = new TextCommandCallingArgs
			{
				RawArgs = new($"regenrange {x1} {z1} {x2} {z2}")
			};
			ApiModHelper.ExecuteServerCommand("wgen", args);
		}

		public static bool IsPlayerInDungeonArea(EntityPlayer player, StoryStructureLocation dungeon, out bool inside)
		{
			var pos = player.Pos;
			var location = dungeon.Location;

			inside = false;

			var inArea = location.MinX <= pos.X && location.MaxX >= pos.X && location.MinZ <= pos.Z && location.MaxZ >= pos.Z;
			if (inArea)
			{
				inside = location.MinY <= pos.Y && location.MaxY >= pos.Y;
			}
			return inArea;
		}

		public static void Expel(EntityPlayer player, StoryStructureLocation structure, bool cheating)
		{
			double x = 0, z = 0;
			switch (ApiModHelper.Rand.Next(4))
			{
				case 0:
					x = structure.Location.SizeX * ApiModHelper.Rand.NextDouble() + structure.Location.MinX;
					z = (structure.Location.MinZ / GlobalConstants.ChunkSize) * GlobalConstants.ChunkSize - 1;
					break;

				case 1:
					x = structure.Location.SizeX * ApiModHelper.Rand.NextDouble() + structure.Location.MinX;
					z = (structure.Location.MaxZ / GlobalConstants.ChunkSize + 1) * GlobalConstants.ChunkSize;
					break;

				case 2:
					x = (structure.Location.MinX / GlobalConstants.ChunkSize) * GlobalConstants.ChunkSize - 1;
					z = structure.Location.SizeZ * ApiModHelper.Rand.NextDouble() + structure.Location.MinZ;
					break;

				case 3:
					x = (structure.Location.MaxX / GlobalConstants.ChunkSize + 1) * GlobalConstants.ChunkSize;
					z = structure.Location.SizeZ * ApiModHelper.Rand.NextDouble() + structure.Location.MinZ;
					break;
			}

			var chunkX = (int)x / GlobalConstants.ChunkSize;
			var chunkZ = (int)z / GlobalConstants.ChunkSize;
			ApiModHelper.SApi.WorldManager.LoadChunkColumnPriority(chunkX, chunkZ, new ChunkLoadOptions()
			{
				OnLoaded = () =>
				{
					var y = ApiModHelper.SApi.WorldManager.GetSurfacePosY((int)x, (int)z) ?? throw new Exception("Cannot tp");
					player.TeleportToDouble(x, y, z);

					if (cheating)
					{
						player.ReceiveDamage(new() { Type = EnumDamageType.Injury, KnockbackStrength = 0 }, 5);
					}
				}
			});
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

		public static void ChangeRole(IServerPlayer player, string role)
		{
			ApiModHelper.Error($@"Changing ""{player.PlayerName}"" role to ""{role}""");
			player.SetRole(role);
		}
	}
}