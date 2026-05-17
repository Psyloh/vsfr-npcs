using HarmonyLib;
using System;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using VSFRNPCS.Server;

namespace VSFRNPCS
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

				Helpers.TeleportXYZ(api, player, $"{x} {y} {z}");
			}
			else if (value == "teleportRole")
			{
				var roleName = data["role"].AsString(player.Role.Name);

				Helpers.TeleportRole(api, player, roleName);
			}
			else if (value == "playsound")
			{
				var path = data["path"].AsString("");

				api.World.PlaySoundFor(AssetLocation.CreateOrNull(path), player);
			}
			else if (value == "checkDungeon")
			{
				var dungeon = data["name"].AsString("");
				var delay = data["delay"].AsDouble(0);

				Helpers.CheckDungeon(api, __instance.entity, dungeon, delay);
			}
			else if (value == "resetDungeon")
			{
				var id = data["name"].AsString("");
				Helpers.ResetDungeon(api, id);
			}
			else if (value == "setRole")
			{
				var role = data["role"].AsString("");
				Helpers.ChangeRole(player, role);
			}
		}
	}

	public class MainModSystem : ModSystem
	{
		ServerSide? _server;
		public ServerSide Server => _server ?? throw new Exception("Server is null");

		public override void StartServerSide(ICoreServerAPI api)
		{
			_server = new(api);

			Harmony harmony = new(Mod.Info.ModID);
			harmony.PatchAll();
		}

		public override void Dispose()
		{
			Harmony harmony = new(Mod.Info.ModID);
			harmony.UnpatchAll();

			_server?.Dispose();
		}
	}
}