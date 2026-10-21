// THROWAWAY spike: streams the emulator screen over gRPC into an Avalonia window,
// forwards mouse and Back/Home, and measures fps and click-to-pixel latency.
// Usage: DisplaySpike --port 8554 --token <token> [--mmap] [--width 540]
using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using Android.Emulation.Control;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Grpc.Core;
using Grpc.Net.Client;

[assembly: System.Runtime.Versioning.SupportedOSPlatform("windows")]

internal static class Program
{
    public static string[] Args = [];

    [STAThread]
    public static void Main(string[] args)
    {
        Args = args;
        AppBuilder.Configure<SpikeApp>().UsePlatformDetect().LogToTrace().StartWithClassicDesktopLifetime(args);
    }

    public static string Arg(string name, string fallback)
    {
        var i = Array.IndexOf(Args, name);
        return i >= 0 && i + 1 < Args.Length ? Args[i + 1] : fallback;
    }
}

internal sealed class SpikeApp : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new SpikeWindow();
        }
        base.OnFrameworkInitializationCompleted();
    }
}

internal sealed class SpikeWindow : Window
{
    private readonly EmulatorController.EmulatorControllerClient _client;
    private readonly Metadata _auth;
    private readonly Avalonia.Controls.Image _view = new() { Stretch = Stretch.Uniform };
    private readonly TextBlock _stats = new() { Margin = new Thickness(8) };
    private readonly int _width = int.Parse(Program.Arg("--width", "540"));
    private readonly bool _mmap = Program.Args.Contains("--mmap");
    private readonly StreamWriter _log;
    private WriteableBitmap? _bitmap;
    private int _deviceW = 1080, _deviceH = 2400, _frameW, _frameH;
    private int _frames;
    private readonly Stopwatch _fpsClock = Stopwatch.StartNew();
    private long _clickTicks;
    private int _received;
    private readonly Stopwatch _rxClock = Stopwatch.StartNew();
    private readonly List<double> _latencies = [];

    public SpikeWindow()
    {
        Title = "DisplaySpike";
        Width = 420;
        Height = 860;
        var port = Program.Arg("--port", "8554");
        _auth = new Metadata { { "authorization", "Bearer " + Program.Arg("--token", "") } };
        _client = new EmulatorController.EmulatorControllerClient(GrpcChannel.ForAddress($"http://127.0.0.1:{port}"));
        _log = new StreamWriter(Path.Combine(AppContext.BaseDirectory, "spike-measures.log"), append: true) { AutoFlush = true };
        _log.WriteLine($"--- {DateTime.Now:O} width={_width} mmap={_mmap}");

        var back = new Button { Content = "Back" };
        back.Click += (_, _) => Key("GoBack");
        var home = new Button { Content = "Home" };
        home.Click += (_, _) => Key("GoHome");
        var bar = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6, Children = { back, home, _stats } };
        var root = new DockPanel();
        DockPanel.SetDock(bar, Avalonia.Controls.Dock.Top);
        root.Children.Add(bar);
        root.Children.Add(_view);
        Content = root;

        _view.PointerPressed += (_, e) => Mouse(e, 1, click: true);
        _view.PointerMoved += (_, e) => { if (e.GetCurrentPoint(_view).Properties.IsLeftButtonPressed) Mouse(e, 1, click: false); };
        _view.PointerReleased += (_, e) => Mouse(e, 0, click: false);

        Opened += async (_, _) =>
        {
            var info = await _client.getStatusAsync(new Google.Protobuf.WellKnownTypes.Empty(), _auth);
            _deviceW = (int)info.HardwareConfig.Entry.First(x => x.Key == "hw.lcd.width").Value.Let(int.Parse);
            _deviceH = (int)info.HardwareConfig.Entry.First(x => x.Key == "hw.lcd.height").Value.Let(int.Parse);
            _ = Task.Run(StreamLoop);
            if (Program.Args.Contains("--auto")) _ = Task.Run(AutoTaps);
        };
        Closed += (_, _) => _log.WriteLine($"latency samples ms: {string.Join(", ", _latencies.Select(x => x.ToString("F0")))}; median={Median():F0}");
    }

    private double Median() => _latencies.Count == 0 ? 0 : _latencies.Order().ElementAt(_latencies.Count / 2);

    private async Task StreamLoop()
    {
        var height = _width * _deviceH / _deviceW;
        var format = new ImageFormat { Format = ImageFormat.Types.ImgFormat.Rgba8888, Width = (uint)_width, Height = (uint)height };
        MemoryMappedFile? mmf = null;
        MemoryMappedViewAccessor? view = null;
        if (_mmap)
        {
            var path = Path.Combine(Path.GetTempPath(), $"andro-spike-{System.Environment.ProcessId}.bin");
            var size = _width * height * 4;
            using (var fs = File.Create(path)) fs.SetLength(size);
            mmf = MemoryMappedFile.CreateFromFile(path, FileMode.Open, null, size, MemoryMappedFileAccess.ReadWrite);
            view = mmf.CreateViewAccessor();
            format.Transport = new ImageTransport { Channel = ImageTransport.Types.TransportChannel.Mmap, Handle = new Uri(path).AbsoluteUri };
        }

        using var call = _client.streamScreenshot(format, _auth);
        byte[] buffer = [];
        await foreach (var img in call.ResponseStream.ReadAllAsync())
        {
            _frameW = (int)img.Format.Width;
            _frameH = (int)img.Format.Height;
            var len = _frameW * _frameH * 4;
            if (buffer.Length != len) buffer = new byte[len];
            if (view is not null) view.ReadArray(0, buffer, 0, len);
            else img.Image_.Span.CopyTo(buffer);

            var clicked = Interlocked.Exchange(ref _clickTicks, 0);
            if (clicked != 0) _latencies.Add(Stopwatch.GetElapsedTime(clicked).TotalMilliseconds);

            Interlocked.Increment(ref _frames);
            _received++;
            if (_rxClock.Elapsed.TotalSeconds >= 1)
            {
                _log.WriteLine($"rx={_received / _rxClock.Elapsed.TotalSeconds:F1} seq={img.Seq} bytes={img.Image_.Length}");
                _received = 0;
                _rxClock.Restart();
            }
            var copy = buffer;
            Dispatcher.UIThread.Post(() => Present(copy));
        }
    }

    private unsafe void Present(byte[] rgba)
    {
        if (_bitmap is null || _bitmap.PixelSize.Width != _frameW || _bitmap.PixelSize.Height != _frameH)
        {
            _bitmap = new WriteableBitmap(new PixelSize(_frameW, _frameH), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Opaque);
        }
        using (var fb = _bitmap.Lock())
        {
            Marshal.Copy(rgba, 0, fb.Address, Math.Min(rgba.Length, fb.RowBytes * _frameH));
        }
        _view.Source = null;
        _view.Source = _bitmap;

        if (_fpsClock.Elapsed.TotalSeconds >= 1)
        {
            var fps = Interlocked.Exchange(ref _frames, 0) / _fpsClock.Elapsed.TotalSeconds;
            _fpsClock.Restart();
            var cpu = Process.GetCurrentProcess().TotalProcessorTime;
            _stats.Text = $"{fps:F0} fps · lat med {Median():F0} ms (n={_latencies.Count})";
            _log.WriteLine($"fps={fps:F1} cpu={cpu.TotalSeconds:F1}s latMedian={Median():F0}");
        }
    }

    private void Mouse(PointerEventArgs e, int buttons, bool click)
    {
        var p = e.GetPosition(_view);
        var bounds = _view.Bounds;
        // Image is Uniform-stretched: map view coords to device pixels.
        var scale = Math.Min(bounds.Width / _deviceW, bounds.Height / _deviceH);
        var offX = (bounds.Width - _deviceW * scale) / 2;
        var offY = (bounds.Height - _deviceH * scale) / 2;
        var x = (int)((p.X - offX) / scale);
        var y = (int)((p.Y - offY) / scale);
        if (click) Interlocked.Exchange(ref _clickTicks, Stopwatch.GetTimestamp());
        _ = _client.sendMouseAsync(new MouseEvent { X = x, Y = y, Buttons = buttons }, _auth).ResponseAsync;
    }

    // Taps a list row (ripple = visible change) 15 times; latency = tap sent -> next frame.
    private async Task AutoTaps()
    {
        await Task.Delay(3000);
        for (var i = 0; i < 15; i++)
        {
            Interlocked.Exchange(ref _clickTicks, Stopwatch.GetTimestamp());
            await _client.sendMouseAsync(new MouseEvent { X = 540, Y = 900, Buttons = 1 }, _auth);
            await Task.Delay(150);
            await _client.sendMouseAsync(new MouseEvent { X = 540, Y = 900, Buttons = 0 }, _auth);
            await Task.Delay(1500);
            Key("GoBack");
            await Task.Delay(1500);
        }
        _log.WriteLine($"AUTO latency samples ms: {string.Join(", ", _latencies.Select(x => x.ToString("F0")))}; median={Median():F0}");
    }

    private void Key(string key) =>
        _ = _client.sendKeyAsync(new KeyboardEvent { EventType = KeyboardEvent.Types.KeyEventType.Keypress, Key = key }, _auth).ResponseAsync;
}

internal static class Ext
{
    public static TOut Let<TIn, TOut>(this TIn value, Func<TIn, TOut> f) => f(value);
}
