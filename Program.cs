using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace HarRecorder;

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

public sealed class MainForm : Form
{
    private readonly TextBox _logBox = new();
    private readonly TextBox _portBox = new();
    private readonly Label _statusLabel = new();
    private readonly Button _startButton = new();
    private readonly Button _stopButton = new();
    private readonly Button _installCertButton = new();
    private readonly Button _mitmItButton = new();

    private Process? _mitmProcess;
    private string? _mitmdumpPath;
    private string? _tempFlowPath;
    private string? _tempHarPath;

    private readonly string _chromeProfile = @"C:\temp\chrome-har-profile";
    private readonly string _certPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".mitmproxy",
        "mitmproxy-ca-cert.cer"
    );

    public MainForm()
    {
        Text = "HarRecorder Standalone";
        Width = 940;
        Height = 700;
        StartPosition = FormStartPosition.CenterScreen;

        BuildUi();

        FormClosing += (_, e) =>
        {
            if (_mitmProcess is { HasExited: false })
            {
                var result = MessageBox.Show(
                    "Recording is still running. Stop and close?",
                    "Recording",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question
                );

                if (result != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }

                StopMitmdumpGracefully();
            }
        };
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 6,
            ColumnCount = 1,
            Padding = new Padding(12)
        };

        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        Controls.Add(root);

        var title = new Label
        {
            Text = "HarRecorder Standalone",
            Font = new System.Drawing.Font("Segoe UI", 16, System.Drawing.FontStyle.Bold),
            Dock = DockStyle.Fill
        };
        root.Controls.Add(title, 0, 0);

        var desc = new Label
        {
            Text = "Start recording, reproduce the popup/window issue in Chrome, then stop and save a HAR file. This version records to a flow file first, then converts it to HAR.",
            Dock = DockStyle.Fill,
            AutoSize = false
        };
        root.Controls.Add(desc, 0, 1);

        var controls = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        root.Controls.Add(controls, 0, 2);

        controls.Controls.Add(new Label
        {
            Text = "Port",
            Width = 38,
            TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
            Height = 28
        });

        _portBox.Text = "8080";
        _portBox.Width = 70;
        controls.Controls.Add(_portBox);

        _startButton.Text = "Start Recording + Open Chrome";
        _startButton.Width = 210;
        _startButton.Click += async (_, _) => await StartRecordingAsync();
        controls.Controls.Add(_startButton);

        _stopButton.Text = "Stop and Save HAR";
        _stopButton.Width = 155;
        _stopButton.Enabled = false;
        _stopButton.Click += (_, _) => StopAndSave();
        controls.Controls.Add(_stopButton);

        _installCertButton.Text = "Install HTTPS Cert";
        _installCertButton.Width = 140;
        _installCertButton.Click += (_, _) => InstallCertificateForCurrentUser();
        controls.Controls.Add(_installCertButton);

        _mitmItButton.Text = "Open mitm.it";
        _mitmItButton.Width = 110;
        _mitmItButton.Click += (_, _) => OpenMitmIt();
        controls.Controls.Add(_mitmItButton);

        _statusLabel.Text = "Status: idle";
        _statusLabel.Dock = DockStyle.Fill;
        root.Controls.Add(_statusLabel, 0, 3);

        _logBox.Multiline = true;
        _logBox.ScrollBars = ScrollBars.Vertical;
        _logBox.ReadOnly = true;
        _logBox.Font = new System.Drawing.Font("Consolas", 9);
        _logBox.Dock = DockStyle.Fill;
        root.Controls.Add(_logBox, 0, 4);

        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft
        };

        var closeButton = new Button { Text = "Close", Width = 90 };
        closeButton.Click += (_, _) => Close();
        bottom.Controls.Add(closeButton);

        var clearButton = new Button { Text = "Clear Log", Width = 100 };
        clearButton.Click += (_, _) => _logBox.Clear();
        bottom.Controls.Add(clearButton);

        root.Controls.Add(bottom, 0, 5);
    }

    private void Log(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => Log(message)));
            return;
        }

        _logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }

    private void SetStatus(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => SetStatus(message)));
            return;
        }

        _statusLabel.Text = $"Status: {message}";
        Log(message);
    }

    private async Task StartRecordingAsync()
    {
        if (_mitmProcess is { HasExited: false })
        {
            MessageBox.Show("Recording is already running.", "Already running", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!int.TryParse(_portBox.Text.Trim(), out var port) || port < 1 || port > 65535)
        {
            MessageBox.Show("Port must be a number from 1 to 65535.", "Port error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        _startButton.Enabled = false;

        try
        {
            var mitmdump = await EnsureMitmdumpAsync();
            if (mitmdump is null)
            {
                _startButton.Enabled = true;
                return;
            }

            _mitmdumpPath = mitmdump;

            var chrome = FindChrome();
            if (chrome is null)
            {
                MessageBox.Show("Chrome executable was not found.", "Chrome not found", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _startButton.Enabled = true;
                return;
            }

            Directory.CreateDirectory(@"C:\temp");
            Directory.CreateDirectory(@"C:\har-capture");
            Directory.CreateDirectory(_chromeProfile);

            var tempDir = Path.Combine(Path.GetTempPath(), "mitm_har_gui");
            Directory.CreateDirectory(tempDir);

            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            _tempFlowPath = Path.Combine(tempDir, $"capture_{stamp}.mitm");
            _tempHarPath = Path.Combine(tempDir, $"capture_{stamp}.har");

            var psi = new ProcessStartInfo
            {
                FileName = mitmdump,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                CreateNoWindow = true
            };

            psi.ArgumentList.Add("--listen-host");
            psi.ArgumentList.Add("127.0.0.1");
            psi.ArgumentList.Add("--listen-port");
            psi.ArgumentList.Add(port.ToString());
            psi.ArgumentList.Add("-w");
            psi.ArgumentList.Add(_tempFlowPath);

            Log("Starting mitmdump...");
            Log($"{mitmdump} --listen-host 127.0.0.1 --listen-port {port} -w {_tempFlowPath}");

            _mitmProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _mitmProcess.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) Log(e.Data); };
            _mitmProcess.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) Log(e.Data); };

            _mitmProcess.Start();
            _mitmProcess.BeginOutputReadLine();
            _mitmProcess.BeginErrorReadLine();

            await Task.Delay(1200);

            if (_mitmProcess.HasExited)
            {
                throw new InvalidOperationException("mitmdump exited immediately. Check the log above.");
            }

            SetStatus($"recording - proxy 127.0.0.1:{port}");

            var chromeArgs =
                $"--user-data-dir=\"{_chromeProfile}\" " +
                $"--proxy-server=\"http://127.0.0.1:{port}\" " +
                "--disable-quic " +
                "--disable-background-networking " +
                "--disable-component-update " +
                "--disable-sync " +
                "about:blank";

            Process.Start(new ProcessStartInfo
            {
                FileName = chrome,
                Arguments = chromeArgs,
                UseShellExecute = false
            });

            Log("Opened Chrome with proxy settings.");
            Log("If HTTPS contents are needed, install the HTTPS certificate, then reopen Chrome.");
            Log("Reproduce the issue, then click Stop and Save HAR.");

            _stopButton.Enabled = true;
        }
        catch (Exception ex)
        {
            Log($"Start failed: {ex}");
            MessageBox.Show(ex.Message, "Start failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _startButton.Enabled = true;
        }
    }

    private async Task<string?> EnsureMitmdumpAsync()
    {
        var mitmdump = FindMitmdump();
        if (mitmdump is not null)
        {
            Log($"mitmdump found: {mitmdump}");
            return mitmdump;
        }

        SetStatus("mitmdump was not found. Trying to install mitmproxy with winget.");

        var winget = FindOnPath("winget.exe");
        if (winget is null)
        {
            MessageBox.Show(
                "mitmdump was not found, and winget is not available.\nInstall mitmproxy manually, then run this app again.",
                "winget not found",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );
            return null;
        }

        var exitCode = await RunProcessWithLogAsync(
            winget,
            "install -e --id mitmproxy.mitmproxy --accept-package-agreements --accept-source-agreements"
        );

        if (exitCode != 0)
        {
            MessageBox.Show("mitmproxy installation failed. Check the log.", "Install failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return null;
        }

        mitmdump = FindMitmdump();
        if (mitmdump is null)
        {
            MessageBox.Show(
                "mitmproxy seems installed, but mitmdump.exe was not found yet.\nRestart this app or log out/in, then try again.",
                "PATH not refreshed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning
            );
            return null;
        }

        Log($"mitmdump found after install: {mitmdump}");
        return mitmdump;
    }

    private async Task<int> RunProcessWithLogAsync(string file, string args)
    {
        var tcs = new TaskCompletionSource<int>();

        var p = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = file,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            },
            EnableRaisingEvents = true
        };

        p.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) Log(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) Log(e.Data); };
        p.Exited += (_, _) => tcs.TrySetResult(p.ExitCode);

        Log($"{file} {args}");
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        return await tcs.Task;
    }

    private void StopAndSave()
    {
        _stopButton.Enabled = false;
        SetStatus("stopping");

        try
        {
            StopMitmdumpGracefully();
            ConvertFlowToHar();

            using var dialog = new SaveFileDialog
            {
                Title = "Save HAR file",
                Filter = "HAR files (*.har)|*.har|All files (*.*)|*.*",
                DefaultExt = "har",
                FileName = $"capture_{DateTime.Now:yyyyMMdd_HHmmss}.har"
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                Log($"Save canceled. Temporary HAR: {_tempHarPath}");
                SetStatus("stopped - save canceled");
                return;
            }

            if (_tempHarPath is null || !File.Exists(_tempHarPath))
                throw new FileNotFoundException($"Temporary HAR was not created: {_tempHarPath}\nTemporary flow: {_tempFlowPath}");

            File.Copy(_tempHarPath, dialog.FileName, overwrite: true);

            Log($"HAR saved: {dialog.FileName}");
            SetStatus("saved");
            MessageBox.Show($"HAR saved:\n{dialog.FileName}", "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Log($"Save failed: {ex}");
            MessageBox.Show(ex.Message, "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            SetStatus("save failed");
        }
        finally
        {
            _startButton.Enabled = true;
            _mitmProcess = null;
        }
    }

    private void StopMitmdumpGracefully()
    {
        if (_mitmProcess is null || _mitmProcess.HasExited)
            return;

        Log("Stopping mitmdump and waiting for flow file flush...");

        try
        {
            _mitmProcess.StandardInput.Close();
        }
        catch (Exception ex)
        {
            Log($"stdin close failed: {ex.Message}");
        }

        try
        {
            if (_mitmProcess.CloseMainWindow() && _mitmProcess.WaitForExit(5000))
                return;
        }
        catch (Exception ex)
        {
            Log($"CloseMainWindow failed: {ex.Message}");
        }

        try
        {
            _mitmProcess.Kill(entireProcessTree: true);
            _mitmProcess.WaitForExit(3000);
        }
        catch (Exception ex)
        {
            Log($"Kill failed: {ex.Message}");
        }

        Thread.Sleep(500);
    }

    private void ConvertFlowToHar()
    {
        if (_mitmdumpPath is null || !File.Exists(_mitmdumpPath))
            throw new FileNotFoundException($"mitmdump.exe not found: {_mitmdumpPath}");

        if (_tempFlowPath is null || !File.Exists(_tempFlowPath))
            throw new FileNotFoundException($"Temporary flow file was not created: {_tempFlowPath}");

        var flowInfo = new FileInfo(_tempFlowPath);
        if (flowInfo.Length == 0)
            throw new InvalidDataException($"Temporary flow file is empty: {_tempFlowPath}");

        if (_tempHarPath is null)
            throw new InvalidOperationException("Temporary HAR path is missing.");

        if (File.Exists(_tempHarPath))
        {
            try { File.Delete(_tempHarPath); } catch { }
        }

        var psi = new ProcessStartInfo
        {
            FileName = _mitmdumpPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        psi.ArgumentList.Add("-q");
        psi.ArgumentList.Add("-n");
        psi.ArgumentList.Add("-r");
        psi.ArgumentList.Add(_tempFlowPath);
        psi.ArgumentList.Add("--set");
        psi.ArgumentList.Add($"hardump={_tempHarPath}");

        Log("Converting flow file to HAR...");
        Log($"{_mitmdumpPath} -q -n -r {_tempFlowPath} --set hardump={_tempHarPath}");

        using var p = new Process { StartInfo = psi, EnableRaisingEvents = false };
        p.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) Log(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) Log(e.Data); };

        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        if (!p.WaitForExit(30000))
        {
            try { p.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException("HAR conversion timed out.");
        }

        if (p.ExitCode != 0)
            throw new InvalidOperationException($"HAR conversion failed. mitmdump exit code: {p.ExitCode}");

        if (!File.Exists(_tempHarPath) || new FileInfo(_tempHarPath).Length == 0)
            throw new FileNotFoundException($"HAR conversion completed, but the HAR file is missing or empty: {_tempHarPath}");

        Log($"HAR conversion completed: {_tempHarPath}");
    }

    private void InstallCertificateForCurrentUser()
    {
        if (!File.Exists(_certPath))
        {
            MessageBox.Show(
                $"Certificate file does not exist yet:\n{_certPath}\n\nStart recording once, then try again. You can also open http://mitm.it.",
                "Certificate not found",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning
            );
            return;
        }

        var answer = MessageBox.Show(
            "This will add the mitmproxy CA certificate to the current user's trusted root store.\n\n" +
            "This enables HTTPS decryption through this proxy.\nDo not do this on a shared/public PC.\n\nContinue?",
            "Install HTTPS certificate",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question
        );

        if (answer != DialogResult.Yes)
            return;

        _ = RunProcessWithLogAsync("certutil.exe", $"-user -addstore root \"{_certPath}\"")
            .ContinueWith(t =>
            {
                BeginInvoke(new Action(() =>
                {
                    if (t.Result == 0)
                    {
                        MessageBox.Show(
                            "Certificate installed for current user.\nReopen Chrome and reproduce the issue again.",
                            "Done",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information
                        );
                    }
                    else
                    {
                        MessageBox.Show("Certificate install failed. Check the log.", "Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }));
            });
    }

    private void OpenMitmIt()
    {
        if (!int.TryParse(_portBox.Text.Trim(), out var port))
            port = 8080;

        var chrome = FindChrome();
        if (chrome is null)
        {
            MessageBox.Show("Chrome executable was not found.", "Chrome not found", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        Directory.CreateDirectory(_chromeProfile);

        var chromeArgs =
            $"--user-data-dir=\"{_chromeProfile}\" " +
            $"--proxy-server=\"http://127.0.0.1:{port}\" " +
            "--disable-quic " +
            "--disable-background-networking " +
            "--disable-component-update " +
            "--disable-sync " +
            "http://mitm.it";

        Process.Start(new ProcessStartInfo
        {
            FileName = chrome,
            Arguments = chromeArgs,
            UseShellExecute = false
        });

        Log("Opened mitm.it in proxied Chrome.");
    }

    private static string? FindMitmdump()
    {
        var onPath = FindOnPath("mitmdump.exe");
        if (onPath is not null)
            return onPath;

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var candidates = new[]
        {
            Path.Combine(localAppData, @"Microsoft\WinGet\Links\mitmdump.exe"),
            @"C:\Program Files\mitmproxy\bin\mitmdump.exe",
            @"C:\Program Files\mitmproxy\mitmdump.exe"
        };

        foreach (var c in candidates)
        {
            if (File.Exists(c))
                return c;
        }

        var roots = new[]
        {
            Path.Combine(localAppData, @"Microsoft\WinGet\Packages"),
            Path.Combine(localAppData, "Programs"),
            @"C:\Program Files"
        };

        foreach (var root in roots)
        {
            if (!Directory.Exists(root))
                continue;

            try
            {
                var found = Directory.EnumerateFiles(root, "mitmdump.exe", SearchOption.AllDirectories).FirstOrDefault();
                if (found is not null)
                    return found;
            }
            catch
            {
                // ignore
            }
        }

        return null;
    }

    private static string? FindChrome()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var candidates = new[]
        {
            @"C:\Program Files\Google\Chrome\Application\chrome.exe",
            @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
            Path.Combine(localAppData, @"Google\Chrome\Application\chrome.exe")
        };

        foreach (var c in candidates)
        {
            if (File.Exists(c))
                return c;
        }

        return FindOnPath("chrome.exe");
    }

    private static string? FindOnPath(string exeName)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir))
                continue;

            try
            {
                var candidate = Path.Combine(dir.Trim('"'), exeName);
                if (File.Exists(candidate))
                    return candidate;
            }
            catch
            {
                // ignore
            }
        }

        return null;
    }
}
