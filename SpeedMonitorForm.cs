using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Windows.Forms;

namespace SpeedMonitor;

public sealed class SpeedMonitorForm : Form
{
    private const int WindowWidth = 160;
    private const int WindowHeight = 70;

    private readonly Label _downloadLabel;
    private readonly Label _uploadLabel;
    private readonly Label _errorLabel;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly Stopwatch _stopwatch = new();
    private readonly ContextMenuStrip _contextMenu;
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
        TopMost = true;
        ShowInTaskbar = false;
        BackColor = Color.FromArgb(6, 10, 18);
        Opacity = 0.94d;
        DoubleBuffered = true;

        _downloadLabel = CreateSpeedLabel("↓ 0 KB/s", Color.FromArgb(49, 208, 255), 8);
        _uploadLabel = CreateSpeedLabel("↑ 0 KB/s", Color.FromArgb(255, 207, 74), 35);
        _errorLabel = CreateSpeedLabel("网速读取失败", Color.FromArgb(255, 107, 107), 23);
        _errorLabel.TextAlign = ContentAlignment.MiddleCenter;
        _errorLabel.Visible = false;

        Controls.Add(_downloadLabel);
        Controls.Add(_uploadLabel);
        Controls.Add(_errorLabel);

        _contextMenu = CreateContextMenu();
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
        InitializeCounters();
        _timer.Start();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _timer.Stop();
        SaveCurrentPosition();
        base.OnFormClosing(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Color.FromArgb(70, 255, 255, 255), 1);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }

    private static Label CreateSpeedLabel(string text, Color color, int top)
    {
        return new Label
        {
            AutoSize = false,
            BackColor = Color.Transparent,
            ForeColor = color,
            Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold, GraphicsUnit.Point),
            Location = new Point(14, top),
            Size = new Size(WindowWidth - 28, 25),
            Text = text,
            TextAlign = ContentAlignment.MiddleLeft,
            UseMnemonic = false
        };
    }

    private ContextMenuStrip CreateContextMenu()
    {
        var menu = new ContextMenuStrip
        {
            ShowImageMargin = false,
            BackColor = Color.White,
            ForeColor = Color.FromArgb(17, 24, 39),
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point)
        };

        var exitItem = new ToolStripMenuItem("退出");
        exitItem.Click += (_, _) => Close();
        menu.Items.Add(exitItem);

        return menu;
    }

    private static Icon LoadAppIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "assets", "fast.ico");
        return File.Exists(iconPath) ? new Icon(iconPath) : SystemIcons.Application;
    }

    private void RestorePosition()
    {
        var area = Screen.PrimaryScreen?.WorkingArea ?? Screen.FromControl(this).WorkingArea;
        var x = _config.X ?? area.Right - Width - 16;
        var y = _config.Y ?? area.Top + 16;

        x = Math.Clamp(x, area.Left, area.Right - Width);
        y = Math.Clamp(y, area.Top, area.Bottom - Height);
        Location = new Point(x, y);
    }

    private void SaveCurrentPosition()
    {
        _config.X = Location.X;
        _config.Y = Location.Y;
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
        SaveCurrentPosition();
    }

    private sealed class AppConfig
    {
        public int? X { get; set; }
        public int? Y { get; set; }

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
