using System.Threading;
using System.Windows;
using SiNet.App.Wpf.WorkSurfaces;
using Xunit;

namespace SiNet.App.Wpf.Tests.WorkSurfaces;

public sealed class TaskSurfaceWindowCoordinatorTests
{
    [Fact]
    public void PrepareOpen_same_kind_returns_existing_window()
    {
        RunSta(() =>
        {
            var sut = new TaskSurfaceWindowCoordinator();
            var first = new Window { Title = "A" };
            first.Show();
            sut.RegisterActive(first, TaskSurfaceWindowKind.EmailWorkItem, taskId: 1);

            var prepared = sut.PrepareOpen(TaskSurfaceWindowKind.EmailWorkItem, taskId: 2);

            Assert.Same(first, prepared);
            Assert.True(sut.IsActiveKind(TaskSurfaceWindowKind.EmailWorkItem));
            first.Close();
        });
    }

    [Fact]
    public void PrepareOpen_other_kind_clears_active_and_returns_null()
    {
        RunSta(() =>
        {
            var sut = new TaskSurfaceWindowCoordinator();
            var email = new Window { Title = "Email" };
            email.Show();
            sut.RegisterActive(email, TaskSurfaceWindowKind.EmailWorkItem, taskId: 1);

            var prepared = sut.PrepareOpen(TaskSurfaceWindowKind.ProjectWork, taskId: 9);

            Assert.Null(prepared);
            Assert.False(sut.IsActiveKind(TaskSurfaceWindowKind.EmailWorkItem));
            Assert.False(sut.IsActiveKind(TaskSurfaceWindowKind.ProjectWork));
            try { email.Close(); } catch { /* may already be closed */ }
        });
    }

    [Fact]
    public void RegisterActive_tracks_latest_window_kind()
    {
        RunSta(() =>
        {
            var sut = new TaskSurfaceWindowCoordinator();
            var first = new Window { Title = "First" };
            first.Show();
            sut.RegisterActive(first, TaskSurfaceWindowKind.Inspection, taskId: 1);

            var second = new Window { Title = "Second" };
            second.Show();
            sut.RegisterActive(second, TaskSurfaceWindowKind.EmailWorkItem, taskId: 2);

            Assert.False(sut.IsActiveKind(TaskSurfaceWindowKind.Inspection));
            Assert.True(sut.IsActiveKind(TaskSurfaceWindowKind.EmailWorkItem));
            try { first.Close(); } catch { /* may already be closed */ }
            second.Close();
        });
    }

    private static void RunSta(Action body)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
            throw error;
    }
}
