using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace BluetoothMonitor;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceMutexName = @"Local\BluetoothMonitor.SingleInstance";
    private const string ActivateSignalName = @"Local\BluetoothMonitor.Activate";
    private static readonly string DiagnosticLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BluetoothMonitor",
        "diagnostics.log");

    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _activateSignal;
    private Thread? _activateListenerThread;

    protected override void OnStartup(StartupEventArgs e)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DiagnosticLogPath)!);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            SignalRunningInstance();
            Shutdown();
            return;
        }

        _activateSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateSignalName);
        _activateListenerThread = new Thread(ListenForActivationRequests)
        {
            IsBackground = true,
            Name = "BluetoothMonitorActivationListener"
        };
        _activateListenerThread.Start();

        base.OnStartup(e);

        var launchHidden = e.Args.Any(argument => argument.Equals("--startup", StringComparison.OrdinalIgnoreCase));
        var mainWindow = new MainWindow(launchHidden);
        MainWindow = mainWindow;

        if (launchHidden)
        {
            mainWindow.ShowInTaskbar = false;
            mainWindow.Visibility = Visibility.Hidden;
        }

        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= OnCurrentDomainUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        _activateSignal?.Dispose();
        try
        {
            _singleInstanceMutex?.ReleaseMutex();
        }
        catch (ApplicationException)
        {
        }

        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private static void SignalRunningInstance()
    {
        try
        {
            using var activateSignal = EventWaitHandle.OpenExisting(ActivateSignalName);
            activateSignal.Set();
        }
        catch
        {
            // If the first instance is exiting, there's nothing to foreground.
        }
    }

    private void ListenForActivationRequests()
    {
        while (_activateSignal is not null)
        {
            try
            {
                _activateSignal.WaitOne();
                Dispatcher.BeginInvoke(() =>
                {
                    if (MainWindow is MainWindow mainWindow)
                        mainWindow.BringToFrontFromExternalLaunch();
                });
            }
            catch (ObjectDisposedException)
            {
                break;
            }
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogDiagnostic("DispatcherUnhandledException", e.Exception);
    }

    private void OnCurrentDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        LogDiagnostic("AppDomainUnhandledException", e.ExceptionObject as Exception, e.ExceptionObject);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogDiagnostic("TaskSchedulerUnobservedTaskException", e.Exception);
    }

    private static void LogDiagnostic(string source, Exception? exception, object? payload = null)
    {
        var lines = new List<string>
        {
            $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {source}"
        };

        if (exception is not null)
        {
            lines.Add(exception.ToString());
        }
        else if (payload is not null)
        {
            lines.Add(payload.ToString() ?? "<null>");
        }
        else
        {
            lines.Add("<no exception payload>");
        }

        lines.Add(string.Empty);

        var content = string.Join(Environment.NewLine, lines);

        try
        {
            File.AppendAllText(DiagnosticLogPath, content + Environment.NewLine);
        }
        catch
        {
            // Avoid crashing while trying to record a crash.
        }
    }
}
