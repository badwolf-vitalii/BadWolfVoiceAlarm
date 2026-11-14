using System;
using System.Threading;
using System.Windows;

namespace BadWolfVoiceAlarm;

public partial class App : Application
{
    private Mutex? _instanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        _instanceMutex = new Mutex(true, @"Local\BadWolfVoiceAlarm_SingleInstance", out bool isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show("BadWolf Voice Alarm is already running. Check the notification area.",
                "BadWolf Voice Alarm", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _instanceMutex?.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // This process does not own the mutex.
        }

        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
