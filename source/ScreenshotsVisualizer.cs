using CommonPluginsShared;
using CommonPluginsShared.Controls;
using CommonPluginsShared.PlayniteExtended;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using ScreenshotsVisualizer.Controls;
using ScreenshotsVisualizer.Models;
using ScreenshotsVisualizer.Services;
using ScreenshotsVisualizer.Views;
using StartPage.SDK;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace ScreenshotsVisualizer
{
    public class ScreenshotsVisualizer : PluginExtended<ScreenshotsVisualizerSettingsViewModel, ScreenshotsVisualizerDatabase>, StartPage.SDK.IStartPageExtension
    {
        public override Guid Id => Guid.Parse("c6c8276f-91bf-48e5-a1d1-4bee0b493488");

        private readonly object _refreshQueueLock = new object();
        private readonly Dictionary<Guid, bool> _refreshQueue = new Dictionary<Guid, bool>();
        private int _selectionRevision;
        private volatile bool _stopping;

        internal TopPanelItem TopPanelItem { get; set; }
        internal SidebarItem SidebarItem { get; set; }
        internal SidebarItemControl SidebarItemControl { get; set; }


        public ScreenshotsVisualizer(IPlayniteAPI api) : base(api, "ScreenshotsVisualizer")
        {
            // Custom theme button
            EventManager.RegisterClassHandler(typeof(Button), Button.ClickEvent, new RoutedEventHandler(OnCustomThemeButtonClick));

            // Custom elements integration
            AddCustomElementSupport(new AddCustomElementSupportArgs
            {
                ElementList = new List<string> { "PluginButton", "PluginSinglePicture", "PluginScreenshots", "PluginListScreenshots", "PluginListScreenshotsVertical", "PluginViewItem" },
                SourceName = PluginName
            });

            // Settings integration
            AddSettingsSupport(new AddSettingsSupportArgs
            {
                SourceName = PluginName,
                SettingsRoot = $"{nameof(PluginSettingsViewModel)}.{nameof(PluginSettingsViewModel.Settings)}"
            });

            _menus = new ScreenshotsVisualizerMenus(PluginSettingsViewModel.Settings, PluginDatabase, this);

            // Initialize top & side bar
            if (API.Instance.ApplicationInfo.Mode == ApplicationMode.Desktop)
            {
                TopPanelItem = new ScreenshotsVisualizerTopPanelItem(this);
                SidebarItem = new ScreenshotsVisualizerViewSidebar(this);
            }
        }

        #region Custom event

        public void OnCustomThemeButtonClick(object sender, RoutedEventArgs e)
        {
            try
            {
                string buttonName = ((Button)sender).Name;
                if (buttonName == "PART_CustomSsvButton")
                {
                    Common.LogDebug("OnCustomThemeButtonClick()");
                    PluginDatabase.PluginWindows.ShowPluginGameDataWindow(PluginDatabase.GameContext);
                }
            }
            catch (Exception ex)
            {
                Common.LogError(ex, false, true, PluginDatabase.PluginName);
            }
        }

        #endregion

        #region Theme integration

        // Button on top panel
        public override IEnumerable<TopPanelItem> GetTopPanelItems()
        {
            yield return TopPanelItem;
        }

        // List custom controls
        public override Control GetGameViewControl(GetGameViewControlArgs args)
        {
            if (args.Name == "PluginButton")
            {
                return new PluginButton();
            }

            if (args.Name == "PluginSinglePicture")
            {
                return new PluginSinglePicture();
            }

            if (args.Name == "PluginScreenshots")
            {
                return new PluginScreenshots();
            }

            if (args.Name == "PluginListScreenshots")
            {
                return new PluginListScreenshots();
            }

            if (args.Name == "PluginListScreenshotsVertical")
            {
                return new PluginListScreenshotsVertical();
            }

            if (args.Name == "PluginViewItem")
            {
                return new PluginViewItem();
            }

            return null;
        }

        public override IEnumerable<SidebarItem> GetSidebarItems()
        {
            return new List<SidebarItem>
            {
                SidebarItem
            };
        }

        #endregion

        #region StartPageExtension

        public StartPageExtensionArgs GetAvailableStartPageViews()
        {
            List<StartPageViewArgsBase> views = new List<StartPageViewArgsBase>
            {
                new StartPageViewArgsBase
                {
                    Name = ResourceProvider.GetString("LOCSsvCarousel"),
                    ViewId = "SsvCarousel",
                    HasSettings = true
                }
            };
            return new StartPageExtensionArgs { ExtensionName = PluginDatabase.PluginName, Views = views };
        }

        public object GetStartPageView(string viewId, Guid instanceId)
        {
            switch (viewId)
            {
                case "SsvCarousel":
                    return new Views.StartPage.SsvCarousel();

                default:
                    return null;
            }
        }

        public Control GetStartPageViewSettings(string viewId, Guid instanceId)
        {
            switch (viewId)
            {
                case "SsvCarousel":
                    return new Views.StartPage.SsvCarouselSettings(this);

                default:
                    return null;
            }
        }

        public void OnViewRemoved(string viewId, Guid instanceId)
        {

        }

        #endregion

        #region Menus

        public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
        {
            return _menus.GetGameMenuItems(args);
        }

        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
        {
            return _menus.GetMainMenuItems(args);
        }

        #endregion

        #region Game event

        public override void OnGameSelected(OnGameSelectedEventArgs args)
        {
            int revision = System.Threading.Interlocked.Increment(ref _selectionRevision);
            Game selected = args?.NewValue?.Count == 1 ? args.NewValue[0] : null;
            if (selected == null || _stopping) return;

            _ = Task.Run(async () =>
            {
                // Never wait forever if initialization failed or Playnite is closing.
                for (int i = 0; i < 200 && !PluginDatabase.IsLoaded && !_stopping; i++)
                    await Task.Delay(100).ConfigureAwait(false);

                if (_stopping || !PluginDatabase.IsLoaded || revision != _selectionRevision) return;
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted) return;
                dispatcher.BeginInvoke((Action)(() =>
                {
                    if (_stopping || revision != _selectionRevision) return;
                    PluginDatabase.GameContext = selected;
                    PluginDatabase.SetThemesResources(selected);
                }));
            });
        }

        // Add code to be executed when game is finished installing.
        public override void OnGameInstalled(OnGameInstalledEventArgs args)
        {
        }

        // Add code to be executed when game is uninstalled.
        public override void OnGameUninstalled(OnGameUninstalledEventArgs args)
        {
        }

        // Add code to be executed when game is preparing to be started.
        public override void OnGameStarting(OnGameStartingEventArgs args)
        {
        }

        // Add code to be executed when game is started running.
        public override void OnGameStarted(OnGameStartedEventArgs args)
        {
        }

        // Add code to be executed when game is preparing to be started.
        public override void OnGameStopped(OnGameStoppedEventArgs args)
        {
            try
            {
                _ = Task.Run(() =>
                {
                    try
                    {
                        GameSettings gameSettings = PluginDatabase.GetGameSettings(args.Game.Id);
                        if (gameSettings != null)
                        {
                            PluginDatabase.SetDataFromSettings(gameSettings);
                        }
                    }
                    catch (Exception ex)
                    {
                        Common.LogError(ex, false, true, PluginDatabase.PluginName);
                    }

                    if (args?.Game != null && PluginDatabase.GameContext?.Id == args.Game.Id)
                    {
                        var dispatcher = Application.Current?.Dispatcher;
                        if (dispatcher != null && !dispatcher.HasShutdownStarted)
                            dispatcher.BeginInvoke((Action)(() => PluginDatabase.SetThemesResources(args.Game)));
                    }
                });
            }
            catch (Exception ex)
            {
                Common.LogError(ex, false, true, PluginDatabase.PluginName);
            }
        }

        #endregion

        #region Application event

        // Add code to be executed when Playnite is initialized.
        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            string pluginUserDataPath = GetPluginUserDataPath();
            Action<ScreenshotsVisualizerSettings> saveSettings = settings =>
            {
                SavePluginSettings(settings);
                PluginDatabase.PluginSettings = settings;
            };

            SsvArchiveSettingsMigration.ScheduleIfNeeded(
                PluginSettingsViewModel.Settings,
                pluginUserDataPath,
                PluginDatabase.PluginName,
                saveSettings,
                () => SsvPresetSettingsMigration.ScheduleIfNeeded(
                    PluginSettingsViewModel.Settings,
                    pluginUserDataPath,
                    PluginDatabase.PluginName,
                    saveSettings,
                    () => SsvGlobalSourceSettingsMigration.ScheduleIfNeeded(
                        PluginSettingsViewModel.Settings,
                        pluginUserDataPath,
                        PluginDatabase.PluginName,
                        saveSettings)));

            _ = Task.Run(() =>
            {
                try
                {
                    _ = System.Threading.SpinWait.SpinUntil(() => PluginDatabase.IsLoaded || _stopping, 10000);
                    PluginDatabase.ScheduleLibraryThumbnailWarmup();
                }
                catch (Exception ex)
                {
                    Common.LogError(ex, false, true, PluginDatabase.PluginName);
                }
            });
        }

        // Add code to be executed when Playnite is shutting down.
        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            _stopping = true;
            System.Threading.Interlocked.Increment(ref _selectionRevision);
        }

        #endregion

        // Add code to be executed when library is updated.
        public override void OnLibraryUpdated(OnLibraryUpdatedEventArgs args)
        {
        }

        #region External refresh

        /// <summary>
        /// Gets the PluginDatabase instance for external plugins to call via reflection.
        /// </summary>
        public ScreenshotsVisualizerDatabase GetDatabase()
        {
            return PluginDatabase;
        }

        /// <summary>
        /// Manually refreshes screenshot data for the specified game (for external plugin calls).
        /// </summary>
        public void RefreshGame(Game game)
        {
            if (game == null || _stopping) return;
            lock (_refreshQueueLock)
            {
                // One worker per game; further requests set a pending flag and are merged.
                if (_refreshQueue.ContainsKey(game.Id))
                {
                    _refreshQueue[game.Id] = true;
                    return;
                }
                _refreshQueue[game.Id] = false;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    // Combine bursts of notifications from multiple screenshot producers.
                    await Task.Delay(250).ConfigureAwait(false);
                    while (!_stopping)
                    {
                        lock (_refreshQueueLock) _refreshQueue[game.Id] = false;
                        try
                        {
                            GameSettings gameSettings = PluginDatabase.GetGameSettings(game.Id);
                            if (gameSettings != null)
                                PluginDatabase.SetDataFromSettings(gameSettings);
                        }
                        catch (Exception ex)
                        {
                            Common.LogError(ex, false, true, PluginDatabase.PluginName);
                        }
                        lock (_refreshQueueLock)
                        {
                            if (!_refreshQueue[game.Id])
                            {
                                _refreshQueue.Remove(game.Id);
                                return;
                            }
                        }
                        await Task.Delay(250).ConfigureAwait(false);
                    }
                }
                finally
                {
                    // A worker that already removed its entry must not remove a newer worker's entry.
                    if (_stopping)
                        lock (_refreshQueueLock) _refreshQueue.Remove(game.Id);
                }
            });
        }

        /// <summary>
        /// Manually refreshes screenshot data for the specified game by game name.
        /// </summary>
        public void RefreshGameByName(string gameName)
        {
            if (string.IsNullOrEmpty(gameName)) return;

            try
            {
                var game = API.Instance.Database.Games
                    .FirstOrDefault(g => g.Name.Equals(gameName, StringComparison.OrdinalIgnoreCase));

                if (game != null)
                {
                    RefreshGame(game);
                }
                else
                {
                    Common.LogDebug(true, $"Game not found: {gameName}");
                }
            }
            catch (Exception ex)
            {
                Common.LogError(ex, false, true, PluginDatabase.PluginName);
            }
        }

        /// <summary>
        /// Refreshes screenshot data for the specified game (compatible with GameSnap calls).
        /// </summary>
        public void RefreshData(Game game)
        {
            RefreshGame(game);
        }

        #endregion

        #region Settings

        public override ISettings GetSettings(bool firstRunSettings)
        {
            return PluginSettingsViewModel;
        }

        public override UserControl GetSettingsView(bool firstRunSettings)
        {
            return new ScreenshotsVisualizerSettingsView();
        }

        #endregion
    }
}
