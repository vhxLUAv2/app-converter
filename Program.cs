using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using Velopack;
using Velopack.Sources;

namespace AppConverter;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        VelopackApp.Build().Run();
        ApplicationConfiguration.Initialize();
        Application.Run(new ConverterForm());
    }
}

internal sealed record ConversionProgress(string Phase, string Detail, int CurrentPercent, int OverallPercent, string ByteDetail);

internal sealed class ConverterForm : Form
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(3) };
    private readonly ListBox files = new() { Dock = DockStyle.Fill, AllowDrop = true, IntegralHeight = false, BorderStyle = BorderStyle.None };
    private readonly Label status = new() { AutoSize = false, Height = 28, Dock = DockStyle.Bottom, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label activityTitle = new() { AutoSize = false, Height = 42, Dock = DockStyle.Top, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 14, FontStyle.Bold) };
    private readonly Label activityDetail = new() { AutoSize = false, Height = 30, Dock = DockStyle.Top, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label fileName = new() { AutoSize = false, Height = 26, Dock = DockStyle.Top, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label byteDetail = new() { AutoSize = false, Height = 24, Dock = DockStyle.Top, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label overallDetail = new() { AutoSize = false, Height = 25, Dock = DockStyle.Top, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ProgressBar fileProgress = new() { Dock = DockStyle.Top, Height = 18, Style = ProgressBarStyle.Continuous };
    private readonly ProgressBar overallProgress = new() { Dock = DockStyle.Top, Height = 18, Style = ProgressBarStyle.Continuous };
    private readonly Button convert = new() { Text = "⇄  Convert to ZIP", Dock = DockStyle.Bottom, Height = 48, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 11, FontStyle.Bold) };
    private readonly Button add = new() { Text = "Browse files…", AutoSize = true, FlatStyle = FlatStyle.Flat };
    private readonly Button clear = new() { Text = "Clear list", AutoSize = true, FlatStyle = FlatStyle.Flat };
    private readonly HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Color background = Color.FromArgb(79, 91, 105);
    private readonly Color surface = Color.FromArgb(54, 66, 81);
    private readonly Color card = Color.FromArgb(42, 54, 69);
    private readonly Color muted = Color.FromArgb(174, 184, 196);
    private bool isConverting;

    public ConverterForm()
    {
        Text = "MRPack to ZIP Converter";
        Width = 1040;
        Height = 680;
        MinimumSize = new Size(820, 560);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = background;
        ForeColor = Color.FromArgb(239, 243, 248);
        Font = new Font("Segoe UI", 9.5f);
        AllowDrop = true;

        var header = new Label
        {
            Text = "MRPack to ZIP Converter",
            Dock = DockStyle.Top,
            Height = 62,
            Padding = new Padding(22, 14, 0, 0),
            Font = new Font("Segoe UI", 17, FontStyle.Bold),
            ForeColor = Color.White
        };

        var columns = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(18, 0, 18, 14), BackColor = background };
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 51));
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 49));
        columns.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var left = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 12, 0), BackColor = background };
        var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 0, 0, 0), BackColor = background };

        var drop = new Label
        {
            Text = "⇧\r\n\r\nDrag and drop .mrpack files here\r\nor use Browse files…",
            Dock = DockStyle.Top,
            Height = 150,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = surface,
            ForeColor = muted,
            BorderStyle = BorderStyle.FixedSingle,
            AllowDrop = true
        };
        drop.DragEnter += FileDragEnter;
        drop.DragDrop += FileDragDrop;
        files.BackColor = card;
        files.ForeColor = Color.White;
        files.Font = new Font("Segoe UI", 10, FontStyle.Bold);
        files.Margin = new Padding(0, 14, 0, 14);

        var listLabel = new Label { Text = "Selected modpacks", Dock = DockStyle.Top, Height = 34, Padding = new Padding(0, 10, 0, 0), Font = new Font("Segoe UI", 11, FontStyle.Bold) };
        var buttonRow = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = background };
        StyleButton(add, Color.FromArgb(0, 158, 220), Color.White);
        StyleButton(clear, surface, Color.White);
        buttonRow.Controls.Add(add);
        buttonRow.Controls.Add(clear);

        var listPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 0, 8), BackColor = card };
        listPanel.Controls.Add(files);
        left.Controls.Add(listPanel);
        left.Controls.Add(listLabel);
        left.Controls.Add(drop);
        left.Controls.Add(buttonRow);

        var activityCard = new Panel { Dock = DockStyle.Fill, BackColor = surface, Padding = new Padding(24) };
        activityTitle.Text = "Ready to convert";
        activityTitle.ForeColor = Color.White;
        activityDetail.Text = "Choose one or more Modrinth modpacks to begin.";
        activityDetail.ForeColor = muted;
        fileName.Text = "Waiting for a file";
        fileName.ForeColor = Color.White;
        byteDetail.Text = "Download size will appear here";
        byteDetail.ForeColor = muted;
        overallDetail.Text = "Overall progress";
        overallDetail.ForeColor = muted;
        fileProgress.Margin = new Padding(0, 0, 0, 18);
        overallProgress.Margin = new Padding(0, 0, 0, 18);
        var separator = new Panel { Dock = DockStyle.Top, Height = 26, BackColor = surface };
        activityCard.Controls.Add(overallProgress);
        activityCard.Controls.Add(overallDetail);
        activityCard.Controls.Add(separator);
        activityCard.Controls.Add(byteDetail);
        activityCard.Controls.Add(fileProgress);
        activityCard.Controls.Add(fileName);
        activityCard.Controls.Add(activityDetail);
        activityCard.Controls.Add(activityTitle);
        right.Controls.Add(activityCard);

        convert.BackColor = Color.FromArgb(0, 153, 218);
        convert.ForeColor = Color.White;
        convert.FlatAppearance.BorderSize = 0;
        convert.FlatAppearance.MouseOverBackColor = Color.FromArgb(0, 137, 199);
        convert.Enabled = false;
        left.Controls.Add(convert);
        columns.Controls.Add(left, 0, 0);
        columns.Controls.Add(right, 1, 0);
        Controls.Add(columns);
        Controls.Add(header);
        status.Text = "0 modpacks queued";
        status.Padding = new Padding(20, 0, 0, 0);
        status.ForeColor = muted;
        status.BackColor = background;
        Controls.Add(status);

        add.Click += (_, _) => PickFiles();
        clear.Click += (_, _) => { if (!isConverting) { files.Items.Clear(); paths.Clear(); ResetActivity(); UpdateStatus(); } };
        convert.Click += async (_, _) => await ConvertFilesAsync();
        files.DragEnter += FileDragEnter;
        files.DragDrop += FileDragDrop;
        DragEnter += FileDragEnter;
        DragDrop += FileDragDrop;
        UpdateStatus();
        Shown += async (_, _) => await CheckForUpdatesAsync();
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var source = new GithubSource("https://github.com/vhxLUAv2/app-converter", null, false);
            var manager = new UpdateManager(source);
            if (!manager.IsInstalled) return;

            var update = await manager.CheckForUpdatesAsync();
            if (update is null) return;

            var answer = MessageBox.Show(
                this,
                $"Version {update.TargetFullRelease.Version} is available. Would you like to download and install it now? The app will restart after installation.",
                "Update available",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);
            if (answer != DialogResult.Yes) return;

            convert.Enabled = false;
            add.Enabled = false;
            clear.Enabled = false;
            activityTitle.Text = "Downloading update";
            activityDetail.Text = $"Downloading version {update.TargetFullRelease.Version}…";
            await manager.DownloadUpdatesAsync(update);
            activityTitle.Text = "Installing update";
            activityDetail.Text = "Restarting with the new version…";
            manager.ApplyUpdatesAndRestart(update);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                $"The update check could not be completed. You can continue using the current version.\r\n\r\n{ex.Message}",
                "Update check failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void StyleButton(Button button, Color back, Color fore)
    {
        button.BackColor = back;
        button.ForeColor = fore;
        button.FlatAppearance.BorderSize = 0;
        button.Padding = new Padding(10, 4, 10, 4);
        button.Margin = new Padding(0, 4, 8, 4);
    }

    private void FileDragEnter(object? sender, DragEventArgs e) =>
        e.Effect = !isConverting && e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;

    private void FileDragDrop(object? sender, DragEventArgs e)
    {
        if (!isConverting && e.Data?.GetData(DataFormats.FileDrop) is string[] dropped) AddPaths(dropped);
    }

    private void PickFiles()
    {
        if (isConverting) return;
        using var dialog = new OpenFileDialog
        {
            Title = "Select Modrinth modpacks",
            Filter = "Modrinth modpacks (*.mrpack)|*.mrpack",
            Multiselect = true
        };
        if (dialog.ShowDialog(this) == DialogResult.OK) AddPaths(dialog.FileNames);
    }

    private void AddPaths(IEnumerable<string> candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!File.Exists(candidate) || !string.Equals(Path.GetExtension(candidate), ".mrpack", StringComparison.OrdinalIgnoreCase))
                continue;
            var fullPath = Path.GetFullPath(candidate);
            if (paths.Add(fullPath)) files.Items.Add(fullPath);
        }
        UpdateStatus();
    }

    private async Task ConvertFilesAsync()
    {
        if (isConverting || files.Items.Count == 0) return;
        isConverting = true;
        convert.Enabled = false;
        add.Enabled = false;
        clear.Enabled = false;
        var succeeded = 0;
        var failures = new List<string>();
        var queue = files.Items.Cast<object>().Select(x => x.ToString()).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().ToArray();
        var progress = new Progress<ConversionProgress>(p =>
        {
            activityTitle.Text = p.Phase;
            activityDetail.Text = p.Detail;
            fileName.Text = p.Detail;
            fileProgress.Value = Math.Clamp(p.CurrentPercent, 0, 100);
            overallProgress.Value = Math.Clamp(p.OverallPercent, 0, 100);
            overallDetail.Text = $"Overall progress  ·  {p.OverallPercent}%";
            byteDetail.Text = p.ByteDetail;
        });

        try
        {
            for (var i = 0; i < queue.Length; i++)
            {
                var source = queue[i];
                var currentIndex = i;
                activityTitle.Text = $"Converting {i + 1} of {queue.Length}";
                activityDetail.Text = Path.GetFileName(source);
                fileName.Text = "Preparing archive…";
                fileProgress.Value = 0;
                try
                {
                    await Task.Run(() => ConvertOne(source, p => ((IProgress<ConversionProgress>)progress).Report(
                        p with { OverallPercent = (int)Math.Round((currentIndex + p.OverallPercent / 100.0) * 100.0 / queue.Length) })));
                    succeeded++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or HttpRequestException or TaskCanceledException or CryptographicException or NotSupportedException)
                {
                    failures.Add($"{Path.GetFileName(source)}: {ex.Message}");
                }
                overallProgress.Value = (int)Math.Round((i + 1) * 100.0 / queue.Length);
            }
        }
        finally
        {
            isConverting = false;
            convert.Enabled = files.Items.Count > 0;
            add.Enabled = true;
            clear.Enabled = true;
        }

        activityTitle.Text = failures.Count == 0 ? "Conversion complete" : "Conversion finished with errors";
        activityDetail.Text = $"{succeeded} succeeded · {failures.Count} failed";
        fileName.Text = failures.Count == 0 ? "All selected modpacks were converted." : "See the error details for files that could not be converted.";
        fileProgress.Value = 100;
        overallProgress.Value = 100;
        overallDetail.Text = "Batch complete";
        byteDetail.Text = "ZIP files are saved beside their source .mrpack files.";
        status.Text = $"Converted: {succeeded}    Failed: {failures.Count}";

        if (failures.Count > 0)
        {
            var details = string.Join(Environment.NewLine + Environment.NewLine, failures.Take(8));
            if (failures.Count > 8) details += Environment.NewLine + $"…and {failures.Count - 8} more.";
            MessageBox.Show(this, "Some modpacks could not be converted. Failed conversions do not keep a partial ZIP." + Environment.NewLine + Environment.NewLine + details,
                "Conversion results", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static void ConvertOne(string source, Action<ConversionProgress> report)
    {
        if (!File.Exists(source)) throw new IOException("Source file no longer exists.");
        var destination = Path.ChangeExtension(source, ".zip");
        if (File.Exists(destination)) throw new IOException("The destination ZIP already exists; it was not overwritten.");

        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using var input = ZipFile.OpenRead(source);
            var indexEntry = input.GetEntry("modrinth.index.json")
                ?? throw new InvalidDataException("modrinth.index.json was not found at the archive root.");
            using var indexStream = indexEntry.Open();
            using var document = JsonDocument.Parse(indexStream);
            var root = document.RootElement;
            if (!root.TryGetProperty("files", out var fileList) || fileList.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("The modpack index has no valid files array.");

            var indexedFiles = fileList.EnumerateArray().ToArray();
            var totalFiles = indexedFiles.Length;
            var processed = 0;
            var outputPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using (var outputFile = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            using (var output = new ZipArchive(outputFile, ZipArchiveMode.Create))
            {
                report(new ConversionProgress("Preparing archive", "Reading overrides and configuration files", 0, 0, "Preparing files…"));
                foreach (var entry in input.Entries)
                {
                    var relative = GetOverridePath(entry.FullName);
                    if (relative is null || entry.FullName.EndsWith("/", StringComparison.Ordinal)) continue;
                    AddUniquePath(outputPaths, relative);
                    var target = output.CreateEntry(relative, CompressionLevel.Optimal);
                    using var sourceStream = entry.Open();
                    using var targetStream = target.Open();
                    sourceStream.CopyTo(targetStream);
                }

                foreach (var file in indexedFiles)
                {
                    if (!file.TryGetProperty("path", out var pathElement) || pathElement.ValueKind != JsonValueKind.String)
                        throw new InvalidDataException("A file entry is missing its path.");
                    var relative = NormalizeRelativePath(pathElement.GetString()!);
                    if (!file.TryGetProperty("downloads", out var downloads) || downloads.ValueKind != JsonValueKind.Array || downloads.GetArrayLength() == 0)
                        throw new InvalidDataException($"No download URL is provided for '{relative}'.");
                    var urls = downloads.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToArray();
                    Exception? lastError = null;
                    var downloaded = false;

                    foreach (var url in urls)
                    {
                        string? downloadTemp = null;
                        try
                        {
                            var uri = new Uri(url, UriKind.Absolute);
                            if (uri.Scheme != Uri.UriSchemeHttps) throw new InvalidDataException("Only HTTPS download URLs are allowed.");
                            using var response = Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
                            response.EnsureSuccessStatusCode();
                            downloadTemp = Path.Combine(Path.GetTempPath(), "AppConverter-" + Guid.NewGuid().ToString("N") + ".tmp");
                            using var remote = response.Content.ReadAsStream();
                            using var sha1Hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
                            using var sha512Hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA512);
                            var expectedSha1 = GetExpectedHash(file, "sha1");
                            var expectedSha512 = GetExpectedHash(file, "sha512");
                            var totalBytes = response.Content.Headers.ContentLength;
                            long received = 0;

                            using (var tempStream = new FileStream(downloadTemp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                            {
                                var buffer = new byte[81920];
                                int read;
                                while ((read = remote.Read(buffer, 0, buffer.Length)) > 0)
                                {
                                    sha1Hash.AppendData(buffer, 0, read);
                                    sha512Hash.AppendData(buffer, 0, read);
                                    tempStream.Write(buffer, 0, read);
                                    received += read;
                                    var percent = totalBytes is > 0 ? (int)Math.Clamp(received * 100.0 / totalBytes.Value, 0, 100) : 0;
                                    report(new ConversionProgress(
                                        "Downloading mod files",
                                        $"Downloading: {Path.GetFileName(relative)}",
                                        percent,
                                        totalFiles == 0 ? 0 : (int)Math.Round((processed + percent / 100.0) * 100.0 / totalFiles),
                                        $"{FormatBytes(received)} / {(totalBytes.HasValue ? FormatBytes(totalBytes.Value) : "unknown size")}"));
                                }
                            }

                            if (expectedSha1 is not null && !string.Equals(Convert.ToHexString(sha1Hash.GetHashAndReset()), expectedSha1, StringComparison.OrdinalIgnoreCase))
                                throw new CryptographicException($"SHA-1 verification failed for '{relative}'.");
                            if (expectedSha512 is not null && !string.Equals(Convert.ToHexString(sha512Hash.GetHashAndReset()), expectedSha512, StringComparison.OrdinalIgnoreCase))
                                throw new CryptographicException($"SHA-512 verification failed for '{relative}'.");

                            AddUniquePath(outputPaths, relative);
                            var target = output.CreateEntry(relative, CompressionLevel.Optimal);
                            using var targetStream = target.Open();
                            using var tempInput = File.OpenRead(downloadTemp);
                            tempInput.CopyTo(targetStream);
                            downloaded = true;
                            report(new ConversionProgress("Verifying and packaging", $"Added {Path.GetFileName(relative)} ({processed + 1}/{totalFiles} files)", 100, totalFiles == 0 ? 100 : (int)Math.Round((processed + 1) * 100.0 / totalFiles), "Hash verified; adding file to ZIP"));
                            break;
                        }
                        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException or InvalidDataException)
                        {
                            lastError = ex;
                        }
                        finally
                        {
                            if (downloadTemp is not null && File.Exists(downloadTemp)) File.Delete(downloadTemp);
                        }
                    }

                    if (!downloaded) throw new IOException($"Could not download '{relative}'. {lastError?.Message ?? "No usable download URL."}", lastError);
                    processed++;
                    report(new ConversionProgress("Downloading mod files", $"Processed {processed} of {totalFiles} indexed files", 100, totalFiles == 0 ? 100 : (int)Math.Round(processed * 100.0 / totalFiles), $"Processed {processed} of {totalFiles} indexed files"));
                }
            }

            report(new ConversionProgress("Finalizing ZIP", "Checking output and saving ZIP archive", 100, 100, "Writing final ZIP archive…"));
            File.Move(temporary, destination);
            report(new ConversionProgress("Conversion complete", $"Saved {Path.GetFileName(destination)}", 100, 100, "ZIP saved successfully"));
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.##} {units[unit]}";
    }

    private void ResetActivity()
    {
        activityTitle.Text = "Ready to convert";
        activityDetail.Text = "Choose one or more Modrinth modpacks to begin.";
        fileName.Text = "Waiting for a file";
        byteDetail.Text = "Downloaded: 0 B";
        overallDetail.Text = "Overall progress";
        fileProgress.Value = 0;
        overallProgress.Value = 0;
    }

    private static string? GetExpectedHash(JsonElement file, string algorithm)
    {
        if (!file.TryGetProperty("hashes", out var hashes) || hashes.ValueKind != JsonValueKind.Object) return null;
        return hashes.TryGetProperty(algorithm, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static string? GetOverridePath(string entryName)
    {
        if (entryName.StartsWith("server-overrides/", StringComparison.Ordinal)) return NormalizeRelativePath(entryName);
        foreach (var folder in new[] { "overrides/", "client-overrides/" })
        {
            if (entryName.StartsWith(folder, StringComparison.Ordinal))
            {
                var relative = entryName[folder.Length..];
                return string.IsNullOrEmpty(relative) ? null : NormalizeRelativePath(relative);
            }
        }
        return null;
    }

    private static string NormalizeRelativePath(string path)
    {
        var normalized = path.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(normalized) || normalized.StartsWith("/", StringComparison.Ordinal) ||
            Path.IsPathRooted(normalized) || normalized.Contains(':'))
            throw new InvalidDataException($"Unsafe archive path: '{path}'.");
        var segments = normalized.Split('/');
        if (segments.Any(x => x.Length == 0 || x == "." || x == ".."))
            throw new InvalidDataException($"Unsafe archive path: '{path}'.");
        return string.Join("/", segments);
    }

    private static void AddUniquePath(HashSet<string> paths, string path)
    {
        if (!paths.Add(path)) throw new InvalidDataException($"Multiple files target the same path: '{path}'.");
    }

    private void UpdateStatus()
    {
        status.Text = $"{files.Items.Count} modpack(s) queued";
        convert.Enabled = files.Items.Count > 0 && !isConverting;
    }
}
