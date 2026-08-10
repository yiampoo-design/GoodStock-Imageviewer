using System;
using System.Windows;

namespace WpfApp1.Services
{
    public enum ThemeMode
    {
        Dark,
        Light
    }

    public sealed class ThemeManager
    {
        private static readonly Uri DarkThemeUri = new("Themes/DarkTheme.xaml", UriKind.Relative);
        private static readonly Uri LightThemeUri = new("Themes/LightTheme.xaml", UriKind.Relative);

        private ThemeMode _current = ThemeMode.Dark;
        public ThemeMode CurrentTheme => _current;

        public event Action<ThemeMode>? ThemeChanged;

        public void ApplyTheme(ThemeMode mode)
        {
            _current = mode;
            var app = Application.Current;
            if (app == null) return;

            var dict = app.Resources.MergedDictionaries;
            dict.Clear();

            var themeUri = mode == ThemeMode.Dark ? DarkThemeUri : LightThemeUri;
            dict.Add(new ResourceDictionary { Source = themeUri });
            dict.Add(new ResourceDictionary { Source = new Uri("Chrome.xaml", UriKind.Relative) });

            ThemeChanged?.Invoke(mode);
        }

        public void Toggle()
        {
            ApplyTheme(_current == ThemeMode.Dark ? ThemeMode.Light : ThemeMode.Dark);
        }
    }
}
