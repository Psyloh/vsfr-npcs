using System.Timers;

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
}