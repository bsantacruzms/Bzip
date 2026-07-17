using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Navigation;
using BoltZip.Core.Compression;
using BoltZip.Core.Hardware;
using BoltZip.Core.Infrastructure;
using Microsoft.Win32;

namespace BoltZip.App;

public partial class MainWindow : Window
{
    private readonly ArchiveService _service = new();
    private HardwareProfile? _hardware;
    private string _lastSuggestedOutput = string.Empty;
    private bool _busy;

    public MainWindow(StartupAction startup)
    {
        InitializeComponent();
        ApplyStartup(startup);
        _ = LoadHardwareAsync();
    }

    private async Task LoadHardwareAsync()
    {
        try
        {
            _hardware = await HardwareProbe.DetectAsync();
            HardwareText.Text = _hardware.Summary();
        }
        catch
        {
            _hardware = HardwareProbe.DetectFast();
            HardwareText.Text = _hardware.Summary();
        }
    }

    private void ApplyStartup(StartupAction startup)
    {
        if (startup.Path is null)
        {
            return;
        }

        if (startup.Mode == StartupMode.Extract)
        {
            ExtractTab.IsChecked = true;
            ArchivePathBox.Text = startup.Path;
            ExtractOutputBox.Text = SuggestExtractDirectory(startup.Path);
        }
        else
        {
            CompressTab.IsChecked = true;
            AddInput(startup.Path);
        }
    }

    // ---- Navigation ----

    private void OnTabChanged(object sender, RoutedEventArgs e)
    {
        if (CompressPanel is null || ExtractPanel is null)
        {
            return;
        }

        var compress = CompressTab.IsChecked == true;
        CompressPanel.Visibility = compress ? Visibility.Visible : Visibility.Collapsed;
        ExtractPanel.Visibility = compress ? Visibility.Collapsed : Visibility.Visible;
    }

    // ---- Compress inputs ----

    private void OnAddFiles(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Add files", Multiselect = true };
        if (dialog.ShowDialog() == true)
        {
            foreach (var file in dialog.FileNames)
            {
                AddInput(file);
            }
        }
    }

    private void OnAddFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Add a folder" };
        if (dialog.ShowDialog() == true)
        {
            AddInput(dialog.FolderName);
        }
    }

    private void OnRemoveInput(object sender, RoutedEventArgs e)
    {
        if (InputList.SelectedItem is string selected)
        {
            InputList.Items.Remove(selected);
            UpdateSuggestedOutput();
        }
    }

    private void OnClearInputs(object sender, RoutedEventArgs e)
    {
        InputList.Items.Clear();
        UpdateSuggestedOutput();
    }

    private void OnInputDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            foreach (var path in paths)
            {
                AddInput(path);
            }
        }
    }

    private void AddInput(string path)
    {
        if (!InputList.Items.Contains(path))
        {
            InputList.Items.Add(path);
        }

        UpdateSuggestedOutput();
    }

    // ---- Options ----

    private void OnFormatChanged(object sender, RoutedEventArgs e)
    {
        if (PasswordBox is null)
        {
            return;
        }

        var isBz = SelectedExtension() == ".bz";
        PasswordBox.IsEnabled = isBz;
        if (!isBz)
        {
            PasswordBox.Clear();
        }

        UpdateSuggestedOutput();
    }

    private void OnGoalChanged(object sender, RoutedEventArgs e)
    {
        if (PlanText is not null)
        {
            PlanText.Text = "Goal changed. Click Preview to see the new plan.";
        }
    }

    private void OnBrowseOutput(object sender, RoutedEventArgs e)
    {
        var extension = SelectedExtension();
        var dialog = new SaveFileDialog
        {
            Title = "Save archive as",
            FileName = Path.GetFileName(OutputPathBox.Text),
            DefaultExt = extension,
            Filter = $"BoltZip archive (*{extension})|*{extension}|All files (*.*)|*.*",
        };

        if (dialog.ShowDialog() == true)
        {
            OutputPathBox.Text = dialog.FileName;
            _lastSuggestedOutput = dialog.FileName;
        }
    }

    private async void OnPreviewPlan(object sender, RoutedEventArgs e)
    {
        try
        {
            var request = BuildCreateRequest(requireInputs: false);
            var plan = await _service.PlanAsync(request);
            PlanText.Text = PlanToText(plan);
        }
        catch (Exception ex)
        {
            PlanText.Text = $"Could not build a plan: {ex.Message}";
        }
    }

    // ---- Compress ----

    private async void OnCompress(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        CreateRequest request;
        try
        {
            request = BuildCreateRequest(requireInputs: true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "BoltZip", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SetBusy(true);
        var stopwatch = Stopwatch.StartNew();
        var progress = CreateProgress(stopwatch);

        try
        {
            var result = await _service.CreateAsync(request, progress);
            var size = new FileInfo(result.OutputPath).Length;
            SetStatus($"Created {Path.GetFileName(result.OutputPath)} ({FormatBytes(size)})", success: true);
            ProgressBar.Value = 100;
        }
        catch (Exception ex)
        {
            SetStatus($"Failed: {ex.Message}", success: false);
            MessageBox.Show(this, ex.Message, "BoltZip", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    // ---- Extract ----

    private void OnArchiveDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } paths)
        {
            ArchivePathBox.Text = paths[0];
            if (string.IsNullOrWhiteSpace(ExtractOutputBox.Text))
            {
                ExtractOutputBox.Text = SuggestExtractDirectory(paths[0]);
            }
        }
    }

    private void OnBrowseArchive(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open archive",
            Filter = "Archives (*.bz;*.zip;*.7z;*.rar;*.tar;*.gz;*.bz2;*.zst;*.xz;*.br)|" +
                     "*.bz;*.zip;*.7z;*.rar;*.tar;*.gz;*.bz2;*.zst;*.xz;*.br|All files (*.*)|*.*",
        };

        if (dialog.ShowDialog() == true)
        {
            ArchivePathBox.Text = dialog.FileName;
            if (string.IsNullOrWhiteSpace(ExtractOutputBox.Text))
            {
                ExtractOutputBox.Text = SuggestExtractDirectory(dialog.FileName);
            }
        }
    }

    private void OnBrowseExtractDir(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Extract to" };
        if (dialog.ShowDialog() == true)
        {
            ExtractOutputBox.Text = dialog.FolderName;
        }
    }

    private async void OnListArchive(object sender, RoutedEventArgs e)
    {
        var archive = ArchivePathBox.Text;
        if (string.IsNullOrWhiteSpace(archive) || !File.Exists(archive))
        {
            SetStatus("Choose an archive first.", success: false);
            return;
        }

        var password = ExtractPasswordBox.Password;
        try
        {
            var entries = await _service.ListAsync(archive, string.IsNullOrEmpty(password) ? null : password);
            EntryList.Items.Clear();
            foreach (var entry in entries.Where(entry => !entry.IsDirectory))
            {
                EntryList.Items.Add($"{entry.Path}   ({FormatBytes(entry.Size)})");
            }

            SetStatus($"{EntryList.Items.Count} file(s).", success: true);
        }
        catch (InvalidOperationException)
        {
            SetStatus("This archive is encrypted \u2014 enter the password.", success: false);
            ExtractPasswordBox.Focus();
        }
        catch (Exception ex)
        {
            SetStatus($"Could not list: {ex.Message}", success: false);
        }
    }

    private async void OnExtract(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        var archive = ArchivePathBox.Text;
        if (string.IsNullOrWhiteSpace(archive) || !File.Exists(archive))
        {
            SetStatus("Choose an archive first.", success: false);
            return;
        }

        var outputDir = string.IsNullOrWhiteSpace(ExtractOutputBox.Text)
            ? SuggestExtractDirectory(archive)
            : ExtractOutputBox.Text;
        ExtractOutputBox.Text = outputDir;

        var password = ExtractPasswordBox.Password;
        var request = new ExtractRequest
        {
            ArchivePath = archive,
            OutputDirectory = outputDir,
            Password = string.IsNullOrEmpty(password) ? null : password,
            Overwrite = OverwriteCheck.IsChecked == true,
        };

        SetBusy(true);
        var stopwatch = Stopwatch.StartNew();
        var progress = CreateProgress(stopwatch);

        try
        {
            await _service.ExtractAsync(request, progress);
            SetStatus($"Extracted to {outputDir}", success: true);
            ProgressBar.Value = 100;
        }
        catch (InvalidOperationException)
        {
            SetStatus("This archive is encrypted \u2014 enter the password.", success: false);
            ExtractPasswordBox.Focus();
        }
        catch (Exception ex)
        {
            SetStatus($"Failed: {ex.Message}", success: false);
            MessageBox.Show(this, ex.Message, "BoltZip", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    // ---- Shell integration ----

    private void OnInstallShell(object sender, RoutedEventArgs e)
    {
        try
        {
            var exe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
            if (exe is null)
            {
                return;
            }

            ShellIntegration.Install(exe);
            SetStatus("Added BoltZip to the right-click menu (see \"Show more options\").", success: true);
        }
        catch (Exception ex)
        {
            SetStatus($"Could not update the menu: {ex.Message}", success: false);
        }
    }

    private void OnRemoveShell(object sender, RoutedEventArgs e)
    {
        try
        {
            ShellIntegration.Uninstall();
            SetStatus("Removed BoltZip from the right-click menu.", success: true);
        }
        catch (Exception ex)
        {
            SetStatus($"Could not update the menu: {ex.Message}", success: false);
        }
    }

    private void OnOpenWebsite(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch
        {
            // ignore navigation failures
        }

        e.Handled = true;
    }

    // ---- Helpers ----

    private CreateRequest BuildCreateRequest(bool requireInputs)
    {
        var inputs = InputList.Items.OfType<string>().ToList();
        if (requireInputs && inputs.Count == 0)
        {
            throw new InvalidOperationException("Add at least one file or folder to compress.");
        }

        var extension = SelectedExtension();
        var output = string.IsNullOrWhiteSpace(OutputPathBox.Text)
            ? SuggestOutputPath(inputs, extension)
            : OutputPathBox.Text;

        if (requireInputs && string.IsNullOrWhiteSpace(output))
        {
            throw new InvalidOperationException("Choose an output path.");
        }

        var password = PasswordBox.Password;
        return new CreateRequest
        {
            OutputPath = string.IsNullOrWhiteSpace(output) ? $"archive{extension}" : output,
            Inputs = inputs,
            Password = extension == ".bz" && !string.IsNullOrEmpty(password) ? password : null,
            Goal = SelectedGoal(),
            Hardware = _hardware,
        };
    }

    private string SelectedExtension() =>
        FormatPanel.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true)?.Tag?.ToString() ?? ".bz";

    private OptimizationGoal SelectedGoal()
    {
        var tag = GoalPanel.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true)?.Tag?.ToString();
        return Enum.TryParse<OptimizationGoal>(tag, out var goal) ? goal : OptimizationGoal.Balanced;
    }

    private void UpdateSuggestedOutput()
    {
        if (OutputPathBox is null)
        {
            return;
        }

        var inputs = InputList.Items.OfType<string>().ToList();
        if (inputs.Count == 0)
        {
            return;
        }

        // Only replace the field if the user hasn't customized it.
        if (!string.IsNullOrEmpty(OutputPathBox.Text) && OutputPathBox.Text != _lastSuggestedOutput)
        {
            return;
        }

        var suggestion = SuggestOutputPath(inputs, SelectedExtension());
        OutputPathBox.Text = suggestion;
        _lastSuggestedOutput = suggestion;
    }

    private static string SuggestOutputPath(IReadOnlyList<string> inputs, string extension)
    {
        if (inputs.Count == 0)
        {
            return string.Empty;
        }

        var first = inputs[0];
        var directory = Path.GetDirectoryName(first) ?? Directory.GetCurrentDirectory();
        var baseName = Directory.Exists(first)
            ? new DirectoryInfo(first).Name
            : Path.GetFileNameWithoutExtension(first);
        return Path.Combine(directory, baseName + extension);
    }

    private static string SuggestExtractDirectory(string archivePath)
    {
        var directory = Path.GetDirectoryName(archivePath) ?? Directory.GetCurrentDirectory();
        var name = Path.GetFileNameWithoutExtension(archivePath);
        return Path.Combine(directory, name);
    }

    private IProgress<ArchiveProgress> CreateProgress(Stopwatch stopwatch)
    {
        return new Progress<ArchiveProgress>(p =>
        {
            ProgressBar.Value = p.Percent;
            var eta = EstimateEta(stopwatch, p);
            var entry = string.IsNullOrEmpty(p.CurrentEntry) ? string.Empty : $"  {p.CurrentEntry}";
            StatusText.Foreground = (Brush)FindResource("SubtleBrush");
            StatusText.Text = $"{p.Phase} {p.Percent:0}%{eta}{entry}";
        });
    }

    private static string EstimateEta(Stopwatch stopwatch, ArchiveProgress p)
    {
        if (p.ProcessedBytes <= 0 || p.TotalBytes <= 0 || p.ProcessedBytes >= p.TotalBytes)
        {
            return string.Empty;
        }

        var elapsed = stopwatch.Elapsed.TotalSeconds;
        if (elapsed < 0.5)
        {
            return string.Empty;
        }

        var remaining = elapsed * (p.TotalBytes - p.ProcessedBytes) / p.ProcessedBytes;
        return $"  ~{remaining:0}s left";
    }

    private void SetStatus(string message, bool success)
    {
        StatusText.Text = message;
        StatusText.Foreground = (Brush)FindResource(success ? "SuccessBrush" : "ErrorBrush");
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        CompressButton.IsEnabled = !busy;
        ExtractButton.IsEnabled = !busy;
        Cursor = busy ? System.Windows.Input.Cursors.Wait : System.Windows.Input.Cursors.Arrow;
        if (busy)
        {
            ProgressBar.Value = 0;
        }
    }

    private static string PlanToText(CompressionPlan plan)
    {
        var header = $"{plan.Goal}: level {plan.Level}, {plan.WorkerThreads} thread(s), " +
                     $"window {plan.WindowMiB:0.#} MiB, buffer {plan.BufferBytes / 1024} KiB, " +
                     $"LDM {(plan.LongDistanceMatching ? "on" : "off")}, " +
                     $"{(plan.HardwareAes ? "hardware AES" : "software AEAD")}.";
        return header + Environment.NewLine + "\u2022 " + string.Join(Environment.NewLine + "\u2022 ", plan.Rationale);
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KiB", "MiB", "GiB", "TiB" };
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} B" : $"{value:0.##} {units[unit]}";
    }
}
