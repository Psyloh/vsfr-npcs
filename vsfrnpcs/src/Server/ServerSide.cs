using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Timers;
using Vintagestory.API.Common;
using Vintagestory.API.Common.CommandAbbr;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace VSFRNPCS.Server
{
	public class ResetCountdown : Timer
	{
		readonly int[] _thresholds;
		int _nextThresholdIndex;

		readonly string _dungeonName;
		public string DungeonName => _dungeonName;

		int _remaining;
		public int Remaining => _remaining;

		public ResetCountdown(string dungeonName, int[] thresholds, int? remaining = null) : base(1000)
		{
			_dungeonName = dungeonName;
			_thresholds = thresholds;
			_remaining = remaining ?? _thresholds[0];

			while (_nextThresholdIndex < _thresholds.Length && _remaining < _thresholds[_nextThresholdIndex])
			{
				_nextThresholdIndex++;
			}
		}

		public bool Check()
		{
			if (_nextThresholdIndex == _thresholds.Length)
			{
				return false;
			}

			var check = _remaining == _thresholds[_nextThresholdIndex];
			if (check)
			{
				_nextThresholdIndex++;
			}
			return check;
		}

		public bool Tick()
		{
			_remaining--;

			return Check();
		}
	}

	public class ServerSide : IDisposable
	{
		readonly ConcurrentDictionary<string, DateTime> _dungeonResets = [];
		readonly ConcurrentDictionary<string, ResetCountdown> _pendingResets = [];
		readonly int[] _secondThresholds;

		readonly ConcurrentDictionary<string, (string, DateTime, bool)> _suspiciousDisconnections = [];

		public Config Config { get; init; }

		public ServerSide(ICoreServerAPI api, Mod mod)
		{
			ApiModHelper.Api = api;
			ApiModHelper.Mod = mod;

			Config = Config.Get();

			_secondThresholds = new int[Config.AnnounceThresholds.Length];
			for (var i = 0; i < _secondThresholds.Length; i++)
			{
				_secondThresholds[i] = (int)(Config.AnnounceThresholds[i] * 60);
			}

			var dungeonResets = api.WorldManager.SaveGame.GetData<Dictionary<string, long>>("dungeonResets") ?? [];
			foreach (var (dungeon, time) in dungeonResets)
			{
				_dungeonResets[dungeon] = DateTime.FromBinary(time);
			}

			var pendingResets = api.WorldManager.SaveGame.GetData<Dictionary<string, int>>("pendingResets") ?? [];
			foreach (var (dungeon, remaining) in pendingResets)
			{
				_pendingResets[dungeon] = new(dungeon, _secondThresholds, remaining);
			}

			api.ChatCommands.Create("resetDungeonDelay")
				.WithAlias("rdd")
				.BeginSub("dungeon")
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
			)
				.EndSub();

			api.ChatCommands.GetOrCreate("rdd")
				.BeginSub("reset")
				.RequiresPrivilege(Privilege.controlserver)
				.WithArgs(api.ChatCommands.Parsers.Word("name"))
				.HandleWith(args =>
				{
					var name = (string)args[0];
					CmdHelpers.ResetDungeon(name);

					return TextCommandResult.Success();
				})
				.EndSub();

			api.ChatCommands.GetOrCreate("rdd")
				.RequiresPrivilege(Privilege.controlserver)
				.BeginSub("dungeons")
				.RequiresPlayer()
				.HandleWith(args =>
				{
					if (args.Caller.Player is IServerPlayer player)
					{
						foreach (var (dungeon, time) in _dungeonResets)
						{
							player.SendMessage(GlobalConstants.GeneralChatGroup, $"{dungeon} {time:F}", EnumChatType.Notification);
						}
					}
					return TextCommandResult.Success();
				})
				.BeginSub("clear")
				.HandleWith(args =>
				{
					_dungeonResets.Clear();

					return TextCommandResult.Success("Cleared!");
				})
				.EndSub()
				.EndSub()
				.BeginSub("pending")
				.RequiresPlayer()
				.HandleWith(args =>
				{
					if (args.Caller.Player is IServerPlayer player)
					{
						foreach (var (dungeon, remaining) in _pendingResets)
						{
							player.SendMessage(GlobalConstants.GeneralChatGroup, $"{dungeon} {remaining.Remaining}", EnumChatType.Notification);
						}
					}
					return TextCommandResult.Success();
				})
				.BeginSub("clear")
				.HandleWith(args =>
				{
					_pendingResets.Clear();

					return TextCommandResult.Success("Cleared!");
				})
				.EndSub()
				.EndSub();

			api.ChatCommands.GetOrCreate("debug")
				.RequiresPlayer()
				.RequiresPrivilege(Privilege.controlserver)
				.BeginSub("fuckingClearVariables")
				.WithAlias("fcv")
				.HandleWith(args =>
				{
					if (args.Caller.Player is IServerPlayer player)
					{
						player.Entity.WatchedAttributes["variables"] = new TreeAttribute();
					}

					var entities = ApiModHelper.Api.World.LoadedEntities.Values.Where(e => e.Code.Domain == "vsfrnpcs"); 
					foreach (var entity in entities)
					{
						entity.WatchedAttributes["variables"] = new TreeAttribute();
					}

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

					var modSys = ApiModHelper.Api.ModLoader.GetModSystem<GenStoryStructures>();
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

			api.Event.PlayerReady += PlayerReady;
			api.Event.PlayerLeave += PlayerLeave;
			api.Event.GameWorldSave += Save;
		}

		void PlayerLeave(IServerPlayer player)
		{
			var modSys = ApiModHelper.Api.ModLoader.GetModSystem<GenStoryStructures>();
			foreach (var structure in modSys.Structures.values.Values)
			{
				if (CmdHelpers.IsPlayerInDungeonArea(player.Entity, structure, out var inside))
				{
					_suspiciousDisconnections.TryAdd(player.PlayerUID, (structure.Code, DateTime.Now, _pendingResets.ContainsKey(structure.Code) && inside));
					break;
				}
			}
		}

		void Save()
		{
			ApiModHelper.Error("Saving");
			ApiModHelper.SaveData("dungeonResets", _dungeonResets.Select(entry => (entry.Key, entry.Value.ToBinary())));
			ApiModHelper.SaveData("pendingResets", _pendingResets.Select(entry => (entry.Key, entry.Value.Remaining)));
			ApiModHelper.Error("Saved");
		}

		void PlayerReady(IServerPlayer player)
		{
			foreach (var (_, timer) in _pendingResets)
			{
				player.SendMessage(GlobalConstants.GeneralChatGroup, GetAnnounce(timer), EnumChatType.Notification);
			}

			if (_suspiciousDisconnections.Remove(player.PlayerUID, out var disconnection))
			{
				var modSys = ApiModHelper.Api.ModLoader.GetModSystem<GenStoryStructures>();
				var structure = modSys.Structures.Get(disconnection.Item1);

				if (_dungeonResets.TryGetValue(structure.Code, out var reset))
				{
					if (reset > disconnection.Item2)
					{
						CmdHelpers.Expel(player.Entity, structure, disconnection.Item3);
					}
				}
			}
		}

		public bool IsPending(string dungeonName) =>
			_pendingResets.ContainsKey(dungeonName);

		public DateTime? GetLastReset(string dungeonName)
		{
			if (_dungeonResets.TryGetValue(dungeonName, out var time))
			{
				return time;
			}
			return null;
		}

		public void SetReset(string dungeon)
		{
			_dungeonResets[dungeon] = DateTime.Now;
		}

		public void RegisterReset(string dungeonName)
		{
			var timer = new ResetCountdown(dungeonName, _secondThresholds);
			_pendingResets[dungeonName] = timer;
			timer.Elapsed += TimerElapsed;
			timer.Disposed += TimerDisposed;
			if (timer.Check())
			{
				Announce(timer);
			}
			timer.Start();
		}

		void Announce(ResetCountdown timer)
		{
			ApiModHelper.Api.Event.EnqueueMainThreadTask(() =>
				ApiModHelper.Api.SendMessageToGroup(GlobalConstants.GeneralChatGroup, GetAnnounce(timer), EnumChatType.Notification),
				"announce");
		}

		string GetAnnounce(ResetCountdown timer)
		{
			var message = Lang.GetUnformatted("vsfrnpcs:dungeon-reset-announce");
			var dungeon = Lang.GetIfExists($"vsfrnpcs:{timer.DungeonName}") ?? timer.DungeonName;

			message = message.Replace("{seconds}", timer.Remaining.ToString()).Replace("{dungeon}", dungeon);
			return GetMessage(message);
		}

		string GetMessage(string message)
		{
			message = $@"<font color=""{Config.TextColor}"">{message}</font>";
			if (Config.BoldText)
			{
				message = $"<strong>{message}</strong>";
			}
			return message;
		}

		void TimerElapsed(object? sender, ElapsedEventArgs e)
		{
			if (sender is ResetCountdown timer)
			{
				if (timer.Tick())
				{
					Announce(timer);
				}

				if (timer.Remaining <= 0)
				{
					timer.Stop();
					timer.Dispose();
				}
			}
		}

		void TimerDisposed(object? sender, EventArgs e)
		{
			if (sender is ResetCountdown timer)
			{
				timer.Elapsed -= TimerElapsed;
				timer.Disposed -= TimerDisposed;

				var dungeon = timer.DungeonName;
				_pendingResets.Remove(dungeon, out var _);

				ApiModHelper.Api.Event.EnqueueMainThreadTask(() => {
					var message = Lang.GetUnformatted("game:reset-dungeon-message").Replace("{dungeon}", dungeon);
					ApiModHelper.Api.SendMessageToGroup(GlobalConstants.GeneralChatGroup, GetMessage(message), EnumChatType.Notification);
					CmdHelpers.ResetDungeon(dungeon);
				}, "resetDungeon");
			}
		}

		public void Dispose()
		{
			foreach (var (_, timer) in _pendingResets)
			{
				timer.Elapsed -= TimerElapsed;
				timer.Disposed -= TimerDisposed;
				timer.Dispose();
			}
			ApiModHelper.Error("Disposed");
			ApiModHelper.Api.Event.GameWorldSave -= Save;
			GC.SuppressFinalize(this);
		}
	}
}