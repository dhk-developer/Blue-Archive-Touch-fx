using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace BlueArchiveTouchFx
{
    internal enum EffectTheme
    {
        Dark,
        Light
    }

    public partial class MainWindow : Window
    {
        private const string AppName = "Blue-Archive-Touch-fx";
        private const string StartupRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

        private readonly ClickEffectSurface _effectSurface = new();

        private IntPtr _mouseHookId = IntPtr.Zero;
        private IntPtr _windowHandle = IntPtr.Zero;

        private HwndSource? _windowSource;
        private LowLevelMouseProc? _mouseProc;
        private Forms.NotifyIcon? _trayIcon;
        private Forms.ToolStripMenuItem? _startupMenuItem;

        private EffectTheme _theme = EffectTheme.Dark;

        private const int QuitHotkeyId = 1;

        public MainWindow()
        {
            InitializeComponent();

            Left = SystemParameters.VirtualScreenLeft;
            Top = SystemParameters.VirtualScreenTop;
            Width = SystemParameters.VirtualScreenWidth;
            Height = SystemParameters.VirtualScreenHeight;

            Loaded += OnLoaded;
            Closed += OnClosed;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _theme = DetectWindowsTheme();
            _effectSurface.SetTheme(_theme);

            OverlayCanvas.IsHitTestVisible = false;
            OverlayCanvas.SnapsToDevicePixels = true;

            _effectSurface.Width = Width;
            _effectSurface.Height = Height;

            OverlayCanvas.Children.Clear();
            OverlayCanvas.Children.Add(_effectSurface);

            MakeWindowClickThrough();
            RegisterHotkeys();
            CreateTrayIcon();
            EnableStartupOnLaunch();

            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

            _mouseProc = MouseHookCallback;
            _mouseHookId = SetMouseHook(_mouseProc);
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;

            _effectSurface.Stop();

            if (_mouseHookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_mouseHookId);
                _mouseHookId = IntPtr.Zero;
            }

            if (_windowHandle != IntPtr.Zero)
            {
                UnregisterHotKey(_windowHandle, QuitHotkeyId);
            }

            if (_windowSource != null)
            {
                _windowSource.RemoveHook(WindowMessageHandler);
                _windowSource = null;
            }

            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }
        }

        private void MakeWindowClickThrough()
        {
            _windowHandle = new WindowInteropHelper(this).Handle;

            int extendedStyle = GetWindowLong(_windowHandle, GwlExStyle);

            SetWindowLong(
                _windowHandle,
                GwlExStyle,
                extendedStyle | WsExTransparent | WsExLayered | WsExToolWindow);
        }

        private void RegisterHotkeys()
        {
            _windowHandle = new WindowInteropHelper(this).Handle;
            _windowSource = HwndSource.FromHwnd(_windowHandle);
            _windowSource?.AddHook(WindowMessageHandler);

            RegisterHotKey(_windowHandle, QuitHotkeyId, ModControl | ModAlt, VirtualKeyQ);
        }

        private void CreateTrayIcon()
        {
            _startupMenuItem = new Forms.ToolStripMenuItem("Start with Windows")
            {
                Checked = IsStartupEnabled(),
                CheckOnClick = true
            };

            _startupMenuItem.CheckedChanged += (_, _) =>
            {
                if (_startupMenuItem.Checked)
                {
                    EnableStartup();
                }
                else
                {
                    DisableStartup();
                }
            };

            Forms.ToolStripMenuItem exitMenuItem = new("Exit");
            exitMenuItem.Click += (_, _) => Close();

            Forms.ContextMenuStrip menu = new();
            menu.Items.Add(_startupMenuItem);
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(exitMenuItem);

            _trayIcon = new Forms.NotifyIcon
            {
                Icon = LoadTrayIcon(),
                Text = AppName,
                ContextMenuStrip = menu,
                Visible = true
            };

            _trayIcon.DoubleClick += (_, _) => Close();
        }

        private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category != UserPreferenceCategory.General &&
                e.Category != UserPreferenceCategory.VisualStyle)
            {
                return;
            }

            Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() =>
                {
                    EffectTheme detectedTheme = DetectWindowsTheme();

                    if (detectedTheme == _theme)
                    {
                        return;
                    }

                    _theme = detectedTheme;
                    _effectSurface.SetTheme(_theme);
                }));
        }

        private static EffectTheme DetectWindowsTheme()
        {
            const string personalisationPath =
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(personalisationPath, false);
            object? value = key?.GetValue("AppsUseLightTheme");

            if (value is int appsUseLightTheme)
            {
                return appsUseLightTheme == 0
                    ? EffectTheme.Dark
                    : EffectTheme.Light;
            }

            return EffectTheme.Dark;
        }

        private static System.Drawing.Icon LoadTrayIcon()
        {
            string? executablePath = Environment.ProcessPath;

            if (string.IsNullOrWhiteSpace(executablePath))
            {
                executablePath = Process.GetCurrentProcess().MainModule?.FileName;
            }

            if (!string.IsNullOrWhiteSpace(executablePath))
            {
                System.Drawing.Icon? extractedIcon =
                    System.Drawing.Icon.ExtractAssociatedIcon(executablePath);

                if (extractedIcon != null)
                {
                    return extractedIcon;
                }
            }

            return System.Drawing.SystemIcons.Application;
        }

        private void EnableStartupOnLaunch()
        {
            if (!IsStartupEnabled())
            {
                EnableStartup();
            }

            if (_startupMenuItem != null)
            {
                _startupMenuItem.Checked = IsStartupEnabled();
            }
        }

        private static bool IsStartupEnabled()
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(StartupRegistryPath, false);

            string? currentValue = key?.GetValue(AppName) as string;
            string? expectedValue = GetStartupCommand();

            return !string.IsNullOrWhiteSpace(currentValue)
                && !string.IsNullOrWhiteSpace(expectedValue)
                && string.Equals(currentValue, expectedValue, StringComparison.OrdinalIgnoreCase);
        }

        private static void EnableStartup()
        {
            string? startupCommand = GetStartupCommand();

            if (string.IsNullOrWhiteSpace(startupCommand))
            {
                return;
            }

            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(StartupRegistryPath, true);
            key?.SetValue(AppName, startupCommand);
        }

        private static void DisableStartup()
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(StartupRegistryPath, true);
            key?.DeleteValue(AppName, false);
        }

        private static string? GetStartupCommand()
        {
            string? executablePath = Environment.ProcessPath;

            if (string.IsNullOrWhiteSpace(executablePath))
            {
                executablePath = Process.GetCurrentProcess().MainModule?.FileName;
            }

            if (string.IsNullOrWhiteSpace(executablePath))
            {
                return null;
            }

            return $"\"{executablePath}\"";
        }

        private IntPtr WindowMessageHandler(
            IntPtr hwnd,
            int message,
            IntPtr wParam,
            IntPtr lParam,
            ref bool handled)
        {
            if (message != WmHotkey)
            {
                return IntPtr.Zero;
            }

            int hotkeyId = wParam.ToInt32();

            if (hotkeyId == QuitHotkeyId)
            {
                Close();
                handled = true;
            }

            return IntPtr.Zero;
        }

        private IntPtr SetMouseHook(LowLevelMouseProc proc)
        {
            using Process currentProcess = Process.GetCurrentProcess();
            using ProcessModule? currentModule = currentProcess.MainModule;

            return SetWindowsHookEx(
                WhMouseLl,
                proc,
                GetModuleHandle(currentModule?.ModuleName),
                0);
        }

        private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam.ToInt32() == WmLeftButtonDown)
            {
                MSLLHOOKSTRUCT hookData = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);

                double x = hookData.pt.x - Left;
                double y = hookData.pt.y - Top;

                Dispatcher.BeginInvoke(
                    DispatcherPriority.Render,
                    new Action(() => _effectSurface.SpawnClickEffect(x, y)));
            }

            return CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
        }

        private sealed class ClickEffectSurface : FrameworkElement
        {
            private readonly Random _random = new();

            private readonly List<BurstParticle> _burstParticles = new();
            private readonly List<PulseParticle> _pulseParticles = new();
            private readonly List<EnergyArc> _energyArcs = new();

            private readonly Stopwatch _timer = Stopwatch.StartNew();

            private EffectTheme _theme = EffectTheme.Dark;
            private double _lastFrameSeconds;
            private bool _isRendering;

            private const double EffectScale = 1.5;

            private const int BaseBurstCount = 7;
            private const int MaxBurstParticles = 32;
            private const int MaxPulseParticles = 3;
            private const int MaxEnergyArcs = 3;

            private const int ArcSegmentCount = 9;
            private const double SmallestArcRadius = 24.0 * EffectScale;

            private static readonly ThemePalette DarkPalette = CreateThemePalette(
                new Color[]
                {
                    Color.FromArgb(235, 110, 235, 255),
                    Color.FromArgb(220, 145, 240, 255),
                    Color.FromArgb(210, 185, 245, 255),
                    Color.FromArgb(225, 225, 250, 255),
                    Color.FromArgb(235, 245, 252, 255)
                },
                pulseFill: Color.FromArgb(42, 140, 225, 255),
                pulseStroke: Color.FromArgb(75, 180, 240, 255),
                arcBloom: Color.FromArgb(90, 90, 225, 255),
                primaryArc: Color.FromArgb(235, 110, 235, 255),
                secondaryArc: Color.FromArgb(225, 225, 250, 255));

            private static readonly ThemePalette LightPalette = CreateThemePalette(
                new Color[]
                {
                    Color.FromArgb(245, 0, 150, 190),   // clear teal-blue
                    Color.FromArgb(235, 0, 170, 205),   // brighter cyan-teal
                    Color.FromArgb(225, 25, 135, 190),  // medium contrast blue-teal
                    Color.FromArgb(235, 0, 125, 175),   // deeper teal for visibility
                    Color.FromArgb(220, 60, 180, 215)   // softer BA-style cyan
                },
                pulseFill: Color.FromArgb(58, 0, 165, 205),
                pulseStroke: Color.FromArgb(125, 0, 130, 185),
                arcBloom: Color.FromArgb(115, 0, 165, 210),
                primaryArc: Color.FromArgb(245, 0, 180, 220),
                secondaryArc: Color.FromArgb(235, 0, 135, 190));

            public ClickEffectSurface()
            {
                IsHitTestVisible = false;
                SnapsToDevicePixels = true;
                UseLayoutRounding = true;
            }

            public void SetTheme(EffectTheme theme)
            {
                _theme = theme;
                InvalidateVisual();
            }

            public void SpawnClickEffect(double x, double y)
            {
                ThemePalette palette = CurrentPalette;

                SpawnPulse(x, y, palette);
                SpawnBurst(x, y, palette);
                SpawnEnergyArc(x, y, palette);

                StartRendering();
                InvalidateVisual();
            }

            public void Stop()
            {
                if (!_isRendering)
                {
                    return;
                }

                CompositionTarget.Rendering -= OnRendering;
                _isRendering = false;
            }

            protected override void OnRender(DrawingContext drawingContext)
            {
                base.OnRender(drawingContext);

                DrawPulses(drawingContext);
                DrawEnergyArcs(drawingContext);
                DrawBurstParticles(drawingContext);
            }

            private ThemePalette CurrentPalette =>
                _theme == EffectTheme.Light ? LightPalette : DarkPalette;

            private void SpawnPulse(double x, double y, ThemePalette palette)
            {
                if (_pulseParticles.Count >= MaxPulseParticles)
                {
                    _pulseParticles.RemoveAt(0);
                }

                _pulseParticles.Add(new PulseParticle
                {
                    X = x,
                    Y = y,
                    Radius = SmallestArcRadius,
                    GrowthSpeed = 30.0 * EffectScale,
                    Life = 0.16,
                    MaxLife = 0.16,
                    FillBrush = palette.PulseFillBrush,
                    StrokePen = palette.PulseStrokePen
                });
            }

            private void SpawnBurst(double x, double y, ThemePalette palette)
            {
                int availableSlots = MaxBurstParticles - _burstParticles.Count;
                int count = Math.Min(BaseBurstCount, Math.Max(availableSlots, 0));

                if (_burstParticles.Count > 20)
                {
                    count = Math.Min(count, 4);
                }

                for (int i = 0; i < count; i++)
                {
                    double angle = _random.NextDouble() * Math.PI * 2.0;
                    double speed = (120 + _random.NextDouble() * 170) * EffectScale;
                    double life = 0.23 + _random.NextDouble() * 0.13;

                    _burstParticles.Add(new BurstParticle
                    {
                        X = x,
                        Y = y,
                        VelocityX = Math.Cos(angle) * speed,
                        VelocityY = Math.Sin(angle) * speed,
                        Size = 7 + _random.NextDouble() * 8,
                        Rotation = _random.NextDouble() * 360.0,
                        RotationSpeed = -230 + _random.NextDouble() * 460,
                        Life = life,
                        MaxLife = life,
                        IsTriangle = _random.NextDouble() < 0.55,
                        IsFilled = _random.NextDouble() < 0.35,
                        Style = PickParticleStyle(palette)
                    });
                }
            }

            private void SpawnEnergyArc(double x, double y, ThemePalette palette)
            {
                if (_energyArcs.Count >= MaxEnergyArcs)
                {
                    _energyArcs.RemoveAt(0);
                }

                double life = 0.19 + _random.NextDouble() * 0.05;

                _energyArcs.Add(new EnergyArc
                {
                    X = x,
                    Y = y,
                    Radius = SmallestArcRadius + _random.NextDouble() * (7.0 * EffectScale),
                    ExpansionSpeed = (75 + _random.NextDouble() * 30) * EffectScale,
                    StartAngle = -40 + _random.NextDouble() * 30,
                    SweepAngle = 95 + _random.NextDouble() * 40,
                    Rotation = _random.NextDouble() * 360.0,
                    RotationSpeed = _random.NextDouble() < 0.5 ? 720 : -720,
                    Life = life,
                    MaxLife = life,
                    Thickness = 1.95,
                    Brush = _random.NextDouble() < 0.7
                        ? palette.PrimaryArcBrush
                        : palette.SecondaryArcBrush,
                    BloomBrush = palette.ArcBloomBrush
                });
            }

            private void StartRendering()
            {
                if (_isRendering)
                {
                    return;
                }

                _lastFrameSeconds = _timer.Elapsed.TotalSeconds;
                CompositionTarget.Rendering += OnRendering;
                _isRendering = true;
            }

            private void OnRendering(object? sender, EventArgs e)
            {
                double now = _timer.Elapsed.TotalSeconds;
                double deltaTime = now - _lastFrameSeconds;
                _lastFrameSeconds = now;

                if (deltaTime <= 0 || deltaTime > 0.05)
                {
                    deltaTime = 0.016;
                }

                Update(deltaTime);
                InvalidateVisual();

                if (!HasActiveEffects)
                {
                    Stop();
                }
            }

            private bool HasActiveEffects =>
                _burstParticles.Count > 0 ||
                _pulseParticles.Count > 0 ||
                _energyArcs.Count > 0;

            private void Update(double deltaTime)
            {
                UpdatePulses(deltaTime);
                UpdateBurstParticles(deltaTime);
                UpdateEnergyArcs(deltaTime);
            }

            private void UpdatePulses(double deltaTime)
            {
                for (int i = _pulseParticles.Count - 1; i >= 0; i--)
                {
                    PulseParticle pulse = _pulseParticles[i];

                    pulse.Life -= deltaTime;

                    if (pulse.Life <= 0)
                    {
                        _pulseParticles.RemoveAt(i);
                        continue;
                    }

                    pulse.Radius += pulse.GrowthSpeed * deltaTime;
                }
            }

            private void UpdateBurstParticles(double deltaTime)
            {
                double damping = Math.Pow(0.06, deltaTime);

                for (int i = _burstParticles.Count - 1; i >= 0; i--)
                {
                    BurstParticle particle = _burstParticles[i];

                    particle.Life -= deltaTime;

                    if (particle.Life <= 0)
                    {
                        _burstParticles.RemoveAt(i);
                        continue;
                    }

                    particle.X += particle.VelocityX * deltaTime;
                    particle.Y += particle.VelocityY * deltaTime;

                    particle.VelocityX *= damping;
                    particle.VelocityY *= damping;

                    particle.Rotation += particle.RotationSpeed * deltaTime;
                }
            }

            private void UpdateEnergyArcs(double deltaTime)
            {
                for (int i = _energyArcs.Count - 1; i >= 0; i--)
                {
                    EnergyArc arc = _energyArcs[i];

                    arc.Life -= deltaTime;

                    if (arc.Life <= 0)
                    {
                        _energyArcs.RemoveAt(i);
                        continue;
                    }

                    arc.Radius += arc.ExpansionSpeed * deltaTime;
                    arc.Rotation += arc.RotationSpeed * deltaTime;
                }
            }

            private void DrawPulses(DrawingContext dc)
            {
                foreach (PulseParticle pulse in _pulseParticles)
                {
                    double lifeRatio = GetLifeRatio(pulse.Life, pulse.MaxLife);
                    double opacity = lifeRatio * 0.55;

                    dc.PushOpacity(opacity);
                    dc.DrawEllipse(
                        pulse.FillBrush,
                        pulse.StrokePen,
                        new Point(pulse.X, pulse.Y),
                        pulse.Radius,
                        pulse.Radius);
                    dc.Pop();
                }
            }

            private void DrawBurstParticles(DrawingContext dc)
            {
                foreach (BurstParticle particle in _burstParticles)
                {
                    double lifeRatio = GetLifeRatio(particle.Life, particle.MaxLife);
                    double size = particle.Size * (0.82 + lifeRatio * 0.24);

                    Brush? fill = particle.IsFilled ? particle.Style.Brush : null;
                    Pen stroke = particle.IsFilled
                        ? particle.Style.FilledPen
                        : particle.Style.OutlinePen;

                    dc.PushOpacity(lifeRatio);
                    dc.PushTransform(new RotateTransform(particle.Rotation, particle.X, particle.Y));

                    if (particle.IsTriangle)
                    {
                        dc.DrawGeometry(
                            fill,
                            stroke,
                            CreateTriangleGeometry(particle.X, particle.Y, size));
                    }
                    else
                    {
                        dc.DrawRectangle(
                            fill,
                            stroke,
                            new Rect(
                                particle.X - size / 2.0,
                                particle.Y - size / 2.0,
                                size,
                                size));
                    }

                    dc.Pop();
                    dc.Pop();
                }
            }

            private void DrawEnergyArcs(DrawingContext dc)
            {
                foreach (EnergyArc arc in _energyArcs)
                {
                    double lifeRatio = GetLifeRatio(arc.Life, arc.MaxLife);
                    double fullStartAngle = arc.StartAngle + arc.Rotation;
                    double segmentSweep = arc.SweepAngle / ArcSegmentCount;

                    for (int i = 0; i < ArcSegmentCount; i++)
                    {
                        double segmentMiddle = (i + 0.5) / ArcSegmentCount;
                        double taper = Math.Sin(segmentMiddle * Math.PI);
                        taper = Math.Pow(taper, 1.8);

                        double visibleSweep = segmentSweep * 0.84;
                        double segmentStartAngle = fullStartAngle + i * segmentSweep;

                        Geometry geometry = CreateArcGeometry(
                            arc.X,
                            arc.Y,
                            arc.Radius,
                            segmentStartAngle,
                            visibleSweep);

                        double mainThickness = arc.Thickness * (0.13 + taper * 0.87);
                        double bloomThickness = mainThickness * 3.0;

                        DrawArcLayer(
                            dc,
                            geometry,
                            arc.BloomBrush,
                            bloomThickness,
                            lifeRatio * taper * 0.22);

                        DrawArcLayer(
                            dc,
                            geometry,
                            arc.Brush,
                            mainThickness,
                            lifeRatio * (0.25 + taper * 0.75));
                    }
                }
            }

            private static void DrawArcLayer(
                DrawingContext dc,
                Geometry geometry,
                Brush brush,
                double thickness,
                double opacity)
            {
                if (opacity <= 0)
                {
                    return;
                }

                Pen pen = new(brush, thickness)
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round
                };

                dc.PushOpacity(opacity);
                dc.DrawGeometry(null, pen, geometry);
                dc.Pop();
            }

            private ParticleStyle PickParticleStyle(ThemePalette palette)
            {
                return palette.ParticleStyles[_random.Next(palette.ParticleStyles.Length)];
            }

            private static Geometry CreateTriangleGeometry(double x, double y, double size)
            {
                double half = size / 2.0;

                StreamGeometry geometry = new();

                using (StreamGeometryContext context = geometry.Open())
                {
                    context.BeginFigure(
                        new Point(x, y - half),
                        true,
                        true);

                    context.LineTo(
                        new Point(x + half, y + half),
                        true,
                        false);

                    context.LineTo(
                        new Point(x - half, y + half),
                        true,
                        false);
                }

                geometry.Freeze();
                return geometry;
            }

            private static Geometry CreateArcGeometry(
                double centerX,
                double centerY,
                double radius,
                double startAngleDegrees,
                double sweepAngleDegrees)
            {
                Point startPoint = PointOnCircle(centerX, centerY, radius, startAngleDegrees);
                Point endPoint = PointOnCircle(centerX, centerY, radius, startAngleDegrees + sweepAngleDegrees);

                StreamGeometry geometry = new();

                using (StreamGeometryContext context = geometry.Open())
                {
                    context.BeginFigure(startPoint, false, false);
                    context.ArcTo(
                        endPoint,
                        new Size(radius, radius),
                        0,
                        Math.Abs(sweepAngleDegrees) > 180,
                        sweepAngleDegrees >= 0
                            ? SweepDirection.Clockwise
                            : SweepDirection.Counterclockwise,
                        true,
                        false);
                }

                geometry.Freeze();
                return geometry;
            }

            private static Point PointOnCircle(
                double centerX,
                double centerY,
                double radius,
                double angleDegrees)
            {
                double radians = angleDegrees * Math.PI / 180.0;

                return new Point(
                    centerX + Math.Cos(radians) * radius,
                    centerY + Math.Sin(radians) * radius);
            }

            private static double GetLifeRatio(double life, double maxLife)
            {
                if (maxLife <= 0)
                {
                    return 0;
                }

                return Math.Clamp(life / maxLife, 0, 1);
            }

            private static ThemePalette CreateThemePalette(
                Color[] particleColours,
                Color pulseFill,
                Color pulseStroke,
                Color arcBloom,
                Color primaryArc,
                Color secondaryArc)
            {
                Brush pulseFillBrush = CreateBrush(pulseFill);
                Pen pulseStrokePen = CreatePen(pulseStroke, 1.0);
                Brush arcBloomBrush = CreateBrush(arcBloom);
                Brush primaryArcBrush = CreateBrush(primaryArc);
                Brush secondaryArcBrush = CreateBrush(secondaryArc);

                ParticleStyle[] particleStyles = new ParticleStyle[particleColours.Length];

                for (int i = 0; i < particleColours.Length; i++)
                {
                    particleStyles[i] = CreateParticleStyle(CreateBrush(particleColours[i]));
                }

                return new ThemePalette
                {
                    PulseFillBrush = pulseFillBrush,
                    PulseStrokePen = pulseStrokePen,
                    ArcBloomBrush = arcBloomBrush,
                    PrimaryArcBrush = primaryArcBrush,
                    SecondaryArcBrush = secondaryArcBrush,
                    ParticleStyles = particleStyles
                };
            }

            private static Brush CreateBrush(Color color)
            {
                SolidColorBrush brush = new(color);
                brush.Freeze();
                return brush;
            }

            private static Pen CreatePen(Color color, double thickness)
            {
                Pen pen = new(CreateBrush(color), thickness);
                pen.Freeze();
                return pen;
            }

            private static ParticleStyle CreateParticleStyle(Brush brush)
            {
                Pen outlinePen = new(brush, 1.35);
                outlinePen.Freeze();

                Pen filledPen = new(brush, 1.05);
                filledPen.Freeze();

                return new ParticleStyle
                {
                    Brush = brush,
                    OutlinePen = outlinePen,
                    FilledPen = filledPen
                };
            }

            private sealed class ThemePalette
            {
                public Brush PulseFillBrush { get; init; } = null!;
                public Pen PulseStrokePen { get; init; } = null!;
                public Brush ArcBloomBrush { get; init; } = null!;
                public Brush PrimaryArcBrush { get; init; } = null!;
                public Brush SecondaryArcBrush { get; init; } = null!;
                public ParticleStyle[] ParticleStyles { get; init; } = Array.Empty<ParticleStyle>();
            }

            private sealed class ParticleStyle
            {
                public Brush Brush { get; init; } = null!;
                public Pen OutlinePen { get; init; } = null!;
                public Pen FilledPen { get; init; } = null!;
            }

            private sealed class BurstParticle
            {
                public double X { get; set; }
                public double Y { get; set; }
                public double VelocityX { get; set; }
                public double VelocityY { get; set; }
                public double Size { get; set; }
                public double Rotation { get; set; }
                public double RotationSpeed { get; set; }
                public double Life { get; set; }
                public double MaxLife { get; set; }
                public bool IsTriangle { get; set; }
                public bool IsFilled { get; set; }
                public ParticleStyle Style { get; set; } = null!;
            }

            private sealed class PulseParticle
            {
                public double X { get; set; }
                public double Y { get; set; }
                public double Radius { get; set; }
                public double GrowthSpeed { get; set; }
                public double Life { get; set; }
                public double MaxLife { get; set; }
                public Brush FillBrush { get; set; } = null!;
                public Pen StrokePen { get; set; } = null!;
            }

            private sealed class EnergyArc
            {
                public double X { get; set; }
                public double Y { get; set; }
                public double Radius { get; set; }
                public double ExpansionSpeed { get; set; }
                public double StartAngle { get; set; }
                public double SweepAngle { get; set; }
                public double Rotation { get; set; }
                public double RotationSpeed { get; set; }
                public double Life { get; set; }
                public double MaxLife { get; set; }
                public double Thickness { get; set; }
                public Brush Brush { get; set; } = null!;
                public Brush BloomBrush { get; set; } = null!;
            }
        }

        private const int WhMouseLl = 14;
        private const int WmLeftButtonDown = 0x0201;
        private const int WmHotkey = 0x0312;

        private const int GwlExStyle = -20;
        private const int WsExTransparent = 0x00000020;
        private const int WsExLayered = 0x00080000;
        private const int WsExToolWindow = 0x00000080;

        private const uint ModAlt = 0x0001;
        private const uint ModControl = 0x0002;

        private const uint VirtualKeyQ = 0x51;

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SetWindowsHookEx(
            int idHook,
            LowLevelMouseProc lpfn,
            IntPtr hMod,
            uint dwThreadId);

        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(
            IntPtr hhk,
            int nCode,
            IntPtr wParam,
            IntPtr lParam);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(
            IntPtr hWnd,
            int id,
            uint fsModifiers,
            uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(
            IntPtr hWnd,
            int id);
    }
}