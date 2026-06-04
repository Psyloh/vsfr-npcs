using HarmonyLib;
using Newtonsoft.Json;
using ProtoBuf;
using System;
using System.Collections.Concurrent;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.CommandAbbr;
using Vintagestory.API.Config;
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

	[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
	public class ClearVariables { }

	public class MainModSystem : ModSystem
	{
		readonly ConcurrentDictionary<string, DialogueController> _controllers = [];

		public void AddController(DialogueController controller)
			=> _controllers[controller.PlayerEntity.PlayerUID] = controller;

		public override void Start(ICoreAPI api)
		{
			ApiModHelper.Mod = Mod;

			api.Network.RegisterChannel(Mod.Info.ModID)
				.RegisterMessageType<ClearVariables>()
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
				})
				.SetMessageHandler<ClearVariables>(data => CmdHelpers.ClearVariables());

			api.ChatCommands.GetOrCreate("debug")
				.RequiresPlayer()
				.BeginSub("checkVariables")
				.WithAlias("cv")
				.HandleWith(args =>
				{
					var modSys = ApiModHelper.GetSystem<VariablesModSystem>();

					((IClientPlayer)args.Caller.Player).ShowChatNotification($"{modSys.VarData.PlayerVariables.Count}");
					((IClientPlayer)args.Caller.Player).ShowChatNotification($"{modSys.VarData.GlobalVariables.Variables.Count}");
					((IClientPlayer)args.Caller.Player).ShowChatNotification($"{modSys.VarData.GroupVariables.Count}");

					return TextCommandResult.Success();
				})
				.EndSub();
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

			api.ChatCommands.GetOrCreate("debug")
				.RequiresPlayer()
				.RequiresPrivilege(Privilege.controlserver)
				.BeginSub("checkVariables")
				.WithAlias("cv")
				.HandleWith(args =>
				{
					var modSys = ApiModHelper.GetSystem<VariablesModSystem>();

					ApiModHelper.ServerNotification(args.Caller.Player, $"{modSys.VarData.PlayerVariables.Count}");
					ApiModHelper.ServerNotification(args.Caller.Player, $"{modSys.VarData.GlobalVariables.Variables.Count}");
					ApiModHelper.ServerNotification(args.Caller.Player, $"{modSys.VarData.GroupVariables.Count}");

					return TextCommandResult.Success();
				})
				.EndSub()
				.BeginSub("fuckingClearVariables")
				.WithAlias("fcv")
				.HandleWith(args =>
				{
					foreach (var player in ApiModHelper.OnlineServerPlayers)
					{
						player.Entity.WatchedAttributes["variables"] = new TreeAttribute();
						player.Entity.WatchedAttributes.MarkPathDirty("variables");
					}

					var entities = ApiModHelper.LoadedEntities.Where(e => e.Code.Domain == "vsfrnpcs");
					foreach (var entity in entities)
					{
						entity.WatchedAttributes["variables"] = new TreeAttribute();
						entity.WatchedAttributes.MarkPathDirty("variables");
					}

					api.Network.GetChannel(Mod.Info.ModID)
						.BroadcastPacket(new ClearVariables());

					CmdHelpers.ClearVariables();

					return TextCommandResult.Success("Fucking cleared!!");
				})
				.EndSub();

			api.ChatCommands.GetOrCreate("vsfr")
				.RequiresPrivilege(Privilege.chat)
				.BeginSub("indungeon")
				.RequiresPlayer()
				.WithArgs(api.ChatCommands.Parsers.Word("dungeon"))
				.HandleWith(args =>
				{
					var dungeonName = (string)args[0];

					var modSys = ApiModHelper.GetSystem<GenStoryStructures>();
					var dungeon = modSys.Structures.Get(dungeonName);

					if (dungeon is null)
					{
						return TextCommandResult.Success($"No such dungeon as {dungeonName}");
					}
					else
					{
						if (CmdHelpers.IsPlayerInDungeonArea(args.Caller.Player.Entity, dungeon, out var inside) && inside)
						{
							return TextCommandResult.Success($"Yes, you are...");
						}
						return TextCommandResult.Success($"Absolutely not!");
					}
				})
				.EndSub();
		}

		static bool Execute(EntityPlayer player, string triggerName, JsonObject data)
		{
			switch (triggerName.ToLowerInvariant())
			{
				case "canresetdungeon":
					var name = data["name"].AsString("");

					return CmdHelpers.CanResetDungeon(name);

				case "hasrole":
					var role = data["role"].AsString("");

					return CmdHelpers.HasRole(player, role);

				case "hasprivilege":
					var privilege = data["privilege"].AsString("");

					return CmdHelpers.HasPrivilege(player, privilege);

				case "hasanyofprivileges":
					var privileges = (data["privileges"].Token?.ToObject<string[]>()) ?? throw new Exception(@"""privileges"" value should be an array");

					return CmdHelpers.HasAnyOfPrivileges(player, privileges);

				case "hasallprivileges":
					privileges = (data["privileges"].Token?.ToObject<string[]>()) ?? throw new Exception(@"""privileges"" value should be an array");

					return CmdHelpers.HasAllPrivileges(player, privileges);

				default:
					return false;
			}
		}

		static string ExecuteSwitch(EntityPlayer player, string triggerName, JsonObject data)
		{
			return triggerName.ToLowerInvariant() switch
			{
				"switchrole" => CmdHelpers.SwitchRole(player, data),
				"switchprivilege" => CmdHelpers.SwitchPrivilege(player, data),
				_ => ""
			};
		}

		public override void Dispose()
		{
			Harmony harmony = new(Mod.Info.ModID);
			harmony.UnpatchAll();
		}
	}
}