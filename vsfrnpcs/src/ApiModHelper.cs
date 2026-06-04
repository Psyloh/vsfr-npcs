using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using VSFRNPCS.Server;

namespace VSFRNPCS
{
	public static class ApiModHelper
	{
		static ICoreClientAPI? _capi;
		public static ICoreClientAPI CApi
		{
			get => _capi ?? throw new Exception();
			set => _capi = value;
		}

		static ICoreServerAPI? _sapi;
		public static ICoreServerAPI SApi
		{
			get => _sapi ?? throw new Exception();
			set => _sapi = value;
		}

		public static bool IsServer => _sapi != null;
		public static bool IsClient => _capi != null;

		public static ICoreAPI Api => _capi as ICoreAPI ?? _sapi as ICoreAPI ?? throw new Exception("Both Apis are null");

		static Mod? _mod;
		public static Mod Mod
		{
			get => _mod ?? throw new Exception("Mod is null");
			set => _mod = value;
		}

		static Caller AdminCaller => new()
		{
			Type = EnumCallerType.Console,
			CallerRole = "admin",
			CallerPrivileges = ["*"],
			FromChatGroupId = GlobalConstants.ConsoleGroup
		};

		public static void Warning(string message)
			=> Mod.Logger.Warning(message);

		public static void Error(string message)
			=> Mod.Logger.Error(message);

		public static void Error(Exception ex)
			=> Mod.Logger.Error(ex);

		public static void ExecuteCommand(string command, TextCommandCallingArgs args, IPlayer? player = null)
		{
			var caller = AdminCaller;
			if (player != null)
			{
				caller.Player = player;
			}
			args.Caller = caller;
			Api.ChatCommands.Execute(command, args);
		}

		public static IServerConfig ServerConfig
			=> SApi.Server.Config;

		public static Random Rand
			=> Api.World.Rand;

		public static IServerEventAPI ServerEvents
			=> SApi.Event;

		public static int GetRainY(int x, int z)
			=> Api.World.BlockAccessor.GetRainMapHeightAt(x, z);

		public static ICollection<Entity> LoadedEntities
			=> SApi.World.LoadedEntities.Values;

		public static T GetSaveGameData<T>(string key)
			=> SApi.WorldManager.SaveGame.GetData<T>(key);

		public static IEnumerable<IServerPlayer> OnlineServerPlayers
			=> SApi.World.AllOnlinePlayers.Cast<IServerPlayer>();

		public static IPlayerRole? GetRole(string name)
			=> ServerConfig.Roles.FirstOrDefault(r => r.Name == name);

		public static T GetSystem<T>() where T : ModSystem
			=> Api.ModLoader.GetModSystem<T>();

		public static void ServerNotification(string message)
			=> SApi.SendMessageToGroup(GlobalConstants.GeneralChatGroup, message, EnumChatType.Notification);

		public static void ServerNotification(IPlayer player, string message)
			=> SApi.SendMessage(player, GlobalConstants.GeneralChatGroup, message, EnumChatType.Notification);

		public static void EnqueueToMainThread(Action action, string label)
			=> SApi.Event.EnqueueMainThreadTask(action, label);

		public static Config LoadConfig(string filename)
			=> Api.LoadModConfig<Config>(filename);

		public static void StoreConfig(Config config, string filename)
			=> Api.StoreModConfig(config, filename);

		public static void SaveData<T>(string key, T data)
			=> SApi.WorldManager.SaveGame.StoreData(key, data);
	}
}