using Bloxstrap.AppData;
using Bloxstrap.Integrations;

namespace Bloxstrap
{
    public class Watcher : IDisposable
    {
        private readonly InterProcessLock _lock = new("Watcher");

        private readonly WatcherData? _watcherData;
        
        private readonly NotifyIconWrapper? _notifyIcon;

        public readonly ActivityWatcher? ActivityWatcher;

        public readonly WindowManipulation? WindowManipulation;

        public readonly DiscordRichPresence? RichPresence;

        public Watcher()
        {
            const string LOG_IDENT = "Watcher";


            if (!_lock.IsAcquired)
            {
                App.Logger.WriteLine(LOG_IDENT, "Watcher instance already exists");
                return;
            }

            string? watcherDataArg = App.LaunchSettings.WatcherFlag.Data;

            if (String.IsNullOrEmpty(watcherDataArg))
            {
#if DEBUG
                string path = new RobloxPlayerData().ExecutablePath;
                if (!File.Exists(path))
                    throw new ApplicationException("Roblox player is not been installed");

                using var gameClientProcess = Process.Start(path);

                while (gameClientProcess.MainWindowHandle == IntPtr.Zero)
                    Thread.Sleep(100);

                _watcherData = new() { ProcessId = gameClientProcess.Id, Handle = gameClientProcess.MainWindowHandle.ToInt64() };
#else
                throw new Exception("Watcher data not specified");
#endif
            }
            else
            {
                _watcherData = JsonSerializer.Deserialize<WatcherData>(Encoding.UTF8.GetString(Convert.FromBase64String(watcherDataArg)));
            }

            if (_watcherData is null)
                throw new Exception("Watcher data is invalid");

            if (App.Settings.Prop.EnableWindowManipulation && _watcherData.Handle != 0)
                WindowManipulation = new(_watcherData.Handle, _watcherData.ProcessId);

            if (App.Settings.Prop.EnableActivityTracking)
            {
                ActivityWatcher = new(_watcherData.LogFile) { PrivateServer = _watcherData.PrivateServer };

                if (App.Settings.Prop.UseDisableAppPatch)
                {
                    ActivityWatcher.OnAppClose += delegate
                    {
                        App.Logger.WriteLine(LOG_IDENT, "Received desktop app exit, closing Roblox");
                        using var process = Process.GetProcessById(_watcherData.ProcessId);
                        process.CloseMainWindow();
                    };
                }

                if (App.Settings.Prop.UseDiscordRichPresence && !App.State.Prop.WatcherRunning)
                {
                    App.Logger.WriteLine(LOG_IDENT, "Running rpc");
                    RichPresence = new(ActivityWatcher);
                }
            }

            _notifyIcon = new(this);
        }

        public void KillRobloxProcess() => CloseProcess(_watcherData!.ProcessId, true);

        private bool _disposed = false;

        /// <summary>
        /// Exits Xyllostrap right away (used by the tray menu "Exit Xyllostrap" option).
        /// </summary>
        public void ExitApp()
        {
            const string LOG_IDENT = "Watcher::ExitApp";

            App.Logger.WriteLine(LOG_IDENT, "Exit requested from tray menu");

            // if a clean exit gets stuck for any reason, force it after a few seconds
            new Thread(() =>
            {
                Thread.Sleep(4000);
                Process.GetCurrentProcess().Kill();
            }) { IsBackground = true }.Start();

            try
            {
                _notifyIcon?.Hide();
                Dispose();
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
            }

            App.Terminate();
        }

        private bool IsRobloxRunning()
        {
            if (_watcherData is not null && Utilities.GetProcessesSafe().Any(x => x.Id == _watcherData.ProcessId))
                return true;

            return Process.GetProcessesByName(Path.GetFileNameWithoutExtension(App.RobloxPlayerAppName)).Length > 0;
        }

        /// <summary>
        /// Safety net: once Roblox has fully closed, make sure Xyllostrap doesn't keep running in the background.
        /// Runs on its own thread so nothing else can block it.
        /// </summary>
        public void StartExitFailsafe()
        {
            const string LOG_IDENT = "Watcher::ExitFailsafe";

            if (!_lock.IsAcquired || _watcherData is null)
                return;

            new Thread(() =>
            {
                while (true)
                {
                    Thread.Sleep(3000);

                    if (IsRobloxRunning())
                        continue;

                    // roblox is gone, give the normal shutdown time to finish first
                    Thread.Sleep(15000);

                    if (IsRobloxRunning())
                        continue;

                    App.Logger.WriteLine(LOG_IDENT, "Roblox has closed but Xyllostrap is still running, forcing exit");

                    _notifyIcon?.Hide();
                    Thread.Sleep(500);
                    Process.GetCurrentProcess().Kill();
                }
            })
            {
                IsBackground = true,
                Name = "Roblox exit failsafe"
            }.Start();
        }

        public void CloseProcess(int pid, bool force = false)
        {
            const string LOG_IDENT = "Watcher::CloseProcess";

            try
            {
                using var process = Process.GetProcessById(pid);

                App.Logger.WriteLine(LOG_IDENT, $"Killing process '{process.ProcessName}' (pid={pid}, force={force})");

                if (process.HasExited)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"PID {pid} has already exited");
                    return;
                }

                if (force)
                    process.Kill();
                else
                    process.CloseMainWindow();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"PID {pid} could not be closed");
                App.Logger.WriteException(LOG_IDENT, ex);
            }
        }

        public async Task Run()
        {
            if (!_lock.IsAcquired || _watcherData is null)
                return;

            ActivityWatcher?.Start();
            WindowManipulation?.Start();

            while (Utilities.GetProcessesSafe().Any(x => x.Id == _watcherData.ProcessId))
                await Task.Delay(1000);

            AppStorageManager.Apply();

            if (_watcherData.AutoclosePids is not null)
            {
                foreach (int pid in _watcherData.AutoclosePids)
                    CloseProcess(pid);
            }

            if (App.LaunchSettings.TestModeFlag.Active)
                Process.Start(Paths.Process, "-settings -testmode");
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            App.Logger.WriteLine("Watcher::Dispose", "Disposing Watcher");

            _notifyIcon?.Dispose();

            try
            {
                RichPresence?.Dispose();
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("Watcher::Dispose", ex);
            }

            App.State.Prop.WatcherRunning = false;

            GC.SuppressFinalize(this);
        }
    }
}
