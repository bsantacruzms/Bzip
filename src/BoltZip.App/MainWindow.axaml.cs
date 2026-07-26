using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using BoltZip.Core.Compression;
using BoltZip.Core.Hardware;

namespace BoltZip.App;

public partial class MainWindow : Window
{
    private static readonly IBrush SuccessBrush = new SolidColorBrush(Color.Parse("#4CAF50"));
    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.Parse("#E5534B"));
    private static readonly IBrush SubtleBrush = new SolidColorBrush(Color.Parse("#9AA0A6"));

    private readonly ArchiveService _service = new();
    private readonly ObservableCollection<string> _inputs = new();
    private readonly ObservableCollection<string> _entries = new();
    private HardwareProfile? _hardware;
    private string _lastSuggestedOutput = string.Empty;
    private bool _busy;

    public MainWindow() : this(new StartupAction(StartupMode.Compress, null))
    {
    }

    public MainWindow(StartupAction startup)
    {
        InitializeComponent();

        InputList.ItemsSource = _inputs;
        EntryList.ItemsSource = _entries;

        InputList.AddHandler(DragDrop.DropEvent, OnInputDrop);
        InputList.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        ExtractDropZone.AddHandler(DragDrop.DropEvent, OnArchiveDrop);
        ExtractDropZone.AddHandler(DragDrop.DragOverEvent, OnDragOver);

        foreach (var radio in FormatPanel.Children.OfType<RadioButton>())
        {
            radio.Click += (_, _) => OnFormatChanged();
        }

        foreach (var radio in GoalPanel.Children.OfType<RadioButton>())
        {
            radio.Click += (_, _) => OnGoalChanged();
        }

        ApplyStartup(startup);
        _ = LoadHardwareAsync();
    }

    private async Task LoadHardwareAsync()
    {
        try
        {
            // Installed builds profile the machine once and cache it; portable builds re-probe.
            _hardware = await HardwareProfileStore.GetProfileAsync(
                useCache: BoltZip.Core.Infrastructure.DeploymentMode.IsInstalled());
        }
        catch
        {
            _hardware = HardwareProbe.DetectFast();
        }

        HardwareText.Text = _hardware.Summary();
    }

    private void ApplyStartup(StartupAction startup)
    {
        if (startup.Path is null)
        {
            return;
        }

        if (startup.Mode == StartupMode.Extract)
        {
            Tabs.SelectedIndex = 1;
            ArchivePathBox.Text = startup.Path;
            ExtractOutputBox.Text = SuggestExtractDirectory(startup.Path);
        }
        else
        {
            Tabs.SelectedIndex = 0;
            AddInput(startup.Path);
        }
    }

    // ---- Drag & drop ----

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(DataFormats.Files) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void OnInputDrop(object? sender, DragEventArgs e)
    {
        foreach (var path in ExtractPaths(e))
        {
            AddInput(path);
        }
    }

    private void OnArchiveDrop(object? sender, DragEventArgs e)
    {
        var first = ExtractPaths(e).FirstOrDefault();
        if (first is null)
        {
            return;
        }

        ArchivePathBox.Text = first;
        if (string.IsNullOrWhiteSpace(ExtractOutputBox.Text))
        {
            ExtractOutputBox.Text = SuggestExtractDirectory(first);
        }
    }

    private static IEnumerable<string> ExtractPaths(DragEventArgs e)
    {
        var files = e.Data.GetFiles();
        if (files is null)
        {
            yield break;
        }

        foreach (var item in files)
        {
            var path = item.TryGetLocalPath();
            if (!string.IsNullOrEmpty(path))
            {
                yield return path;
            }
        }
    }

    // ---- Compress inputs ----

    private async void OnAddFiles(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Add files",
            AllowMultiple = true,
        });

        foreach (var file in files)
        {
            var path = file.TryGetLocalPath();
            if (!string.IsNullOrEmpty(path))
            {
                AddInput(path);
            }
        }
    }

    private async void OnAddFolder(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Add a folder",
            AllowMultiple = false,
        });

        var path = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
        if (!string.IsNullOrEmpty(path))
        {
            AddInput(path);
        }
    }

    private void OnRemoveInput(object? sender, RoutedEventArgs e)
    {
        if (InputList.SelectedItem is string selected)
        {
            _inputs.Remove(selected);
            UpdateSuggestedOutput();
        }
    }

    private void OnClearInputs(object? sender, RoutedEventArgs e)
    {
        _inputs.Clear();
        UpdateSuggestedOutput();
    }

    private void AddInput(string path)
    {
        if (!_inputs.Contains(path))
        {
            _inputs.Add(path);
        }

        UpdateSuggestedOutput();
    }

    // ---- Options ----

    private void OnFormatChanged()
    {
        var isBz = SelectedExtension() == ".bz";
        PasswordBox.IsEnabled = isBz;
        if (!isBz)
        {
            PasswordBox.Text = string.Empty;
        }

        UpdateSuggestedOutput();
    }

    private void OnGoalChanged()
    {
        PlanText.Text = "Goal changed. Click Preview to see the new plan.";
    }

    private async void OnBrowseOutput(object? sender, RoutedEventArgs e)
    {
        var extension = SelectedExtension();
        var suggested = string.IsNullOrWhiteSpace(OutputPathBox.Text)
            ? "archive" + extension
            : Path.GetFileName(OutputPathBox.Text);

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save archive as",
            SuggestedFileName = suggested,
            DefaultExtension = extension.TrimStart('.'),
        });

        var path = file?.TryGetLocalPath();
        if (!string.IsNullOrEmpty(path))
        {
            OutputPathBox.Text = path;
            _lastSuggestedOutput = path;
        }
    }

    private async void OnPreviewPlan(object? sender, RoutedEventArgs e)
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

    private async void OnCompress(object? sender, RoutedEventArgs e)
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
            SetStatus(ex.Message, success: false);
            return;
        }

        SetBusy(true);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var result = await _service.CreateAsync(request, CreateProgress(stopwatch));
            var size = new FileInfo(result.OutputPath).Length;
            SetStatus($"Created {Path.GetFileName(result.OutputPath)} ({FormatBytes(size)})", success: true);
            Progress.Value = 100;
        }
        catch (Exception ex)
        {
            SetStatus($"Failed: {ex.Message}", success: false);
        }
        finally
        {
            SetBusy(false);
        }
    }

    // ---- Extract ----

    private async void OnBrowseArchive(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open archive",
            AllowMultiple = false,
        });

        var path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (!string.IsNullOrEmpty(path))
        {
            ArchivePathBox.Text = path;
            if (string.IsNullOrWhiteSpace(ExtractOutputBox.Text))
            {
                ExtractOutputBox.Text = SuggestExtractDirectory(path);
            }
        }
    }

    private async void OnBrowseExtractDir(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Extract to",
            AllowMultiple = false,
        });

        var path = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
        if (!string.IsNullOrEmpty(path))
        {
            ExtractOutputBox.Text = path;
        }
    }

    private async void OnListArchive(object? sender, RoutedEventArgs e)
    {
        var archive = ArchivePathBox.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(archive) || !File.Exists(archive))
        {
            SetStatus("Choose an archive first.", success: false);
            return;
        }

        var password = ExtractPasswordBox.Text;
        try
        {
            var entries = await _service.ListAsync(archive, string.IsNullOrEmpty(password) ? null : password);
            _entries.Clear();
            foreach (var entry in entries.Where(entry => !entry.IsDirectory))
            {
                _entries.Add($"{entry.Path}   ({FormatBytes(entry.Size)})");
            }

            SetStatus($"{_entries.Count} file(s).", success: true);
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

    private async void OnExtract(object? sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        var archive = ArchivePathBox.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(archive) || !File.Exists(archive))
        {
            SetStatus("Choose an archive first.", success: false);
            return;
        }

        var outputDir = string.IsNullOrWhiteSpace(ExtractOutputBox.Text)
            ? SuggestExtractDirectory(archive)
            : ExtractOutputBox.Text!;
        ExtractOutputBox.Text = outputDir;

        var password = ExtractPasswordBox.Text;
        var request = new ExtractRequest
        {
            ArchivePath = archive,
            OutputDirectory = outputDir,
            Password = string.IsNullOrEmpty(password) ? null : password,
            Overwrite = OverwriteCheck.IsChecked == true,
        };

        SetBusy(true);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            await _service.ExtractAsync(request, CreateProgress(stopwatch));
            SetStatus($"Extracted to {outputDir}", success: true);
            Progress.Value = 100;
        }
        catch (InvalidOperationException)
        {
            SetStatus("This archive is encrypted \u2014 enter the password.", success: false);
            ExtractPasswordBox.Focus();
        }
        catch (Exception ex)
        {
            SetStatus($"Failed: {ex.Message}", success: false);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void OnOpenWebsite(object? sender, PointerPressedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://github.com/bsantacruzms/Bzip") { UseShellExecute = true });
        }
        catch
        {
            // ignore
        }
    }

    // ---- Helpers ----

    private CreateRequest BuildCreateRequest(bool requireInputs)
    {
        var inputs = _inputs.ToList();
        if (requireInputs && inputs.Count == 0)
        {
            throw new InvalidOperationException("Add at least one file or folder to compress.");
        }

        var extension = SelectedExtension();
        var output = string.IsNullOrWhiteSpace(OutputPathBox.Text)
            ? SuggestOutputPath(inputs, extension)
            : OutputPathBox.Text!;

        var password = PasswordBox.Text;
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
        FormatPanel.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true)?.Tag as string ?? ".bz";

    private OptimizationGoal SelectedGoal()
    {
        var tag = GoalPanel.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true)?.Tag as string;
        return Enum.TryParse<OptimizationGoal>(tag, out var goal) ? goal : OptimizationGoal.Balanced;
    }

    private void UpdateSuggestedOutput()
    {
        if (_inputs.Count == 0)
        {
            return;
        }

        var current = OutputPathBox.Text ?? string.Empty;
        if (!string.IsNullOrEmpty(current) && current != _lastSuggestedOutput)
        {
            return;
        }

        var suggestion = SuggestOutputPath(_inputs.ToList(), SelectedExtension());
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
        return new Progress<ArchiveProgress>(p => Dispatcher.UIThread.Post(() =>
        {
            Progress.Value = p.Percent;
            var eta = EstimateEta(stopwatch, p);
            var entry = string.IsNullOrEmpty(p.CurrentEntry) ? string.Empty : $"  {p.CurrentEntry}";
            StatusText.Foreground = SubtleBrush;
            StatusText.Text = $"{p.Phase} {p.Percent:0}%{eta}{entry}";
        }));
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
        StatusText.Foreground = success ? SuccessBrush : ErrorBrush;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        CompressButton.IsEnabled = !busy;
        ExtractButton.IsEnabled = !busy;
        Cursor = busy ? new Cursor(StandardCursorType.Wait) : Cursor.Default;
        if (busy)
        {
            Progress.Value = 0;
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
