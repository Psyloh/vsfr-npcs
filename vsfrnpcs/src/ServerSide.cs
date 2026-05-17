using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.CommandAbbr;
using Vintagestory.API.Server;

namespace VSFRNPCS.Server
{
	public class ServerSide : IDisposable
	{
		readonly ICoreServerAPI _api;

		Dictionary<string, double> _dungeonReset;

		public ServerSide(ICoreServerAPI api)
		{
			_api = api;
			_dungeonReset = api.WorldManager.SaveGame.GetData<Dictionary<string, double>>("dungeonReset") ?? [];

			api.ChatCommands.Create("regenArch").RequiresPrivilege(Privilege.chat).WithDesc("Testing").HandleWith((TextCommandCallingArgs args) => {

				Helpers.ResetDungeon(api, "resonancearchive");

				return TextCommandResult.Success("Disappeared!");
			});

			api.Event.GameWorldSave += Save;
		}

		void Save()
		{
			if (_dungeonReset.Count > 0)
			{
				_api.WorldManager.SaveGame.StoreData("dungeonReset", _dungeonReset);
			}
		}

		public double GetLastReset(string dungeonName)
		{
			if (_dungeonReset.TryGetValue(dungeonName, out var time))
			{
				return time;
			}
			return -1;
		}

		public void SetLastReset(string dungeonName, double time)
		{
			_dungeonReset[dungeonName] = time;
		}

		public void Dispose()
		{

		}
	}
}