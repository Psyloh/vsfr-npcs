using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;
using VSFRNPCS.Server;

namespace VSFRNPCS
{
	[HarmonyPatch(typeof(DialogueController), nameof(DialogueController.JumpTo))]
	public static class DialogueControllerJumpToPatch
	{
		public static bool Prefix(DialogueController __instance, string code)
		{
			if (code == null)
			{
				__instance.Trigger(__instance.NPCEntity, "closedialogue", null);
				return false;
			}
			return true;
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
				ApiModHelper.Api.Logger.Error("CodeMatcher can't find the instruction sequence x_x");
			}
			return matcher.Instructions();
		}

		static string? Execute(DialogueController controller, DialogueComponent component)
		{
			if (component.Type == "talk" || component.Type == "condition")
			{
				return component.Execute();
			}
			
			if (component.Type != "jump")
			{
				component.Execute();
				if (component.JumpTo == null)
				{
					controller.Trigger(null, "closedialogue", null);
				}
				return component.JumpTo;
			}

			var trigger = component.Trigger;
			var data = component.TriggerData;

			if (data.KeyExists("values"))
			{
				return ExecuteSwitch(controller, trigger, data);
			}

			var jumpIf = data["if"].AsString("");
			var jumpElse = data["else"].AsString("");

			return Execute(controller, trigger, data) ? jumpIf : jumpElse;
		}

		static bool Execute(DialogueController controller, string triggerName, JsonObject data)
		{
			switch (triggerName)
			{
				case "canResetDungeon":
					var name = data["name"].AsString("");

					return CmdHelpers.CanResetDungeon(name);

				case "hasRole":
					var role = data["role"].AsString("");

					return CmdHelpers.HasRole(controller.PlayerEntity, role);

				case "hasPrivilege":
					var privilege = data["privilege"].AsString("");

					return CmdHelpers.HasPrivilege(controller.PlayerEntity, privilege);

				case "hasAnyOfPrivileges":
					var privileges = (data["privileges"].Token?.ToObject<string[]>()) ?? throw new Exception(@"""privileges"" value should be an array");

					return CmdHelpers.HasAnyOfPrivileges(controller.PlayerEntity, privileges);

				case "hasAllPrivileges":
					privileges = (data["privileges"].Token?.ToObject<string[]>()) ?? throw new Exception(@"""privileges"" value should be an array");

					return CmdHelpers.HasAllPrivileges(controller.PlayerEntity, privileges);

				default:
					return false;
			}
		}

		static string ExecuteSwitch(DialogueController controller, string triggerName, JsonObject data)
		{
			return triggerName switch
			{
				"switchRole" => CmdHelpers.SwitchRole(controller.PlayerEntity, data),
				"switchPrivilege" => CmdHelpers.SwitchPrivilege(controller.PlayerEntity, data),
				_ => "",
			};
		}
	}
}