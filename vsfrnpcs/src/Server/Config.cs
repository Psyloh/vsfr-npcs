using System;
using System.Collections.Generic;

namespace VSFRNPCS.Server
{
	public class Config
	{
		const string FILENAME = "dungeonReset.json";

		public double GlobalDelay { get; set; } = 24;
		public Dictionary<string, double> Delays { get; set; } = [];
		public double[] AnnounceThresholds { get; set; } = [10, 5, 3, 1, 0.5];

		public bool BoldText { get; set; } = true;
		public string TextColor { get; set; } = "red";

		public static Config Get()
		{
			Config config;
			try
			{
				config = ApiModHelper.LoadConfig(FILENAME);
				config ??= new();
			}
			catch (Exception e)
			{
				ApiModHelper.Warning("Configuration file corrupted - loading default settings! Please fix or delete the file...");
				ApiModHelper.Error(e);

				return new();
			}
			config.AnnounceThresholds.Sort(Comparer<double>.Create((d1, d2) => -d1.CompareTo(d2)));
			config.Save();

			return config;
		}

		public void Save()
		{
			ApiModHelper.StoreConfig(this, FILENAME);
		}
	}
}