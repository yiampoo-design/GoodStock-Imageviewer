using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using Microsoft.Win32;
using Directory = System.IO.Directory;
using Microsoft.VisualBasic.FileIO;

namespace WpfApp1
{
    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<ThumbItem> _thumbs = new();
        private readonly List<string> _history = new();
        private int _historyIndex = -1;
        private bool _isFullscreen;
        private WindowState _savedWindowState;
        private WindowStyle _savedWindowStyle;
        private bool _savedTopmost;
        private Thickness _savedMargin;
        private bool _suppressTreeSelection;
        private readonly List<ThumbItem> _selectedItems = new();
        private int _lastClickIndex = -1;
        private string _currentFolder = "";
        private string _currentPreviewPath = "";
        private readonly List<string> _imageFiles = new();
        private int _viewerIndex = -1;
        private double _viewerZoom = 1.0;
        private double _viewerRotation = 0;
        private double _viewerOrigWidth;
        private double _viewerOrigHeight;
        private double _savedWindowLeft;
        private double _savedWindowTop;
        private double _savedWindowWidth;
        private double _savedWindowHeight;
        private WindowStartupLocation _savedStartupLocation;
        private bool _isDraggingViewer;
        private Point _dragScreenStart;
        private double _dragWindowStartX;
        private double _dragWindowStartY;
        private bool _viewerDragReady;
        private const double DragThreshold = 4.0;
        private bool _isPanning;
        private Point _lastPanPoint;
        private double _panX;
        private double _panY;
        private bool _isMiddleDrag;
        private Point _lastMiddleDragPoint;
        private double _savedZoomBeforeMiddleDrag;
        private bool _isInViewer;

        private string _sortBy = "name";
        private bool _sortAscending = true;
        private string? _exifToolPath;
        private CancellationTokenSource? _metadataCts;

        private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
            { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".tif", ".webp", ".heic", ".ico" };

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            AllowDrop = true;
            Drop += MainWindow_Drop;
            DragOver += MainWindow_DragOver;

            BuildFolderTree();
            LoadDefaultFolder();
            _ = EnsureExifToolAsync();
        }

        private async Task EnsureExifToolAsync()
        {
            var appTools = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools");
            var writableTools = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WpfApp1", "tools");
            var inspection = ExifToolService.Inspect(appTools, writableTools);

            if (inspection.Valid)
            {
                _exifToolPath = inspection.BinaryPath;
                ExifToolBadge.Visibility = Visibility.Collapsed;
                return;
            }

            ExifToolBadge.Visibility = Visibility.Visible;
            ExifToolStatusText.Text = "Installing ExifTool...";

            try
            {
                Directory.CreateDirectory(writableTools);
                var archivePath = Path.Combine(writableTools, "exiftool.zip");
                await ExifToolService.DownloadArchiveAsync(archivePath, null, CancellationToken.None);
                var binaryPath = await ExifToolService.InstallArchiveAtomicallyAsync(archivePath, writableTools, CancellationToken.None);
                _exifToolPath = binaryPath;
                ExifToolBadge.Visibility = Visibility.Collapsed;
            }
            catch
            {
                ExifToolStatusText.Text = "ExifTool unavailable";
            }
        }

        #region Drag & Drop

        private void MainWindow_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            e.Handled = true;
        }

        private void MainWindow_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (paths == null || paths.Length == 0) return;

            var first = paths[0];
            if (Directory.Exists(first))
                LoadFolder(first);
            else if (File.Exists(first) && ImageExtensions.Contains(Path.GetExtension(first)))
            {
                var dir = Path.GetDirectoryName(first);
                if (dir != null) LoadFolder(dir);
            }
        }

        #endregion

        #region Folder Tree

        private void BuildFolderTree()
        {
            FolderTree.Items.Clear();
            var rootIcon = new TextBlock
            {
                Text = "\uE80F",
                FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
                FontSize = 14,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#58A6FF")),
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Margin = new System.Windows.Thickness(0, 0, 6, 0)
            };
            var rootText = new TextBlock
            {
                Text = "Quick Access",
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };
            var rootPanel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
            rootPanel.Children.Add(rootIcon);
            rootPanel.Children.Add(rootText);
            var root = new TreeViewItem
            {
                Header = rootPanel,
                Tag = "root",
                IsExpanded = true
            };

            var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

            var special = new[] { ("Pictures", pictures), ("Desktop", desktop), ("Downloads", downloads) };
            foreach (var (name, path) in special)
            {
                if (Directory.Exists(path))
                {
                    var item = CreateFolderItem(name, path);
                    root.Items.Add(item);
                }
            }

            var drives = DriveInfo.GetDrives()
                .Where(d => d.IsReady && d.DriveType == DriveType.Fixed);
            foreach (var drive in drives)
            {
                string label = drive.Name.TrimEnd('\\');
                double freeGB = drive.AvailableFreeSpace / 1024.0 / 1024.0 / 1024.0;
                var item = CreateFolderItem($"{label}  ({freeGB:F1} GB free)", drive.Name);
                root.Items.Add(item);
            }

            FolderTree.Items.Add(root);
            root.IsSelected = true;
        }

        private TreeViewItem CreateFolderItem(string header, string path)
        {
            var icon = new System.Windows.Controls.Image
            {
                Width = 18,
                Height = 18,
                Source = (System.Windows.Media.ImageSource)FindResource("FolderIcon"),
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Margin = new System.Windows.Thickness(0, 0, 6, 0)
            };
            var text = new TextBlock
            {
                Text = header,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };
            var panel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
            panel.Children.Add(icon);
            panel.Children.Add(text);

            var item = new TreeViewItem
            {
                Header = panel,
                Tag = path
            };
            item.Expanded += FolderItem_Expanded;
            item.Collapsed += FolderItem_Collapsed;
            item.Items.Add(new TreeViewItem { Header = "", Tag = "__dummy__" });
            return item;
        }

        private void FolderItem_Expanded(object sender, RoutedEventArgs e)
        {
            var item = sender as TreeViewItem;
            if (item == null) return;
            UpdateFolderIcon(item, true);
            if (item.Items.Count != 1 || !(item.Items[0] is TreeViewItem t) || t.Tag?.ToString() != "__dummy__") return;

            string? path = item.Tag?.ToString();
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;

            item.Items.Clear();
            try
            {
                foreach (var subdir in Directory.GetDirectories(path))
                {
                    var name = Path.GetFileName(subdir);
                    if (name.StartsWith(".")) continue;
                    item.Items.Add(CreateFolderItem(name, subdir));
                }
            }
            catch { }
        }

        private void FolderItem_Collapsed(object sender, RoutedEventArgs e)
        {
            var item = sender as TreeViewItem;
            if (item != null) UpdateFolderIcon(item, false);
        }

        private void UpdateFolderIcon(TreeViewItem item, bool isExpanded)
        {
            if (item.Header is StackPanel panel && panel.Children.Count > 0 && panel.Children[0] is System.Windows.Controls.Image img)
            {
                img.Source = (System.Windows.Media.ImageSource)FindResource(
                    isExpanded ? "FolderOpenIcon" : "FolderIcon");
            }
        }

        private void FolderTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (_suppressTreeSelection) return;
            if (FolderTree.SelectedItem is TreeViewItem selected)
            {
                var tag = selected.Tag?.ToString() ?? "";
                if (Directory.Exists(tag))
                    LoadFolder(tag);
            }
        }

        private void LoadDefaultFolder()
        {
            var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            if (Directory.Exists(pictures))
                LoadFolder(pictures);
            else
                LoadFolder(Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
        }

        private async void LoadFolder(string path)
        {
            _currentFolder = path;
            _imageFiles.Clear();
            _thumbs.Clear();
            ClearSelection();

            if (_historyIndex < 0 || _history[_historyIndex] != path)
            {
                _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
                _history.Add(path);
                _historyIndex = _history.Count - 1;
            }

            StatusPath.Text = path;
            ThumbsGrid.ItemsSource = _thumbs;
            UpdateBreadcrumbs(path);

            int idx = 1;
            try
            {
                var dirs = await Task.Run(() =>
                    Directory.GetDirectories(path)
                        .Where(d => !Path.GetFileName(d).StartsWith("."))
                        .Select(d => new { Name = Path.GetFileName(d), Path = d })
                        .ToList());

                foreach (var d in dirs)
                {
                    _thumbs.Add(new ThumbItem
                    {
                        FilePath = d.Path,
                        FileName = d.Name,
                        FileType = "Folder",
                        IsFolder = true,
                        Index = idx++
                    });
                }
            }
            catch { }

            StatusFiles.Text = $"{_thumbs.Count} items";
            StatusSelection.Text = "0 selected";

            _ = LoadFolderThumbnailsAsync();

            try
            {
                var imageFiles = await Task.Run(() =>
                    Directory.EnumerateFiles(path)
                        .Where(f => ImageExtensions.Contains(Path.GetExtension(f)))
                        .OrderBy(f => f)
                        .ToList());

                foreach (var file in imageFiles)
                {
                    _imageFiles.Add(file);
                    var fi = new FileInfo(file);
                    _thumbs.Add(new ThumbItem
                    {
                        FilePath = file,
                        FileName = fi.Name,
                        FileSize = FormatFileSize(fi.Length),
                        FileType = fi.Extension.TrimStart('.').ToUpper(),
                        Index = idx++
                    });
                }
            }
            catch { }

            StatusFiles.Text = $"{_thumbs.Count} items";

            ApplySort();

            _ = LoadThumbnailsAsync();
            SelectTreeItem(path);

            if (_currentFolder != null)
                Title = $"GoodStock Image Viewer — {_currentFolder}";
        }

        private void SelectTreeItem(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            var normalized = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            _suppressTreeSelection = true;
            foreach (var rootItem in FolderTree.Items)
            {
                if (rootItem is TreeViewItem root)
                {
                    if (SelectTreeItemRecursive(root, normalized))
                        break;
                }
            }
            _suppressTreeSelection = false;
        }

        private bool SelectTreeItemRecursive(TreeViewItem parent, string path)
        {
            foreach (var child in parent.Items)
            {
                if (child is TreeViewItem item && item.Tag is string tag)
                {
                    var normalizedTag = tag.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    if (string.Equals(normalizedTag, path, StringComparison.OrdinalIgnoreCase))
                    {
                        item.IsExpanded = true;
                        item.IsSelected = true;
                        item.BringIntoView();
                        return true;
                    }
                    if (path.StartsWith(normalizedTag + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    {
                        item.IsExpanded = true;
                        if (SelectTreeItemRecursive(item, path))
                            return true;
                    }
                }
            }
            return false;
        }

        private async Task LoadFolderThumbnailsAsync()
        {
            var folders = _thumbs.Where(t => t.IsFolder).ToList();
            foreach (var folder in folders)
            {
                try
                {
                    var images = await Task.Run(() =>
                        Directory.GetFiles(folder.FilePath)
                            .Where(f => ImageExtensions.Contains(Path.GetExtension(f)))
                            .Take(4)
                            .ToList());
                    if (images.Count == 0) continue;

                    if (images.Count == 1)
                    {
                        var bi = new BitmapImage();
                        bi.BeginInit();
                        bi.UriSource = new Uri(images[0], UriKind.Absolute);
                        bi.CacheOption = BitmapCacheOption.OnLoad;
                        bi.DecodePixelWidth = 200;
                        bi.EndInit();
                        bi.Freeze();
                        folder.Thumbnail = bi;
                    }
                    else
                    {
                        folder.Thumbnail = CreateCompositeThumbnail(images);
                    }
                }
                catch { }
                await Task.Delay(1);
            }
        }

        private static BitmapSource? CreateCompositeThumbnail(List<string> imagePaths)
        {
            int gridSize = 2;
            int cellSize = 100;
            int totalSize = cellSize * gridSize;
            var cellImages = new List<BitmapImage>();

            foreach (var path in imagePaths)
            {
                try
                {
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.UriSource = new Uri(path, UriKind.Absolute);
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.DecodePixelWidth = cellSize;
                    bi.EndInit();
                    bi.Freeze();
                    cellImages.Add(bi);
                }
                catch { }
            }

            if (cellImages.Count == 0) return null;

            var visual = new System.Windows.Media.DrawingVisual();
            using (var ctx = visual.RenderOpen())
            {
                var bgBrush = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1E2530"));
                ctx.DrawRectangle(bgBrush, null, new System.Windows.Rect(0, 0, totalSize, totalSize));

                for (int i = 0; i < cellImages.Count; i++)
                {
                    int col = i % gridSize;
                    int row = i / gridSize;
                    double x = col * cellSize;
                    double y = row * cellSize;

                    var img = cellImages[i];
                    double scaleW = (double)cellSize / img.PixelWidth;
                    double scaleH = (double)cellSize / img.PixelHeight;
                    double scale = Math.Max(scaleW, scaleH);
                    double w = img.PixelWidth * scale;
                    double h = img.PixelHeight * scale;
                    double offsetX = x + (cellSize - w) / 2;
                    double offsetY = y + (cellSize - h) / 2;

                    ctx.DrawImage(img, new System.Windows.Rect(offsetX, offsetY, w, h));
                }
            }

            var rtb = new RenderTargetBitmap(totalSize, totalSize, 96, 96,
                System.Windows.Media.PixelFormats.Pbgra32);
            rtb.Render(visual);
            rtb.Freeze();
            return rtb;
        }

        private static string FormatFileSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            return $"{bytes / 1024.0 / 1024.0:F1} MB";
        }

        private void UpdateBreadcrumbs(string path)
        {
            Breadcrumbs.Children.Clear();
            var parts = new List<string>();
            var dir = new DirectoryInfo(path);
            while (dir != null)
            {
                parts.Insert(0, dir.Name);
                dir = dir.Parent;
            }

            for (int i = 0; i < parts.Count; i++)
            {
                if (i > 0)
                {
                    Breadcrumbs.Children.Add(new TextBlock
                    {
                        Text = "›",
                        Foreground = (Brush)FindResource("TextMutedBrush"),
                        Margin = new Thickness(4, 0, 4, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                        FontSize = 12
                    });
                }

                bool isLast = i == parts.Count - 1;
                var tagPath = string.Join(Path.DirectorySeparatorChar.ToString(), parts.Take(i + 1));
                if (i == 0 && parts[0].Contains(":"))
                    tagPath = parts[0] + Path.DirectorySeparatorChar;

                var btn = new Button
                {
                    Content = parts[i],
                    Style = (Style)FindResource("CrumbBtn"),
                    Tag = tagPath
                };
                btn.Click += Breadcrumb_Click;
                if (isLast)
                {
                    btn.FontWeight = FontWeights.SemiBold;
                    btn.Foreground = (Brush)FindResource("TextPrimaryBrush");
                }
                Breadcrumbs.Children.Add(btn);
            }
        }

        private void Breadcrumb_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string path && Directory.Exists(path))
            {
                LoadFolder(path);
            }
        }

        private void NavigateTo(string tag, bool pushHistory) { }

        private void NavigateBack()
        {
            if (_historyIndex > 0)
            {
                _historyIndex--;
                if (Directory.Exists(_history[_historyIndex]))
                    LoadFolder(_history[_historyIndex]);
            }
        }

        private void NavigateUp()
        {
            if (!string.IsNullOrEmpty(_currentFolder))
            {
                var parent = Directory.GetParent(_currentFolder);
                if (parent != null)
                    LoadFolder(parent.FullName);
            }
        }

        #endregion

        #region Image Loading

        private async Task LoadThumbnailsAsync()
        {
            var files = _imageFiles.ToList();
            for (int i = 0; i < files.Count; i++)
            {
                var file = files[i];
                if (!File.Exists(file)) continue;
                try
                {
                    var thumb = _thumbs.FirstOrDefault(t => t.FilePath == file);
                    if (thumb == null) continue;

                    var (bi, dims, mp) = await Task.Run(() =>
                    {
                        var image = new BitmapImage();
                        image.BeginInit();
                        image.UriSource = new Uri(file, UriKind.Absolute);
                        image.CacheOption = BitmapCacheOption.OnLoad;
                        image.DecodePixelWidth = 200;
                        image.EndInit();
                        image.Freeze();

                        string d = "", m = "";
                        try
                        {
                            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                            var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                            if (decoder.Frames.Count > 0)
                            {
                                var frame = decoder.Frames[0];
                                d = $"{frame.PixelWidth}×{frame.PixelHeight}";
                                double mpVal = (double)frame.PixelWidth * frame.PixelHeight / 1_000_000;
                                m = mpVal >= 1.0 ? $"{mpVal:F1} MP" : $"{mpVal * 1000:F0} KP";
                            }
                        }
                        catch { }
                        return (image, d, m);
                    });

                    thumb.Thumbnail = bi;
                    thumb.Dimensions = dims;
                    thumb.Megapixels = mp;
                }
                catch { }

                if (i % 5 == 0)
                    await Task.Delay(1);
            }
        }

        #endregion

        #region Thumbnail Click

        private void Thumb_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ThumbItem item)
            {
                bool ctrl = Keyboard.Modifiers == ModifierKeys.Control;
                bool shift = Keyboard.Modifiers == ModifierKeys.Shift;

                if (ctrl)
                {
                    item.IsSelected = !item.IsSelected;
                    if (item.IsSelected)
                        _selectedItems.Add(item);
                    else
                        _selectedItems.Remove(item);
                }
                else if (shift && _selectedItems.Count > 0)
                {
                    int startIdx = _lastClickIndex;
                    int endIdx = _thumbs.IndexOf(item);
                    if (startIdx > endIdx) (startIdx, endIdx) = (endIdx, startIdx);
                    ClearSelection();
                    for (int i = startIdx; i <= endIdx; i++)
                    {
                        _thumbs[i].IsSelected = true;
                        _selectedItems.Add(_thumbs[i]);
                    }
                }
                else
                {
                    ClearSelection();
                    item.IsSelected = true;
                    _selectedItems.Add(item);
                    _lastClickIndex = _thumbs.IndexOf(item);
                }

                UpdateSelectionStatus(item);
            }
        }

        private void ClearSelection()
        {
            foreach (var item in _selectedItems)
                item.IsSelected = false;
            _selectedItems.Clear();
        }

        private void UpdateSelectionStatus(ThumbItem item)
        {
            if (item.IsFolder)
            {
                int folderCount = 0, fileCount = 0;
                try
                {
                    folderCount = Directory.GetDirectories(item.FilePath).Length;
                    fileCount = Directory.GetFiles(item.FilePath)
                        .Count(f => ImageExtensions.Contains(Path.GetExtension(f)));
                }
                catch { }
                StatusSelection.Text = _selectedItems.Count > 1
                    ? $"{_selectedItems.Count} items selected"
                    : $"{item.FileName}  —  {folderCount} folders, {fileCount} images";
                StatusSize.Text = item.FilePath;
            }
            else
            {
                ShowPreview(item);
                StatusSelection.Text = _selectedItems.Count > 1
                    ? $"{_selectedItems.Count} items selected"
                    : $"#{item.Index}  {item.FileName}";
            }
        }

        private void Thumb_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ThumbItem item)
            {
                if (item.IsFolder)
                {
                    if (Directory.Exists(item.FilePath))
                        LoadFolder(item.FilePath);
                    return;
                }
                _viewerIndex = _thumbs.IndexOf(item);
                ShowViewer(item);
            }
        }

        private void ShowPreview(ThumbItem item)
        {
            _currentPreviewPath = item.FilePath;
            PreviewPlaceholder.Visibility = Visibility.Collapsed;
            PreviewImage.Visibility = Visibility.Visible;

            if (item.Thumbnail != null)
                PreviewImage.Source = item.Thumbnail;
            else if (!string.IsNullOrEmpty(item.FilePath) && File.Exists(item.FilePath))
                LoadPreviewImage(item.FilePath);

            InfoFileName.Text = item.FileName;
            InfoDimensions.Text = $"{item.Dimensions}  {item.FileSize}";
            StatusSelection.Text = $"#{item.Index}  {item.FileName}";
            StatusSize.Text = item.FileSize;

            MetaFileName.Text = item.FileName;
            MetaFileType.Text = item.FileType;
            MetaDimensions.Text = item.Dimensions;
            MetaFileSize.Text = item.FileSize;
            MetaCamera.Text = "—";
            MetaLens.Text = "—";
            MetaDateTaken.Text = "—";
            MetaDescription.Text = "—";
            MetaArtist.Text = "—";
            MetaCopyright.Text = "—";
            try
            {
                var fi = new FileInfo(item.FilePath);
                MetaModified.Text = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm");
                MetaFolder.Text = fi.DirectoryName ?? "—";
            }
            catch
            {
                MetaModified.Text = "—";
                MetaFolder.Text = "—";
            }

            LoadExifData(item.FilePath);
            LoadMetadataPanel(item.FilePath);
            LoadHistogram(item.FilePath);
        }

        private void LoadPreviewImage(string path)
        {
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.UriSource = new Uri(path, UriKind.Absolute);
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.EndInit();
                bi.Freeze();
                PreviewImage.Source = bi;

                var fi = new FileInfo(path);
                InfoDimensions.Text = $"{bi.PixelWidth}×{bi.PixelHeight}  {FormatFileSize(fi.Length)}";
            }
            catch { }
        }



        private static readonly Dictionary<string, string> ExifTagNames = new()
        {
            ["010F"] = "Camera Make", ["0110"] = "Camera Model",
            ["9003"] = "Date Taken", ["0132"] = "Date Modified",
            ["829D"] = "Aperture", ["829A"] = "Exposure Time",
            ["920A"] = "Focal Length", ["8827"] = "ISO Speed",
            ["A433"] = "Lens Model", ["A434"] = "Lens Make",
            ["010E"] = "Description", ["013B"] = "Artist",
            ["8298"] = "Copyright", ["0131"] = "Software",
            ["0112"] = "Orientation", ["9209"] = "Flash",
            ["9207"] = "Metering Mode", ["9286"] = "User Comment",
            ["9C9C"] = "Title", ["9C9E"] = "Author",
            ["9C9F"] = "Tags", ["9C9D"] = "Comment",
        };

        private async void LoadExifData(string path)
        {
            _metadataCts?.Cancel();
            _metadataCts = new CancellationTokenSource();
            var ct = _metadataCts.Token;

            ExifCamera.Text = "—";
            ExifFocalLength.Text = "—";
            ExifFStop.Text = "—";
            ExifShutterSpeed.Text = "—";
            ExifISO.Text = "—";
            ExifLens.Text = "—";
            ExifDateTaken.Text = "—";
            ExifNoData.Visibility = Visibility.Collapsed;

            if (string.IsNullOrEmpty(path) || !File.Exists(path)) { ExifNoData.Visibility = Visibility.Visible; return; }
            if (_exifToolPath == null) { ExifNoData.Text = "ExifTool not installed."; ExifNoData.Visibility = Visibility.Visible; return; }

            bool hasExifData = false;
            try
            {
                var metadata = await ReadMetadataWithExifToolAsync(path);
                if (ct.IsCancellationRequested) return;
                if (metadata == null) { ExifNoData.Visibility = Visibility.Visible; return; }

                var make = GetJsonString(metadata, "Make");
                var model = GetJsonString(metadata, "Model");
                if (!string.IsNullOrEmpty(model))
                { ExifCamera.Text = string.IsNullOrEmpty(make) ? model : $"{make} {model}"; hasExifData = true; }

                var focalLength = GetJsonString(metadata, "FocalLength");
                if (!string.IsNullOrEmpty(focalLength))
                { ExifFocalLength.Text = focalLength; hasExifData = true; }

                var fNumber = GetJsonString(metadata, "FNumber");
                if (!string.IsNullOrEmpty(fNumber))
                { ExifFStop.Text = $"f/{fNumber}"; hasExifData = true; }

                var exposureTime = GetJsonString(metadata, "ExposureTime");
                if (!string.IsNullOrEmpty(exposureTime))
                { ExifShutterSpeed.Text = exposureTime.Contains("/") ? $"1/{double.Parse(exposureTime.Split('/')[1]):F0}s" : $"{exposureTime}s"; hasExifData = true; }

                var iso = GetJsonString(metadata, "ISO");
                if (!string.IsNullOrEmpty(iso))
                { ExifISO.Text = $"ISO {iso}"; hasExifData = true; }

                var lens = GetJsonString(metadata, "LensModel");
                if (!string.IsNullOrEmpty(lens))
                { ExifLens.Text = lens; hasExifData = true; }

                var dateTaken = GetJsonString(metadata, "DateTimeOriginal");
                if (string.IsNullOrEmpty(dateTaken)) dateTaken = GetJsonString(metadata, "DateTime");
                if (!string.IsNullOrEmpty(dateTaken))
                { ExifDateTaken.Text = dateTaken; hasExifData = true; }
            }
            catch { }

            if (!hasExifData)
            {
                ExifCamera.Text = "—"; ExifFocalLength.Text = "—"; ExifFStop.Text = "—";
                ExifShutterSpeed.Text = "—"; ExifISO.Text = "—"; ExifLens.Text = "—"; ExifDateTaken.Text = "—";
                ExifNoData.Text = "No EXIF data available for this image type.";
                ExifNoData.Visibility = Visibility.Visible;
            }
        }

        private async void LoadHistogram(string path)
        {
            var red = new double[] { 60, 80, 45, 70, 55, 35 };
            var green = new double[] { 50, 70, 55, 65, 45, 30 };
            var blue = new double[] { 45, 60, 50, 75, 60, 40 };

            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                SetHistogramBars(red, green, blue);
                return;
            }

            try
            {
                var (r, g, b) = await Task.Run(() =>
                {
                    using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    var ext = Path.GetExtension(path).ToLowerInvariant();
                    BitmapDecoder? decoder = ext switch
                    {
                        ".jpg" or ".jpeg" => new JpegBitmapDecoder(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.None),
                        ".png" => new PngBitmapDecoder(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.None),
                        ".tiff" or ".tif" => new TiffBitmapDecoder(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.None),
                        ".bmp" => new BmpBitmapDecoder(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.None),
                        ".gif" => new GifBitmapDecoder(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.None),
                        _ => null
                    };
                    if (decoder == null || decoder.Frames.Count == 0)
                        return (red, green, blue);

                    var frame = decoder.Frames[0];
                    int maxDim = 256;
                    double scale = Math.Min((double)maxDim / frame.PixelWidth, (double)maxDim / frame.PixelHeight);
                    if (scale > 1) scale = 1;
                    var scaled = new TransformedBitmap(frame, new ScaleTransform(scale, scale));
                    var formatted = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
                    var pixels = new byte[formatted.PixelWidth * formatted.PixelHeight * 4];
                    formatted.CopyPixels(pixels, formatted.PixelWidth * 4, 0);

                    var rHist = new int[256];
                    var gHist = new int[256];
                    var bHist = new int[256];
                    for (int i = 0; i < pixels.Length; i += 4)
                    {
                        bHist[pixels[i]]++;
                        gHist[pixels[i + 1]]++;
                        rHist[pixels[i + 2]]++;
                    }

                    int binsPerBar = 256 / 6;
                    double maxR = 0, maxG = 0, maxB = 0;
                    var rBins = new double[6];
                    var gBins = new double[6];
                    var bBins = new double[6];
                    for (int b2 = 0; b2 < 6; b2++)
                    {
                        for (int i = b2 * binsPerBar; i < (b2 + 1) * binsPerBar; i++)
                        {
                            rBins[b2] += rHist[i];
                            gBins[b2] += gHist[i];
                            bBins[b2] += bHist[i];
                        }
                        if (rBins[b2] > maxR) maxR = rBins[b2];
                        if (gBins[b2] > maxG) maxG = gBins[b2];
                        if (bBins[b2] > maxB) maxB = bBins[b2];
                    }

                    double maxAll = Math.Max(maxR, Math.Max(maxG, maxB));
                    var rr = new double[6];
                    var gg = new double[6];
                    var bb = new double[6];
                    if (maxAll > 0)
                    {
                        for (int i = 0; i < 6; i++)
                        {
                            rr[i] = rBins[i] / maxAll * 90;
                            gg[i] = gBins[i] / maxAll * 90;
                            bb[i] = bBins[i] / maxAll * 90;
                        }
                    }
                    return (rr, gg, bb);
                });
                red = r; green = g; blue = b;
            }
            catch { }

            SetHistogramBars(red, green, blue);
        }

        private void SetHistogramBars(double[] red, double[] green, double[] blue)
        {
            HistRedBars.Children.Clear();
            HistGreenBars.Children.Clear();
            HistBlueBars.Children.Clear();

            for (int i = 0; i < 6; i++)
            {
                HistRedBars.Children.Add(new Border
                {
                    Width = 30, Height = red[i], CornerRadius = new CornerRadius(2),
                    Background = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)),
                    Margin = new Thickness(1, 0, 1, 0)
                });
                HistGreenBars.Children.Add(new Border
                {
                    Width = 30, Height = green[i], CornerRadius = new CornerRadius(2),
                    Background = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E)),
                    Margin = new Thickness(1, 0, 1, 0)
                });
                HistBlueBars.Children.Add(new Border
                {
                    Width = 30, Height = blue[i], CornerRadius = new CornerRadius(2),
                    Background = new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)),
                    Margin = new Thickness(1, 0, 1, 0)
                });
            }
        }

        #endregion

        #region Metadata Panel

        private async void LoadMetadataPanel(string path)
        {
            var ct = _metadataCts?.Token ?? CancellationToken.None;

            MetaCamera.Text = "—";
            MetaLens.Text = "—";
            MetaDateTaken.Text = "—";
            MetaDescription.Text = "—";
            MetaArtist.Text = "—";
            MetaCopyright.Text = "—";

            if (string.IsNullOrEmpty(path) || !File.Exists(path) || _exifToolPath == null) return;

            try
            {
                var metadata = await ReadMetadataWithExifToolAsync(path);
                if (ct.IsCancellationRequested) return;
                if (metadata == null) return;

                var make = GetJsonString(metadata, "Make");
                var model = GetJsonString(metadata, "Model");
                if (!string.IsNullOrEmpty(model))
                    MetaCamera.Text = string.IsNullOrEmpty(make) ? model : $"{make} {model}";

                var lens = GetJsonString(metadata, "LensModel");
                if (!string.IsNullOrEmpty(lens)) MetaLens.Text = lens;

                var dateTaken = GetJsonString(metadata, "DateTimeOriginal");
                if (string.IsNullOrEmpty(dateTaken)) dateTaken = GetJsonString(metadata, "DateTime");
                if (!string.IsNullOrEmpty(dateTaken)) MetaDateTaken.Text = dateTaken;

                var desc = GetJsonString(metadata, "ImageDescription");
                if (string.IsNullOrEmpty(desc)) desc = GetJsonString(metadata, "XMP-dc:Description");
                if (string.IsNullOrEmpty(desc)) desc = GetJsonString(metadata, "IPTC:Caption-Abstract");
                if (!string.IsNullOrEmpty(desc)) MetaDescription.Text = desc;

                var artist = GetJsonString(metadata, "Artist");
                if (string.IsNullOrEmpty(artist)) artist = GetJsonString(metadata, "XMP-dc:Creator");
                if (string.IsNullOrEmpty(artist)) artist = GetJsonString(metadata, "IPTC:By-line");
                if (!string.IsNullOrEmpty(artist)) MetaArtist.Text = artist;

                var copyright = GetJsonString(metadata, "Copyright");
                if (!string.IsNullOrEmpty(copyright)) MetaCopyright.Text = copyright;
            }
            catch { }
        }

        private static string? GetJsonString(JsonElement? metadata, string tag)
        {
            if (metadata == null || metadata.Value.ValueKind == JsonValueKind.Null) return null;
            var m = metadata.Value;

            if (m.TryGetProperty(tag, out var val))
                return ExtractStringValue(val);

            var groupedTags = new[] { $"EXIF:{tag}", $"XMP:{tag}", $"IPTC:{tag}", $"File:{tag}" };
            foreach (var grouped in groupedTags)
            {
                if (m.TryGetProperty(grouped, out var groupedVal))
                    return ExtractStringValue(groupedVal);
            }

            return null;
        }

        private static string? ExtractStringValue(JsonElement val)
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

        private async Task<JsonElement?> ReadMetadataWithExifToolAsync(string path)
        {
            if (_exifToolPath == null) return null;

            try
            {
                var args = new List<string>
                {
                    "-json",
                    "-G1",
                    "-DateTimeOriginal", "-DateTime",
                    "-Make", "-Model", "-LensModel",
                    "-FocalLength", "-FNumber", "-ExposureTime", "-ISO",
                    "-ImageDescription", "-Artist", "-Copyright",
                    "-XMP-dc:Title", "-XMP-dc:Description", "-XMP-dc:Subject", "-XMP-dc:Creator",
                    "-IPTC:ObjectName", "-IPTC:Caption-Abstract", "-IPTC:Keywords", "-IPTC:By-line",
                    "-UserComment", "--", path
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

        private void ManageMetadata_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentPreviewPath) || !File.Exists(_currentPreviewPath)) return;

            var dlg = new MetadataDialog(_currentPreviewPath) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                LoadExifData(_currentPreviewPath);
                LoadMetadataPanel(_currentPreviewPath);
            }
        }

        #endregion

        #region Preview Pane

        private void PreviewToggle_Click(object sender, RoutedEventArgs e)
        {
            PreviewPane.Visibility = PreviewToggle.IsChecked == true
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void PreviewTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Name == "TabMeta")
            {
                ExifPanel.Visibility = Visibility.Collapsed;
                HistogramPanel.Visibility = Visibility.Collapsed;
                MetaPanel.Visibility = Visibility.Visible;
            }
            else
            {
                ExifPanel.Visibility = Visibility.Visible;
                HistogramPanel.Visibility = Visibility.Collapsed;
                MetaPanel.Visibility = Visibility.Collapsed;
            }
        }

        #endregion

        #region Search

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(SearchBox.Text))
            {
                ThumbsGrid.ItemsSource = _thumbs;
            }
            else
            {
                ThumbsGrid.ItemsSource = _thumbs
                    .Where(t => t.FileName.Contains(SearchBox.Text, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
        }

        #endregion

        #region Crop

        private void BtnCrop_Click(object sender, RoutedEventArgs e)
        {
            if (_viewerIndex < 0 || _viewerIndex >= _thumbs.Count || _thumbs[_viewerIndex].IsFolder) return;
            ShowOverlay(CropOverlay);
            if (PreviewImage.Source != null)
                CropImage.Source = PreviewImage.Source;
        }

        private void ApplyCrop_Click(object sender, RoutedEventArgs e)
        {
            HideOverlay(CropOverlay);
        }

        private void CancelCrop_Click(object sender, RoutedEventArgs e)
        {
            HideOverlay(CropOverlay);
        }

        #endregion

        #region Draw

        private void BtnDraw_Click(object sender, RoutedEventArgs e)
        {
            if (_viewerIndex < 0 || _viewerIndex >= _thumbs.Count || _thumbs[_viewerIndex].IsFolder) return;
            ShowOverlay(DrawOverlay);
            if (PreviewImage.Source != null)
                DrawImage.Source = PreviewImage.Source;
        }

        private void ApplyDraw_Click(object sender, RoutedEventArgs e)
        {
            HideOverlay(DrawOverlay);
        }

        private void CancelDraw_Click(object sender, RoutedEventArgs e)
        {
            HideOverlay(DrawOverlay);
        }

        #endregion

        #region Sorting

        private void SortBtn_Click(object sender, RoutedEventArgs e)
        {
            SortPopup.IsOpen = !SortPopup.IsOpen;
        }

        private void SortOption_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string sortBy)
            {
                _sortBy = sortBy;
                SortLabel.Text = sortBy;
                SortPopup.IsOpen = false;
                ApplySort();
            }
        }

        private void SortDirection_Click(object sender, RoutedEventArgs e)
        {
            _sortAscending = !_sortAscending;
            SortDirectionLabel.Text = _sortAscending ? "↑ Ascending" : "↓ Descending";
            ApplySort();
        }

        private void ApplySort()
        {
            if (_thumbs.Count == 0) return;

            var folders = _thumbs.Where(t => t.IsFolder).ToList();
            var files = _thumbs.Where(t => !t.IsFolder).ToList();

            Func<ThumbItem, object> keySelector = _sortBy switch
            {
                "date" => t => GetFileDate(t.FilePath),
                "size" => t => GetFileSizeBytes(t.FilePath),
                "type" => t => Path.GetExtension(t.FilePath)?.ToLowerInvariant() ?? "",
                _ => t => Path.GetFileNameWithoutExtension(t.FilePath)?.ToLowerInvariant() ?? ""
            };

            files = _sortAscending
                ? files.OrderBy(keySelector).ToList()
                : files.OrderByDescending(keySelector).ToList();

            _thumbs.Clear();
            foreach (var f in folders) _thumbs.Add(f);
            foreach (var f in files) _thumbs.Add(f);

            UpdateIndexes();
        }

        private DateTime GetFileDate(string path)
        {
            try { return File.GetLastWriteTime(path); }
            catch { return DateTime.MinValue; }
        }

        private long GetFileSizeBytes(string path)
        {
            try { return new FileInfo(path).Length; }
            catch { return 0; }
        }

        private void UpdateIndexes()
        {
            for (int i = 0; i < _thumbs.Count; i++)
                _thumbs[i].Index = i + 1;
        }

        #endregion

        #region File Operations

        private void DeleteSelectedItems()
        {
            if (_selectedItems.Count == 0) return;
            var names = _selectedItems.Take(3).Select(i => i.FileName).ToList();
            string msg = _selectedItems.Count == 1
                ? $"Move '{names[0]}' to Recycle Bin?"
                : $"Move {_selectedItems.Count} items to Recycle Bin? ({string.Join(", ", names)}...)";

            if (MessageBox.Show(msg, "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                var failed = new List<string>();
                foreach (var item in _selectedItems.ToList())
                {
                    try
                    {
                        if (item.IsFolder)
                            FileSystem.DeleteDirectory(item.FilePath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                        else
                            FileSystem.DeleteFile(item.FilePath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                        _thumbs.Remove(item);
                    }
                    catch (Exception ex)
                    {
                        failed.Add($"{item.FileName}: {ex.Message}");
                    }
                }
                ClearSelection();
                StatusFiles.Text = $"{_thumbs.Count} items";
                StatusSelection.Text = "0 selected";
                if (failed.Count > 0)
                {
                    MessageBox.Show($"Failed to delete {failed.Count} item(s):\n{string.Join("\n", failed.Take(5))}",
                        "Delete Errors", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private ThumbItem? _renameTarget;

        private void RenameItem(ThumbItem item)
        {
            if (item == null) return;
            _renameTarget = item;

            var currentName = item.FileName;
            var ext = item.IsFolder ? "" : Path.GetExtension(currentName);
            var nameWithoutExt = item.IsFolder ? currentName : Path.GetFileNameWithoutExtension(currentName);

            var dialog = new Window
            {
                Title = "Rename",
                Width = 380,
                Height = 160,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = (System.Windows.Media.Brush)FindResource("BgBrush"),
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                BorderBrush = (System.Windows.Media.Brush)FindResource("BorderBrush"),
                BorderThickness = new Thickness(1)
            };

            var stack = new StackPanel { Margin = new Thickness(16) };
            var label = new TextBlock
            {
                Text = item.IsFolder ? "Enter new folder name:" : "Enter new file name:",
                Foreground = (System.Windows.Media.Brush)FindResource("TextPrimaryBrush"),
                FontSize = 12,
                Margin = new Thickness(0, 8, 0, 8)
            };
            var textBox = new TextBox
            {
                Text = currentName,
                FontSize = 13,
                Padding = new Thickness(6, 4, 6, 4),
                Margin = new Thickness(0, 0, 0, 12),
                CaretBrush = (System.Windows.Media.Brush)FindResource("TextPrimaryBrush"),
                Foreground = (System.Windows.Media.Brush)FindResource("TextPrimaryBrush"),
                Background = (System.Windows.Media.Brush)FindResource("BgInstructBrush")
            };
            textBox.SelectAll();

            var btnPanel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancelBtn = new Button
            {
                Content = "Cancel",
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(0, 0, 8, 0),
                Cursor = Cursors.Hand,
                Foreground = (System.Windows.Media.Brush)FindResource("TextPrimaryBrush"),
                Background = (System.Windows.Media.Brush)FindResource("BtnBgBrush")
            };
            cancelBtn.Click += (_, _) => { dialog.DialogResult = false; dialog.Close(); };

            var okBtn = new Button
            {
                Content = "Rename",
                Padding = new Thickness(12, 6, 12, 6),
                Cursor = Cursors.Hand,
                Foreground = System.Windows.Media.Brushes.White,
                Background = (System.Windows.Media.Brush)FindResource("AccentBrush")
            };
            okBtn.Click += (_, _) => { dialog.DialogResult = true; dialog.Close(); };

            btnPanel.Children.Add(cancelBtn);
            btnPanel.Children.Add(okBtn);
            stack.Children.Add(label);
            stack.Children.Add(textBox);
            stack.Children.Add(btnPanel);
            dialog.Content = stack;

            textBox.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) { dialog.DialogResult = true; dialog.Close(); }
                else if (e.Key == Key.Escape) { dialog.DialogResult = false; dialog.Close(); }
            };

            if (dialog.ShowDialog() != true || _renameTarget == null)
            {
                _renameTarget = null;
                return;
            }

            string newName = textBox.Text.Trim();
            if (string.IsNullOrEmpty(newName) || newName == currentName)
            {
                _renameTarget = null;
                return;
            }

            if (!item.IsFolder)
            {
                var newNameExt = Path.GetExtension(newName);
                if (string.IsNullOrEmpty(newNameExt))
                    newName = newName + ext;
            }

            try
            {
                string? dir = Path.GetDirectoryName(item.FilePath);
                if (string.IsNullOrEmpty(dir)) return;
                string newPath = Path.Combine(dir, newName);

                if (File.Exists(newPath) || Directory.Exists(newPath))
                {
                    MessageBox.Show($"An item named '{newName}' already exists.", "Rename", MessageBoxButton.OK, MessageBoxImage.Warning);
                    _renameTarget = null;
                    return;
                }

                if (item.IsFolder)
                    Directory.Move(item.FilePath, newPath);
                else
                    File.Move(item.FilePath, newPath);

                item.FilePath = newPath;
                item.FileName = newName;
                if (!item.IsFolder)
                    item.FileType = Path.GetExtension(newName).TrimStart('.').ToUpper();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Rename failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            _renameTarget = null;
        }

        private ThumbItem? _clipboardItem;
        private bool _clipboardIsCut;

        private void CtxOpen_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is ThumbItem item)
            {
                if (item.IsFolder && Directory.Exists(item.FilePath))
                    LoadFolder(item.FilePath);
                else if (!item.IsFolder && File.Exists(item.FilePath))
                {
                    _viewerIndex = _thumbs.IndexOf(item);
                    ShowViewer(item);
                }
            }
        }

        private void CtxCopy_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is ThumbItem item)
            {
                _clipboardItem = item;
                _clipboardIsCut = false;
                StatusSelection.Text = $"Copied: {item.FileName}";
            }
        }

        private void CtxCut_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is ThumbItem item)
            {
                _clipboardItem = item;
                _clipboardIsCut = true;
                StatusSelection.Text = $"Cut: {item.FileName}";
            }
        }

        private void CtxPaste_Click(object sender, RoutedEventArgs e)
        {
            if (_clipboardItem == null || string.IsNullOrEmpty(_currentFolder)) return;
            try
            {
                string dest = Path.Combine(_currentFolder, _clipboardItem.FileName);
                if (_clipboardItem.IsFolder)
                    Directory.Move(_clipboardItem.FilePath, dest);
                else
                    File.Copy(_clipboardItem.FilePath, dest, false);

                if (_clipboardIsCut)
                    File.Delete(_clipboardItem.FilePath);

                _clipboardItem = null;
                LoadFolder(_currentFolder);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Paste failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CtxDelete_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is ThumbItem item)
            {
                _selectedItems.Clear();
                item.IsSelected = true;
                _selectedItems.Add(item);
                DeleteSelectedItems();
            }
        }

        private void CtxRename_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is ThumbItem item)
                RenameItem(item);
        }

        private void CtxProperties_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is ThumbItem item && !item.IsFolder)
            {
                var dialog = new MetadataDialog(item.FilePath) { Owner = this };
                dialog.ShowDialog();
            }
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            ClearSelection();
            foreach (var item in _thumbs)
            {
                item.IsSelected = true;
                _selectedItems.Add(item);
            }
            StatusSelection.Text = $"{_selectedItems.Count} items selected";
        }

        private void DeselectAll_Click(object sender, RoutedEventArgs e)
        {
            ClearSelection();
            StatusSelection.Text = "0 selected";
        }

        #endregion

        #region Viewer

        private void ShowViewer(ThumbItem item)
        {
            if (!_isInViewer)
            {
                _savedWindowLeft = Left;
                _savedWindowTop = Top;
                _savedWindowWidth = Width;
                _savedWindowHeight = Height;
                _savedStartupLocation = WindowStartupLocation;
            }
            _isInViewer = true;

            ShowOverlay(ViewerOverlay);
            ViewerTitle.Text = item.FileName;
            ViewerCounter.Text = $"#{item.Index} of {_thumbs.Count}";
            ViewerZoom.Text = "100%";
            _viewerZoom = 1.0;
            _viewerRotation = 0;
            ViewerImage.RenderTransform = null;

            _clippingWarningEnabled = false;
            ClippingOverlay.Source = null;
            _clippingBitmap = null;

            TitleBarRow.Height = new GridLength(0);
            var chrome = WindowChrome.GetWindowChrome(this);
            if (chrome != null) chrome.CaptionHeight = 0;

            if (!string.IsNullOrEmpty(item.FilePath) && File.Exists(item.FilePath))
            {
                try
                {
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.UriSource = new Uri(item.FilePath, UriKind.Absolute);
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.EndInit();
                    bi.Freeze();
                    ViewerImage.Source = bi;

                    _viewerOrigWidth = bi.PixelWidth;
                    _viewerOrigHeight = bi.PixelHeight;

                    var fi = new FileInfo(item.FilePath);
                    ViewerStatusDims.Text = $"{bi.PixelWidth}×{bi.PixelHeight}  ({item.Megapixels})";
                    ViewerStatusSize.Text = item.FileSize;
                }
                catch { }
            }

            ViewerStatusZoom.Text = "100%";

            var workArea = GetCurrentMonitorWorkArea();
            double winW = Math.Max(480, Math.Min(_viewerOrigWidth, workArea.Width - 16));
            double winH = Math.Max(300, Math.Min(_viewerOrigHeight + 32 + 24, workArea.Height - 16));
            Width = winW;
            Height = winH;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = (workArea.Width - winW) / 2 + workArea.Left;
            Top = (workArea.Height - winH) / 2 + workArea.Top;

            Focus();
        }

        private void NavigateViewerPrev()
        {
            if (_thumbs.Count == 0) return;
            int idx = _viewerIndex;
            do
            {
                idx = (idx - 1 + _thumbs.Count) % _thumbs.Count;
            } while (_thumbs[idx].IsFolder && idx != _viewerIndex);
            if (_thumbs[idx].IsFolder) return;
            _viewerIndex = idx;
            ShowViewer(_thumbs[_viewerIndex]);
        }

        private void NavigateViewerNext()
        {
            if (_thumbs.Count == 0) return;
            int idx = _viewerIndex;
            do
            {
                idx = (idx + 1) % _thumbs.Count;
            } while (_thumbs[idx].IsFolder && idx != _viewerIndex);
            if (_thumbs[idx].IsFolder) return;
            _viewerIndex = idx;
            ShowViewer(_thumbs[_viewerIndex]);
        }

        private void CloseViewer_Click(object sender, RoutedEventArgs e)
        {
            HideOverlay(ViewerOverlay);
        }

        private void ViewerMinimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void ViewerMaximize_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
                ViewerMaxBtn.Content = "\uE739";
            }
            else
            {
                WindowState = WindowState.Maximized;
                ViewerMaxBtn.Content = "\uE923";
            }
        }

        private void ViewerImage_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            HideOverlay(ViewerOverlay);
        }

        private void ViewerImageBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                HideOverlay(ViewerOverlay);
                return;
            }
            _isPanning = true;
            _lastPanPoint = e.GetPosition(ViewerImageBorder);
            ViewerImageBorder.Cursor = Cursors.SizeAll;
            ViewerImageBorder.CaptureMouse();
        }

        private void ViewerImageBorder_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isPanning)
            {
                var pos = e.GetPosition(ViewerImageBorder);
                _panX += pos.X - _lastPanPoint.X;
                _panY += pos.Y - _lastPanPoint.Y;
                _lastPanPoint = pos;
                ApplyPan();
            }
            else if (_isMiddleDrag)
            {
                var pos = e.GetPosition(ViewerImageBorder);
                _panX += pos.X - _lastMiddleDragPoint.X;
                _panY += pos.Y - _lastMiddleDragPoint.Y;
                _lastMiddleDragPoint = pos;
                ApplyPan();
            }
        }

        private void ViewerImageBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isPanning)
            {
                _isPanning = false;
                ViewerImageBorder.Cursor = Cursors.Arrow;
                ViewerImageBorder.ReleaseMouseCapture();
            }
        }

        private void ViewerImageBorder_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Middle) return;
            _savedZoomBeforeMiddleDrag = _viewerZoom;
            _viewerZoom = 1.0;
            ApplyViewerTransform();

            _isMiddleDrag = true;
            _lastMiddleDragPoint = e.GetPosition(ViewerImageBorder);
            ViewerImageBorder.Cursor = Cursors.SizeAll;
            ViewerImageBorder.CaptureMouse();
        }

        private void ViewerImageBorder_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Middle || !_isMiddleDrag) return;
            _isMiddleDrag = false;
            _viewerZoom = _savedZoomBeforeMiddleDrag;
            _panX = 0;
            _panY = 0;
            ApplyViewerTransform();
            ViewerImageBorder.Cursor = Cursors.Arrow;
            ViewerImageBorder.ReleaseMouseCapture();
        }

        private void ViewerImageBorder_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Delta > 0)
                NavigateViewerPrev();
            else
                NavigateViewerNext();
        }

        private void ViewerTitlebar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var source = e.OriginalSource as DependencyObject;
            while (source != null && source != sender)
            {
                if (source is Button) return;
                source = VisualTreeHelper.GetParent(source);
            }
            if (e.ClickCount == 2)
            {
                if (WindowState == WindowState.Maximized)
                {
                    WindowState = WindowState.Normal;
                    ViewerMaxBtn.Content = "\uE739";
                }
                else
                {
                    WindowState = WindowState.Maximized;
                    ViewerMaxBtn.Content = "\uE923";
                }
                return;
            }
            _dragScreenStart = PointToScreen(e.GetPosition(this));
            _dragWindowStartX = Left;
            _dragWindowStartY = Top;
            _viewerDragReady = true;
            Mouse.Capture(sender as IInputElement);
        }

        private void ViewerTitlebar_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_viewerDragReady) return;
            var currentScreen = PointToScreen(e.GetPosition(this));
            double dx = currentScreen.X - _dragScreenStart.X;
            double dy = currentScreen.Y - _dragScreenStart.Y;
            if (!_isDraggingViewer)
            {
                if (Math.Abs(dx) > DragThreshold || Math.Abs(dy) > DragThreshold)
                {
                    _isDraggingViewer = true;
                    if (WindowState == WindowState.Maximized)
                    {
                        var workArea = SystemParameters.WorkArea;
                        double ratioX = _dragScreenStart.X / workArea.Width;
                        WindowState = WindowState.Normal;
                        Left = _dragScreenStart.X - (Width * ratioX);
                        Top = workArea.Top;
                        _dragWindowStartX = Left;
                        _dragWindowStartY = Top;
                        _dragScreenStart = currentScreen;
                        ViewerMaxBtn.Content = "\uE739";
                    }
                }
            }
            if (_isDraggingViewer)
            {
                Left = _dragWindowStartX + dx;
                Top = _dragWindowStartY + dy;
            }
        }

        private void ViewerTitlebar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDraggingViewer)
            {
                _isDraggingViewer = false;
            }
            _viewerDragReady = false;
            Mouse.Capture(null);
        }

        private void ViewerPrev_Click(object sender, RoutedEventArgs e)
        {
            NavigateViewerPrev();
        }

        private void ViewerNext_Click(object sender, RoutedEventArgs e)
        {
            NavigateViewerNext();
        }

        private void ViewerZoomOut_Click(object sender, RoutedEventArgs e)
        {
            _viewerZoom = Math.Max(0.1, _viewerZoom - 0.25);
            ApplyViewerTransform();
        }

        private void ViewerZoomIn_Click(object sender, RoutedEventArgs e)
        {
            _viewerZoom = Math.Min(10.0, _viewerZoom + 0.25);
            ApplyViewerTransform();
        }

        private void ViewerFit_Click(object sender, RoutedEventArgs e)
        {
            _viewerZoom = 1.0;
            _viewerRotation = 0;
            ApplyViewerTransform();
        }

        private void ViewerRotate_Click(object sender, RoutedEventArgs e)
        {
            _viewerRotation = (_viewerRotation + 90) % 360;
            ApplyViewerTransform();
        }

        private void ApplyViewerTransform()
        {
            _panX = 0;
            _panY = 0;

            var group = new TransformGroup();
            group.Children.Add(new ScaleTransform(_viewerZoom, _viewerZoom));
            group.Children.Add(new RotateTransform(_viewerRotation));
            group.Children.Add(new TranslateTransform(_panX, _panY));
            ViewerImage.RenderTransform = group;
            ClippingOverlay.RenderTransform = group;
            ViewerZoom.Text = $"{(int)(_viewerZoom * 100)}%";
            ViewerStatusZoom.Text = $"{(int)(_viewerZoom * 100)}%";

            bool isRotated = (_viewerRotation % 180) != 0;
            double baseW = _viewerOrigWidth;
            double baseH = _viewerOrigHeight;
            double desiredW, desiredH;
            if (isRotated)
            {
                desiredW = baseH * _viewerZoom;
                desiredH = baseW * _viewerZoom;
            }
            else
            {
                desiredW = baseW * _viewerZoom;
                desiredH = baseH * _viewerZoom;
            }

            var workArea = GetCurrentMonitorWorkArea();
            double maxW = workArea.Width - 16;
            double maxH = workArea.Height - 16;
            Width = Math.Max(480, Math.Min(desiredW, maxW));
            Height = Math.Max(300, Math.Min(desiredH + 32, maxH));
        }

        private bool _clippingWarningEnabled = false;
        private WriteableBitmap? _clippingBitmap;

        private void ClippingWarning_Click(object sender, RoutedEventArgs e)
        {
            _clippingWarningEnabled = !_clippingWarningEnabled;

            if (_clippingWarningEnabled)
            {
                GenerateClippingOverlay();
            }
            else
            {
                ClippingOverlay.Source = null;
                _clippingBitmap = null;
            }
        }

        private void GenerateClippingOverlay()
        {
            if (ViewerImage.Source is not BitmapSource src) return;

            var formatted = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
            int maxDim = 512;
            double scale = Math.Min((double)maxDim / formatted.PixelWidth, (double)maxDim / formatted.PixelHeight);
            if (scale > 1) scale = 1;
            var scaled = new TransformedBitmap(formatted, new ScaleTransform(scale, scale));
            int w = scaled.PixelWidth;
            int h = scaled.PixelHeight;
            var pixels = new byte[w * h * 4];
            scaled.CopyPixels(pixels, w * 4, 0);

            var overlay = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
            var overlayPixels = new byte[w * h * 4];

            const int overexposedThreshold = 250;
            const int underexposedThreshold = 5;

            for (int i = 0; i < pixels.Length; i += 4)
            {
                byte b = pixels[i];
                byte g = pixels[i + 1];
                byte r = pixels[i + 2];

                int brightness = (r + g + b) / 3;

                if (brightness >= overexposedThreshold)
                {
                    overlayPixels[i] = 0;
                    overlayPixels[i + 1] = 0;
                    overlayPixels[i + 2] = 255;
                    overlayPixels[i + 3] = 200;
                }
                else if (brightness <= underexposedThreshold)
                {
                    overlayPixels[i] = 255;
                    overlayPixels[i + 1] = 0;
                    overlayPixels[i + 2] = 0;
                    overlayPixels[i + 3] = 200;
                }
            }

            overlay.WritePixels(new Int32Rect(0, 0, w, h), overlayPixels, w * 4, 0);
            _clippingBitmap = overlay;
            ClippingOverlay.Source = overlay;
        }

        private void ApplyPan()
        {
            var group = ViewerImage.RenderTransform as TransformGroup;
            if (group == null || group.Children.Count < 3) return;
            ((TranslateTransform)group.Children[2]).X = _panX;
            ((TranslateTransform)group.Children[2]).Y = _panY;
            ClippingOverlay.RenderTransform = group;
        }

        #endregion

        #region Overlay Management

        private void ShowOverlay(Border overlay)
        {
            overlay.Visibility = Visibility.Visible;
        }

        private void HideOverlay(Border overlay)
        {
            overlay.Visibility = Visibility.Collapsed;
            if (overlay == ViewerOverlay)
            {
                _isInViewer = false;
                Width = _savedWindowWidth;
                Height = _savedWindowHeight;
                Left = _savedWindowLeft;
                Top = _savedWindowTop;
                WindowStartupLocation = _savedStartupLocation;
                TitleBarRow.Height = new GridLength(28);
                var chrome = WindowChrome.GetWindowChrome(this);
                if (chrome != null) chrome.CaptionHeight = 28;
            }
        }

        private void HideAllOverlays()
        {
            HideOverlay(ViewerOverlay);
            HideOverlay(CropOverlay);
            HideOverlay(DrawOverlay);
        }

        #endregion

        #region Menu Handlers

        private void MenuOpen_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "All Supported|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tiff;*.webp;*.heic|All Files|*.*",
                Title = "Open Image",
                Multiselect = false
            };
            if (dlg.ShowDialog() == true)
            {
                var dir = Path.GetDirectoryName(dlg.FileName);
                if (dir != null)
                    LoadFolder(dir);
                var item = _thumbs.FirstOrDefault(t => t.FilePath == dlg.FileName);
                if (item != null)
                {
                    _viewerIndex = _thumbs.IndexOf(item);
                    ShowPreview(item);
                }
            }
        }

        private void MenuOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFolderDialog
            {
                Title = "Select Folder"
            };
            if (dlg.ShowDialog() == true)
            {
                LoadFolder(dlg.FolderName);
            }
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            NavigateBack();
        }

        private void Up_Click(object sender, RoutedEventArgs e)
        {
            NavigateUp();
        }

        private void MenuSave_Click(object sender, RoutedEventArgs e)
        {
            var sourcePath = !string.IsNullOrEmpty(_currentPreviewPath) && File.Exists(_currentPreviewPath)
                ? _currentPreviewPath
                : null;

            if (sourcePath == null)
            {
                MessageBox.Show("No image selected to save.", "Save As", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new SaveFileDialog
            {
                Filter = "PNG|*.png|JPEG|*.jpg|BMP|*.bmp|TIFF|*.tiff|All Files|*.*",
                DefaultExt = ".png",
                FileName = Path.GetFileNameWithoutExtension(_thumbs.ElementAtOrDefault(_viewerIndex)?.FileName ?? "image")
            };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    var ext = Path.GetExtension(dlg.FileName).ToLower();
                    BitmapDecoder decoder;
                    using (var fs = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        decoder = ext switch
                        {
                            ".jpg" or ".jpeg" => new JpegBitmapDecoder(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.None),
                            ".bmp" => new BmpBitmapDecoder(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.None),
                            ".tiff" or ".tif" => new TiffBitmapDecoder(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.None),
                            _ => new PngBitmapDecoder(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.None)
                        };
                    }

                    if (decoder.Frames.Count == 0)
                    {
                        MessageBox.Show("Could not decode the source image.", "Save As", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    var frame = decoder.Frames[0];
                    BitmapEncoder encoder = ext switch
                    {
                        ".jpg" or ".jpeg" => new JpegBitmapEncoder { QualityLevel = 95 },
                        ".bmp" => new BmpBitmapEncoder(),
                        ".tiff" or ".tif" => new TiffBitmapEncoder(),
                        _ => new PngBitmapEncoder()
                    };
                    encoder.Frames.Add(BitmapFrame.Create(frame));
                    using var outStream = File.Create(dlg.FileName);
                    encoder.Save(outStream);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Save error:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void MenuRefresh_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_currentFolder) && Directory.Exists(_currentFolder))
                LoadFolder(_currentFolder);
        }

        private void MenuSettings_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Settings dialog — implement with theme, language, default paths, etc.", "Settings",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void MenuBatchConvert_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Batch Convert dialog — implement with format selection, quality, output folder.", "Batch Convert",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ToggleTheme_Click(object sender, RoutedEventArgs e)
        {
        }

        #endregion

        #region Fullscreen

        public void EnterFullscreen()
        {
            if (_isFullscreen) return;
            _savedWindowState = WindowState;
            _savedWindowStyle = WindowStyle;
            _savedTopmost = Topmost;
            _savedMargin = Margin;

            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Maximized;
            Topmost = true;
            Margin = new Thickness(0);
            _isFullscreen = true;
        }

        public void ExitFullscreen()
        {
            if (!_isFullscreen) return;
            WindowStyle = _savedWindowStyle;
            WindowState = _savedWindowState;
            Topmost = _savedTopmost;
            Margin = _savedMargin;
            _isFullscreen = false;
        }

        public void ToggleFullscreen()
        {
            if (_isFullscreen) ExitFullscreen();
            else EnterFullscreen();
        }

        #endregion

        #region Window Chrome

        private const int MONITOR_DEFAULTTONEAREST = 2;

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr handle, int flags);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private static readonly IntPtr HWND_TOP = IntPtr.Zero;
        private const uint SWP_FRAMECHANGED = 0x0020;

        private Rect GetCurrentMonitorWorkArea()
        {
            var handle = new WindowInteropHelper(this).Handle;
            var monitor = MonitorFromWindow(handle, MONITOR_DEFAULTTONEAREST);
            var info = new MONITORINFO();
            info.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
            if (GetMonitorInfo(monitor, ref info))
            {
                return new Rect(
                    info.rcWork.Left, info.rcWork.Top,
                    info.rcWork.Right - info.rcWork.Left,
                    info.rcWork.Bottom - info.rcWork.Top);
            }
            return SystemParameters.WorkArea;
        }

        private void Titlebar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
                Maximize_Click(sender, e);
            else
                DragMove();
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
            => WindowState = WindowState.Minimized;

        private void Maximize_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Normal)
                WindowState = WindowState.Maximized;
            else
                WindowState = WindowState.Normal;
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                var handle = new WindowInteropHelper(this).Handle;
                var monitor = MonitorFromWindow(handle, MONITOR_DEFAULTTONEAREST);
                var info = new MONITORINFO();
                info.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
                if (GetMonitorInfo(monitor, ref info))
                {
                    int x = info.rcWork.Left;
                    int y = info.rcWork.Top;
                    int w = info.rcWork.Right - info.rcWork.Left;
                    int h = info.rcWork.Bottom - info.rcWork.Top;
                    SetWindowPos(handle, HWND_TOP, x, y, w, h, SWP_FRAMECHANGED);
                }
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
            => Close();

        #endregion

        #region Thumb Size

        private void ThumbSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (ThumbsGrid?.ItemsPanel == null) return;
            var size = (int)ThumbSizeSlider.Value;
            var wp = FindWrapPanel(ThumbsGrid);
            if (wp != null)
            {
                wp.ItemWidth = size;
                wp.ItemHeight = size * 1.2125;
            }
        }

        private void ThumbImageBorder_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (sender is Border b && b.ActualWidth > 0)
            {
                double newH = b.ActualWidth * 0.675;
                if (Math.Abs(b.Height - newH) > 0.5)
                    b.Height = newH;
            }
        }

        private static WrapPanel? FindWrapPanel(DependencyObject parent)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is WrapPanel wp) return wp;
                var result = FindWrapPanel(child);
                if (result != null) return result;
            }
            return null;
        }

        #endregion

        #region Keyboard

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);

            bool isViewer = ViewerOverlay.Visibility == Visibility.Visible;

            if (e.Key == Key.Escape)
            {
                if (isViewer)
                    HideOverlay(ViewerOverlay);
                else if (CropOverlay.Visibility == Visibility.Visible)
                    HideOverlay(CropOverlay);
                else if (DrawOverlay.Visibility == Visibility.Visible)
                    HideOverlay(DrawOverlay);
                else if (_isFullscreen)
                    ExitFullscreen();
                else
                {
                    ClearSelection();
                    StatusSelection.Text = "0 selected";
                }
            }

            if (e.Key == Key.F11)
                ToggleFullscreen();

            if (isViewer)
            {
                if (e.Key == Key.Left)
                    NavigateViewerPrev();
                else if (e.Key == Key.Right)
                    NavigateViewerNext();
                else if (e.Key == Key.J)
                    ClippingWarning_Click(this, new RoutedEventArgs());
                return;
            }

            if (e.Key == Key.Delete && _selectedItems.Count > 0)
                DeleteSelectedItems();
            else if (e.Key == Key.F2 && _selectedItems.Count == 1)
                RenameItem(_selectedItems[0]);
            else if (e.Key == Key.Enter && _selectedItems.Count == 1)
            {
                var item = _selectedItems[0];
                if (item.IsFolder && Directory.Exists(item.FilePath))
                    LoadFolder(item.FilePath);
                else if (!item.IsFolder && File.Exists(item.FilePath))
                {
                    _viewerIndex = _thumbs.IndexOf(item);
                    ShowViewer(item);
                }
            }
            else if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control)
            {
                ClearSelection();
                foreach (var item in _thumbs)
                {
                    item.IsSelected = true;
                    _selectedItems.Add(item);
                }
                StatusSelection.Text = $"{_selectedItems.Count} items selected";
            }
            else if (e.Key == Key.Back)
                NavigateUp();
        }

        #endregion
    }

    public class ThumbItem : System.ComponentModel.INotifyPropertyChanged
    {
        private BitmapSource? _thumbnail;
        private bool _isSelected;
        public string FileName { get; set; } = "";
        public string FilePath { get; set; } = "";
        public string Dimensions { get; set; } = "";
        public string Megapixels { get; set; } = "";
        public string FileSize { get; set; } = "";
        public string FileType { get; set; } = "";
        public int Index { get; set; }
        public bool IsFolder { get; set; }
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsSelected))); }
        }
        public BitmapSource? Thumbnail
        {
            get => _thumbnail;
            set { _thumbnail = value; PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Thumbnail))); }
        }
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }
}
