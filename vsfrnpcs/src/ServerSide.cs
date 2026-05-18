using System;
using System.Collections.Generic;
using System.ComponentModel;
using Vintagestory.API.Common;
using Vintagestory.API.Common.CommandAbbr;
using Vintagestory.API.Server;

namespace VSFRNPCS.Server
{
	public class Config
	{
		const string FILENAME = "dungeonReset.json";

		public double GlobalDelay { get; set; }
		public Dictionary<string, double> Delays { get; set; } = [];

		public static Config Get()
		{
			Config config;
			try
			{
				config = ApiHelper.LoadConfig(FILENAME);
				config ??= new();
			}
			catch (Exception e)
			{
				ApiHelper.Warning("Configuration file corrupted - loading default settings! Please fix or delete the file...");
				ApiHelper.Error(e);

				return new();
			}
			ApiHelper.Api.StoreModConfig(config, FILENAME);

			return config;
		}
	}

	public class ServerSide : IDisposable
	{
		readonly Dictionary<string, long> _dungeonReset;

		public Config Config { get; init; }

		public ServerSide(ICoreServerAPI api, Mod mod)
		{
			ApiHelper.Api = api;
			ApiHelper.Mod = mod;

			Config = Config.Get();

			_dungeonReset = api.WorldManager.SaveGame.GetData<Dictionary<string, long>>("dungeonReset") ?? [];

			api.ChatCommands.Create("regenArch").RequiresPrivilege(Privilege.chat).WithDesc("Testing").HandleWith(args => {

				Helpers.ResetDungeon(api, "resonancearchive");

				return TextCommandResult.Success("Disappeared!");
			});

			api.Event.GameWorldSave += Save;
		}

		void Save()
		{
			if (_dungeonReset.Count > 0)
			{
				ApiHelper.Api.WorldManager.SaveGame.StoreData("dungeonReset", _dungeonReset);
			}
		}

		public DateTime? GetLastReset(string dungeonName)
		{
			if (_dungeonReset.TryGetValue(dungeonName, out var time))
			{
				return DateTime.FromBinary(time);
			}
			return null;
		}

		public void SetLastReset(string dungeonName, DateTime time)
		{
			_dungeonReset[dungeonName] = time.ToBinary();
		}

		public void Dispose()
		{

		}
	}

	public static class ApiHelper
	{
		static ICoreServerAPI? _api;
		public static ICoreServerAPI Api
		{
			get => _api ?? throw new Exception("Api is null");
			set => _api = value;
		}

		static Mod? _mod;
		public static Mod Mod
		{
			get => _mod ?? throw new Exception("Mod is null");
			set => _mod = value;
		}

		public static void Warning(string message) => Mod.Logger.Warning(message);
		public static void Error(string message) => Mod.Logger.Error(message);
		public static void Error(Exception ex) => Mod.Logger.Error(ex);

		public static Config LoadConfig(string filename) => Api.LoadModConfig<Config>(filename);
	}
}