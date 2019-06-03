namespace QModManager
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection;
    using System.Text.RegularExpressions;
    using API;
    using API.ModLoading;
    using API.ModLoading.Internal;
    using Checks;
    using Harmony;
    using QModManager.Patching;
    using Utility;

    /// <summary>
    /// The main class which handles all of QModManager's patching
    /// </summary>
    public static class Patcher
    {
        internal static HarmonyInstance Harmony;

        internal static readonly Regex IDRegex = new Regex("[^0-9a-z_]", RegexOptions.IgnoreCase);

        internal static string QModBaseDir
        {
            get
            {
                if (Environment.CurrentDirectory.Contains("system32") && Environment.CurrentDirectory.Contains("Windows"))
                    return null;
                else
                    return Path.Combine(Environment.CurrentDirectory, "QMods");
            }
        }

        private static bool Patched = false;
        internal static Game CurrentlyRunningGame { get; private set; } = Game.None;
        internal static int ErrorModCount { get; private set; }

        internal static void Patch()
        {
            try
            {
                if (Patched)
                {
                    Logger.Warn("Patch method was called multiple times!");
                    return;
                }
                Patched = true;

                Logger.Info($"Loading QModManager v{Assembly.GetExecutingAssembly().GetName().Version.ToStringParsed()}...");

                if (QModBaseDir == null)
                {
                    Logger.Fatal("A fatal error has occurred.");
                    Logger.Fatal("There was an error with the QMods directory");
                    Logger.Fatal("Please make sure that you ran Subnautica from Steam/Epic/Discord, and not from the executable file!");
                    return;
                }

                try
                {
                    IOUtilities.LogFolderStructureAsTree();
                }
                catch (Exception e)
                {
                    Logger.Error("There was an error while trying to display the folder structure.");
                    Logger.Exception(e);
                }

                QModHooks.Load();

                PirateCheck.CheckIfPirate(Environment.CurrentDirectory);

                var gameDetector = new GameDetector();

                if (!gameDetector.IsValidGameRunning)
                    return;

                CurrentlyRunningGame = gameDetector.CurrentlyRunningGame;

                PatchHarmony();

                if (NitroxCheck.IsInstalled)
                {
                    Logger.Fatal($"Nitrox was detected!");
                    Dialog.Show("Both QModManager and Nitrox detected. QModManager is not compatible with Nitrox. Please uninstall one of them.", Dialog.Button.Disabled, Dialog.Button.Disabled, false);
                    return;
                }

                VersionCheck.CheckForUpdates();

                Logger.Info("Started loading mods");

                AddAssemblyResolveEvent();

                var modFactory = new QModFactory();
                List<QMod> modsToLoad = modFactory.BuildModLoadingList(QModBaseDir);
                ErrorModCount = modFactory.FailedToCreate;

                var initializer = new Initializer(modsToLoad, CurrentlyRunningGame);
                initializer.Initialize();
                ErrorModCount += initializer.FailedToLoad;

                QModHooks.OnLoadEnd?.Invoke();

                int loadedMods = 0;
                foreach (QMod mod in modsToLoad)
                {
                    if (mod.IsLoaded)
                        loadedMods++;
                }

                Logger.Info($"Finished loading QModManager. Loaded {loadedMods} mods");

                if (ErrorModCount > 0)
                    Logger.Info($"A total of {ErrorModCount} mods failed to load");

            }
            catch (Exception e)
            {
                Logger.Error("EXCEPTION CAUGHT!");
                Logger.Exception(e);
            }
        }

        private static void AddAssemblyResolveEvent()
        {
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                FileInfo[] allDlls = new DirectoryInfo(QModBaseDir).GetFiles("*.dll", SearchOption.AllDirectories);
                foreach (FileInfo dll in allDlls)
                {
                    if (args.Name.Contains(Path.GetFileNameWithoutExtension(dll.Name)))
                    {
                        return Assembly.LoadFrom(dll.FullName);
                    }
                }

                return null;
            };

            Logger.Debug("Added AssemblyResolve event");
        }

        private static void PatchHarmony()
        {
            Harmony = HarmonyInstance.Create("qmodmanager");
            Harmony.PatchAll();
            Logger.Debug("Patched!");
        }
    }
}
