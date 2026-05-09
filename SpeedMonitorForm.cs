using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SpeedMonitor;

public sealed class SpeedMonitorForm : Form
{
    private const int WindowWidth = 160;
    private const int WindowHeight = 72;
    private const int CornerRadius = 12;
    private const string StartupValueName = "SpeedMonitor";
    private const string StartupRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly Label _downloadLabel;
    private readonly Label _uploadLabel;
    private readonly Label _errorLabel;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly Stopwatch _stopwatch = new();
    private readonly ContextMenuStrip _contextMenu;
    private readonly ToolStripMenuItem _pinItem;
    private readonly ToolStripMenuItem _startupItem;
    private readonly ToolStripMenuItem _themeItem;
    private readonly ToolStripMenuItem[] _opacityItems;
    private readonly string _configPath;

    private AppConfig _config;
    private Point _dragOffset;
    private bool _dragging;
    private long _lastReceived;
    private long _lastSent;

    public SpeedMonitorForm()
    {
        _configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
        _config = AppConfig.Load(_configPath);

        Text = "网速监控";
        Icon = LoadAppIcon();
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(WindowWidth, WindowHeight);
        MinimumSize = Size;
        MaximumSize = Size;
        TopMost = _config.TopMostEnabled ?? true;
        ShowInTaskbar = false;
        BackColor = GetTheme().WindowBackColor;
        Opacity = GetOpacity(_config.OpacityPercent);
        DoubleBuffered = true;

        _downloadLabel = CreateSpeedLabel("↓ 0 KB/s", Color.FromArgb(114, 222, 84), 12);
        _uploadLabel = CreateSpeedLabel("↑ 0 KB/s", Color.FromArgb(58, 160, 255), 36);
        _errorLabel = CreateSpeedLabel("读取中...", Color.FromArgb(255, 107, 107), 22);
        _errorLabel.TextAlign = ContentAlignment.MiddleCenter;
        _errorLabel.Visible = false;

        Controls.Add(_downloadLabel);
        Controls.Add(_uploadLabel);
        Controls.Add(_errorLabel);

        (_contextMenu, _pinItem, _startupItem, _themeItem, _opacityItems) = CreateContextMenu();
        ContextMenuStrip = _contextMenu;

        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += (_, _) => UpdateSpeed();

        MouseDown += OnDragMouseDown;
        MouseMove += OnDragMouseMove;
        MouseUp += OnDragMouseUp;
        foreach (Control control in Controls)
        {
            control.MouseDown += OnDragMouseDown;
            control.MouseMove += OnDragMouseMove;
            control.MouseUp += OnDragMouseUp;
        }

        RestorePosition();
        ApplyRoundedRegion();
        InitializeCounters();
        _timer.Start();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ApplyRoundedRegion();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        ApplyRoundedRegion();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _timer.Stop();
        SaveCurrentSettings();
        base.OnFormClosing(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = CreateRoundRectPath(new Rectangle(0, 0, Width - 1, Height - 1), CornerRadius);
        var theme = GetTheme();
        using var brush = new SolidBrush(theme.PanelColor);
        using var borderPen = new Pen(theme.BorderColor, 1);

        e.Graphics.FillPath(brush, path);
        e.Graphics.DrawPath(borderPen, path);
    }

    private static Label CreateSpeedLabel(string text, Color color, int top)
    {
        return new Label
        {
            AutoSize = false,
            BackColor = Color.Transparent,
            ForeColor = color,
            Font = new Font("Segoe UI Semibold", 13F, FontStyle.Bold, GraphicsUnit.Point),
            Location = new Point(16, top),
            Size = new Size(WindowWidth - 32, 24),
            Text = text,
            TextAlign = ContentAlignment.MiddleLeft,
            UseMnemonic = false
        };
    }

    private (ContextMenuStrip Menu, ToolStripMenuItem PinItem, ToolStripMenuItem StartupItem, ToolStripMenuItem ThemeItem, ToolStripMenuItem[] OpacityItems) CreateContextMenu()
    {
        var theme = GetTheme();
        var menu = new ContextMenuStrip
        {
            ShowCheckMargin = false,
            ShowImageMargin = false,
            BackColor = theme.MenuBackColor,
            ForeColor = theme.MenuForeColor,
            Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Regular, GraphicsUnit.Point),
            Renderer = new ThemedMenuRenderer(() => GetTheme()),
            Padding = new Padding(4)
        };

        var pinItem = new ToolStripMenuItem("📌  置顶");
        pinItem.Click += (_, _) =>
        {
            TopMost = !TopMost;
            _config.TopMostEnabled = TopMost;
            UpdateMenuState();
            SaveCurrentSettings();
        };

        var opacityItem = new ToolStripMenuItem("🖼  透明度");
        var transparencyValues = new[] { 0, 20, 40, 60, 80 };
        var opacityItems = transparencyValues
            .Select(transparency =>
            {
                var label = transparency == 20 ? "20% (默认)" : $"{transparency}%";
                var item = new ToolStripMenuItem(label)
                {
                    Tag = transparency
                };
                item.Click += (_, _) => SetOpacity(transparency);
                opacityItem.DropDownItems.Add(item);
                return item;
            })
            .ToArray();

        var startupItem = new ToolStripMenuItem("🚀  开机自启");
        startupItem.Click += (_, _) =>
        {
            SetStartupEnabled(!IsStartupEnabled());
            UpdateMenuState();
        };

        var themeItem = new ToolStripMenuItem("🌓  主题切换");
        themeItem.Click += (_, _) =>
        {
            _config.Theme = IsLightTheme() ? ThemeMode.Dark : ThemeMode.Light;
            ApplyTheme();
            SaveCurrentSettings();
        };

        var aboutItem = new ToolStripMenuItem("ⓘ  关于");
        aboutItem.Click += (_, _) => MessageBox.Show(
            "网速监控\n\n实时显示上传/下载速度，支持拖动位置、透明度调整、置顶显示和开机自启。",
            "关于",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);

        var exitItem = new ToolStripMenuItem("❌  退出");
        exitItem.Click += (_, _) => Close();

        menu.Items.Add(pinItem);
        menu.Items.Add(opacityItem);
        menu.Items.Add(startupItem);
        menu.Items.Add(themeItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(aboutItem);
        menu.Items.Add(exitItem);

        menu.Opening += (_, _) =>
        {
            ApplyTheme();
            UpdateMenuState();
        };

        foreach (ToolStripMenuItem item in opacityItem.DropDownItems)
        {
            item.BackColor = theme.MenuBackColor;
            item.ForeColor = theme.MenuForeColor;
        }

        UpdateMenuState(pinItem, startupItem, themeItem, opacityItems);

        return (menu, pinItem, startupItem, themeItem, opacityItems);
    }

    private static Icon LoadAppIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "assets", "fast.ico");
        return File.Exists(iconPath) ? new Icon(iconPath) : SystemIcons.Application;
    }

    private void RestorePosition()
    {
        var area = Screen.PrimaryScreen?.WorkingArea ?? Screen.FromControl(this).WorkingArea;
        var x = _config.X ?? area.Right - Width - 28;
        var y = _config.Y ?? area.Top + 28;

        x = Math.Clamp(x, area.Left, area.Right - Width);
        y = Math.Clamp(y, area.Top, area.Bottom - Height);
        Location = new Point(x, y);
    }

    private void SaveCurrentSettings()
    {
        _config.X = Location.X;
        _config.Y = Location.Y;
        _config.TopMostEnabled = TopMost;
        _config.OpacityPercent = GetCurrentOpacityPercent();
        _config.Save(_configPath);
    }

    private void InitializeCounters()
    {
        try
        {
            var snapshot = ReadNetworkBytes();
            _lastReceived = snapshot.Received;
            _lastSent = snapshot.Sent;
            _stopwatch.Restart();
            ShowSpeedLabels();
        }
        catch
        {
            ShowError();
        }
    }

    private void UpdateSpeed()
    {
        try
        {
            var elapsedSeconds = Math.Max(_stopwatch.Elapsed.TotalSeconds, 0.001d);
            var snapshot = ReadNetworkBytes();

            var downloadSpeed = (snapshot.Received - _lastReceived) / elapsedSeconds;
            var uploadSpeed = (snapshot.Sent - _lastSent) / elapsedSeconds;

            _lastReceived = snapshot.Received;
            _lastSent = snapshot.Sent;
            _stopwatch.Restart();

            _downloadLabel.Text = $"↓ {FormatSpeed(downloadSpeed)}";
            _uploadLabel.Text = $"↑ {FormatSpeed(uploadSpeed)}";
            ShowSpeedLabels();
        }
        catch
        {
            ShowError();
        }
    }

    private static (long Received, long Sent) ReadNetworkBytes()
    {
        long received = 0;
        long sent = 0;

        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up)
            {
                continue;
            }

            if (networkInterface.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            var stats = networkInterface.GetIPv4Statistics();
            received += stats.BytesReceived;
            sent += stats.BytesSent;
        }

        return (received, sent);
    }

    private static string FormatSpeed(double bytesPerSecond)
    {
        var kbPerSecond = Math.Max(bytesPerSecond, 0d) / 1024d;
        return kbPerSecond < 1024d
            ? $"{kbPerSecond:0} KB/s"
            : $"{kbPerSecond / 1024d:0.00} MB/s";
    }

    private void ShowSpeedLabels()
    {
        _errorLabel.Visible = false;
        _downloadLabel.Visible = true;
        _uploadLabel.Visible = true;
    }

    private void ShowError()
    {
        _downloadLabel.Visible = false;
        _uploadLabel.Visible = false;
        _errorLabel.Visible = true;
    }

    private void SetOpacity(int transparency)
    {
        _config.OpacityPercent = transparency;
        Opacity = GetOpacity(transparency);
        UpdateMenuState();
        SaveCurrentSettings();
    }

    private void ApplyTheme()
    {
        var theme = GetTheme();
        BackColor = theme.WindowBackColor;
        _contextMenu.BackColor = theme.MenuBackColor;
        _contextMenu.ForeColor = theme.MenuForeColor;

        foreach (ToolStripItem item in _contextMenu.Items)
        {
            if (item is ToolStripMenuItem menuItem)
            {
                ApplyMenuItemTheme(menuItem, theme);
            }
        }

        UpdateMenuState();
        Invalidate();
    }

    private static void ApplyMenuItemTheme(ToolStripMenuItem item, ThemePalette theme)
    {
        item.BackColor = theme.MenuBackColor;
        item.ForeColor = theme.MenuForeColor;
        foreach (ToolStripItem child in item.DropDownItems)
        {
            if (child is ToolStripMenuItem childMenuItem)
            {
                ApplyMenuItemTheme(childMenuItem, theme);
            }
        }
    }

    private void UpdateMenuState()
    {
        UpdateMenuState(_pinItem, _startupItem, _themeItem, _opacityItems);
    }

    private void UpdateMenuState(
        ToolStripMenuItem pinItem,
        ToolStripMenuItem startupItem,
        ToolStripMenuItem themeItem,
        ToolStripMenuItem[] opacityItems)
    {
        pinItem.Text = $"{(TopMost ? "✓ " : "    ")}📌  置顶";
        startupItem.Text = $"{(IsStartupEnabled() ? "✓ " : "    ")}🚀  开机自启";
        themeItem.Text = IsLightTheme() ? "    ☀️  亮色模式" : "    🌙  暗色模式";

        var currentTransparency = (int)Math.Round((1d - Opacity) * 100d);
        foreach (var item in opacityItems)
        {
            if (item.Tag is not int transparency)
            {
                continue;
            }

            var label = transparency == 20 ? "20% (默认)" : $"{transparency}%";
            item.Text = $"{(transparency == currentTransparency ? "✓ " : "    ")}{label}";
        }
    }

    private int GetCurrentOpacityPercent()
    {
        return (int)Math.Round((1d - Opacity) * 100d);
    }

    private static double GetOpacity(int? transparency)
    {
        var transparencyPercent = Math.Clamp(transparency ?? 20, 0, 80);
        return (100d - transparencyPercent) / 100d;
    }

    private bool IsLightTheme()
    {
        return string.Equals(_config.Theme, ThemeMode.Light, StringComparison.OrdinalIgnoreCase);
    }

    private ThemePalette GetTheme()
    {
        return IsLightTheme()
            ? new ThemePalette(
                Color.FromArgb(252, 252, 253),
                Color.FromArgb(248, 250, 252),
                Color.FromArgb(226, 232, 240),
                Color.FromArgb(255, 255, 255),
                Color.FromArgb(15, 23, 42),
                Color.FromArgb(226, 232, 240),
                Color.FromArgb(241, 245, 249))
            : new ThemePalette(
                Color.FromArgb(13, 17, 23),
                Color.FromArgb(22, 27, 34),
                Color.FromArgb(48, 54, 61),
                Color.FromArgb(22, 27, 34),
                Color.FromArgb(201, 209, 217),
                Color.FromArgb(48, 54, 61),
                Color.FromArgb(33, 38, 45));
    }

    private static bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryPath, false);
        var value = key?.GetValue(StartupValueName) as string;
        var exePath = Application.ExecutablePath;
        return !string.IsNullOrWhiteSpace(value)
            && value.Trim('"').Equals(exePath, StringComparison.OrdinalIgnoreCase);
    }

    private static void SetStartupEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(StartupRegistryPath, true);
        if (key is null)
        {
            return;
        }

        if (enabled)
        {
            key.SetValue(StartupValueName, $"\"{Application.ExecutablePath}\"");
        }
        else
        {
            key.DeleteValue(StartupValueName, false);
        }
    }

    private void ApplyRoundedRegion()
    {
        Region?.Dispose();
        using var path = CreateRoundRectPath(new Rectangle(0, 0, Width, Height), CornerRadius);
        Region = new Region(path);
    }

    private static GraphicsPath CreateRoundRectPath(Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();

        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private void OnDragMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        _dragging = true;
        _dragOffset = PointToClient(Cursor.Position);
    }

    private void OnDragMouseMove(object? sender, MouseEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        var cursor = Cursor.Position;
        Location = new Point(cursor.X - _dragOffset.X, cursor.Y - _dragOffset.Y);
    }

    private void OnDragMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        _dragging = false;
        SaveCurrentSettings();
    }

    private sealed class ThemedMenuRenderer : ToolStripProfessionalRenderer
    {
        private readonly Func<ThemePalette> _getTheme;

        public ThemedMenuRenderer(Func<ThemePalette> getTheme)
        {
            _getTheme = getTheme;
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using var brush = new SolidBrush(_getTheme().MenuBackColor);
            e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
        {
            // Do not render the image margin (white strip)
            using var brush = new SolidBrush(_getTheme().MenuBackColor);
            e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using var pen = new Pen(_getTheme().MenuBorderColor);
            e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected)
            {
                return;
            }

            var rect = new Rectangle(4, 2, e.Item.Width - 8, e.Item.Height - 4);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = CreateRoundRectPath(rect, 4);
            using var brush = new SolidBrush(_getTheme().MenuSelectedColor);
            e.Graphics.FillPath(brush, path);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using var pen = new Pen(_getTheme().MenuBorderColor);
            e.Graphics.DrawLine(pen, 10, e.Item.Height / 2, e.Item.Width - 10, e.Item.Height / 2);
        }
    }

    private static class ThemeMode
    {
        public const string Dark = "Dark";
        public const string Light = "Light";
    }

    private readonly record struct ThemePalette(
        Color WindowBackColor,
        Color PanelColor,
        Color BorderColor,
        Color MenuBackColor,
        Color MenuForeColor,
        Color MenuBorderColor,
        Color MenuSelectedColor);

    private sealed class AppConfig
    {
        public int? X { get; set; }
        public int? Y { get; set; }
        public int? OpacityPercent { get; set; }
        public bool? TopMostEnabled { get; set; }
        public string? Theme { get; set; }

        public static AppConfig Load(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return new AppConfig();
                }

                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<AppConfig>(
                    json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new AppConfig();
            }
            catch
            {
                return new AppConfig();
            }
        }

        public void Save(string path)
        {
            try
            {
                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
            }
            catch
            {
                // Configuration persistence should not interrupt the monitor.
            }
        }
    }
}
