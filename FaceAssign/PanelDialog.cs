using System.Windows;

namespace FaceAssign;

/// <summary>
/// パネルのウィンドウを親にしてダイアログを出す。
///
/// 接続テストのようにパネル上の操作で始めたものは、結果もそのウィンドウに返す。
/// 親が分からない場合でもダイアログ自体は出す (親なしになるだけ)。
/// </summary>
internal static class PanelDialog
{
    private static WeakReference<Window>? owner;

    /// <summary>パネルの View が読み込まれたときに自分を登録する。</summary>
    public static void SetOwner(Window? window) =>
        owner = window is null ? null : new WeakReference<Window>(window);

    public static void Show(string message, string title, bool isError)
    {
        var icon = isError ? MessageBoxImage.Warning : MessageBoxImage.Information;

        if (owner is not null && owner.TryGetTarget(out var window) && window.IsLoaded)
        {
            MessageBox.Show(window, message, title, MessageBoxButton.OK, icon);
            return;
        }

        MessageBox.Show(message, title, MessageBoxButton.OK, icon);
    }
}
