using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using VSFRNPCS.Server;

namespace VSFRNPCS
{
	[HarmonyPatch(typeof(EntityBehaviorConversable), "Controller_DialogTriggers")]
	public static class ConversableTriggersPatch
	{
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

				CmdHelpers.TeleportXYZ(player, $"{x} {y} {z}");
			}
			else if (value == "teleportRole")
			{
				var roleName = data["role"].AsString(player.Role.Name);

				CmdHelpers.TeleportRole(player, roleName);
			}
			else if (value == "playsound")
			{
				var path = data["path"].AsString("");

				api.World.PlaySoundFor(AssetLocation.CreateOrNull(path), player);
			}
			else if (value == "resetDungeon")
			{
				var id = data["name"].AsString("");
				CmdHelpers.RegisterDungeonReset(id);
			}
			else if (value == "setRole")
			{
				var role = data["role"].AsString("");
				CmdHelpers.ChangeRole(player, role);
			}
		}
	}
}