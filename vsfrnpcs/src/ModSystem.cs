using HarmonyLib;
using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using VSFRNPCS.Server;

namespace VSFRNPCS
{
	public class MainModSystem : ModSystem
	{
		ServerSide? _server;
		public ServerSide Server => _server ?? throw new Exception("Server is null");

		public override void Start(ICoreAPI api)
		{
			DialogueControllerContinueExecutePatch.Api = api;

			Harmony harmony = new(Mod.Info.ModID);
			harmony.UnpatchAll();
		}

		public override void StartClientSide(ICoreClientAPI api)
		{
		}

		public override void StartServerSide(ICoreServerAPI api)
		{
			_server = new(api, Mod);
		}

		public override void Dispose()
		{
			Harmony harmony = new(Mod.Info.ModID);
			harmony.UnpatchAll();

			_server?.Dispose();
		}
	}
}