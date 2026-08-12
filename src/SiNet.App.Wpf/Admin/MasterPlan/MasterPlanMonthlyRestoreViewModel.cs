using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using SiNet.App.Wpf.Inbox;
using SiNet.App.Wpf.Inspection;

namespace SiNet.App.Wpf.Admin.MasterPlan;

public sealed class MasterPlanMonthlyRestoreViewModel : ObservableObject
{
    private readonly AsyncRelayCommand _browseCommand;
    private readonly AsyncRelayCommand _runCommand;
    private CancellationTokenSource? _runCts;
    private string? _backupPath;
    private string _statusMessage =
        "בחר קובץ .bak והפעל שחזור חודשי. הפעולה משחזרת את Db_Mp_SiEng ומחליפה את טבלאות MP_* ברפליקה.";
    private string _outputLog = string.Empty;
    private bool _isBusy;

    public MasterPlanMonthlyRestoreViewModel()
    {
        _browseCommand = new AsyncRelayCommand(BrowseAsync, () => !IsBusy);
        _runCommand = new AsyncRelayCommand(RunAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(BackupPath));
    }

    public ICommand BrowseCommand => _browseCommand;
    public ICommand RunCommand => _runCommand;

    public string? BackupPath
    {
        get => _backupPath;
        set
        {
            if (SetField(ref _backupPath, value))
            {
                _runCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public string OutputLog
    {
        get => _outputLog;
        private set => SetField(ref _outputLog, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                _browseCommand.RaiseCanExecuteChanged();
                _runCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private Task BrowseAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "בחירת גיבוי MasterPlan",
            Filter = "Backup (*.bak)|*.bak|All files (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() == true)
        {
            BackupPath = dialog.FileName;
            StatusMessage = $"נבחר: {BackupPath}";
        }

        return Task.CompletedTask;
    }

    private async Task RunAsync()
    {
        if (string.IsNullOrWhiteSpace(BackupPath))
        {
            StatusMessage = "יש לבחור קובץ .bak.";
            return;
        }

        var confirm = MessageBox.Show(
            "פעולה הרסנית:\n\n" +
            "• ישחזר את הגיבוי על Db_Mp_SiEng (ReplaceDatabase).\n" +
            "• ירשום אי-התאמות מול הרפליקה הנוכחית ללוג SyncEngine.\n" +
            "• ימחק ויבנה מחדש את טבלאות MP_* ב־Replica_DB מהגיבוי.\n\n" +
            "שרת ה-SQL חייב להיות מסוגל לקרוא את נתיב הקובץ.\n\n" +
            $"קובץ: {BackupPath}\n\n" +
            "להמשיך?",
            "שחזור חודשי MasterPlan — אישור",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (confirm != MessageBoxResult.Yes)
        {
            StatusMessage = "בוטל על ידי המשתמש.";
            return;
        }

        var exe = MasterPlanSyncEngineLauncher.ResolveExecutablePath();
        if (exe is null)
        {
            StatusMessage =
                "MasterPlan.SyncEngine.exe לא נמצא. בדוק את הנתיב שפורסם או בנה את SyncEngine מקומית.";
            return;
        }

        IsBusy = true;
        OutputLog = string.Empty;
        StatusMessage = $"מריץ SyncEngine…\n{exe}";
        _runCts = new CancellationTokenSource();

        try
        {
            var progress = new Progress<string>(line =>
            {
                OutputLog = string.IsNullOrEmpty(OutputLog) ? line : OutputLog + Environment.NewLine + line;
                if (line.Contains("הכול תואם", StringComparison.Ordinal)
                    || line.Contains("אי-התאמות", StringComparison.Ordinal)
                    || line.Contains("[COMPARE", StringComparison.Ordinal)
                    || line.Contains("GATE", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("SUCCESS", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("FAILURE", StringComparison.OrdinalIgnoreCase))
                {
                    StatusMessage = line;
                }
            });

            var (exitCode, combined) = await MasterPlanSyncEngineLauncher
                .RunMonthlyAsync(exe, BackupPath, progress, _runCts.Token)
                .ConfigureAwait(true);

            if (string.IsNullOrWhiteSpace(OutputLog) && !string.IsNullOrWhiteSpace(combined))
            {
                OutputLog = combined;
            }

            StatusMessage = exitCode == 0
                ? "השחזור החודשי הסתיים בהצלחה. פירוט אי-התאמות בלוג SyncEngine (מרכזי/מקומי)."
                : $"השחזור נכשל (קוד יציאה {exitCode}). ראו את הפלט ואת לוג SyncEngine.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "ההרצה בוטלה.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"שגיאה: {ex.Message}";
            OutputLog = string.IsNullOrEmpty(OutputLog)
                ? ex.ToString()
                : OutputLog + Environment.NewLine + ex;
        }
        finally
        {
            _runCts.Dispose();
            _runCts = null;
            IsBusy = false;
        }
    }
}
