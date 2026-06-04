using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
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

		static Mod? _mod;
		public static Mod Mod
		{
			get => _mod ?? throw new Exception("Mod is null");
			set => _mod = value;
		}

		public static void Warning(string message) => Mod.Logger.Warning(message);
		public static void Error(string message) => Mod.Logger.Error(message);
		public static void Error(Exception ex) => Mod.Logger.Error(ex);

		public static T GetServerSystem<T>() where T : ModSystem => SApi.ModLoader.GetModSystem<T>();

		public static Config LoadServerConfig(string filename) => SApi.LoadModConfig<Config>(filename);
		public static void SaveData<T>(string key, T data) => SApi.WorldManager.SaveGame.StoreData(key, data);
	}
}