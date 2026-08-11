using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using WpfApp1.Models;
using WpfApp1.Services;

namespace WpfApp1.ViewModels
{
    public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly IMetadataService _metadataService;
        private readonly IFileOperationService _fileOperationService;
        private readonly ThemeManager _themeManager;
        private CancellationTokenSource? _metadataCts;
        private bool _disposed;

        public MainViewModel()
        {
            _metadataService = new MetadataService();
            _fileOperationService = new FileOperationService();
            _themeManager = new ThemeManager();

            ToggleThemeCommand = new RelayCommand(() => _themeManager.Toggle());

            _themeManager.ThemeChanged += _ => OnPropertyChanged(nameof(CurrentTheme));
            _themeManager.ThemeChanged += _ => OnPropertyChanged(nameof(ThemeButtonText));
        }

        public ICommand ToggleThemeCommand { get; }

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

        public string CurrentTheme => _themeManager.CurrentTheme.ToString();
        public string ThemeButtonText => _themeManager.CurrentTheme == ThemeMode.Dark ? "Light Mode" : "Dark Mode";
        public bool IsMetadataAvailable => _metadataService.ExifToolPath != null;

        public void RefreshMetadataAvailability()
        {
            OnPropertyChanged(nameof(IsMetadataAvailable));
        }

        public void InvalidateCache(string filePath)
        {
            if (_metadataService is MetadataService ms)
                ms.InvalidateCache(filePath);
        }

        public async Task LoadMetadataForFileAsync(string? filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return;

            _metadataCts?.Cancel();
            _metadataCts = new CancellationTokenSource();
            var ct = _metadataCts.Token;

            try
            {
                var metadata = await _metadataService.ReadMetadataAsync(filePath, ct);
                ct.ThrowIfCancellationRequested();
                if (metadata != null)
                    CurrentMetadata = metadata;
            }
            catch (OperationCanceledException) { }
            catch { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _metadataCts?.Cancel();
            _metadataCts?.Dispose();
            _metadataService.Dispose();
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
}
