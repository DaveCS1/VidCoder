﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using Microsoft.AnyContainer;
using Newtonsoft.Json;
using VidCoder.Resources;
using VidCoder.Services;
using VidCoder.Services.Windows;
using VidCoder.ViewModel;
using VidCoderCommon;
using VidCoderCommon.Model;

namespace VidCoder.Model
{
	public static class Database
	{
		private const string BackupFolderName = "Backups";
		private const string ConfigDatabaseFileWithoutExtension = "VidCoder";
		private const string ConfigDatabaseFileExtension = ".sqlite";
		private const string ConfigDatabaseFile = ConfigDatabaseFileWithoutExtension + ConfigDatabaseFileExtension;

		private static SQLiteConnection connection;

		private static ThreadLocal<SQLiteConnection> threadLocalConnection = new ThreadLocal<SQLiteConnection>();

		private static long mainThreadId;

		private static Lazy<string> lazyDatabaseFile = new Lazy<string>(GetDatabaseFilePath); 

		public static void Initialize()
		{
			mainThreadId = Thread.CurrentThread.ManagedThreadId;

			int databaseVersion = DatabaseConfig.Version;
			if (databaseVersion > Utilities.CurrentDatabaseVersion)
			{
				databaseVersion = HandleTooHighDatabaseVersion(databaseVersion);
			}

			if (databaseVersion == Utilities.CurrentDatabaseVersion)
			{
				return;
			}

			BackupDatabaseFile(databaseVersion);

			using (SQLiteTransaction transaction = Connection.BeginTransaction())
			{
				// Update DB schema
				if (databaseVersion < 35)
				{
					string message = string.Format(CultureInfo.CurrentCulture, MainRes.DataTooOldRunVidCoderVersion, "3.15");
					StaticResolver.Resolve<IMessageBoxService>().Show(message);
					StaticResolver.Resolve<IMessageBoxService>().Show(message);
					throw new InvalidOperationException("Database too old");
				}

				if (databaseVersion < 36)
				{
					UpgradeDatabaseTo36();
				}

				// Update encoding profiles if we need to. Everything is at least 28 now from the JSON upgrade.
				int oldDatabaseVersion = Math.Max(databaseVersion, 28);
				if (oldDatabaseVersion < Utilities.LastUpdatedEncodingProfileDatabaseVersion)
				{
					UpgradeEncodingProfiles(oldDatabaseVersion);
				}

				if (oldDatabaseVersion < Utilities.LastUpdatedPickerDatabaseVersion)
				{
					UpgradePickers(oldDatabaseVersion);
				}

				SetDatabaseVersionToLatest();
				transaction.Commit();
			}
		}

		private static int HandleTooHighDatabaseVersion(int databaseVersion)
		{
			string messageLine1 = string.Format(
				CultureInfo.CurrentCulture,
				MainRes.RenameDatabaseFileLine1,
				databaseVersion,
				Utilities.VersionString,
				Utilities.CurrentDatabaseVersion);

			// See if we have a backup with the correct version
			int backupVersion = FindBackupDatabaseFile();
			if (backupVersion >= 0)
			{
				string messageLine2 = string.Format(
					CultureInfo.CurrentCulture,
					MainRes.UseBackupDatabaseLine,
					backupVersion);

				string message = string.Format(
					CultureInfo.CurrentCulture,
					"{0}{1}{1}{2}",
					messageLine1,
					Environment.NewLine,
					messageLine2);

				var messageService = StaticResolver.Resolve<IMessageBoxService>();
				messageService.Show(message, MainRes.IncompatibleDatabaseFileTitle, MessageBoxButton.YesNo);

				if (messageService.Show(
					message,
					MainRes.IncompatibleDatabaseFileTitle,
					MessageBoxButton.YesNo) == MessageBoxResult.Yes)
				{
					try
					{
						Connection.Close();
						connection = null;

						MoveCurrentDatabaseFile(databaseVersion);
						string backupFilePath = GetBackupDatabaseFilePath(backupVersion);
						File.Copy(backupFilePath, DatabaseFile);

						databaseVersion = backupVersion;
					}
					catch (IOException)
					{
						HandleCriticalFileError();
					}
					catch (UnauthorizedAccessException)
					{
						HandleCriticalFileError();
					}
				}
				else
				{
					Environment.Exit(0);
				}
			}
			else
			{
				string messageLine2 = MainRes.RenameDatabaseFileLine2;

				string message = string.Format(
					CultureInfo.CurrentCulture,
					"{0}{1}{1}{2}",
					messageLine1,
					Environment.NewLine,
					messageLine2);

				var messageService = StaticResolver.Resolve<IMessageBoxService>();
				messageService.Show(message, MainRes.IncompatibleDatabaseFileTitle, MessageBoxButton.YesNo);
				if (messageService.Show(
					message,
					MainRes.IncompatibleDatabaseFileTitle,
					MessageBoxButton.YesNo) == MessageBoxResult.Yes)
				{
					Connection.Close();

					try
					{
						MoveCurrentDatabaseFile(databaseVersion);
						connection = null;
						databaseVersion = DatabaseConfig.Version;
					}
					catch (IOException)
					{
						HandleCriticalFileError();
					}
					catch (UnauthorizedAccessException)
					{
						HandleCriticalFileError();
					}
				}
				else
				{
					Environment.Exit(0);
				}
			}

			return databaseVersion;
		}

		private static void MoveCurrentDatabaseFile(int databaseVersion)
		{
			string newFileName = GetBackupDatabaseFileName(databaseVersion);
			string newFilePath = FileUtilities.CreateUniqueFileName(newFileName, BackupDatabaseFolder, new HashSet<string>());

			File.Move(DatabaseFile, newFilePath);
		}

		private static void BackupDatabaseFile(int databaseVersion)
		{
			Connection.Close();
			connection = null;

			try
			{
				string backupFilePath = GetBackupDatabaseFilePath(databaseVersion);
				Directory.CreateDirectory(BackupDatabaseFolder);

				File.Copy(DatabaseFile, backupFilePath, overwrite: true);
			}
			catch (Exception exception)
			{
				StaticResolver.Resolve<IAppLogger>().Log("Could not backup database file:" + Environment.NewLine + exception);
			}
		}

		private static string BackupDatabaseFolder => Path.Combine(Utilities.AppFolder, BackupFolderName);

		/// <summary>
		/// Returns the version number of highest version database file that is still compatible with this build of VidCoder.
		/// </summary>
		/// <returns>The best version match.</returns>
		private static int FindBackupDatabaseFile()
		{
			DirectoryInfo backupDirectoryInfo = new DirectoryInfo(BackupDatabaseFolder);
			if (!backupDirectoryInfo.Exists)
			{
				return -1;
			}

			FileInfo[] backupFiles = backupDirectoryInfo.GetFiles();
			Regex regex = new Regex(@"^VidCoder-v(?<version>\d+)\.sqlite$");

			int bestCandidate = -1;
			foreach (FileInfo backupFile in backupFiles)
			{
				Match match = regex.Match(backupFile.Name);
				if (match.Success)
				{
					string versionString = match.Groups["version"].Captures[0].Value;
					int version;
					if (int.TryParse(versionString, out version))
					{
						if (version <= Utilities.CurrentDatabaseVersion && version > bestCandidate)
						{
							bestCandidate = version;
						}
					}
				}
			}

			return bestCandidate;
		}

		private static string GetBackupDatabaseFilePath(int databaseVersion)
		{
			string backupFileName = GetBackupDatabaseFileName(databaseVersion);
			string backupFileFolder = BackupDatabaseFolder;
			return Path.Combine(backupFileFolder, backupFileName);
		}

		private static string GetBackupDatabaseFileName(int databaseVersion)
		{
			return ConfigDatabaseFileWithoutExtension + "-v" + databaseVersion + ConfigDatabaseFileExtension;
		}

		private static void UpgradeDatabaseTo36()
		{
			ExecuteNonQuery(
				"CREATE TABLE presetFolders (" +
				"id INTEGER PRIMARY KEY AUTOINCREMENT," +
				"name TEXT, " +
				"parentId INTEGER, " +
				"isExpanded INTEGER)", connection);
		}

		private static void UpgradeEncodingProfiles(int databaseVersion)
		{
			// Upgrade encoding profiles on presets (encoder/mixdown changes)
			var presets = PresetStorage.GetJsonPresetListFromDb();

			foreach (Preset preset in presets)
			{
				PresetStorage.UpgradeEncodingProfile(preset.EncodingProfile, databaseVersion);
			}

			var presetJsonList = presets.Select(PresetStorage.SerializePreset).ToList();

			PresetStorage.SavePresets(presetJsonList, Connection);

			// Upgrade encoding profiles on old queue items.
			Config.EnsureInitialized(Connection);
			string jobsJson = Config.EncodeJobs2;
			if (!string.IsNullOrEmpty(jobsJson))
			{
				IList<EncodeJobWithMetadata> jobs = EncodeJobStorage.ParseAndErrorCheckJobsJson(jobsJson);
				foreach (EncodeJobWithMetadata job in jobs)
				{
					PresetStorage.UpgradeEncodingProfile(job.Job.EncodingProfile, databaseVersion);
				}

				Config.EncodeJobs2 = EncodeJobStorage.SerializeJobs(jobs);
			}
		}

		private static void UpgradePickers(int databaseVersion)
		{
			var pickers = PickerStorage.GetPickerListFromDb();

			foreach (Picker picker in pickers)
			{
				PickerStorage.UpgradePicker(picker, databaseVersion);
			}

			var pickerJsonList = pickers.Select(PickerStorage.SerializePicker).ToList();

			PickerStorage.SavePickers(pickerJsonList, Connection);
		}

		private static void HandleCriticalFileError()
		{
			var messageService = StaticResolver.Resolve<IMessageBoxService>();

			messageService.Show(CommonRes.FileFailureErrorMessage, CommonRes.FileFailureErrorTitle, MessageBoxButton.OK);
			Environment.Exit(1);
		}

		private static void SetDatabaseVersionToLatest()
		{
			DatabaseConfig.Set("Version", Utilities.CurrentDatabaseVersion, Database.Connection);
		}

		/// <summary>
		/// Gets the database file path.
		/// </summary>
		public static string DatabaseFile
		{
			get { return lazyDatabaseFile.Value; }
		}

		private static string GetDatabaseFilePath()
		{
			if (Utilities.IsPortable)
			{
				string portableExeFolder = GetPortableExeFolder();
				if (FileUtilities.HasWriteAccessOnFolder(portableExeFolder))
				{
					// Portable location for database is beside the portable exe.
					string portableDatabasePath = Path.Combine(portableExeFolder, ConfigDatabaseFile);
					if (File.Exists(portableDatabasePath))
					{
						// Use portable location for database file if it exists
						return portableDatabasePath;
					}

					string appDataDatabaseFile = NonPortableDatabaseFile;
					if (File.Exists(appDataDatabaseFile))
					{
						// If database file does exist in appdata, use it.
						return appDataDatabaseFile;
					}
					else
					{
						// If no file could be found, use the portable location.
						return portableDatabasePath;
					}
				}
				else
				{
					// Fall back to non-portable path if we don't have write access to the folder.
					return NonPortableDatabaseFile;
				}
			}
			else
			{
				return NonPortableDatabaseFile;
			}
		}

		/// <summary>
		/// Gets the folder that contains the portable executable that launched us.
		/// </summary>
		/// <returns></returns>
		private static string GetPortableExeFolder()
		{
			if (!Utilities.IsPortable)
			{
				throw new InvalidOperationException("Called GetPortableExeFolder on a non-portable install.");
			}

			Process parentProcess = ParentProcessUtilities.GetParentProcess();
			return Path.GetDirectoryName(parentProcess.MainModule.FileName);
		}

		/// <summary>
		/// Gets the database file path for a non-portable version. This may be in
		/// the standard %appdata% folder or it may be in a user-specified directory.
		/// </summary>
		public static string NonPortableDatabaseFile
		{
			get
			{
				string appDataFolder = Utilities.AppFolder;

				if (!Directory.Exists(appDataFolder))
				{
					Directory.CreateDirectory(appDataFolder);
				}

				return Path.Combine(appDataFolder, ConfigDatabaseFile);
			}
		}

		public static string ConnectionString
		{
			get
			{
				return "Data Source=" + DatabaseFile;
			}
		}

		public static SQLiteConnection Connection
		{
			get
			{
				return connection ?? (connection = CreateConnection());
			}
		}

		public static SQLiteConnection ThreadLocalConnection
		{
			get
			{
				if (IsMainThread)
				{
					return Connection;
				}

				if (!threadLocalConnection.IsValueCreated)
				{
					threadLocalConnection.Value = Database.CreateConnection();
				}

				return threadLocalConnection.Value;
			}
		}

		public static bool IsMainThread
		{
			get
			{
				return Thread.CurrentThread.ManagedThreadId == mainThreadId;
			}
		}

		public static SQLiteConnection CreateConnection()
		{
			if (!Directory.Exists(Utilities.AppFolder))
			{
				if (CommonUtilities.Beta && Directory.Exists(Utilities.GetAppFolder(beta: false)))
				{
					// In beta mode if we don't have the appdata folder copy the stable appdata folder
					try
					{
						FileUtilities.CopyDirectory(
							Utilities.GetAppFolder(beta: false),
							Utilities.GetAppFolder(beta: true));
					}
					catch (Exception)
					{
					}
				}
				else
				{
					Directory.CreateDirectory(Utilities.AppFolder);
				}
			}

			bool newDataFile = !File.Exists(DatabaseFile);

			var newConnection = new SQLiteConnection(ConnectionString);
			newConnection.Open();

			if (newDataFile)
			{
				CreateTables(newConnection);

				var settingsList = new Dictionary<string, string> {{"Version", Utilities.CurrentDatabaseVersion.ToString(CultureInfo.InvariantCulture)}};
				AddSettingsList(newConnection, settingsList);
			}

			return newConnection;
		}

		public static void CreateTables(SQLiteConnection connection)
		{
			ExecuteNonQuery(
				"CREATE TABLE presetsJson (" +
				"json TEXT)", connection);

			ExecuteNonQuery(
				"CREATE TABLE presetFolders (" +
				"id INTEGER PRIMARY KEY AUTOINCREMENT," +
				"name TEXT, " +
				"parentId INTEGER, " +
				"isExpanded INTEGER)", connection);

			ExecuteNonQuery(
				"CREATE TABLE pickersJson (" +
				"json TEXT)", connection);

			ExecuteNonQuery(
				"CREATE TABLE settings (" +
				"name TEXT PRIMARY KEY, " +
				"value TEXT)", connection);

			ExecuteNonQuery(
				"CREATE TABLE workerLogs (" +
				"workerGuid TEXT, " +
				"message TEXT, " +
				"level INTEGER, " + 
				"time TEXT)", connection);
		}

		public static void ExecuteNonQuery(string query, SQLiteConnection connection)
		{
			using (var command = new SQLiteCommand(connection))
			{
				command.CommandText = query;
				command.ExecuteNonQuery();
			}
		}

		private static void AddSettingsList(SQLiteConnection connection, Dictionary<string, string> settingsList)
		{
			using (var settingsInsert = new SQLiteCommand(connection))
			{
				// Add settings
				var settingsName = new SQLiteParameter();
				var settingsValue = new SQLiteParameter();

				settingsInsert.CommandText = "INSERT INTO settings (name, value) VALUES (?, ?)";
				settingsInsert.Parameters.Add(settingsName);
				settingsInsert.Parameters.Add(settingsValue);

				foreach (KeyValuePair<string, string> pair in settingsList)
				{
					settingsName.Value = pair.Key;
					settingsValue.Value = pair.Value;

					settingsInsert.ExecuteNonQuery();
				}
			}
		}
	}
}
