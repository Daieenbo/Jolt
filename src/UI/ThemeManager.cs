using System;
using Microsoft.Win32;

namespace Voltide.UI
{
    /// <summary>Which skin the tray surface draws with.</summary>
    public enum ThemeMode
    {
        /// <summary>Always the dark skin.</summary>
        Dark = 0,

        /// <summary>Always the light skin.</summary>
        Light = 1,

        /// <summary>Follow the Windows app-light-theme toggle.</summary>
        System = 2
    }

    /// <summary>
    /// Persists the chosen skin in HKCU and raises a change event so the tray menu
    /// and the hover card can repaint on the spot. "System" is resolved on every
    /// read, so an OS theme switch is picked up without a restart.
    /// </summary>
    public static class ThemeManager
    {
        private const string SettingsKey = @"Software\Jolt";
        private const string ValueName = "ThemeMode";
        private const string PersonalizeKey =
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

        private static bool _loaded;
        private static ThemeMode _mode = ThemeMode.System;

        public static event EventHandler Changed;

        public static ThemeMode Mode
        {
            get
            {
                Load();
                return _mode;
            }
        }

        /// <summary>True when the surfaces should draw dark right now.</summary>
        public static bool IsDark
        {
            get
            {
                switch (Mode)
                {
                    case ThemeMode.Dark:
                        return true;
                    case ThemeMode.Light:
                        return false;
                    default:
                        return !SystemUsesLightTheme();
                }
            }
        }

        public static string NameOf(ThemeMode mode)
        {
            switch (mode)
            {
                case ThemeMode.Dark: return "深色";
                case ThemeMode.Light: return "浅色";
                default: return "跟随系统";
            }
        }

        public static void Set(ThemeMode mode)
        {
            Load();
            if (_mode == mode) return;

            _mode = mode;
            Save();

            if (Changed != null) Changed(null, EventArgs.Empty);
        }

        private static bool SystemUsesLightTheme()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(PersonalizeKey, false))
                {
                    if (key == null) return false;
                    object raw = key.GetValue("SystemUsesLightTheme");
                    return raw != null && Convert.ToInt32(raw) != 0;
                }
            }
            catch
            {
                return false;
            }
        }

        private static void Load()
        {
            if (_loaded) return;
            _loaded = true;

            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(SettingsKey, false))
                {
                    if (key == null) return;
                    object raw = key.GetValue(ValueName);
                    if (raw == null) return;

                    int value = Convert.ToInt32(raw);
                    _mode = (value == 0) ? ThemeMode.Dark
                          : (value == 1) ? ThemeMode.Light
                          : ThemeMode.System;
                }
            }
            catch
            {
                _mode = ThemeMode.System;
            }
        }

        private static void Save()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(SettingsKey))
                {
                    if (key != null) key.SetValue(ValueName, (int)_mode);
                }
            }
            catch
            {
            }
        }
    }
}
