using System.Windows.Controls;

namespace FaceAssign;

public partial class FaceAssignToolView : UserControl
{
    private bool syncingApiKey;

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
        PanelDialog.SetOwner(System.Windows.Window.GetWindow(this));

        // PasswordBox.Password はバインドできないので、開いたときに流し込む。
        syncingApiKey = true;
        ApiKeyBox.Password = viewModel.Config.ApiKey;
        syncingApiKey = false;

        // 開いている間だけ、ログの更新をこのパネルへ流す。
        FaceAssignLog.Changed = viewModel.RefreshLog;
        viewModel.RefreshLog();
    }

    private void OnApiKeyChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        if (syncingApiKey || DataContext is not FaceAssignToolViewModel viewModel)
            return;

        viewModel.Config.ApiKey = ApiKeyBox.Password;
    }

    private void OnUnloaded(object sender, System.Windows.RoutedEventArgs e)
    {
        FaceAssignLog.Changed = null;

        if (DataContext is FaceAssignToolViewModel viewModel)
            TimelineContextMenuHook.Detach(viewModel);
    }
}
