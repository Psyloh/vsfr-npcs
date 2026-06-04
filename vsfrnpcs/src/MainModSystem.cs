using HarmonyLib;
using Newtonsoft.Json;
using ProtoBuf;
using System;
using System.Collections.Concurrent;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using VSFRNPCS.Server;

namespace VSFRNPCS
{
	[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
	public class JumpTo
	{
		public string? Code { get; init; } = null;

		public static JumpTo New(string code)
			=> new() { Code = code };
	}

	[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
	public class Trigger
	{
		public required string Name { get; init; }
		public required string Data { get; init; }

		public static Trigger New(string name, JsonObject data)
			=> new() { Name = name, Data = data.Token!.ToString(Formatting.None) };
	}

	public class MainModSystem : ModSystem
	{
		readonly ConcurrentDictionary<string, DialogueController> _controllers = [];

		public void AddController(DialogueController controller)
			=> _controllers[controller.PlayerEntity.PlayerUID] = controller;

		public override void Start(ICoreAPI api)
		{
			ApiModHelper.Mod = Mod;

			api.Network.RegisterChannel(Mod.Info.ModID)
				.RegisterMessageType<Trigger>()
				.RegisterMessageType<JumpTo>();
		}

		public override void StartClientSide(ICoreClientAPI api)
		{
			ApiModHelper.CApi = api;

			Harmony harmony = new(Mod.Info.ModID);
			harmony.PatchAllUncategorized();

			api.Network.GetChannel(Mod.Info.ModID)
				.SetMessageHandler<JumpTo>(data =>
				{
					DialogueControllerContinueExecutePatch.Controller?.JumpTo(data.Code);
				});
		}

		public override void StartServerSide(ICoreServerAPI api)
		{
			ApiModHelper.SApi = api;

			Harmony harmony = new(Mod.Info.ModID);
			harmony.PatchCategory("server");

			if (api.Server.IsDedicated)
			{
				harmony.PatchAllUncategorized();
			}

			api.Network.GetChannel(Mod.Info.ModID)
				.SetMessageHandler<Trigger>((player, data) =>
				{
					var triggerName = data.Name;
					var triggerData = JsonObject.FromJson(data.Data);

					string? result;
					if (triggerData.KeyExists("values"))
					{
						result = ExecuteSwitch(player.Entity, triggerName, triggerData);
					}
					else
					{
						var jumpIf = triggerData["if"].AsString("");
						var jumpElse = triggerData["else"].AsString("");

						result = Execute(player.Entity, triggerName, triggerData) ? jumpIf : jumpElse;
					}
					api.Network.GetChannel(Mod.Info.ModID).SendPacket(JumpTo.New(result), [player]);

					if (_controllers.TryGetValue(player.PlayerUID, out var controller))
					{
						controller.JumpTo(result);
					}
				});
		}

		static bool Execute(EntityPlayer player, string triggerName, JsonObject data)
		{
			switch (triggerName)
			{
				case "canResetDungeon":
					var name = data["name"].AsString("");

					return CmdHelpers.CanResetDungeon(name);

				case "hasRole":
					var role = data["role"].AsString("");

					return CmdHelpers.HasRole(player, role);

				case "hasPrivilege":
					var privilege = data["privilege"].AsString("");

					return CmdHelpers.HasPrivilege(player, privilege);

				case "hasAnyOfPrivileges":
					var privileges = (data["privileges"].Token?.ToObject<string[]>()) ?? throw new Exception(@"""privileges"" value should be an array");

					return CmdHelpers.HasAnyOfPrivileges(player, privileges);

				case "hasAllPrivileges":
					privileges = (data["privileges"].Token?.ToObject<string[]>()) ?? throw new Exception(@"""privileges"" value should be an array");

					return CmdHelpers.HasAllPrivileges(player, privileges);

				default:
					return false;
			}
		}

		static string ExecuteSwitch(EntityPlayer player, string triggerName, JsonObject data)
		{
			return triggerName switch
			{
				"switchRole" => CmdHelpers.SwitchRole(player, data),
				"switchPrivilege" => CmdHelpers.SwitchPrivilege(player, data),
				_ => "",
			};
		}

		public override void Dispose()
		{
			Harmony harmony = new(Mod.Info.ModID);
			harmony.UnpatchAll();
		}
	}
}