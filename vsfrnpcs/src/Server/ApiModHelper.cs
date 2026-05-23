using System;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace VSFRNPCS.Server
{
	public static class ApiModHelper
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
		public static void SaveData<T>(string key, T data) => Api.WorldManager.SaveGame.StoreData(key, data);
	}
}