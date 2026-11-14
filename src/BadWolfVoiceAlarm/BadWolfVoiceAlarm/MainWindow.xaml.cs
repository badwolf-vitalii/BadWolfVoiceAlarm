using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using BadWolfVoiceAlarm.Models;
using BadWolfVoiceAlarm.Services;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace BadWolfVoiceAlarm;

public partial class MainWindow : Window
{
    private readonly SettingsStore _settingsStore = new();
    private readonly DiscordVoiceMonitor _monitor = new();
    private readonly AlarmService _alarm = new();
    private readonly Forms.NotifyIcon _trayIcon;
    private AppSettings _settings = new();
    private bool _isConnecting;
    private bool _isExiting;

    public MainWindow()
    {
        InitializeComponent();
        _trayIcon = CreateTrayIcon();
        _monitor.StatusChanged += status => OnUiThread(() => SetStatus(status));
        _monitor.MessageLogged += message => OnUiThread(() => AppendLog(message));
        _monitor.UserEnteredChannel += notification => OnUiThread(() => HandleVoiceJoin(notification));

        try
        {
            _settings = _settingsStore.Load();
            ApplySettings(_settings);
        }
        catch (Exception ex)
        {
            AppendLog($"Could not load settings: {ex.Message}");
        }

        AppendLog("Application started. Waiting for the bot connection.");
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainWindow_Loaded;
        bool startMinimized = Array.Exists(Environment.GetCommandLineArgs(),
            argument => string.Equals(argument, "--minimized", StringComparison.OrdinalIgnoreCase));

        if (startMinimized)
        {
            Hide();
        }

        if (_settings.AutoConnect && !string.IsNullOrWhiteSpace(BotTokenInput.Password))
        {
            await ConnectAsync();
        }
    }

    private Forms.NotifyIcon CreateTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open Voice Alarm", null, (_, _) => OnUiThread(ShowFromTray));
        menu.Items.Add("Stop Alarm", null, (_, _) => OnUiThread(StopAlarm));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => OnUiThread(() => _ = ExitAsync()));

        Drawing.Icon appIcon;
        try
        {
            appIcon = Drawing.Icon.ExtractAssociatedIcon(
                System.Windows.Forms.Application.ExecutablePath) ?? Drawing.SystemIcons.Application;
        }
        catch
        {
            appIcon = Drawing.SystemIcons.Application;
        }

        var icon = new Forms.NotifyIcon
        {
            Icon = appIcon,
            Text = "BadWolf Voice Alarm",
            ContextMenuStrip = menu,
            Visible = true
        };

        icon.DoubleClick += (_, _) => OnUiThread(ShowFromTray);
        return icon;
    }

    private void ApplySettings(AppSettings settings)
    {
        try
        {
            BotTokenInput.Password = SettingsStore.DecryptToken(settings.EncryptedBotToken);
        }
        catch (Exception ex)
        {
            AppendLog($"Saved token cannot be decrypted for this Windows account: {ex.Message}");
            BotTokenInput.Password = string.Empty;
        }

        GuildIdInput.Text = settings.GuildId;
        ChannelIdsInput.Text = settings.ChannelIds;
        AlertOnMovesCheckBox.IsChecked = settings.AlertOnMoves;
        IgnoreBotsCheckBox.IsChecked = settings.IgnoreBots;
        ForceMaxVolumeCheckBox.IsChecked = settings.ForceMaximumVolume;
        AutoConnectCheckBox.IsChecked = settings.AutoConnect;

        try
        {
            LaunchAtStartupCheckBox.IsChecked = StartupManager.IsEnabled();
        }
        catch (Exception ex)
        {
            AppendLog($"Could not read Windows startup setting: {ex.Message}");
        }
    }

    private VoiceFilter ReadFilter() => VoiceFilter.Create(
        GuildIdInput.Text, ChannelIdsInput.Text,
        AlertOnMovesCheckBox.IsChecked == true,
        IgnoreBotsCheckBox.IsChecked == true);

    private bool SaveSettings()
    {
        try
        {
            VoiceFilter filter = ReadFilter();
            var updated = new AppSettings
            {
                EncryptedBotToken = SettingsStore.EncryptToken(BotTokenInput.Password),
                GuildId = GuildIdInput.Text.Trim(),
                ChannelIds = ChannelIdsInput.Text.Trim(),
                AlertOnMoves = AlertOnMovesCheckBox.IsChecked == true,
                IgnoreBots = IgnoreBotsCheckBox.IsChecked == true,
                ForceMaximumVolume = ForceMaxVolumeCheckBox.IsChecked == true,
                AutoConnect = AutoConnectCheckBox.IsChecked == true
            };

            _settingsStore.Save(updated);
            StartupManager.SetEnabled(LaunchAtStartupCheckBox.IsChecked == true);
            _settings = updated;
            _monitor.UpdateFilter(filter);
            AppendLog("Settings saved. Channel filters take effect immediately.");
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Cannot save settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            AppendLog($"Cannot save settings: {ex.Message}");
            return false;
        }
    }

    private async Task ConnectAsync()
    {
        if (_isConnecting || _monitor.IsRunning)
        {
            return;
        }

        VoiceFilter filter;
        try
        {
            filter = ReadFilter();
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show(ex.Message, "Invalid Discord filter", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(BotTokenInput.Password))
        {
            MessageBox.Show("Enter your bot token from the Discord Developer Portal.",
                "Missing token", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!SaveSettings())
        {
            return;
        }

        _isConnecting = true;
        SetStatus("Connecting...");
        UpdateButtons();
        try
        {
            await _monitor.ConnectAsync(BotTokenInput.Password, filter);
            AppendLog("Gateway connection requested. Waiting for Discord READY...");
        }
        catch (Exception ex)
        {
            SetStatus("Disconnected");
            AppendLog($"Connection failed: {ex.Message}");
            MessageBox.Show($"Failed to connect Discord bot:\n\n{ex.Message}",
                "Discord connection error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _isConnecting = false;
            UpdateButtons();
        }
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e) => await ConnectAsync();

    private async void DisconnectButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _monitor.DisconnectAsync();
            AppendLog("Discord bot disconnected by user.");
        }
        catch (Exception ex)
        {
            AppendLog($"Disconnect failed: {ex.Message}");
        }
        finally
        {
            UpdateButtons();
        }
    }

    private void SaveSettingsButton_Click(object sender, RoutedEventArgs e) => SaveSettings();

    private void TestAlarmButton_Click(object sender, RoutedEventArgs e) =>
        StartAlarm("Test alarm: this is how Discord voice activity will sound.");

    private void StopAlarmButton_Click(object sender, RoutedEventArgs e) => StopAlarm();

    private void HandleVoiceJoin(VoiceJoinEvent notification)
    {
        string verb = notification.IsMove ? "moved into" : "joined";
        string message = $"{notification.UserName} {verb} #{notification.ChannelName} ({notification.GuildName})";
        AppendLog(message);
        StartAlarm(message);
    }

    private void StartAlarm(string description)
    {
        if (_alarm.IsActive)
        {
            AppendLog("The alarm is already playing; another event was received.");
            AlarmText.Text = description;
            return;
        }

        try
        {
            if (_alarm.Start(ForceMaxVolumeCheckBox.IsChecked == true))
            {
                AlarmText.Text = description;
                StopAlarmButton.IsEnabled = true;
                AppendLog("Alarm started. Use STOP ALARM, Escape, or the tray menu to stop it.");
                _trayIcon.BalloonTipTitle = "Discord voice activity";
                _trayIcon.BalloonTipText = description.Length > 240 ? description[..240] : description;
                _trayIcon.ShowBalloonTip(4000);
                ShowFromTray();
            }
        }
        catch (Exception ex)
        {
            AppendLog($"Could not start audio: {ex.Message}");
            MessageBox.Show($"Alarm playback failed:\n\n{ex.Message}",
                "Audio error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void StopAlarm()
    {
        if (!_alarm.IsActive)
        {
            return;
        }

        try
        {
            _alarm.Stop();
            AppendLog("Alarm stopped. Windows volume was restored if it was changed.");
        }
        catch (Exception ex)
        {
            AppendLog($"Alarm stop/volume restore warning: {ex.Message}");
        }
        finally
        {
            AlarmText.Text = "No active alarm";
            StopAlarmButton.IsEnabled = false;
        }
    }

    private void UpdateButtons()
    {
        ConnectButton.IsEnabled = !_isConnecting && !_monitor.IsRunning;
        DisconnectButton.IsEnabled = !_isConnecting && _monitor.IsRunning;
    }

    private void SetStatus(string status)
    {
        StatusText.Text = status;
        StatusIndicator.Fill = new SolidColorBrush(status switch
        {
            "Connected" => Color.FromRgb(34, 197, 94),
            "Connecting..." or "Reconnecting..." => Color.FromRgb(245, 158, 11),
            _ => Color.FromRgb(239, 68, 68)
        });
    }

    private void AppendLog(string message)
    {
        if (EventLogText.Text.Length > 30000)
        {
            EventLogText.Text = EventLogText.Text[^18000..];
        }

        EventLogText.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        EventLogText.ScrollToEnd();
    }

    private void OnUiThread(Action action)
    {
        if (_isExiting || Dispatcher.HasShutdownStarted)
        {
            return;
        }

        Dispatcher.BeginInvoke(action);
    }

    private void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    private void Window_StateChanged(object sender, EventArgs e)
    {
        if (!_isExiting && WindowState == WindowState.Minimized)
        {
            Hide();
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_isExiting)
        {
            e.Cancel = true;
            Hide();
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _alarm.IsActive)
        {
            StopAlarm();
            e.Handled = true;
        }
    }

    private async Task ExitAsync()
    {
        if (_isExiting)
        {
            return;
        }

        _isExiting = true;
        try
        {
            _alarm.Stop();
            await _monitor.DisposeAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Shutdown warning: {ex.Message}", "BadWolf Voice Alarm");
        }
        finally
        {
            try
            {
                _alarm.Dispose();
            }
            catch (Exception ex)
            {
                AppendLog($"Audio cleanup warning: {ex.Message}");
            }

            _trayIcon.Visible = false;
            _trayIcon.ContextMenuStrip?.Dispose();
            _trayIcon.Dispose();
            Close();
        }
    }
}
