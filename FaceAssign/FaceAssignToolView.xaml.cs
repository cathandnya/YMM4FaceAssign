using System.Windows.Controls;

namespace FaceAssign;

public partial class FaceAssignToolView : UserControl
{
    public FaceAssignToolView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is not FaceAssignToolViewModel viewModel)
            return;

        TimelineContextMenuHook.Attach(viewModel);

        // 開いている間だけ、ログの更新をこのパネルへ流す。
        FaceAssignLog.Changed = viewModel.RefreshLog;
        viewModel.RefreshLog();
    }

    private void OnUnloaded(object sender, System.Windows.RoutedEventArgs e)
    {
        FaceAssignLog.Changed = null;

        if (DataContext is FaceAssignToolViewModel viewModel)
            TimelineContextMenuHook.Detach(viewModel);
    }
}
