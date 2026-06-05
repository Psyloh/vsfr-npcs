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
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace VSFRNPCS.Server
{
	public class DungeonResetModSystem : ModSystem
	{
		readonly ConcurrentDictionary<string, DateTime> _pastResets = [];
		readonly ConcurrentDictionary<string, ResetCountdown?> _pendingResets = [];

		int[]? _secondThresholds;
		public int[] SecondThresholds => _secondThresholds ?? throw new Exception();

		readonly ConcurrentDictionary<string, (string, DateTime, bool)> _suspiciousDisconnections = [];

		Config? _config;
		public Config Config => _config ?? throw new Exception();

		public bool IsPending(string dungeonName) =>
			_pendingResets.ContainsKey(dungeonName);

		public DateTime? GetLastReset(string dungeonName)
		{
			if (_pastResets.TryGetValue(dungeonName, out var time))
			{
				return time;
			}
			return null;
		}

		public override bool ShouldLoad(EnumAppSide side)
			=> side == EnumAppSide.Server;

		public void SetReset(string dungeonName)
		{
			_pastResets[dungeonName] = DateTime.Now;
			_pendingResets.Remove(dungeonName);
		}

		public void RegisterReset(string dungeonName)
		{
			var timer = new ResetCountdown(dungeonName, SecondThresholds);
			_pendingResets[dungeonName] = timer;
			timer.Elapsed += TimerElapsed;
			timer.Disposed += TimerDisposed;
			if (timer.Check())
			{
				Announce(timer);
			}
			timer.Start();
		}

		public override void StartServerSide(ICoreServerAPI api)
		{
			_config = Config.Get();
			_secondThresholds = new int[Config.AnnounceThresholds.Length];
			for (var i = 0; i < SecondThresholds.Length; i++)
			{
				SecondThresholds[i] = (int)(Config.AnnounceThresholds[i] * 60);
			}

			api.Event.SaveGameLoaded += SaveGameLoaded;
			api.Event.GameWorldSave += GameWorldSave;
			api.Event.PlayerNowPlaying += PlayerNowPlaying;
			api.Event.PlayerLeave += PlayerLeave;



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
						foreach (var (dungeon, time) in _pastResets)
						{
							player.SendMessage(GlobalConstants.GeneralChatGroup, $"{dungeon} {time:F}", EnumChatType.Notification);
						}
					}
					return TextCommandResult.Success();
				})
				.BeginSub("clear")
				.HandleWith(args =>
				{
					_pastResets.Clear();

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
							player.SendMessage(GlobalConstants.GeneralChatGroup, $"{dungeon} {remaining!.Remaining}", EnumChatType.Notification);
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
		}

		void PlayerNowPlaying(IServerPlayer player)
		{
			foreach (var (_, timer) in _pendingResets)
			{
				ApiModHelper.ServerNotification(player, GetAnnounce(timer!));
			}

			if (_suspiciousDisconnections.Remove(player.PlayerUID, out var disconnection))
			{
				ApiModHelper.Error($"{disconnection.Item1} {disconnection.Item2:F} {disconnection.Item3}");

				var modSys = ApiModHelper.GetSystem<GenStoryStructures>();
				var structure = modSys.Structures.Get(disconnection.Item1);

				if (_pastResets.TryGetValue(structure.Code, out var reset))
				{
					ApiModHelper.Error($"{reset:F}");

					if (reset > disconnection.Item2)
					{
						CmdHelpers.Expel(player.Entity, structure, disconnection.Item3);
					}
				}
			}
		}

		void SaveGameLoaded()
		{
			var dungeonResets = ApiModHelper.GetSaveGameData<Dictionary<string, long>>("pastResets") ?? [];
			foreach (var (dungeon, time) in dungeonResets)
			{
				_pastResets[dungeon] = DateTime.FromBinary(time);
			}

			var pendingResets = ApiModHelper.GetSaveGameData<Dictionary<string, int>>("pendingResets") ?? [];
			foreach (var (dungeon, remaining) in pendingResets)
			{
				_pendingResets[dungeon] = new(dungeon, SecondThresholds, remaining);
			}
		}

		void GameWorldSave()
		{
			ApiModHelper.SaveData("dungeonResets", _pastResets.Select(entry => (entry.Key, entry.Value.ToBinary())));
			ApiModHelper.SaveData("pendingResets", _pendingResets.Select(entry => (entry.Key, entry.Value?.Remaining)));
		}

		void PlayerLeave(IServerPlayer player)
		{
			ApiModHelper.Error("leaving");

			var modSys = ApiModHelper.GetSystem<GenStoryStructures>();
			foreach (var structure in modSys.Structures.values.Values)
			{
				if (CmdHelpers.IsPlayerInDungeonArea(player.Entity, structure, out var inside))
				{
					var playerUID = player.PlayerUID;
					var date = DateTime.Now;
					var cheating = _pendingResets.ContainsKey(structure.Code) && inside;

					ApiModHelper.Error($"{playerUID} {date:D} {cheating}");

					_suspiciousDisconnections.TryAdd(player.PlayerUID, (structure.Code, DateTime.Now, cheating));
					break;
				}
			}
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

				ApiModHelper.EnqueueToMainThread(() =>
				{
					var message = Lang.GetUnformatted("game:reset-dungeon-message").Replace("{dungeon}", dungeon);
					ApiModHelper.ServerNotification(GetMessage(message));
					CmdHelpers.ResetDungeon(dungeon);
				}, "resetDungeon");
			}
		}

		void Announce(ResetCountdown timer)
		{
			ApiModHelper.EnqueueToMainThread(() =>
				ApiModHelper.ServerNotification(GetAnnounce(timer)), "announce");
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

		public override void Dispose()
		{
			foreach (var (_, timer) in _pendingResets)
			{
				timer!.Elapsed -= TimerElapsed;
				timer!.Disposed -= TimerDisposed;
				timer!.Dispose();
			}

			ApiModHelper.ServerEvents.SaveGameLoaded -= SaveGameLoaded;
			ApiModHelper.ServerEvents.GameWorldSave -= GameWorldSave;
			ApiModHelper.ServerEvents.PlayerNowPlaying -= PlayerNowPlaying;
			ApiModHelper.ServerEvents.PlayerLeave -= PlayerLeave;
		}
	}
}