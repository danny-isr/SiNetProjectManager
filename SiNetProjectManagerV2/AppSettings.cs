using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SiNetProjectManagerV2
{
    public class AppSettings : INotifyPropertyChanged
    {
        public AppSettings()
        {
            fontFamily = "Segoe UI";
            fontSize = 12.0;
            foregroundColor = "#000000"; // שחור
            backgroundColor = "#FFFFFF"; // לבן
            allowMultipleInstances = true; // default to multiple instances
            loggingEnabled = false; // default: OFF
            logDirectory = string.Empty; // empty means use default
        }

        private string fontFamily;
        public string FontFamily
        {
            get => fontFamily;
            set { if (value == fontFamily) return; fontFamily = value; OnPropertyChanged(); }
        }

        private double fontSize;
        public double FontSize
        {
            get => fontSize;
            set { if (value == fontSize) return; fontSize = value; OnPropertyChanged(); }
        }

        private string foregroundColor;
        public string ForegroundColor
        {
            get => foregroundColor;
            set { if (value == foregroundColor) return; foregroundColor = value; OnPropertyChanged(); }
        }

        private string backgroundColor;
        public string BackgroundColor
        {
            get => backgroundColor;
            set { if (value == backgroundColor) return; backgroundColor = value; OnPropertyChanged(); }
        }

        private bool allowMultipleInstances;
        public bool AllowMultipleInstances
        {
            get => allowMultipleInstances;
            set { if (value == allowMultipleInstances) return; allowMultipleInstances = value; OnPropertyChanged(); }
        }

        // === Logging Settings ===

        private bool loggingEnabled;
        /// <summary>
        /// Whether file logging is enabled. Default: false (OFF).
        /// When false, no log files are created.
        /// </summary>
        public bool LoggingEnabled
        {
            get => loggingEnabled;
            set { if (value == loggingEnabled) return; loggingEnabled = value; OnPropertyChanged(); }
        }

        private string logDirectory;
        /// <summary>
        /// Custom log directory path. Empty string means use default location.
        /// Default location: %LocalAppData%\SiNetProjectManagerV2\Logs
        /// </summary>
        public string LogDirectory
        {
            get => logDirectory;
            set { if (value == logDirectory) return; logDirectory = value ?? string.Empty; OnPropertyChanged(); }
        }

        // === Floating Tasks Window Position ===

        private double floatingTasksTop = double.NaN;
        public double FloatingTasksTop
        {
            get => floatingTasksTop;
            set { if (value == floatingTasksTop) return; floatingTasksTop = value; OnPropertyChanged(); }
        }

        private double floatingTasksLeft = double.NaN;
        public double FloatingTasksLeft
        {
            get => floatingTasksLeft;
            set { if (value == floatingTasksLeft) return; floatingTasksLeft = value; OnPropertyChanged(); }
        }

        private double floatingTasksWidth = 380;
        public double FloatingTasksWidth
        {
            get => floatingTasksWidth;
            set { if (value == floatingTasksWidth) return; floatingTasksWidth = value; OnPropertyChanged(); }
        }

        private double floatingTasksHeight = double.NaN;
        public double FloatingTasksHeight
        {
            get => floatingTasksHeight;
            set { if (value == floatingTasksHeight) return; floatingTasksHeight = value; OnPropertyChanged(); }
        }

        // === Floating Inspection Window Position ===

        private double floatingInspectionTop = double.NaN;
        public double FloatingInspectionTop
        {
            get => floatingInspectionTop;
            set { if (value == floatingInspectionTop) return; floatingInspectionTop = value; OnPropertyChanged(); }
        }

        private double floatingInspectionLeft = double.NaN;
        public double FloatingInspectionLeft
        {
            get => floatingInspectionLeft;
            set { if (value == floatingInspectionLeft) return; floatingInspectionLeft = value; OnPropertyChanged(); }
        }

        private double floatingInspectionWidth = 420;
        public double FloatingInspectionWidth
        {
            get => floatingInspectionWidth;
            set { if (value == floatingInspectionWidth) return; floatingInspectionWidth = value; OnPropertyChanged(); }
        }

        private double floatingInspectionHeight = 850;
        public double FloatingInspectionHeight
        {
            get => floatingInspectionHeight;
            set { if (value == floatingInspectionHeight) return; floatingInspectionHeight = value; OnPropertyChanged(); }
        }

        // === Floating Window Opacity ===

        private double floatingWindowActiveOpacity = 1.0;
        /// <summary>
        /// Opacity when the mouse is over the floating window. Range: 0.1–1.0.
        /// </summary>
        public double FloatingWindowActiveOpacity
        {
            get => floatingWindowActiveOpacity;
            set
            {
                var clamped = Math.Clamp(value, 0.1, 1.0);
                if (clamped == floatingWindowActiveOpacity) return;
                floatingWindowActiveOpacity = clamped;
                OnPropertyChanged();
            }
        }

        private double floatingWindowIdleOpacity = 0.7;
        /// <summary>
        /// Opacity when the mouse leaves the floating window. Range: 0.1–1.0.
        /// </summary>
        public double FloatingWindowIdleOpacity
        {
            get => floatingWindowIdleOpacity;
            set
            {
                var clamped = Math.Clamp(value, 0.1, 1.0);
                if (clamped == floatingWindowIdleOpacity) return;
                floatingWindowIdleOpacity = clamped;
                OnPropertyChanged();
            }
        }

        private bool enableAuthorizationTestMode;
        /// <summary>
        /// DEBUG ONLY: Enables the Authorization Role Selector on startup. Default: false.
        /// </summary>
        public bool EnableAuthorizationTestMode
        {
            get => enableAuthorizationTestMode;
            set { if (value == enableAuthorizationTestMode) return; enableAuthorizationTestMode = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

}
