using System.Windows;
using SiNetProjectManagerV2.Services;

namespace SiNetProjectManagerV2.Dialogs;

/// <summary>
/// Shown when the user tries to close the email window or quit the app while ACC
/// background upload/download work is still in progress.
/// </summary>
public partial class BackgroundUploadsDialog : Window
{
    private readonly BackgroundCloseScope _scope;
    private bool _waitingForCompletion;

    public BackgroundUploadsDialog(BackgroundCloseScope scope = BackgroundCloseScope.Application)
    {
        _scope = scope;
        InitializeComponent();
        ApplyScopeCopy();
        UpdateCountText(AccBackgroundWorkMonitor.TotalActiveCount);
        AccBackgroundWorkMonitor.TotalActiveCountChanged += OnActiveUploadsChanged;
    }

    private void ApplyScopeCopy()
    {
        if (_scope == BackgroundCloseScope.EmailWindow)
        {
            Title = "סגירת חלון מיילים";
            HeaderText.Text = "תהליכי ACC פעילים ברקע";
            InfoText.Text =
                "יש תהליכי העלאה/הורדה ל-ACC ברקע. סגירת חלון המיילים לא עוצרת את התהליכים — הם ממשיכים כל עוד התוכנה פתוחה.";
            CloseWhenDoneButton.Content = "סגור חלון אחרי סיום";
            CloseWhenDoneButton.ToolTip = "החלון ייסגר אוטומטית כשכל התהליכים יסתיימו. ההעלאה/ההורדה ממשיכה עד הסוף.";
            ForceCloseButton.Content = "סגור חלון — המשך ברקע";
            ForceCloseButton.ToolTip = "החלון ייסגר עכשיו. תהליכי ה-ACC ימשיכו לרוץ ברקע בתוכנה.";
            CancelButton.Content = "הישאר בחלון";
            CancelButton.ToolTip = "לא סוגרים — נשארים בחלון המיילים. התהליכים ממשיכים.";
            return;
        }

        Title = "יציאה מהתוכנה";
        HeaderText.Text = "תהליכי ACC פעילים ברקע";
        InfoText.Text =
            "יש תהליכי העלאה/הורדה ל-ACC ברקע. יציאה מהתוכנה עכשיו עלולה להפסיק אותם ולגרום לאובדן קבצים שטרם הועלו.";
        CloseWhenDoneButton.Content = "צא אחרי סיום";
        CloseWhenDoneButton.ToolTip = "התוכנה תיסגר אוטומטית כשכל התהליכים יסתיימו.";
        ForceCloseButton.Content = "צא עכשיו בכל זאת";
        ForceCloseButton.ToolTip = "סוגר את התוכנה מיד — תהליכי ACC פעילים עלולים להיפסק.";
        CancelButton.Content = "הישאר בתוכנה";
        CancelButton.ToolTip = "לא יוצאים — התוכנה נשארת פתוחה והתהליכים ממשיכים.";
    }

    private void OnActiveUploadsChanged(int count)
    {
        Dispatcher.Invoke(() =>
        {
            UpdateCountText(count);

            if (_waitingForCompletion && count == 0)
            {
                DialogResult = true;
            }
        });
    }

    private void UpdateCountText(int count)
    {
        CountText.Text = count == 1
            ? "תהליך אחד פעיל ברקע…"
            : $"{count} תהליכים פעילים ברקע…";
    }

    private void CloseWhenDone_Click(object sender, RoutedEventArgs e)
    {
        _waitingForCompletion = true;
        CloseWhenDoneButton.IsEnabled = false;
        ForceCloseButton.IsEnabled = true;
        InfoText.Text = _scope == BackgroundCloseScope.EmailWindow
            ? "חלון המיילים ייסגר אוטומטית כשכל התהליכים יסתיימו. ההעלאה/ההורדה ממשיכה עד הסוף."
            : "התוכנה תיסגר אוטומטית כשכל התהליכים יסתיימו.";

        if (AccBackgroundWorkMonitor.TotalActiveCount == 0)
        {
            DialogResult = true;
        }
    }

    private void ForceClose_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    protected override void OnClosed(EventArgs e)
    {
        AccBackgroundWorkMonitor.TotalActiveCountChanged -= OnActiveUploadsChanged;
        base.OnClosed(e);
    }
}
