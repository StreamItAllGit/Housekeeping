using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Housekeeping.Installer;

public sealed class InstallerWizardForm : Form
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;
    private const int CollapsedClientHeight = 434;
    private const int ExpandedClientHeight = 544;
    private const int DefaultClientWidth = 650;

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private readonly EmbeddedMetadata _meta;
    private readonly string? _dataDir;
    private readonly string? _logDir;
    private readonly bool _testMode;
    private readonly bool _initialUninstallMode;

    private readonly Panel _headerPanel;
    private readonly Panel _bodyPanel;
    private readonly Panel _footerPanel;
    private readonly Panel _optionsPanel;
    private readonly Label _lblTitle;
    private readonly Label _lblSub;
    private readonly Label _lblDirHeader;
    private readonly TextBox _txtInstallDir;
    private readonly Button _btnBrowse;
    private readonly Panel _existingActionRow;
    private readonly RadioButton _radUpgrade;
    private readonly RadioButton _radRepair;
    private readonly RadioButton _radUninstall;
    private readonly CheckBox _chkShortcuts;
    private readonly CheckBox _chkAutoStart;
    private readonly CheckBox _chkLaunchAfter;
    private readonly CheckBox _chkPurgeData;
    private readonly Label _lblModeBadge;
    private readonly Label _lblStatus;
    private readonly ProgressBar _progressBar;
    private readonly Button _btnToggleDetails;
    private readonly TextBox _txtLog;
    private readonly Button _btnPrimary;
    private readonly Button _btnCancel;

    private readonly bool _controlsInitialized;
    private bool _completedSuccessfully;
    private bool _launchedAfterSetup;
    private bool _isDetailsExpanded;
    private string _completedInstallDir = string.Empty;

    private static readonly Color BgPrimary = Color.FromArgb(14, 17, 23);
    private static readonly Color BgCard = Color.FromArgb(22, 27, 36);
    private static readonly Color BgInput = Color.FromArgb(17, 21, 29);
    private static readonly Color BorderSubtle = Color.FromArgb(42, 51, 68);
    private static readonly Color TextPrimary = Color.FromArgb(236, 240, 248);
    private static readonly Color TextMuted = Color.FromArgb(148, 163, 184);
    private static readonly Color AccentAmber = Color.FromArgb(245, 158, 11);
    private static readonly Color AccentEmerald = Color.FromArgb(16, 185, 129);
    private static readonly Color AccentDanger = Color.FromArgb(239, 68, 68);

    public int ExitCode { get; private set; } = 1;

    public InstallerWizardForm(
        EmbeddedMetadata meta,
        string? initialInstallDir,
        string? dataDir = null,
        string? logDir = null,
        bool testMode = false,
        bool isUninstallMode = false,
        bool initialCreateShortcuts = true)
    {
        _meta = meta ?? throw new ArgumentNullException(nameof(meta));
        _dataDir = dataDir;
        _logDir = logDir;
        _testMode = testMode;
        _initialUninstallMode = isUninstallMode;

        Text = isUninstallMode
            ? $"Housekeeping Maintenance Suite — Uninstall (v{meta.Version})"
            : $"Housekeeping Maintenance Suite — Setup (v{meta.Version})";
        ClientSize = new Size(DefaultClientWidth, CollapsedClientHeight);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = BgPrimary;
        ForeColor = TextPrimary;
        Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;

        _headerPanel = new Panel
        {
            Dock = DockStyle.Top,
            Size = new Size(DefaultClientWidth, 76),
            BackColor = BgCard,
            Padding = new Padding(22, 14, 22, 12)
        };

        _lblTitle = new Label
        {
            Text = isUninstallMode
                ? "Housekeeping Maintenance Suite — Remove Installation"
                : "Housekeeping Maintenance Suite — Setup Wizard",
            Font = new Font("Segoe UI Semibold", 13.0f, FontStyle.Bold),
            ForeColor = TextPrimary,
            AutoSize = true,
            Location = new Point(22, 13)
        };

        _lblSub = new Label
        {
            Text = $"Version {meta.Version}  •  Clean Release Root (Housekeeping.exe + app\\, scripts\\, data\\, logs\\)",
            Font = new Font("Segoe UI", 8.75f, FontStyle.Regular),
            ForeColor = TextMuted,
            AutoSize = true,
            Location = new Point(22, 43)
        };

        _lblModeBadge = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 8.5f, FontStyle.Bold),
            ForeColor = AccentAmber,
            Location = new Point(480, 16)
        };

        _headerPanel.Controls.Add(_lblTitle);
        _headerPanel.Controls.Add(_lblSub);
        _headerPanel.Controls.Add(_lblModeBadge);

        _bodyPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = BgPrimary
        };

        _lblDirHeader = new Label
        {
            Text = "Target Installation Directory:",
            Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
            ForeColor = TextPrimary,
            AutoSize = true,
            Location = new Point(22, 88)
        };

        _txtInstallDir = new TextBox
        {
            Text = initialInstallDir,
            Location = new Point(22, 112),
            Width = 502,
            Height = 29,
            BackColor = BgInput,
            ForeColor = TextPrimary,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 9.5f, FontStyle.Regular)
        };
        _txtInstallDir.TextChanged += (_, _) => UpdateDetectionState();

        _btnBrowse = new Button
        {
            Text = "Browse...",
            Location = new Point(532, 110),
            Size = new Size(96, 29),
            FlatStyle = FlatStyle.Flat,
            BackColor = BgCard,
            ForeColor = TextPrimary,
            Cursor = Cursors.Hand
        };
        _btnBrowse.FlatAppearance.BorderColor = BorderSubtle;
        _btnBrowse.Click += OnBrowseClicked;

        _optionsPanel = new Panel
        {
            Location = new Point(22, 150),
            Size = new Size(606, 136),
            BackColor = BgCard
        };

        _existingActionRow = new Panel
        {
            Location = new Point(14, 8),
            Size = new Size(578, 30),
            BackColor = Color.Transparent
        };
        _radUpgrade = new RadioButton
        {
            Text = "Upgrade Existing Install",
            Checked = !isUninstallMode,
            AutoSize = true,
            ForeColor = AccentEmerald,
            Font = new Font("Segoe UI Semibold", 9.0f, FontStyle.Bold),
            Location = new Point(0, 4)
        };
        _radRepair = new RadioButton
        {
            Text = "Repair Files",
            Checked = false,
            AutoSize = true,
            ForeColor = TextPrimary,
            Location = new Point(190, 4)
        };
        _radUninstall = new RadioButton
        {
            Text = "Uninstall",
            Checked = isUninstallMode,
            AutoSize = true,
            ForeColor = AccentDanger,
            Location = new Point(310, 4)
        };
        _radUpgrade.CheckedChanged += (_, _) => SyncActionModeUI();
        _radRepair.CheckedChanged += (_, _) => SyncActionModeUI();
        _radUninstall.CheckedChanged += (_, _) => SyncActionModeUI();
        _existingActionRow.Controls.Add(_radUpgrade);
        _existingActionRow.Controls.Add(_radRepair);
        _existingActionRow.Controls.Add(_radUninstall);

        _chkShortcuts = new CheckBox
        {
            Text = "Create Start Menu shortcut (Housekeeping Maintenance Suite)",
            Checked = initialCreateShortcuts,
            AutoSize = true,
            ForeColor = TextPrimary,
            Location = new Point(16, 42)
        };

        _chkAutoStart = new CheckBox
        {
            Text = "Start Housekeeping tray daemon automatically with Windows",
            Checked = false,
            AutoSize = true,
            ForeColor = TextPrimary,
            Location = new Point(16, 68)
        };

        _chkLaunchAfter = new CheckBox
        {
            Text = "Launch Housekeeping after setup completes",
            Checked = true,
            AutoSize = true,
            ForeColor = TextPrimary,
            Location = new Point(16, 94)
        };

        _chkPurgeData = new CheckBox
        {
            Text = "Also delete user data, settings.json, logs\\, and backups\\ (Purge all data)",
            Checked = false,
            AutoSize = true,
            ForeColor = AccentDanger,
            Font = new Font("Segoe UI Semibold", 9.0f, FontStyle.Bold),
            Location = new Point(16, 68),
            Visible = isUninstallMode
        };

        _optionsPanel.Controls.Add(_existingActionRow);
        _optionsPanel.Controls.Add(_chkShortcuts);
        _optionsPanel.Controls.Add(_chkAutoStart);
        _optionsPanel.Controls.Add(_chkLaunchAfter);
        _optionsPanel.Controls.Add(_chkPurgeData);

        _lblStatus = new Label
        {
            Text = "Ready to install or upgrade Housekeeping Maintenance Suite.",
            AutoSize = false,
            Location = new Point(22, 294),
            Size = new Size(606, 22),
            ForeColor = TextMuted
        };

        _progressBar = new ProgressBar
        {
            Location = new Point(22, 318),
            Size = new Size(606, 18),
            Style = ProgressBarStyle.Blocks,
            Minimum = 0,
            Maximum = 100,
            Value = 0
        };

        _btnToggleDetails = new Button
        {
            Text = "[>] Show Details",
            Location = new Point(22, 342),
            Size = new Size(138, 26),
            FlatStyle = FlatStyle.Flat,
            BackColor = BgPrimary,
            ForeColor = AccentAmber,
            Font = new Font("Segoe UI Semibold", 8.5f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            Cursor = Cursors.Hand
        };
        _btnToggleDetails.FlatAppearance.BorderSize = 0;
        _btnToggleDetails.Click += (_, _) => SetDetailsExpanded(!_isDetailsExpanded);

        _txtLog = new TextBox
        {
            Location = new Point(22, 372),
            Size = new Size(606, 102),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = BgInput,
            ForeColor = TextMuted,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 8.5f, FontStyle.Regular),
            Visible = false
        };

        _footerPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Size = new Size(DefaultClientWidth, 56),
            BackColor = BgCard,
            Padding = new Padding(22, 10, 22, 10)
        };

        _btnCancel = new Button
        {
            Text = "Cancel",
            Size = new Size(104, 34),
            Location = new Point(376, 11),
            FlatStyle = FlatStyle.Flat,
            BackColor = BgPrimary,
            ForeColor = TextPrimary,
            Cursor = Cursors.Hand
        };
        _btnCancel.FlatAppearance.BorderColor = BorderSubtle;
        _btnCancel.Click += (_, _) => { ExitCode = 1; Close(); };

        _btnPrimary = new Button
        {
            Text = isUninstallMode ? "Uninstall" : "Install Now",
            Size = new Size(138, 34),
            Location = new Point(490, 11),
            FlatStyle = FlatStyle.Flat,
            BackColor = AccentAmber,
            ForeColor = Color.Black,
            Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        _btnPrimary.FlatAppearance.BorderSize = 0;
        _btnPrimary.Click += async (_, _) => await ExecuteSetupActionAsync();

        _footerPanel.Controls.Add(_btnCancel);
        _footerPanel.Controls.Add(_btnPrimary);

        Controls.Add(_bodyPanel);
        Controls.Add(_lblDirHeader);
        Controls.Add(_txtInstallDir);
        Controls.Add(_btnBrowse);
        Controls.Add(_optionsPanel);
        Controls.Add(_lblStatus);
        Controls.Add(_progressBar);
        Controls.Add(_btnToggleDetails);
        Controls.Add(_txtLog);
        Controls.Add(_footerPanel);
        Controls.Add(_headerPanel);

        _lblDirHeader.BringToFront();
        _txtInstallDir.BringToFront();
        _btnBrowse.BringToFront();
        _optionsPanel.BringToFront();
        _lblStatus.BringToFront();
        _progressBar.BringToFront();
        _btnToggleDetails.BringToFront();
        _txtLog.BringToFront();

        AcceptButton = _btnPrimary;
        CancelButton = _btnCancel;
        _controlsInitialized = true;
        UpdateDetectionState();
        AppendLog($"Housekeeping Setup Wizard v{meta.Version} ready.");
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (OperatingSystem.IsWindows())
        {
            try
            {
                int useDark = 1;
                if (DwmSetWindowAttribute(Handle, DwmwaUseImmersiveDarkMode, ref useDark, sizeof(int)) != 0)
                {
                    DwmSetWindowAttribute(Handle, DwmwaUseImmersiveDarkModeBefore20H1, ref useDark, sizeof(int));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"DWM dark mode attribute warning: {ex.Message}");
            }
        }
    }

    public void SetDetailsExpanded(bool expanded)
    {
        _isDetailsExpanded = expanded;
        _txtLog.Visible = expanded;
        _btnToggleDetails.Text = expanded ? "[v] Hide Details" : "[>] Show Details";
        ClientSize = new Size(DefaultClientWidth, expanded ? ExpandedClientHeight : CollapsedClientHeight);
    }

    private void UpdateDetectionState()
    {
        if (!_controlsInitialized) return;
        string candidate = _txtInstallDir.Text.Trim();
        bool existing = Program.IsExistingInstallation(candidate, out string? prevVer);
        _existingActionRow.Visible = existing || _initialUninstallMode;
        if (!existing && !_initialUninstallMode)
        {
            _radUpgrade.Checked = true;
        }
        SyncActionModeUI(existing, prevVer);
    }

    private void SyncActionModeUI(bool? existingOverride = null, string? prevVer = null)
    {
        bool existing = existingOverride ?? Program.IsExistingInstallation(_txtInstallDir.Text.Trim(), out prevVer);
        bool isUninst = _initialUninstallMode || (existing && _radUninstall.Checked);
        _chkShortcuts.Visible = !isUninst;
        _chkAutoStart.Visible = !isUninst;
        _chkLaunchAfter.Visible = !isUninst;
        _chkPurgeData.Visible = isUninst;

        if (_completedSuccessfully) return;
        if (isUninst)
        {
            _lblModeBadge.Text = "UNINSTALL MODE";
            _lblModeBadge.ForeColor = AccentDanger;
            _btnPrimary.Text = "Uninstall";
            _btnPrimary.BackColor = AccentDanger;
            _btnPrimary.ForeColor = Color.White;
        }
        else if (existing && _radRepair.Checked)
        {
            _lblModeBadge.Text = "REPAIR INSTALL";
            _lblModeBadge.ForeColor = AccentAmber;
            _btnPrimary.Text = "Repair Now";
            _btnPrimary.BackColor = AccentAmber;
            _btnPrimary.ForeColor = Color.Black;
        }
        else if (existing)
        {
            _lblModeBadge.Text = $"EXISTING INSTALL (v{prevVer ?? "detected"})";
            _lblModeBadge.ForeColor = AccentEmerald;
            _btnPrimary.Text = "Upgrade Now";
            _btnPrimary.BackColor = AccentEmerald;
            _btnPrimary.ForeColor = Color.Black;
        }
        else
        {
            _lblModeBadge.Text = "FRESH INSTALL";
            _lblModeBadge.ForeColor = AccentAmber;
            _btnPrimary.Text = "Install Now";
            _btnPrimary.BackColor = AccentAmber;
            _btnPrimary.ForeColor = Color.Black;
        }
        _lblModeBadge.Location = new Point(Math.Max(260, DefaultClientWidth - 22 - _lblModeBadge.PreferredWidth), 16);
    }

    private void OnBrowseClicked(object? sender, EventArgs e)
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "Select Housekeeping installation directory",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(_txtInstallDir.Text) ? _txtInstallDir.Text : ""
        };
        if (dlg.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(dlg.SelectedPath))
        {
            _txtInstallDir.Text = dlg.SelectedPath;
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_completedSuccessfully && !_launchedAfterSetup)
        {
            bool isUninst = _initialUninstallMode || _radUninstall.Checked;
            if (!isUninst && _chkLaunchAfter.Checked && !_testMode)
            {
                _launchedAfterSetup = true;
                Program.TryLaunchInstalledHousekeeping(_completedInstallDir, _logDir);
            }
        }
        base.OnFormClosing(e);
    }

    private async Task ExecuteSetupActionAsync()
    {
        if (_completedSuccessfully)
        {
            bool isUninst = _initialUninstallMode || _radUninstall.Checked;
            if (!isUninst && _chkLaunchAfter.Checked && !_testMode && !_launchedAfterSetup)
            {
                _launchedAfterSetup = true;
                Program.TryLaunchInstalledHousekeeping(_completedInstallDir, _logDir);
            }
            ExitCode = 0;
            Close();
            return;
        }

        string targetDir = Path.GetFullPath(_txtInstallDir.Text.Trim());
        bool isUninstall = _initialUninstallMode || _radUninstall.Checked;
        bool createShortcuts = _chkShortcuts.Checked;
        bool purgeData = _chkPurgeData.Checked;

        _txtInstallDir.Enabled = false;
        _btnBrowse.Enabled = false;
        _optionsPanel.Enabled = false;
        _btnPrimary.Enabled = false;
        _progressBar.Style = ProgressBarStyle.Marquee;
        _lblStatus.Text = isUninstall ? "Stopping running processes & uninstalling Housekeeping..." : "Stopping running processes, backing up state & extracting payload...";
        AppendLog(isUninstall ? $"Starting uninstall from: {targetDir}" : $"Starting installation to: {targetDir}");

        int code = await Task.Run(() =>
        {
            try
            {
                if (isUninstall)
                {
                    return Program.RunUninstall(targetDir, _dataDir, _logDir, _testMode, dryRun: false, purgeData, _meta);
                }
                else
                {
                    return Program.RunInstall(
                        targetDir,
                        _dataDir,
                        _logDir,
                        _testMode,
                        dryRun: false,
                        createShortcuts,
                        _meta,
                        autoRelaunchIfRunning: false,
                        forceLaunch: false);
                }
            }
            catch (Exception ex)
            {
                AppendLog($"[FATAL] Action failed: {ex.Message}");
                return 1;
            }
        });

        _progressBar.Style = ProgressBarStyle.Blocks;
        if (code == 0)
        {
            _completedSuccessfully = true;
            _completedInstallDir = targetDir;
            _progressBar.Value = 100;
            ExitCode = 0;
            if (isUninstall)
            {
                _lblStatus.Text = "Housekeeping was removed successfully.";
                _btnPrimary.Text = "Close";
                _btnPrimary.BackColor = BgCard;
                _btnPrimary.ForeColor = TextPrimary;
                _btnPrimary.Enabled = true;
                _btnCancel.Visible = false;
                AppendLog("Uninstall transaction finished successfully.");
            }
            else
            {
                _lblStatus.Text = "Setup completed successfully! Housekeeping is ready.";
                _btnPrimary.Text = _chkLaunchAfter.Checked ? "Finish & Launch" : "Finish";
                _btnPrimary.BackColor = AccentEmerald;
                _btnPrimary.ForeColor = Color.Black;
                _btnPrimary.Enabled = true;
                _btnCancel.Visible = false;
                AppendLog($"Installation verified successfully at {targetDir}.");
            }
        }
        else
        {
            _progressBar.Value = 0;
            _lblStatus.Text = "Setup encountered an error. Check details below.";
            _btnPrimary.Text = "Retry";
            _btnPrimary.Enabled = true;
            _txtInstallDir.Enabled = true;
            _btnBrowse.Enabled = true;
            _optionsPanel.Enabled = true;
            SetDetailsExpanded(true);
            AppendLog($"[ERROR] Action exited with return code {code}.");
        }
    }

    private void AppendLog(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => AppendLog(message)));
            return;
        }
        string stamp = DateTime.Now.ToString("HH:mm:ss");
        _txtLog.AppendText($"[{stamp}] {message}{Environment.NewLine}");
    }
}
