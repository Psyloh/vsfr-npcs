using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Timers;
using Vintagestory.API.Common;
using Vintagestory.API.Common.CommandAbbr;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Client.NoObf;
using Vintagestory.GameContent;

namespace VSFRNPCS.Server
{
	public class ResetCountdown : Timer
	{
		readonly List<int> _thresholds = [];
		int _nextThreshold;

		readonly string _dungeonName;
		public string DungeonName => _dungeonName;

		int _remaining;
		public int Remaining => _remaining;

		public ResetCountdown(string dungeonName, double[] thresholds, int? remaining = null) : base(1000)
		{
			_dungeonName = dungeonName;

			foreach (var threshold in thresholds)
			{
				_thresholds.Add((int)(threshold * 60));
			}
			_remaining = remaining ?? _thresholds[0];

			SetNextThreshold();
		}

		public bool Check()
		{
			return _remaining == _nextThreshold;
		}

		public void SetNextThreshold()
		{
			_nextThreshold = _thresholds.FirstOrDefault(t => _remaining >= t);
		}

		public int Tick()
		{
			return _remaining--;
		}
	}

	public class ServerSide : IDisposable
	{
		readonly ConcurrentDictionary<string, DateTime> _dungeonResets = [];
		readonly ConcurrentDictionary<string, ResetCountdown> _pendingResets = [];

		public Config Config { get; init; }

		public ServerSide(ICoreServerAPI api, Mod mod)
		{
			ApiModHelper.Api = api;
			ApiModHelper.Mod = mod;

			Config = Config.Get();

			var dungeonResets = api.WorldManager.SaveGame.GetData<Dictionary<string, long>>("dungeonResets") ?? [];
			foreach (var (dungeon, time) in dungeonResets)
			{
				_dungeonResets[dungeon] = DateTime.FromBinary(time);
			}

			var pendingResets = api.WorldManager.SaveGame.GetData<Dictionary<string, int>>("pendingResets") ?? [];
			foreach (var (dungeon, remaining) in pendingResets)
			{
				_pendingResets[dungeon] = new(dungeon, Config.AnnounceThresholds, remaining);
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

			api.Event.PlayerReady += PlayerReady;
			api.Event.GameWorldSave += Save;
		}

		void PlayerReady(IServerPlayer player)
		{
			foreach (var (_, timer) in _pendingResets)
			{
				player.SendMessage(GlobalConstants.GeneralChatGroup, Announce(timer), EnumChatType.Notification);
			}
		}

		string Announce(ResetCountdown timer)
		{
			var message = $@"<font color=""{Config.TextColor}"">{timer.Remaining} seconds until {timer.DungeonName} dungeon gets reset! Leave the area or suffer consequences...</font>";
			if (Config.BoldText)
			{
				message = $"<strong>{message}</strong>";
			}
			return message;
		}

		void Save()
		{
			ApiModHelper.SaveData("dungeonResets", _dungeonResets.Select(entry => (entry.Key, entry.Value.ToBinary())));
			ApiModHelper.SaveData("pendingResets", _pendingResets.Select(entry => (entry.Key, entry.Value.Remaining)));
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
			var timer = new ResetCountdown(dungeonName, Config.AnnounceThresholds);
			_pendingResets[dungeonName] = timer;
			timer.Elapsed += TimerElapsed;
			timer.Disposed += TimerDisposed;
			Check(timer);
			timer.Start();
		}

		public bool Check(ResetCountdown timer)
		{
			var check = timer.Check();
			if (check)
			{
				ApiModHelper.Api.Event.EnqueueMainThreadTask(() =>
				{
					ApiModHelper.Api.SendMessageToGroup(GlobalConstants.GeneralChatGroup, Announce(timer), EnumChatType.Notification);
				}, "announce");
			}
			return check;
		}

		void TimerElapsed(object? sender, ElapsedEventArgs e)
		{
			if (sender is ResetCountdown timer)
			{
				var remaining = timer.Tick();

				if (Check(timer))
				{
					timer.SetNextThreshold();
				}

				if (remaining <= 0)
				{
					timer.Stop();
					timer.Dispose();
				}
			}
		}

		void TimerDisposed(object? sender, EventArgs e)
		{
			ApiModHelper.Error("disposed");
			if (sender is ResetCountdown timer)
			{
				timer.Elapsed -= TimerElapsed;
				timer.Disposed -= TimerDisposed;

				var dungeon = timer.DungeonName;
				_pendingResets.Remove(dungeon, out var _);

				ApiModHelper.Api.Event.EnqueueMainThreadTask(() =>
					CmdHelpers.ResetDungeon(dungeon), "resetDungeon");
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
			ApiModHelper.Api.Event.GameWorldSave -= Save;
			GC.SuppressFinalize(this);
		}
	}
}