using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SiNetProjectManager.WPF
{
    public class BaseWindow : Window
    {
        /// <summary>
        /// כאשר true, התוכן נעטף ב-ScrollViewer אוטומטית.
        /// חלונות עם תוכן שמנהל את הגלילה בעצמו (כגון WebView2, ListBox) צריכים להגדיר false.
        /// </summary>
        public bool AutoScrollEnabled { get; set; } = true;

        public BaseWindow()
        {
            // החלת סטייל דינאמי
            this.SetResourceReference(FontFamilyProperty, "AppFontFamily");
            this.SetResourceReference(FontSizeProperty, "AppFontSize");
            this.SetResourceReference(ForegroundProperty, "AppForeground");
            this.SetResourceReference(BackgroundProperty, "AppBackground");

            // Size to content אם צריך
            this.SizeToContent = SizeToContent.Manual;
            this.MinHeight = 400;
            this.MinWidth = 600;

            // עטיפת התוכן ב-ScrollViewer בזמן ריצה (רק אם מופעל)
            this.Loaded += (s, e) =>
            {
                if (!AutoScrollEnabled) return;

                // אם התוכן קיים ולא כבר ScrollViewer
                if (this.Content is FrameworkElement content && !(content is ScrollViewer))
                {
                    var scroll = new ScrollViewer
                    {
                        Content = content,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
                    };

                    // יירוט אירוע גלגלת עכבר ב-ScrollViewer
                    scroll.PreviewMouseWheel += Scroll_PreviewMouseWheel;

                    // מחליפים את התוכן
                    this.Content = scroll;
                }
            };
        }

        // גלילה אנכית — כאשר מסתובבים בגלגלת מעל ה-ScrollViewer, הוא יגלול את עצמו.
        private void Scroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer sv)
            {
                sv.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta);
                e.Handled = true;
            }
        }
    }
}
