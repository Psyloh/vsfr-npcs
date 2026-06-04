using HarmonyLib;
using System.Collections.Generic;
using System.Reflection.Emit;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace VSFRNPCS
{
	[HarmonyPatch(typeof(DialogueController), nameof(DialogueController.JumpTo))]
	public static class DialogueControllerJumpToPatch
	{
		public static bool Prefix(DialogueController __instance, string code)
		{
			if (code == null)
			{
				__instance.Trigger(null, "closedialogue", null);
				return false;
			}
			return true;
		}
	}

	[HarmonyPatch(typeof(DialogueController), nameof(DialogueController.ContinueExecute))]
	public static class DialogueControllerContinueExecutePatch
	{
		public static DialogueController? Controller;

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
				ApiModHelper.CApi.Logger.Error("CodeMatcher can't find the instruction sequence x_x");
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

			var api = controller.NPCEntity.Api;
			if (api.Side == EnumAppSide.Client)
			{
				Controller = controller;
				ApiModHelper.CApi.Network.GetChannel(ApiModHelper.Mod.Info.ModID).SendPacket(Trigger.New(component.Trigger, component.TriggerData));
			}
			else if (api.Side == EnumAppSide.Server)
			{
				var modSys = ApiModHelper.GetSystem<MainModSystem>();
				modSys.AddController(controller);
			}
			return null;
		}
	}
}