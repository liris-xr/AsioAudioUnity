using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AsioAudioUnity
{
    /// <summary>
    /// The package's REAPER Lua scripts, and their copies installed in REAPER's Scripts folder (%APPDATA%\REAPER\Scripts\AsioAudioUnity).
    /// REAPER runs the installed copies, so they must be updated when the package's scripts change.
    /// </summary>
    internal static class ReaperScriptInstaller
    {
        /// <summary>The package's Lua scripts folder, or null.</summary>
        public static string PackageFolder
        {
            get
            {
                UnityEditor.PackageManager.PackageInfo package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(ReaperController).Assembly);
                string folder = package != null ? Path.Combine(package.resolvedPath, "Modules", "ReaperOsc", "Lua Scripts") : null;
                return folder != null && Directory.Exists(folder) ? folder : null;
            }
        }

        /// <summary>REAPER's resource folder (%APPDATA%\REAPER).</summary>
        public static string ReaperFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "REAPER");

        /// <summary>Where the scripts are installed (%APPDATA%\REAPER\Scripts\AsioAudioUnity).</summary>
        public static string InstalledFolder => Path.Combine(ReaperFolder, "Scripts", "AsioAudioUnity");

        /// <summary>Whether the scripts were installed into REAPER (the folder exists).</summary>
        public static bool IsInstalled => Directory.Exists(InstalledFolder);

        /// <summary>
        /// The installed scripts that differ from the package's (missing ones included), by file name. Empty when the scripts aren't installed
        /// (e.g. a portable REAPER, set up by hand): only an existing installation is checked.
        /// </summary>
        public static List<string> OutdatedScripts()
        {
            List<string> outdated = new List<string>();
            string source = PackageFolder;
            if (source == null || !IsInstalled) return outdated;
            try
            {
                foreach (string script in Directory.GetFiles(source, "*.lua"))
                {
                    string installed = Path.Combine(InstalledFolder, Path.GetFileName(script));
                    if (!File.Exists(installed) || !File.ReadAllBytes(installed).SequenceEqual(File.ReadAllBytes(script))) outdated.Add(Path.GetFileName(script));
                }
            }
            catch (Exception)
            {
                // Unreadable files: nothing to report
            }
            return outdated;
        }

        /// <summary>Copies the package's scripts to REAPER's Scripts folder. Returns the number of scripts copied; throws on failure.</summary>
        public static int Install()
        {
            string source = PackageFolder ?? throw new DirectoryNotFoundException("The package's Lua scripts folder was not found.");
            if (!Directory.Exists(ReaperFolder))
                throw new DirectoryNotFoundException("REAPER's resource folder was not found (" + ReaperFolder + "). For a portable REAPER, copy the scripts from \"Show Scripts\" to its Scripts folder.");
            Directory.CreateDirectory(InstalledFolder);
            string[] scripts = Directory.GetFiles(source, "*.lua");
            foreach (string script in scripts) File.Copy(script, Path.Combine(InstalledFolder, Path.GetFileName(script)), true);
            return scripts.Length;
        }
    }
}
