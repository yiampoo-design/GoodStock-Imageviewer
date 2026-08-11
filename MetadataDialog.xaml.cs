using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using WpfApp1.Models;
using WpfApp1.Services;

namespace WpfApp1
{
    public partial class MetadataDialog : Window
    {
        private readonly string _filePath;
        private readonly IMetadataService _metadataService;
        private System.Windows.Point _dragStart;
        private bool _isDragging;

        public MetadataDialog(string filePath)
        {
            InitializeComponent();
            _filePath = filePath;
            _metadataService = new MetadataService();
            _ = LoadMetadataAsync();
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

            if (!await _metadataService.IsAvailableAsync())
            {
                MetaOriginalData.Text = "ExifTool is not installed. Cannot read metadata.";
                return;
            }

            try
            {
                var meta = await _metadataService.ReadMetadataAsync(_filePath);
                if (meta == null)
                {
                    MetaOriginalData.Text = "No metadata found in this image.";
                    return;
                }

                MetaTitle.Text = meta.Title ?? "";
                MetaDescription.Text = meta.Description ?? "";
                MetaCreator.Text = meta.Creator ?? "";
                MetaCopyright.Text = meta.Copyright ?? "";
                MetaTags.Text = meta.Keywords.Count > 0 ? string.Join(", ", meta.Keywords) : "";
                MetaDateTaken.Text = meta.DateTaken?.ToString("yyyy-MM-dd HH:mm:ss") ?? "";

                var lines = new List<string>();
                if (!string.IsNullOrEmpty(meta.Make)) lines.Add($"Make: {meta.Make}");
                if (!string.IsNullOrEmpty(meta.Model)) lines.Add($"Model: {meta.Model}");
                if (!string.IsNullOrEmpty(meta.Lens)) lines.Add($"Lens: {meta.Lens}");
                if (!string.IsNullOrEmpty(meta.Title)) lines.Add($"Title: {meta.Title}");
                if (!string.IsNullOrEmpty(meta.Description)) lines.Add($"Description: {meta.Description}");
                if (!string.IsNullOrEmpty(meta.Creator)) lines.Add($"Creator: {meta.Creator}");
                if (!string.IsNullOrEmpty(meta.Copyright)) lines.Add($"Copyright: {meta.Copyright}");
                if (meta.Keywords.Count > 0) lines.Add($"Keywords: {string.Join(", ", meta.Keywords)}");
                if (meta.DateTaken != null) lines.Add($"Date Taken: {meta.DateTaken:yyyy-MM-dd HH:mm:ss}");
                if (!string.IsNullOrEmpty(meta.IccProfile)) lines.Add($"ICC Profile: {meta.IccProfile}");
                if (meta.GpsLatitude != null) lines.Add($"GPS: {meta.GpsLatitude}, {meta.GpsLongitude}");

                MetaOriginalData.Text = lines.Count > 0 ? string.Join("\n", lines) : "No metadata found.";
            }
            catch (Exception ex)
            {
                MetaOriginalData.Text = $"Error: {ex.Message}";
            }
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!await _metadataService.IsAvailableAsync())
            {
                MessageBox.Show("ExifTool is not installed. Cannot save metadata.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                var keywords = string.IsNullOrWhiteSpace(MetaTags.Text)
                    ? new List<string>()
                    : new List<string>(MetaTags.Text.Split(new[] { ", ", "," }, StringSplitOptions.RemoveEmptyEntries));

                var patch = new MetadataPatch
                {
                    Title = MetaTitle.Text,
                    Description = MetaDescription.Text,
                    Creator = MetaCreator.Text,
                    Copyright = MetaCopyright.Text,
                    Keywords = keywords,
                    DateTaken = MetaDateTaken.Text,
                };

                var result = await _metadataService.WriteMetadataAsync(_filePath, patch);
                if (!result.Success)
                {
                    MessageBox.Show($"Error saving metadata:\n{result.ErrorMessage}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
