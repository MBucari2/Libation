using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Dinah.Core;
using LibationFileManager;
using LibationWinForms.Dialogs;
using Serilog;

namespace LibationWinForms
{
	static class Program
	{
		[System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
		[return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
		static extern bool AllocConsole();

		[STAThread]
		static void Main()
		{
			try
			{
				//// Uncomment to see Console. Must be called before anything writes to Console.
				//// Only use while debugging. Acts erratically in the wild
				//AllocConsole();

				// run as early as possible. see notes in postLoggingGlobalExceptionHandling
				Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

				ApplicationConfiguration.Initialize();

				AppScaffolding.LibationScaffolding.SetReleaseIdentifier(AppScaffolding.ReleaseIdentifier.WindowsClassic);

				//***********************************************//
				//                                               //
				//   do not use Configuration before this line   //
				//                                               //
				//***********************************************//
				// Migrations which must occur before configuration is loaded for the first time. Usually ones which alter the Configuration
				var config = AppScaffolding.LibationScaffolding.RunPreConfigMigrations();

				// do this as soon as possible (post-config)
				RunInstaller(config);

				// most migrations go in here
				AppScaffolding.LibationScaffolding.RunPostConfigMigrations(config);

				// migrations which require Forms or are long-running
				RunWindowsOnlyMigrations(config);

				MessageBoxLib.VerboseLoggingWarning_ShowIfTrue();

#if !DEBUG
				checkForUpdate();
#endif
				// logging is init'd here
				AppScaffolding.LibationScaffolding.RunPostMigrationScaffolding(config);
			}
			catch (Exception ex)
			{
				var title = "Fatal error, pre-logging";
				var body = "An unrecoverable error occurred. Since this error happened before logging could be initialized, this error can not be written to the log file.";
				try
				{
					MessageBoxLib.ShowAdminAlert(null, body, title, ex);
				}
				catch
				{
					MessageBox.Show($"{body}\r\n\r\n{ex.Message}\r\n\r\n{ex.StackTrace}", title, MessageBoxButtons.OK, MessageBoxIcon.Error);
				}
				return;
			}

			// global exception handling (ShowAdminAlert) attempts to use logging. only call it after logging has been init'd
			postLoggingGlobalExceptionHandling();

			Application.Run(new Form1());
		}

		private static void RunInstaller(Configuration config)
		{
			// all returns should be preceded by either:
			// - if config.LibationSettingsAreValid
			// - error message, Exit()

			if (config.LibationSettingsAreValid)
				return;

			var defaultLibationFilesDir = Configuration.UserProfile;

			// check for existing settings in default location
			var defaultSettingsFile = Path.Combine(defaultLibationFilesDir, "Settings.json");
			if (Configuration.SettingsFileIsValid(defaultSettingsFile))
				Configuration.SetLibationFiles(defaultLibationFilesDir);

			if (config.LibationSettingsAreValid)
				return;

			static void CancelInstallation()
			{
				MessageBox.Show("Initial set up cancelled.", "Cancelled", MessageBoxButtons.OK, MessageBoxIcon.Warning);
				Application.Exit();
				Environment.Exit(0);
			}

			var setupDialog = new SetupDialog();
			if (setupDialog.ShowDialog() != DialogResult.OK)
			{
				CancelInstallation();
				return;
			}

			if (setupDialog.IsNewUser)
				Configuration.SetLibationFiles(defaultLibationFilesDir);
			else if (setupDialog.IsReturningUser)
			{
				var libationFilesDialog = new LibationFilesDialog();

				if (libationFilesDialog.ShowDialog() != DialogResult.OK)
				{
					CancelInstallation();
					return;
				}

				Configuration.SetLibationFiles(libationFilesDialog.SelectedDirectory);
				if (config.LibationSettingsAreValid)
					return;

				// path did not result in valid settings
				var continueResult = MessageBox.Show(
					$"No valid settings were found at this location.\r\nWould you like to create a new install settings in this folder?\r\n\r\n{libationFilesDialog.SelectedDirectory}",
					"New install?",
					MessageBoxButtons.YesNo,
					MessageBoxIcon.Question);

				if (continueResult != DialogResult.Yes)
				{
					CancelInstallation();
					return;
				}
			}

			// INIT DEFAULT SETTINGS
			// if 'new user' was clicked, or if 'returning user' chose new install: show basic settings dialog
			config.Books ??= Path.Combine(defaultLibationFilesDir, "Books");

			if (new SettingsDialog().ShowDialog() != DialogResult.OK)
			{
				CancelInstallation();
				return;
			}

			if (config.LibationSettingsAreValid)
				return;

			CancelInstallation();
		}

		/// <summary>migrations which require Forms or are long-running</summary>
		private static void RunWindowsOnlyMigrations(Configuration config)
		{
			// examples:
			// - only supported in winforms. don't move to app scaffolding
			// - long running. won't get a chance to finish in cli. don't move to app scaffolding
		}

		private static void checkForUpdate()
		{
			AppScaffolding.UpgradeProperties upgradeProperties;

			try
			{
				upgradeProperties = AppScaffolding.LibationScaffolding.GetLatestRelease();
				if (upgradeProperties is null)
					return;
			}
			catch (Exception ex)
			{
				MessageBoxLib.ShowAdminAlert(null, "Error checking for update", "Error checking for update", ex);
				return;
			}

			if (upgradeProperties.ZipUrl is null)
			{
				MessageBox.Show(upgradeProperties.HtmlUrl, "New version available");
				return;
			}

			Updater.Run(upgradeProperties);
		}

		private static void postLoggingGlobalExceptionHandling()
		{
			// this line is all that's needed for strict handling
			AppDomain.CurrentDomain.UnhandledException += (_, e) => MessageBoxLib.ShowAdminAlert(null, "Libation has crashed due to an unhandled error.", "Application crash!", (Exception)e.ExceptionObject);

			// these 2 lines makes it graceful. sync (eg in main form's ctor) and thread exceptions will still crash us, but event (sync, void async, Task async) will not
			Application.ThreadException += (_, e) => MessageBoxLib.ShowAdminAlert(null, "Libation has encountered an unexpected error.", "Unexpected error", e.Exception);
			// move to beginning of execution. crashes app if this is called post-RunInstaller: System.InvalidOperationException: 'Thread exception mode cannot be changed once any Controls are created on the thread.'
			//// I never found a case where including made a difference. I think this enum is default and including it will override app user config file
			//Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
		}
	}
}