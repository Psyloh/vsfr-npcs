using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection.Emit;
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
			else if (value == "setvar")
			{
				var name = data["name"].AsString("");
			}
		}
	}

	[HarmonyPatch(typeof(DialogueController), nameof(DialogueController.ContinueExecute))]
	public static class DialogueControllerContinueExecutePatch
	{
		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
		{
			var matcher = new CodeMatcher(instructions, generator);
			matcher.MatchStartForward(
				CodeMatch.LoadsArgument(),
				CodeMatch.LoadsField(AccessTools.Field(typeof(DialogueController), "currentDialogueCmp")),
				CodeMatch.Calls(() => default(DialogueComponent)!.Execute)
			);

			if (matcher.IsValid)
			{
				matcher.RemoveInstructions(3);
				matcher.Insert(
					CodeInstruction.LoadArgument(0),
					new CodeInstruction(OpCodes.Dup),
					CodeInstruction.LoadField(typeof(DialogueController), "currentDialogueCmp"),
					CodeInstruction.Call(typeof(DialogueControllerContinueExecutePatch), nameof(Execute), [typeof(DialogueController), typeof(DialogueComponent)])
				);
			}
			else
			{
				ApiHelper.Api.Logger.Error("CodeMatcher can't find the instruction sequence x_x");
			}
			return matcher.Instructions();
		}

		static string Execute(DialogueController controller, DialogueComponent component)
		{
			if (component.Type != "jump")
			{
				return component.Execute();
			}

			var trigger = component.Trigger;
			var data = component.TriggerData;
			var jumpIf = data["if"].AsString("");
			var jumpElse = data["else"].AsString("");

			return Execute(controller, trigger, data) ? jumpIf : jumpElse;
		}

		static bool Execute(DialogueController controller, string triggerName, JsonObject data)
		{
			if (triggerName == "canResetDungeon")
			{
				var name = data["name"].AsString("");

				return Helpers.CanResetDungeon(ApiHelper.Api, name);
			}
			return false;
		}
	}

	public class MainModSystem : ModSystem
	{
		ServerSide? _server;
		public ServerSide Server => _server ?? throw new Exception("Server is null");

		public override void StartServerSide(ICoreServerAPI api)
		{
			_server = new(api, Mod);

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