using System.Windows;
using YukkuriMovieMaker.Plugin;

namespace FaceAssign;

public class FaceAssignToolPlugin : IToolPlugin
{
    public FaceAssignToolPlugin()
    {
        // 起動時に右クリックメニューのフックを張る。
        // これでツールパネルを一度も開かなくても項目が出る。
        var application = Application.Current;
        if (application is not null)
            application.Dispatcher.BeginInvoke(TimelineContextMenuHook.Register);
        else
            TimelineContextMenuHook.Register();
    }

    // ツールメニューは Name と Title を並べて出すので、両方を短く揃える。
    public string Name => "表情割り当て";

    public Type ViewModelType => typeof(FaceAssignToolViewModel);

    public Type ViewType => typeof(FaceAssignToolView);

    public bool AllowMultipleInstances => false;
}
