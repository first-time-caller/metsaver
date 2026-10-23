using System.Windows;
using Windows.Devices.Sensors;

namespace MetSaver;

/// <summary>
/// Reads the room: dims the display with the ambient light sensor (laptops and tablets usually have one), falling back to
/// the clock (dimmer late at night) on machines without one, and notices when the machine is running on battery.
/// </summary>
sealed class Ambient : IDisposable
{
    readonly bool _enabled;
    readonly System.Windows.Threading.Dispatcher _ui = Application.Current.Dispatcher;
    readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(60) };
    LightSensor? _sensor;
    double? _lux;

    /// <summary>1 = full brightness; lower = dimmer (never below 0.6).</summary>
    public double Dim { get; private set; } = 1;
    public bool OnBattery { get; private set; }
    public bool HasSensor => _sensor is not null;
    public event Action? Changed;

    public Ambient(Settings s)
    {
        _enabled = s.AmbientAware;
        if (!_enabled) return;
        try
        {
            _sensor = LightSensor.GetDefault();
            if (_sensor is not null)
            {
                _sensor.ReportInterval = Math.Max(_sensor.MinimumReportInterval, 2000);
                _sensor.ReadingChanged += (_, e) => { _lux = e.Reading.IlluminanceInLux; _ui.BeginInvoke(Refresh); };
            }
        }
        catch (Exception e) { Log.Error(e, "light sensor"); _sensor = null; }
        Log.Info(_sensor is null ? "ambient: no light sensor, using the clock" : "ambient: light sensor found");
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
        Refresh();
    }

    void Refresh()
    {
        if (!_enabled) return;
        double dim = _lux is { } lux ? FromLux(lux) : FromClock(DateTime.Now);
        bool battery = SystemParameters.PowerLineStatus == PowerLineStatus.Offline;
        if (Math.Abs(dim - Dim) < 0.01 && battery == OnBattery) return;
        Dim = dim; OnBattery = battery;
        Changed?.Invoke();
    }

    public static double FromLux(double lux) => lux switch { < 5 => 0.6, < 30 => 0.72, < 150 => 0.86, _ => 1.0 };

    public static double FromClock(DateTime now) => now.Hour switch { >= 23 or < 6 => 0.7, >= 21 or < 7 => 0.85, _ => 1.0 };

    public void Dispose() { _timer.Stop(); }
}
