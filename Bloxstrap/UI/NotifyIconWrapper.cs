using Bloxstrap.Integrations;
using Bloxstrap.UI.Elements.About;
using Bloxstrap.UI.Elements.ContextMenu;

namespace Bloxstrap.UI
{
    public class NotifyIconWrapper : IDisposable
    {
        // lol who needs properly structured mvvm and xaml when you have the absolute catastrophe that this is

        private bool _disposing = false;

        // Xyllostrap: the tray icon lives on its own thread with its own message loop,
        // so right-clicking it always works even if the main UI thread is busy or stuck.
        private System.Windows.Forms.NotifyIcon _notifyIcon = null!;

        private System.Windows.Forms.ContextMenuStrip _fallbackMenu = null!;

        private SynchronizationContext _trayContext = null!;

        private readonly MenuContainer _menuContainer;

        private readonly Watcher _watcher;

        private ActivityWatcher? _activityWatcher => _watcher.ActivityWatcher;

        EventHandler? _alertClickHandler;

        public NotifyIconWrapper(Watcher watcher)
        {
            App.Logger.WriteLine("NotifyIconWrapper::NotifyIconWrapper", "Initializing notification area icon");

            _watcher = watcher;

            using var ready = new ManualResetEventSlim();

            var trayThread = new Thread(() =>
            {
                _trayContext = new System.Windows.Forms.WindowsFormsSynchronizationContext();
                SynchronizationContext.SetSynchronizationContext(_trayContext);

                _fallbackMenu = new System.Windows.Forms.ContextMenuStrip();
                _fallbackMenu.Items.Add("Exit Xyllostrap", null, (_, _) => _watcher.ExitApp());

                _notifyIcon = new(new System.ComponentModel.Container())
                {
                    Icon = Properties.Resources.IconBloxstrap,
                    Text = "Xyllostrap",
                    Visible = true
                };

                _notifyIcon.MouseClick += MouseClickEventHandler;

                ready.Set();

                System.Windows.Forms.Application.Run();
            })
            {
                IsBackground = true,
                Name = "Xyllostrap tray icon"
            };

            trayThread.SetApartmentState(ApartmentState.STA);
            trayThread.Start();
            ready.Wait();

            if (_activityWatcher is not null && App.Settings.Prop.ShowServerDetails)
                _activityWatcher.ShowNotif += ShowNotif;

            _menuContainer = new(_watcher);
            _menuContainer.Show();
        }

        #region Context menu
        public void MouseClickEventHandler(object? sender, System.Windows.Forms.MouseEventArgs e)
        {
            if (e.Button != System.Windows.Forms.MouseButtons.Right)
                return;

            var op = _menuContainer.Dispatcher.BeginInvoke(() =>
            {
                _menuContainer.Activate();
                _menuContainer.ContextMenu.IsOpen = true;
            });

            // if the main UI doesn't respond within a second, show a simple menu so the user can always exit
            var cursor = System.Windows.Forms.Cursor.Position;

            Task.Delay(1000).ContinueWith(_ =>
            {
                if (op.Status != System.Windows.Threading.DispatcherOperationStatus.Pending)
                    return;

                App.Logger.WriteLine("NotifyIconWrapper::MouseClickEventHandler", "Main UI is not responding, showing fallback tray menu");
                _trayContext.Post(_ => _fallbackMenu.Show(cursor), null);
            });
        }

        /// <summary>
        /// Hides the tray icon without waiting on anything (safe to call from any thread).
        /// </summary>
        public void Hide()
        {
            try
            {
                _trayContext.Send(_ => _notifyIcon.Visible = false, null);
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("NotifyIconWrapper::Hide", ex);
            }
        }
        #endregion

        #region Activity handlers
        public async void ShowNotif(object? sender, EventArgs e)
        {
            if (_activityWatcher is null)
                return;

            string title = _activityWatcher.Data.ServerType switch
            {
                ServerType.Public => Strings.ContextMenu_ServerInformation_Notification_Title_Public,
                ServerType.Private => Strings.ContextMenu_ServerInformation_Notification_Title_Private,
                ServerType.Reserved => Strings.ContextMenu_ServerInformation_Notification_Title_Reserved,
                _ => ""
            };

            string? serverLocation = await _activityWatcher.Data.QueryServerLocation();
            string? serverUptime;

            DateTime? serverTime = _activityWatcher.Data.StartTime;
            if (serverTime is not null)
            {
                TimeSpan _serverUptime = DateTime.UtcNow - serverTime.Value;

                if (_serverUptime.TotalMinutes == 0)
                    serverUptime = "0 minutes"; // :sob:
                else
                    serverUptime = Time.FormatTimeSpan(_serverUptime);
            }
            else
                serverUptime = Strings.Common_Unknown; // this should never happen

            ShowAlert(
                title,
                String.Format(
                    Strings.ContextMenu_ServerDetails_Notification_Text,
                    serverLocation,
                    serverUptime
                    ),
                10,
                (_, _) => _menuContainer.ShowServerInformationWindow()
            );
        }
        #endregion

        // we may need to create our own handler for this, because this sorta sucks
        public void ShowAlert(string caption, string message, int duration, EventHandler? clickHandler)
        {
            string id = Guid.NewGuid().ToString()[..8];

            string LOG_IDENT = $"NotifyIconWrapper::ShowAlert.{id}";

            App.Logger.WriteLine(LOG_IDENT, $"Showing alert for {duration} seconds (clickHandler={clickHandler is not null})");
            App.Logger.WriteLine(LOG_IDENT, $"{caption}: {message.Replace("\n", "\\n")}");

            // balloon clicks arrive on the tray thread, but the handlers open WPF windows, so hop to the UI thread
            EventHandler? uiClickHandler = clickHandler is null
                ? null
                : (s, e) => _menuContainer.Dispatcher.BeginInvoke(() => clickHandler(s, e));

            _trayContext.Post(_ =>
            {
                _notifyIcon.BalloonTipTitle = caption;
                _notifyIcon.BalloonTipText = message;

                if (_alertClickHandler is not null)
                {
                    App.Logger.WriteLine(LOG_IDENT, "Previous alert still present, erasing click handler");
                    _notifyIcon.BalloonTipClicked -= _alertClickHandler;
                }

                _alertClickHandler = uiClickHandler;
                _notifyIcon.BalloonTipClicked += uiClickHandler;

                _notifyIcon.ShowBalloonTip(duration);
            }, null);

            clickHandler = uiClickHandler;

            Task.Run(async () =>
            {
                await Task.Delay(duration * 1000);

                _trayContext.Post(_ => _notifyIcon.BalloonTipClicked -= clickHandler, null);

                App.Logger.WriteLine(LOG_IDENT, "Duration over, erasing current click handler");

                if (_alertClickHandler == clickHandler)
                    _alertClickHandler = null;
                else
                    App.Logger.WriteLine(LOG_IDENT, "Click handler has been overridden by another alert");
            });
        }

        public void Dispose()
        {
            if (_disposing)
                return;

            _disposing = true;

            App.Logger.WriteLine("NotifyIconWrapper::Dispose", "Disposing NotifyIcon");

            // don't block on the UI thread here, it may be the thing that's stuck
            _menuContainer.Dispatcher.BeginInvoke(_menuContainer.Close);

            try
            {
                _trayContext.Send(_ =>
                {
                    _notifyIcon.Visible = false;
                    _notifyIcon.Dispose();
                    _fallbackMenu.Dispose();
                    System.Windows.Forms.Application.ExitThread();
                }, null);
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("NotifyIconWrapper::Dispose", ex);
            }

            GC.SuppressFinalize(this);
        }
    }
}
