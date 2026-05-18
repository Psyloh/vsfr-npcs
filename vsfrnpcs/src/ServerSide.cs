using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.CommandAbbr;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

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

		public void Save()
		{
			ApiHelper.Api.StoreModConfig(this, FILENAME);
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

			api.ChatCommands.Create("resetDungeonDelay")
				.WithAlias("rdd")
				.RequiresPrivilege(Privilege.controlserver)
				.WithArgs(api.ChatCommands.Parsers.Word("name"), api.ChatCommands.Parsers.OptionalDouble("delay"))
				.HandleWith(args =>
				{
					var name = (string)args[0];
					if (name != "global")
					{
						var structure = api.ModLoader.GetModSystem<GenStoryStructures>().Structures.Get(name);
						if (structure == null)
						{
							return TextCommandResult.Success($"{name} is not a valid dungeon name");
						}

						if (args.Parsers[1].IsMissing)
						{
							return Config.Delays.TryGetValue(name, out var delay) ?
								TextCommandResult.Success($@"""{name}"" dungeon reset delay is {delay} hours") :
								TextCommandResult.Success($@"""{name}"" dungeon doesn't have a specified delay and thus falls back to global delay : {Config.GlobalDelay} hours");
						}

						var value = (double)args[1];
						Config.Delays[name] = value;
						Config.Save();

						return TextCommandResult.Success($@"Changed ""{name}"" dungeon reset delay to {value} hours");
					}
					else
					{
						if (args.Parsers[1].IsMissing)
						{
							return TextCommandResult.Success($@"Global dungeon reset delay is {Config.GlobalDelay} hours");
						}

						var value = (double)args[1];
						Config.GlobalDelay = value;
						Config.Save();

						return TextCommandResult.Success($@"Changed global dungeon reset delay to {value} hours");
					}
				}
			);

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