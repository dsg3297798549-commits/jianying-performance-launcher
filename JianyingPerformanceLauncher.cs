using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("剪映性能启动器")]
[assembly: AssemblyDescription("为剪映绑定高性能 GPU、解除 EcoQoS 节流并持续优化子进程")]
[assembly: AssemblyProduct("剪映性能启动器")]
[assembly: AssemblyVersion("2.0.0.0")]
[assembly: AssemblyFileVersion("2.0.0.0")]

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }
}

internal sealed class MainForm : Form
{
    private const uint PROCESS_SET_INFORMATION = 0x0200;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint ABOVE_NORMAL_PRIORITY_CLASS = 0x00008000;
    private const int ProcessPowerThrottling = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct PowerState { public uint Version, ControlMask, StateMask; }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int processId);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetPriorityClass(IntPtr process, uint priorityClass);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetProcessInformation(IntPtr process, int infoClass, ref PowerState state, uint size);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);

    private readonly Color backgroundColor = Color.FromArgb(15, 19, 28);
    private readonly Color cardColor = Color.FromArgb(25, 31, 43);
    private readonly Color accentColor = Color.FromArgb(45, 214, 172);
    private readonly Color textColor = Color.FromArgb(239, 244, 250);
    private readonly Color mutedColor = Color.FromArgb(151, 162, 181);
    private readonly Label pathLabel;
    private readonly Label statusLabel;
    private readonly Button launchButton;
    private readonly Timer monitorTimer;
    private readonly HashSet<int> seenPids = new HashSet<int>();
    private string jianyingPath;
    private readonly string logPath;

    public MainForm()
    {
        Text = "剪映性能启动器";
        ClientSize = new Size(560, 520);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = backgroundColor;
        ForeColor = textColor;
        Font = new Font("Microsoft YaHei UI", 9F);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;

        string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JianyingOptimizer");
        Directory.CreateDirectory(logDir);
        logPath = Path.Combine(logDir, "launcher.log");

        Controls.Add(NewLabel("JY PERFORMANCE", 28, 24, 190, 24, 10F, accentColor, FontStyle.Bold));
        Controls.Add(NewLabel("剪映性能启动器", 28, 58, 500, 48, 26F, textColor, FontStyle.Bold));
        Controls.Add(NewLabel("启动前完成 GPU 绑定，运行中持续保持剪映性能。", 30, 108, 500, 28, 10.5F, mutedColor, FontStyle.Regular));

        Panel card = new Panel { Location = new Point(28, 153), Size = new Size(504, 182), BackColor = cardColor };
        card.Controls.Add(Feature("GPU", "高性能显卡绑定", "自动匹配当前剪映版本", 18));
        card.Controls.Add(Feature("CPU", "解除 EcoQoS 节流", "避免渲染进程被降速", 70));
        card.Controls.Add(Feature("LIVE", "持续进程优化", "自动接管后续新建的子进程", 122));
        Controls.Add(card);

        Controls.Add(NewLabel("剪映位置", 30, 353, 90, 22, 9F, mutedColor, FontStyle.Regular));
        pathLabel = NewLabel("正在检测……", 30, 377, 415, 24, 9F, textColor, FontStyle.Regular);
        pathLabel.AutoEllipsis = true;
        Controls.Add(pathLabel);

        Button browse = new Button { Text = "选择", Location = new Point(456, 370), Size = new Size(76, 32), FlatStyle = FlatStyle.Flat, BackColor = cardColor, ForeColor = textColor, Cursor = Cursors.Hand };
        browse.FlatAppearance.BorderColor = Color.FromArgb(55, 65, 81);
        browse.Click += BrowseClicked;
        Controls.Add(browse);

        launchButton = new Button { Text = "优化并启动剪映", Location = new Point(134, 421), Size = new Size(292, 54), FlatStyle = FlatStyle.Flat, BackColor = accentColor, ForeColor = Color.FromArgb(8, 27, 23), Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold), Cursor = Cursors.Hand };
        launchButton.FlatAppearance.BorderSize = 0;
        launchButton.Click += LaunchClicked;
        Controls.Add(launchButton);

        statusLabel = NewLabel("仅优化剪映 · 不影响其他应用 · 无需管理员权限", 30, 486, 500, 22, 8.5F, mutedColor, FontStyle.Regular);
        statusLabel.TextAlign = ContentAlignment.MiddleCenter;
        Controls.Add(statusLabel);

        monitorTimer = new Timer { Interval = 3000 };
        monitorTimer.Tick += MonitorTick;
        Shown += delegate { DetectJianying(); };
    }

    private Control Feature(string badge, string heading, string description, int y)
    {
        Panel row = new Panel { Location = new Point(16, y), Size = new Size(470, 46), BackColor = cardColor };
        Label mark = NewLabel(badge, 0, 5, 54, 27, 8F, accentColor, FontStyle.Bold);
        mark.BackColor = Color.FromArgb(17, 57, 52);
        mark.TextAlign = ContentAlignment.MiddleCenter;
        row.Controls.Add(mark);
        row.Controls.Add(NewLabel(heading, 70, 0, 180, 22, 10F, textColor, FontStyle.Bold));
        row.Controls.Add(NewLabel(description, 70, 22, 350, 20, 8.5F, mutedColor, FontStyle.Regular));
        return row;
    }

    private static Label NewLabel(string text, int x, int y, int width, int height, float size, Color color, FontStyle style)
    {
        return new Label { Text = text, Location = new Point(x, y), Size = new Size(width, height), Font = new Font("Microsoft YaHei UI", size, style), ForeColor = color, BackColor = Color.Transparent };
    }

    private void DetectJianying()
    {
        jianyingPath = JianyingFinder.Find();
        if (jianyingPath != null)
        {
            pathLabel.Text = jianyingPath;
            statusLabel.Text = "已找到剪映，准备就绪";
            launchButton.Enabled = true;
        }
        else
        {
            pathLabel.Text = "未自动找到，请点击“选择”定位 JianyingPro.exe";
            statusLabel.Text = "支持默认目录、注册表、自定义磁盘目录和手动选择";
            launchButton.Enabled = false;
        }
    }

    private void BrowseClicked(object sender, EventArgs e)
    {
        using (OpenFileDialog dialog = new OpenFileDialog())
        {
            dialog.Title = "选择剪映主程序 JianyingPro.exe";
            dialog.Filter = "剪映主程序 (JianyingPro.exe)|JianyingPro.exe|应用程序 (*.exe)|*.exe";
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            if (!string.Equals(Path.GetFileName(dialog.FileName), "JianyingPro.exe", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(this, "请选择 JianyingPro.exe。", "文件不正确", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            jianyingPath = dialog.FileName;
            JianyingFinder.Remember(jianyingPath);
            pathLabel.Text = jianyingPath;
            statusLabel.Text = "已记住自定义安装位置";
            launchButton.Enabled = true;
        }
    }

    private void LaunchClicked(object sender, EventArgs e)
    {
        try
        {
            if (jianyingPath == null || !File.Exists(jianyingPath))
            {
                DetectJianying();
                if (jianyingPath == null) return;
            }
            BindHighPerformanceGpu(jianyingPath);
            JianyingFinder.Remember(jianyingPath);
            Log("GPU high-performance preference set: " + jianyingPath);

            Process[] running = Process.GetProcessesByName("JianyingPro");
            if (running.Length == 0)
            {
                ProcessStartInfo info = new ProcessStartInfo(jianyingPath) { WorkingDirectory = Path.GetDirectoryName(jianyingPath), UseShellExecute = true };
                Process.Start(info);
                statusLabel.Text = "正在启动剪映并接管性能调度……";
            }
            else
            {
                foreach (Process process in running)
                {
                    Tune(process);
                    if (process.MainWindowHandle != IntPtr.Zero) { ShowWindow(process.MainWindowHandle, 9); SetForegroundWindow(process.MainWindowHandle); }
                    process.Dispose();
                }
                statusLabel.Text = "剪映已运行，当前进程已优化";
            }
            launchButton.Text = "性能监控运行中";
            launchButton.Enabled = false;
            monitorTimer.Start();
            MonitorTick(null, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Log("ERROR: " + ex);
            MessageBox.Show(this, ex.Message, "启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            launchButton.Enabled = true;
        }
    }

    private void MonitorTick(object sender, EventArgs e)
    {
        Process[] processes = Process.GetProcessesByName("JianyingPro");
        if (processes.Length == 0) { statusLabel.Text = "等待剪映进程启动……"; return; }
        foreach (Process process in processes) { Tune(process); process.Dispose(); }
        statusLabel.Text = string.Format("优化监控中 · 已接管 {0} 个剪映进程", processes.Length);
    }

    private void Tune(Process process)
    {
        IntPtr handle = OpenProcess(PROCESS_SET_INFORMATION | PROCESS_QUERY_LIMITED_INFORMATION, false, process.Id);
        if (handle == IntPtr.Zero) return;
        try
        {
            bool priority = SetPriorityClass(handle, ABOVE_NORMAL_PRIORITY_CLASS);
            PowerState state = new PowerState { Version = 1, ControlMask = 1, StateMask = 0 };
            bool throttle = SetProcessInformation(handle, ProcessPowerThrottling, ref state, (uint)Marshal.SizeOf(typeof(PowerState)));
            if (seenPids.Add(process.Id)) Log(string.Format("PID {0}: priority={1}, throttleOff={2}", process.Id, priority, throttle));
        }
        finally { CloseHandle(handle); }
    }

    private static void BindHighPerformanceGpu(string exe)
    {
        using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\DirectX\UserGpuPreferences")) key.SetValue(exe, "GpuPreference=2;", RegistryValueKind.String);
    }

    private void Log(string message)
    {
        try { File.AppendAllText(logPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + message + Environment.NewLine); } catch { }
    }
}

internal static class JianyingFinder
{
    private const string SettingsKey = @"Software\JianyingOptimizer";

    public static string Find()
    {
        foreach (Process process in Process.GetProcessesByName("JianyingPro"))
        {
            try { if (Valid(process.MainModule.FileName)) return process.MainModule.FileName; }
            catch { }
            finally { process.Dispose(); }
        }
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string fromVersions = FindLatestVersion(Path.Combine(local, "JianyingPro", "Apps"));
        if (Valid(fromVersions)) return fromVersions;
        string registry = FindFromRegistry();
        if (Valid(registry)) return registry;
        string remembered = ReadRemembered();
        if (Valid(remembered)) return remembered;
        string[] direct = {
            Path.Combine(local, "JianyingPro", "Apps", "JianyingPro.exe"),
            Path.Combine(local, "Programs", "JianyingPro", "JianyingPro.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "JianyingPro", "JianyingPro.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "JianyingPro", "JianyingPro.exe")
        };
        foreach (string item in direct) if (Valid(item)) return item;
        foreach (DriveInfo drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
        {
            string[] roots = { "JianyingPro", "剪映", "剪映专业版", "Program Files\\JianyingPro", "Apps\\JianyingPro" };
            foreach (string relative in roots)
            {
                string root = Path.Combine(drive.RootDirectory.FullName, relative);
                string directExe = Path.Combine(root, "JianyingPro.exe");
                if (Valid(directExe)) return directExe;
                string versionExe = FindLatestVersion(root);
                if (Valid(versionExe)) return versionExe;
            }
        }
        return null;
    }

    public static void Remember(string path)
    {
        using (RegistryKey key = Registry.CurrentUser.CreateSubKey(SettingsKey)) key.SetValue("JianyingPath", path, RegistryValueKind.String);
    }

    private static string ReadRemembered()
    {
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(SettingsKey)) return key == null ? null : key.GetValue("JianyingPath") as string;
    }

    private static string FindLatestVersion(string apps)
    {
        try
        {
            if (!Directory.Exists(apps)) return null;
            return Directory.GetDirectories(apps).Select(dir => new { Dir = dir, Version = ParseVersion(Path.GetFileName(dir)) })
                .Where(x => x.Version != null && Valid(Path.Combine(x.Dir, "JianyingPro.exe"))).OrderByDescending(x => x.Version)
                .Select(x => Path.Combine(x.Dir, "JianyingPro.exe")).FirstOrDefault();
        }
        catch { return null; }
    }

    private static Version ParseVersion(string value)
    {
        Version version;
        return Version.TryParse(value, out version) ? version : null;
    }

    private static string FindFromRegistry()
    {
        RegistryKey[] hives = { Registry.CurrentUser, Registry.LocalMachine };
        string[] locations = { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" };
        foreach (RegistryKey hive in hives)
        foreach (string location in locations)
        {
            try
            {
                using (RegistryKey root = hive.OpenSubKey(location))
                {
                    if (root == null) continue;
                    foreach (string name in root.GetSubKeyNames())
                    using (RegistryKey app = root.OpenSubKey(name))
                    {
                        string display = Convert.ToString(app.GetValue("DisplayName"));
                        if (display.IndexOf("Jianying", StringComparison.OrdinalIgnoreCase) < 0 && display.IndexOf("剪映", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        string icon = CleanPath(Convert.ToString(app.GetValue("DisplayIcon")));
                        if (Valid(icon)) return icon;
                        string folder = Convert.ToString(app.GetValue("InstallLocation"));
                        string exe = Path.Combine(folder ?? "", "JianyingPro.exe");
                        if (Valid(exe)) return exe;
                    }
                }
            }
            catch { }
        }
        return null;
    }

    private static string CleanPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim().Trim('"');
        int comma = value.LastIndexOf(',');
        int index;
        if (comma > 0 && int.TryParse(value.Substring(comma + 1).Trim(), out index)) value = value.Substring(0, comma).Trim().Trim('"');
        return value;
    }

    private static bool Valid(string path)
    {
        return !string.IsNullOrWhiteSpace(path) && File.Exists(path) && string.Equals(Path.GetFileName(path), "JianyingPro.exe", StringComparison.OrdinalIgnoreCase);
    }
}
