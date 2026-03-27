using System.Security.Principal;
using System.Windows;

namespace SiNetProjectManager.WPF
{
    /// <summary>
    /// Interaction logic for SplashWindow.xaml
    /// </summary>
    public partial class SplashWindow : Window
    {
        public SplashWindow()
        {
            InitializeComponent();

            // הצג את שם המשתמש
            string user = WindowsIdentity.GetCurrent().Name;
            UsernameText.Text = $"משתמש: {user}";

            // טיימר לסגירה אוטומטית
            Loaded += (s, e) =>
            {
                var timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(3)
                };
                timer.Tick += (sender, args) =>
                {
                    timer.Stop();
                    this.Close();
                };
                timer.Start();
            };
        }
    }
}
