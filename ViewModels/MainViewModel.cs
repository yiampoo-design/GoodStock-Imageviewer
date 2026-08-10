using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using WpfApp1.Models;
using WpfApp1.Services;

namespace WpfApp1.ViewModels
{
    public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly IMetadataService _metadataService;
        private readonly MetadataCacheService _cacheService;
        private readonly IFileOperationService _fileOperationService;
        private readonly ThemeManager _themeManager;
        private CancellationTokenSource? _metadataCts;
        private CancellationTokenSource? _histogramCts;
        private CancellationTokenSource? _clippingCts;
        private bool _disposed;

        public MainViewModel()
        {
            _metadataService = new MetadataService();
            _cacheService = new MetadataCacheService();
            _fileOperationService = new FileOperationService();
            _themeManager = new ThemeManager();

            ToggleThemeCommand = new RelayCommand(() => _themeManager.Toggle());
            LoadMetadataForFileCommand = new AsyncRelayCommand<string>(LoadMetadataForFileAsync);
            LoadHistogramForFileCommand = new AsyncRelayCommand<string>(LoadHistogramForFileAsync);
            AnalyzeClippingForFileCommand = new AsyncRelayCommand<string>(AnalyzeClippingForFileAsync);
            RunPreflightCommand = new AsyncRelayCommand(RunPreflightAsync);

            _themeManager.ThemeChanged += mode => OnPropertyChanged(nameof(CurrentTheme));
            _themeManager.ThemeChanged += _ => OnPropertyChanged(nameof(ThemeButtonText));
        }

        public ICommand ToggleThemeCommand { get; }
        public ICommand LoadMetadataForFileCommand { get; }
        public ICommand LoadHistogramForFileCommand { get; }
        public ICommand AnalyzeClippingForFileCommand { get; }
        public ICommand RunPreflightCommand { get; }

        public ObservableCollection<ThumbItem> Thumbnails { get; } = new();

        private PhotoMetadata? _currentMetadata;
        public PhotoMetadata? CurrentMetadata
        {
            get => _currentMetadata;
            set
            {
                _currentMetadata = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasMetadata));
                OnPropertyChanged(nameof(MetaTitleDisplay));
                OnPropertyChanged(nameof(MetaCameraDisplay));
                OnPropertyChanged(nameof(MetaLensDisplay));
                OnPropertyChanged(nameof(MetaDateDisplay));
                OnPropertyChanged(nameof(MetaDescriptionDisplay));
                OnPropertyChanged(nameof(MetaCreatorDisplay));
                OnPropertyChanged(nameof(MetaCopyrightDisplay));
                OnPropertyChanged(nameof(MetaKeywordsDisplay));
            }
        }

        public bool HasMetadata => CurrentMetadata != null;
        public string MetaTitleDisplay => CurrentMetadata?.Title ?? "\u2014";
        public string MetaCameraDisplay
        {
            get
            {
                if (CurrentMetadata == null) return "\u2014";
                var make = CurrentMetadata.Make;
                var model = CurrentMetadata.Model;
                if (string.IsNullOrEmpty(model)) return "\u2014";
                return string.IsNullOrEmpty(make) ? model : $"{make} {model}";
            }
        }
        public string MetaLensDisplay => CurrentMetadata?.Lens ?? "\u2014";
        public string MetaDateDisplay => CurrentMetadata?.DateTaken?.ToString("yyyy-MM-dd HH:mm") ?? "\u2014";
        public string MetaDescriptionDisplay => CurrentMetadata?.Description ?? "\u2014";
        public string MetaCreatorDisplay => CurrentMetadata?.Creator ?? "\u2014";
        public string MetaCopyrightDisplay => CurrentMetadata?.Copyright ?? "\u2014";
        public string MetaKeywordsDisplay => CurrentMetadata?.Keywords.Count > 0 ? string.Join(", ", CurrentMetadata.Keywords) : "\u2014";

        private HistogramData? _histogramData;
        public HistogramData? HistogramData
        {
            get => _histogramData;
            set
            {
                _histogramData = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HistogramRedBars));
                OnPropertyChanged(nameof(HistogramGreenBars));
                OnPropertyChanged(nameof(HistogramBlueBars));
                OnPropertyChanged(nameof(HasHistogram));
            }
        }

        public bool HasHistogram => HistogramData != null;
        public double[] HistogramRedBars => HistogramData != null ? HistogramService.DownsampleForDisplay(HistogramData.Red, 6, 90) : new double[6];
        public double[] HistogramGreenBars => HistogramData != null ? HistogramService.DownsampleForDisplay(HistogramData.Green, 6, 90) : new double[6];
        public double[] HistogramBlueBars => HistogramData != null ? HistogramService.DownsampleForDisplay(HistogramData.Blue, 6, 90) : new double[6];

        private ClippingInfo? _clippingInfo;
        public ClippingInfo? ClippingInfo
        {
            get => _clippingInfo;
            set
            {
                _clippingInfo = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ClippingHighlightText));
                OnPropertyChanged(nameof(ClippingShadowText));
                OnPropertyChanged(nameof(HasClippingInfo));
            }
        }

        public bool HasClippingInfo => ClippingInfo != null;
        public string ClippingHighlightText => ClippingInfo != null ? $"Highlight: {ClippingInfo.HighlightPercentage:F1}%" : "";
        public string ClippingShadowText => ClippingInfo != null ? $"Shadow: {ClippingInfo.ShadowPercentage:F1}%" : "";

        private bool _clippingEnabled;
        public bool ClippingEnabled
        {
            get => _clippingEnabled;
            set { _clippingEnabled = value; OnPropertyChanged(); }
        }

        private string _statusText = "";
        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        private string _selectionStatusText = "0 selected";
        public string SelectionStatusText
        {
            get => _selectionStatusText;
            set { _selectionStatusText = value; OnPropertyChanged(); }
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set { _isBusy = value; OnPropertyChanged(); }
        }

        private string _currentFolder = "";
        public string CurrentFolder
        {
            get => _currentFolder;
            set { _currentFolder = value; OnPropertyChanged(); OnPropertyChanged(nameof(FolderDisplay)); }
        }

        public string FolderDisplay => string.IsNullOrEmpty(CurrentFolder) ? "No folder" : Path.GetFileName(CurrentFolder.TrimEnd(Path.DirectorySeparatorChar));

        public string CurrentTheme => _themeManager.CurrentTheme.ToString();
        public string ThemeButtonText => _themeManager.CurrentTheme == ThemeMode.Dark ? "Light Mode" : "Dark Mode";

        public bool IsMetadataAvailable => _metadataService.ExifToolPath != null;

        private async Task LoadMetadataForFileAsync(string? filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return;

            _metadataCts?.Cancel();
            _metadataCts = new CancellationTokenSource();
            var ct = _metadataCts.Token;

            try
            {
                var cached = _cacheService.GetCached(filePath);
                if (cached != null)
                {
                    CurrentMetadata = cached;
                    return;
                }

                var metadata = await _metadataService.ReadMetadataAsync(filePath, ct);
                ct.ThrowIfCancellationRequested();
                if (metadata != null)
                {
                    CurrentMetadata = metadata;
                    _ = _cacheService.SetCacheAsync(filePath, metadata, ct);
                }
            }
            catch (OperationCanceledException) { }
            catch { }
        }

        private async Task LoadHistogramForFileAsync(string? filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return;

            _histogramCts?.Cancel();
            _histogramCts = new CancellationTokenSource();
            var ct = _histogramCts.Token;

            try
            {
                var analysisBitmap = await ImageDecodeService.LoadAnalysisBitmapAsync(filePath, 256, ct);
                ct.ThrowIfCancellationRequested();
                if (analysisBitmap != null)
                {
                    var data = await HistogramService.ComputeAsync(analysisBitmap, ct);
                    ct.ThrowIfCancellationRequested();
                    HistogramData = data;
                }
            }
            catch (OperationCanceledException) { }
            catch { }
        }

        private async Task AnalyzeClippingForFileAsync(string? filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return;

            _clippingCts?.Cancel();
            _clippingCts = new CancellationTokenSource();
            var ct = _clippingCts.Token;

            try
            {
                var analysisBitmap = await ImageDecodeService.LoadAnalysisBitmapAsync(filePath, 512, ct);
                ct.ThrowIfCancellationRequested();
                if (analysisBitmap != null)
                {
                    var info = await ClippingAnalyzer.AnalyzeAsync(analysisBitmap, ClippingMode.RgbChannels, ct: ct);
                    ct.ThrowIfCancellationRequested();
                    ClippingInfo = info;
                }
            }
            catch (OperationCanceledException) { }
            catch { }
        }

        private async Task RunPreflightAsync()
        {
            if (CurrentMetadata == null) return;
            IsBusy = true;
            try
            {
                var report = StockPreflightService.RunCheck(CurrentMetadata);
                var needsAttention = report.Checks.Where(c => c.Status != PreflightStatus.Ready).ToList();
                if (needsAttention.Count == 0)
                {
                    StatusText = "Preflight: All checks passed";
                }
                else
                {
                    var msgs = needsAttention.Select(c => $"{c.Name}: {c.Message}");
                    StatusText = $"Preflight: {string.Join(" | ", msgs)}";
                }
            }
            finally { IsBusy = false; }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _metadataCts?.Cancel();
            _metadataCts?.Dispose();
            _histogramCts?.Cancel();
            _histogramCts?.Dispose();
            _clippingCts?.Cancel();
            _clippingCts?.Dispose();
            _metadataService.Dispose();
            _cacheService.Dispose();
            _fileOperationService.Dispose();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool>? _canExecute;
        public RelayCommand(Action execute, Func<bool>? canExecute = null) { _execute = execute; _canExecute = canExecute; }
        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;
        public void Execute(object? parameter) => _execute();
        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    public class AsyncRelayCommand<T> : ICommand
    {
        private readonly Func<T?, Task> _execute;
        private readonly Func<T?, bool>? _canExecute;
        private bool _isExecuting;
        public AsyncRelayCommand(Func<T?, Task> execute, Func<T?, bool>? canExecute = null) { _execute = execute; _canExecute = canExecute; }
        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? parameter) => !_isExecuting && (_canExecute?.Invoke((T?)parameter) ?? true);
        public async void Execute(object? parameter)
        {
            if (_isExecuting) return;
            _isExecuting = true;
            RaiseCanExecuteChanged();
            try { await _execute((T?)parameter); }
            finally { _isExecuting = false; RaiseCanExecuteChanged(); }
        }
        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    public class AsyncRelayCommand : ICommand
    {
        private readonly Func<Task> _execute;
        private readonly Func<bool>? _canExecute;
        private bool _isExecuting;
        public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null) { _execute = execute; _canExecute = canExecute; }
        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? parameter) => !_isExecuting && (_canExecute?.Invoke() ?? true);
        public async void Execute(object? parameter)
        {
            if (_isExecuting) return;
            _isExecuting = true;
            RaiseCanExecuteChanged();
            try { await _execute(); }
            finally { _isExecuting = false; RaiseCanExecuteChanged(); }
        }
        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
