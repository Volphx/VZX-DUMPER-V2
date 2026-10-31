using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Il2CppDumper_ImGuiStyle;
public sealed class EspDiagnostic
{
    private readonly Window? owner;
    private readonly Action<string> report;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private DiagnosticOverlay? overlay;
    private int pid;
    private bool stopped, reading;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private long lastReport;
    public EspDiagnostic(Window? owner, Action<string> report) { this.owner = owner; this.report = report; timer.Tick += Tick; }
    public async Task StartAsync(OffsetAnalysis profile, string library)
    {
        var processes = Process.GetProcessesByName("HD-Player");
        try
        {
            if (processes.Length == 0) throw new InvalidOperationException("BlueStacks não encontrado. Abra o jogo e tente novamente.");
            pid = processes.Length == 1 ? processes[0].Id : ChooseProcess(processes);
        }
        finally { foreach (var process in processes) process.Dispose(); }
        if (pid == 0) throw new OperationCanceledException("Seleção de instância cancelada.");
        var connected = await Task.Run(() => OffsetRvaNative.StartEsp(profile, library, pid));
        if (stopped) { await Task.Run(OffsetRvaNative.StopEsp); return; }
        report(connected.Status + " Selecione a janela do jogo.");
        overlay = new DiagnosticOverlay(); overlay.Show(); timer.Start();
        if (owner != null) owner.Closed += OwnerClosed;
    }
    private int ChooseProcess(Process[] processes)
    {
        int selected = 0;
        var dialog = new Window { Title = "Instância do BlueStacks", Width = 390, Height = 120 + processes.Length * 44, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = owner, Background = new SolidColorBrush(Color.FromRgb(10, 10, 10)), Foreground = Brushes.White, ResizeMode = ResizeMode.NoResize };
        var panel = new StackPanel { Margin = new Thickness(16) }; panel.Children.Add(new TextBlock { Text = "Escolha a instância usada para o teste:", Margin = new Thickness(0, 0, 0, 12) });
        foreach (var process in processes)
        {
            var id = process.Id; var button = new Button { Content = $"PID {id} — {process.MainWindowTitle}", Margin = new Thickness(0, 0, 0, 6), Padding = new Thickness(8), Background = new SolidColorBrush(Color.FromRgb(23, 23, 23)), Foreground = Brushes.White };
            button.Click += (_, _) => { selected = id; dialog.DialogResult = true; }; panel.Children.Add(button);
        }
        dialog.Content = panel; return dialog.ShowDialog() == true ? selected : 0;
    }
    private async void OwnerClosed(object? sender, EventArgs e) => await StopAsync();
    private async void Tick(object? sender, EventArgs e)
    {
        if (stopped || reading || overlay == null) return;
        reading = true;
        try
        {
            var frame = await Task.Run(OffsetRvaNative.ReadEspFrame);
            if (stopped || overlay == null) return;
            overlay.Present(frame, pid);
            if (clock.ElapsedMilliseconds - lastReport > 500) { report(frame.Status); lastReport = clock.ElapsedMilliseconds; }
        }
        catch (Exception ex) { overlay?.Clear(); report("ESP: " + ex.Message); }
        finally { reading = false; }
    }
    public async Task StopAsync()
    {
        if (stopped) return; stopped = true; timer.Stop(); overlay?.Close(); overlay = null;
        if (owner != null) owner.Closed -= OwnerClosed;
        await Task.Run(OffsetRvaNative.StopEsp);
    }
}

internal sealed class DiagnosticOverlay : Window
{
    private readonly EspDrawing drawing = new();
    private IntPtr handle;
    public DiagnosticOverlay()
    {
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ShowInTaskbar = false; ShowActivated = false; Topmost = true; Focusable = false; IsHitTestVisible = false;
        ResizeMode = ResizeMode.NoResize; Width = Height = 1; Left = Top = -10000; Content = drawing;
        SourceInitialized += (_, _) => { handle = new WindowInteropHelper(this).Handle; var style = GetWindowLongPtr(handle, -20).ToInt64(); SetWindowLongPtr(handle, -20, new IntPtr(style | 0x20 | 0x80 | 0x08000000)); };
    }
    public void Clear() => drawing.Update(new EspFrame());
    public void Present(EspFrame frame, int pid)
    {
        if (!frame.Valid) { Clear(); return; }
        using var process = Process.GetProcessById(pid); process.Refresh(); var main = process.MainWindowHandle;
        if (main == IntPtr.Zero || IsIconic(main)) { Clear(); return; }
        var viewport = main;
        foreach (var name in new[] { "BlueStacksApp", "MSIAppPlayer", "subWin" })
        {
            var child = FindWindowEx(main, IntPtr.Zero, name, null);
            if (child != IntPtr.Zero && IsWindowVisible(child) && GetClientRect(child, out var size) && size.Right > 100 && size.Bottom > 100) { viewport = child; break; }
        }
        if (viewport == main)
        {
            IntPtr largest = IntPtr.Zero, named = IntPtr.Zero; long largestArea = 0, namedArea = 0;
            EnumChildWindows(main, (child, _) =>
            {
                if (!IsWindowVisible(child) || !GetClientRect(child, out var size) || size.Right < 100 || size.Bottom < 100) return true;
                long area = (long)size.Right * size.Bottom;
                if (area > largestArea) { largest = child; largestArea = area; }
                var name = new StringBuilder(128); GetClassName(child, name, name.Capacity);
                if (name.ToString() is "BlueStacksApp" or "MSIAppPlayer" or "subWin" && area > namedArea) { named = child; namedArea = area; }
                return true;
            }, IntPtr.Zero);
            viewport = named != IntPtr.Zero ? named : largest != IntPtr.Zero ? largest : main;
        }
        if (!GetClientRect(viewport, out var rect)) { Clear(); return; }
        var point = new NativePoint(); if (!ClientToScreen(viewport, ref point)) { Clear(); return; }
        GetWindowThreadProcessId(GetForegroundWindow(), out var foreground);
        if (foreground != pid || rect.Right < 100 || rect.Bottom < 100) { Clear(); return; }
        SetWindowPos(handle, new IntPtr(-1), point.X, point.Y, rect.Right, rect.Bottom, 0x10 | 0x40);
        drawing.Update(frame);
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hwnd, ref NativePoint point);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr child, string name, string? title);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    private delegate bool ChildCallback(IntPtr hwnd, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, ChildCallback callback, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder text, int capacity);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
}

internal sealed class EspDrawing : FrameworkElement
{
    private EspFrame frame = new();
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private long received;
    private readonly Pen boxPen = new(Brushes.Cyan, 1.2), bonePen = new(Brushes.White, 1);
    private readonly DispatcherTimer expiry = new() { Interval = TimeSpan.FromMilliseconds(100) };
    public EspDrawing()
    {
        Loaded += (_, _) => expiry.Start(); Unloaded += (_, _) => expiry.Stop();
        expiry.Tick += (_, _) => { if (frame.Valid && clock.ElapsedMilliseconds - received > 300) Update(new EspFrame()); };
    }
    public void Update(EspFrame next) { frame = next; received = clock.ElapsedMilliseconds; InvalidateVisual(); }
    protected override void OnRender(DrawingContext dc)
    {
        if (!frame.Valid || clock.ElapsedMilliseconds - received > 300 || ActualWidth <= 0 || ActualHeight <= 0) return;
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight)));
        foreach (var item in frame.Boxes)
        {
            double height = (item.Bottom - item.Top) * ActualHeight, width = height * .42, x = item.X * ActualWidth, top = item.Top * ActualHeight;
            if (!double.IsFinite(height + width + x + top) || height < 3 || item.MaxHp <= 0) continue;
            dc.DrawRectangle(null, boxPen, new Rect(x - width / 2, top, width, height));
            double ratio = Math.Clamp((double)item.Hp / item.MaxHp, 0, 1);
            dc.DrawRectangle(Brushes.Black, null, new Rect(x - width / 2 - 6, top, 3, height));
            dc.DrawRectangle(Brushes.LimeGreen, null, new Rect(x - width / 2 - 6, top + height * (1 - ratio), 3, height * ratio));
            foreach (var bone in item.Bones) if (bone.Length == 4 && bone.All(double.IsFinite)) dc.DrawLine(bonePen, new Point(bone[0] * ActualWidth, bone[1] * ActualHeight), new Point(bone[2] * ActualWidth, bone[3] * ActualHeight));
            var label = Label($"{(string.IsNullOrWhiteSpace(item.Name) ? "Entidade" : item.Name)} [{item.Distance:F0} m] · {item.Hp}/{item.MaxHp}", 12);
            var origin = new Point(x - label.Width / 2, Math.Max(24, top - 20));
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(190, 0, 0, 0)), null, new Rect(origin.X - 3, origin.Y - 1, label.Width + 6, label.Height + 2)); dc.DrawText(label, origin);
        }
        var heading = Label($"TESTE ESP · {frame.Accepted} alvos · somente leitura", 13);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(210, 10, 10, 10)), null, new Rect(12, 12, heading.Width + 20, 28), 5, 5); dc.DrawText(heading, new Point(22, 17));
        dc.Pop();
    }
    private FormattedText Label(string text, double size) => new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
}
