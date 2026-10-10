using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AppConverter;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new ConverterForm());
    }
}

internal sealed class ConverterForm : Form
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(3) };
    private readonly ListBox files = new() { Dock = DockStyle.Fill, AllowDrop = true };
    private readonly Label status = new() { Dock = DockStyle.Bottom, Height = 28, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
    private readonly Button convert = new() { Text = "Convert to ZIP", AutoSize = true };
    private readonly HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);

    public ConverterForm()
    {
        Text = ".mrpack to .zip Converter";
        Width = 680;
        Height = 460;
        MinimumSize = new System.Drawing.Size(500, 340);
        StartPosition = FormStartPosition.CenterScreen;

        var hint = new Label
        {
            Text = "Drop Modrinth .mrpack files here. Downloads listed mods and includes overrides.",
            Dock = DockStyle.Top,
            Height = 48,
            TextAlign = System.Drawing.ContentAlignment.MiddleCenter
        };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        var add = new Button { Text = "Add files…", AutoSize = true };
        var clear = new Button { Text = "Clear list", AutoSize = true };
        convert.Click += async (_, _) => await ConvertFilesAsync();
        add.Click += (_, _) => PickFiles();
        clear.Click += (_, _) => { files.Items.Clear(); paths.Clear(); UpdateStatus(); };
        buttons.Controls.Add(convert);
        buttons.Controls.Add(add);
        buttons.Controls.Add(clear);
        Controls.Add(files);
        Controls.Add(buttons);
        Controls.Add(status);
        Controls.Add(hint);

        files.DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        files.DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] dropped) AddPaths(dropped);
        };
        UpdateStatus();
    }

    private void PickFiles()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Select .mrpack files",
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
        if (files.Items.Count == 0) return;

        convert.Enabled = false;
        var succeeded = 0;
        var failures = new List<string>();
        var queue = files.Items.Cast<object>().Select(x => x.ToString()).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().ToArray();

        try
        {
            for (var i = 0; i < queue.Length; i++)
            {
                var source = queue[i];
                status.Text = $"Converting {i + 1} of {queue.Length}: {Path.GetFileName(source)}";
                try
                {
                    await Task.Run(() => ConvertOne(source));
                    succeeded++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or HttpRequestException or TaskCanceledException or CryptographicException or NotSupportedException)
                {
                    failures.Add($"{Path.GetFileName(source)}: {ex.Message}");
                }
            }
        }
        finally
        {
            convert.Enabled = true;
        }

        status.Text = $"Converted: {succeeded}    Failed: {failures.Count}";
        if (failures.Count > 0)
        {
            var details = string.Join(Environment.NewLine + Environment.NewLine, failures.Take(8));
            if (failures.Count > 8) details += Environment.NewLine + $"…and {failures.Count - 8} more.";
            MessageBox.Show(this, "Some modpacks could not be converted. No partial ZIP was kept for a failed conversion." + Environment.NewLine + Environment.NewLine + details,
                "Conversion results", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static void ConvertOne(string source)
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

            var outputPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var outputFile = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            using (var output = new ZipArchive(outputFile, ZipArchiveMode.Create))
            {
                // Merge common and side-specific overrides into the instance root.
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

                foreach (var file in fileList.EnumerateArray())
                {
                    if (!file.TryGetProperty("path", out var pathElement) || pathElement.ValueKind != JsonValueKind.String)
                        throw new InvalidDataException("A file entry is missing its path.");
                    var relative = NormalizeRelativePath(pathElement.GetString()!);
                    if (!file.TryGetProperty("downloads", out var downloads) || downloads.ValueKind != JsonValueKind.Array || downloads.GetArrayLength() == 0)
                        throw new InvalidDataException($"No download URL is provided for '{relative}'.");

                    var urls = downloads.EnumerateArray()
                        .Where(x => x.ValueKind == JsonValueKind.String)
                        .Select(x => x.GetString()!)
                        .ToArray();

                    Exception? lastError = null;
                    var downloaded = false;
                    foreach (var url in urls)
                    {
                        try
                        {
                            var uri = new Uri(url, UriKind.Absolute);
                            if (uri.Scheme != Uri.UriSchemeHttps)
                                throw new InvalidDataException("Only HTTPS download URLs are allowed.");

                            using var response = Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
                            response.EnsureSuccessStatusCode();
                            var downloadTemp = Path.Combine(Path.GetTempPath(), "AppConverter-" + Guid.NewGuid().ToString("N") + ".tmp");
                            try
                            {
                                using var remote = response.Content.ReadAsStream();
                                using var sha1Hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
                                using var sha512Hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA512);
                                var expectedSha1 = GetExpectedHash(file, "sha1");
                                var expectedSha512 = GetExpectedHash(file, "sha512");

                                using (var tempStream = new FileStream(downloadTemp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                                {
                                    var buffer = new byte[81920];
                                    int read;
                                    while ((read = remote.Read(buffer, 0, buffer.Length)) > 0)
                                    {
                                        sha1Hash.AppendData(buffer, 0, read);
                                        sha512Hash.AppendData(buffer, 0, read);
                                        tempStream.Write(buffer, 0, read);
                                    }
                                }

                                if (expectedSha1 is not null &&
                                    !string.Equals(Convert.ToHexString(sha1Hash.GetHashAndReset()), expectedSha1, StringComparison.OrdinalIgnoreCase))
                                    throw new CryptographicException($"SHA-1 verification failed for '{relative}'.");
                                if (expectedSha512 is not null &&
                                    !string.Equals(Convert.ToHexString(sha512Hash.GetHashAndReset()), expectedSha512, StringComparison.OrdinalIgnoreCase))
                                    throw new CryptographicException($"SHA-512 verification failed for '{relative}'.");

                                AddUniquePath(outputPaths, relative);
                                var target = output.CreateEntry(relative, CompressionLevel.Optimal);
                                using var targetStream = target.Open();
                                using var tempInput = File.OpenRead(downloadTemp);
                                tempInput.CopyTo(targetStream);
                                downloaded = true;
                            }
                            finally
                            {
                                if (File.Exists(downloadTemp)) File.Delete(downloadTemp);
                            }
                            break;
                        }
                        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException or InvalidDataException)
                        {
                            lastError = ex;
                        }
                    }

                    if (!downloaded)
                        throw new IOException($"Could not download '{relative}'. {lastError?.Message ?? "No usable download URL."}", lastError);
                }
            }

            // Move only after the ZIP archive has been fully written and closed.
            File.Move(temporary, destination);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static string? GetExpectedHash(JsonElement file, string algorithm)
    {
        if (!file.TryGetProperty("hashes", out var hashes) || hashes.ValueKind != JsonValueKind.Object)
            return null;
        return hashes.TryGetProperty(algorithm, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static string? GetOverridePath(string entryName)
    {
        foreach (var folder in new[] { "overrides/", "client-overrides/", "server-overrides/" })
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
        if (!paths.Add(path))
            throw new InvalidDataException($"Multiple files target the same path: '{path}'.");
    }

    private void UpdateStatus() => status.Text = $"{files.Items.Count} .mrpack file(s) queued";
}
