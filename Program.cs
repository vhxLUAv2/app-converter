using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    private readonly ListBox files = new() { Dock = DockStyle.Fill, AllowDrop = true };
    private readonly Label status = new() { Dock = DockStyle.Bottom, Height = 28, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
    private readonly HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);

    public ConverterForm()
    {
        Text = ".mrpack to .zip Converter";
        Width = 620;
        Height = 440;
        MinimumSize = new System.Drawing.Size(480, 320);
        StartPosition = FormStartPosition.CenterScreen;

        var hint = new Label
        {
            Text = "Drop .mrpack files here. Files are renamed to .zip; contents are not modified.",
            Dock = DockStyle.Top,
            Height = 48,
            TextAlign = System.Drawing.ContentAlignment.MiddleCenter
        };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        var convert = new Button { Text = "Rename to .zip", AutoSize = true };
        var add = new Button { Text = "Add files…", AutoSize = true };
        var clear = new Button { Text = "Clear list", AutoSize = true };
        convert.Click += (_, _) => ConvertFiles();
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
            Filter = "Modrinth modpacks (*.mrpack)|*.mrpack|All files (*.*)|*.*",
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

    private void ConvertFiles()
    {
        int converted = 0, skipped = 0;
        var remaining = new List<string>();
        foreach (var item in files.Items)
        {
            var source = item?.ToString();
            if (string.IsNullOrWhiteSpace(source) || !File.Exists(source)) { skipped++; continue; }
            var destination = Path.ChangeExtension(source, ".zip");
            if (File.Exists(destination)) { skipped++; remaining.Add(source); continue; }

            try
            {
                File.Move(source, destination);
                paths.Remove(source);
                converted++;
            }
            catch (IOException) { skipped++; remaining.Add(source); }
            catch (UnauthorizedAccessException) { skipped++; remaining.Add(source); }
        }

        files.Items.Clear();
        paths.Clear();
        foreach (var path in remaining)
        {
            files.Items.Add(path);
            paths.Add(path);
        }
        status.Text = $"Renamed: {converted}    Skipped: {skipped}    Remaining: {remaining.Count}";
        if (skipped > 0)
            MessageBox.Show(this, "Some files were skipped. A destination .zip may already exist, or the file may be inaccessible.", "Conversion completed", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void UpdateStatus() => status.Text = $"{files.Items.Count} .mrpack file(s) queued";
}
