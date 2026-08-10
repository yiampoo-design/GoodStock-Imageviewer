using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace WpfApp1
{
    public partial class MetadataDialog : Window
    {
        private readonly string _filePath;
        private readonly string? _exifToolPath;
        private System.Windows.Point _dragStart;
        private bool _isDragging;

        public MetadataDialog(string filePath)
        {
            InitializeComponent();
            _filePath = filePath;
            _exifToolPath = FindExifTool();
            _ = LoadMetadataAsync();
        }

        private static string? FindExifTool()
        {
            var appTools = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools");
            var writableTools = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WpfApp1", "tools");
            var inspection = ExifToolService.Inspect(appTools, writableTools);
            return inspection.Valid ? inspection.BinaryPath : null;
        }

        private async Task LoadMetadataAsync()
        {
            var fi = new FileInfo(_filePath);
            MetaFileName.Text = fi.Name;
            MetaFileInfo.Text = $"{fi.Length / 1024.0 / 1024.0:F1} MB  ·  {fi.LastWriteTime:yyyy-MM-dd HH:mm}";

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(_filePath, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                MetaPreview.Source = bitmap;
            }
            catch { }

            if (_exifToolPath == null)
            {
                MetaOriginalData.Text = "ExifTool is not installed. Cannot read metadata.";
                return;
            }

            try
            {
                var metadata = await ReadMetadataWithExifToolAsync();
                if (metadata == null)
                {
                    MetaOriginalData.Text = "No metadata found in this image.";
                    return;
                }

                var title = GetJsonString(metadata, "XMP-dc:Title") ?? GetJsonString(metadata, "IPTC:ObjectName") ?? "";
                var description = GetJsonString(metadata, "XMP-dc:Description") ?? GetJsonString(metadata, "IPTC:Caption-Abstract") ?? GetJsonString(metadata, "ImageDescription") ?? "";
                var artist = GetJsonString(metadata, "XMP-dc:Creator") ?? GetJsonString(metadata, "Artist") ?? GetJsonString(metadata, "IPTC:By-line") ?? "";
                var copyright = GetJsonString(metadata, "Copyright") ?? "";
                var keywords = GetJsonString(metadata, "XMP-dc:Subject") ?? GetJsonString(metadata, "IPTC:Keywords") ?? "";
                var dateTaken = GetJsonString(metadata, "DateTimeOriginal") ?? GetJsonString(metadata, "DateTime") ?? "";
                var make = GetJsonString(metadata, "Make") ?? "";
                var model = GetJsonString(metadata, "Model") ?? "";
                var lens = GetJsonString(metadata, "LensModel") ?? "";

                MetaTitle.Text = !string.IsNullOrEmpty(title) ? title : "";
                MetaDescription.Text = !string.IsNullOrEmpty(description) ? description : "";
                MetaCreator.Text = !string.IsNullOrEmpty(artist) ? artist : "";
                MetaCopyright.Text = !string.IsNullOrEmpty(copyright) ? copyright : "";
                MetaTags.Text = !string.IsNullOrEmpty(keywords) ? keywords : "";
                MetaDateTaken.Text = !string.IsNullOrEmpty(dateTaken) ? dateTaken : "";

                var lines = new List<string>();
                if (!string.IsNullOrEmpty(make)) lines.Add($"Make: {make}");
                if (!string.IsNullOrEmpty(model)) lines.Add($"Model: {model}");
                if (!string.IsNullOrEmpty(lens)) lines.Add($"Lens: {lens}");
                if (!string.IsNullOrEmpty(title)) lines.Add($"Title: {title}");
                if (!string.IsNullOrEmpty(description)) lines.Add($"Description: {description}");
                if (!string.IsNullOrEmpty(artist)) lines.Add($"Artist: {artist}");
                if (!string.IsNullOrEmpty(copyright)) lines.Add($"Copyright: {copyright}");
                if (!string.IsNullOrEmpty(keywords)) lines.Add($"Keywords: {keywords}");
                if (!string.IsNullOrEmpty(dateTaken)) lines.Add($"Date Taken: {dateTaken}");

                MetaOriginalData.Text = lines.Count > 0 ? string.Join("\n", lines) : "No metadata found.";
            }
            catch (Exception ex)
            {
                MetaOriginalData.Text = $"Error: {ex.Message}";
            }
        }

        private async Task<JsonElement?> ReadMetadataWithExifToolAsync()
        {
            if (_exifToolPath == null) return null;

            try
            {
                var args = new List<string>
                {
                    "-json",
                    "-XMP-dc:Title", "-XMP-dc:Description", "-XMP-dc:Subject", "-XMP-dc:Creator",
                    "-IPTC:ObjectName", "-IPTC:Caption-Abstract", "-IPTC:Keywords", "-IPTC:By-line",
                    "-ImageDescription", "-Artist", "-Copyright",
                    "-DateTimeOriginal", "-DateTime",
                    "-Make", "-Model", "-LensModel",
                    "-UserComment", "--", _filePath
                };

                var result = await ExifToolRunner.RunAsync(_exifToolPath, args, TimeSpan.FromSeconds(10));
                if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StandardOutput)) return null;

                var arr = JsonSerializer.Deserialize<JsonElement>(result.StandardOutput);
                if (arr.ValueKind == JsonValueKind.Array && arr.GetArrayLength() > 0)
                    return arr[0];
                return null;
            }
            catch { return null; }
        }

        private static string? GetJsonString(JsonElement? metadata, string tag)
        {
            if (metadata == null || metadata.Value.ValueKind == JsonValueKind.Null) return null;
            var m = metadata.Value;
            if (m.TryGetProperty(tag, out var val))
            {
                if (val.ValueKind == JsonValueKind.String)
                    return val.GetString();
                if (val.ValueKind == JsonValueKind.Array)
                {
                    var parts = new List<string>();
                    foreach (var item in val.EnumerateArray())
                        if (item.ValueKind == JsonValueKind.String)
                            parts.Add(item.GetString()!);
                    return parts.Count > 0 ? string.Join(", ", parts) : null;
                }
                return val.ToString();
            }
            return null;
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            if (_exifToolPath == null)
            {
                MessageBox.Show("ExifTool is not installed. Cannot save metadata.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                var args = new List<string>
                {
                    "-overwrite_original",
                    "-sep", ", ",
                };

                if (!string.IsNullOrWhiteSpace(MetaTitle.Text))
                {
                    args.Add($"-XMP-dc:Title={MetaTitle.Text}");
                    args.Add($"-IPTC:ObjectName={MetaTitle.Text}");
                }
                else
                {
                    args.Add("-XMP-dc:Title=");
                    args.Add("-IPTC:ObjectName=");
                }

                if (!string.IsNullOrWhiteSpace(MetaDescription.Text))
                {
                    args.Add($"-XMP-dc:Description={MetaDescription.Text}");
                    args.Add($"-IPTC:Caption-Abstract={MetaDescription.Text}");
                    args.Add($"-ImageDescription={MetaDescription.Text}");
                }
                else
                {
                    args.Add("-XMP-dc:Description=");
                    args.Add("-IPTC:Caption-Abstract=");
                    args.Add("-ImageDescription=");
                }

                if (!string.IsNullOrWhiteSpace(MetaCreator.Text))
                {
                    args.Add($"-XMP-dc:Creator={MetaCreator.Text}");
                    args.Add($"-Artist={MetaCreator.Text}");
                    args.Add($"-IPTC:By-line={MetaCreator.Text}");
                }
                else
                {
                    args.Add("-XMP-dc:Creator=");
                    args.Add("-Artist=");
                    args.Add("-IPTC:By-line=");
                }

                if (!string.IsNullOrWhiteSpace(MetaCopyright.Text))
                    args.Add($"-Copyright={MetaCopyright.Text}");
                else
                    args.Add("-Copyright=");

                if (!string.IsNullOrWhiteSpace(MetaTags.Text))
                {
                    args.Add($"-XMP-dc:Subject={MetaTags.Text}");
                    args.Add($"-IPTC:Keywords={MetaTags.Text}");
                }
                else
                {
                    args.Add("-XMP-dc:Subject=");
                    args.Add("-IPTC:Keywords=");
                }

                args.Add("--");
                args.Add(_filePath);

                var result = await ExifToolRunner.RunAsync(_exifToolPath, args, TimeSpan.FromSeconds(30));
                if (result.ExitCode != 0)
                {
                    var errMsg = result.StandardError.Length > 200 ? result.StandardError[..200] : result.StandardError;
                    MessageBox.Show($"Error saving metadata:\n{errMsg}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                MessageBox.Show("Metadata saved successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving metadata: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ResetFromExif_Click(object sender, RoutedEventArgs e)
        {
            _ = LoadMetadataAsync();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Titlebar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStart = e.GetPosition(this);
            _isDragging = true;
            CaptureMouse();
        }

        private void Titlebar_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging) return;
            var pos = e.GetPosition(this);
            Left += pos.X - _dragStart.X;
            Top += pos.Y - _dragStart.Y;
        }

        private void Titlebar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
            ReleaseMouseCapture();
        }
    }
}
