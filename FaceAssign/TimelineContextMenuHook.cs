using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace FaceAssign;

/// <summary>
/// タイムラインの右クリックメニューに項目を差し込む。
///
/// YMM4 にはメニューを拡張する公開APIが無い。ContextMenuOpening を主ウィンドウで待つ方法は
/// メニューをプログラム的に開かれると発生しないので、ContextMenu.Opened のクラスハンドラで
/// アプリ内のあらゆる ContextMenu を捕まえてから、タイムラインのものだけに項目を足している。
/// 非公式なので、効かなくなっても「項目が出ない」だけで済ませる。
/// </summary>
internal static class TimelineContextMenuHook
{
    private const string MarkerTag = "FaceAssign.ContextMenuItem";

    private static bool registered;
    private static FaceAssignToolViewModel? current;

    /// <summary>
    /// 既定はオフ。項目が出ないときに true にすると、捕まえた ContextMenu の
    /// 配置先と DataContext の型名を書き出す。
    /// 実測で当たっていた型: PlacementTarget=YukkuriMovieMaker.Views.TimelineItemView,
    /// DataContext=TimelineViewModel / TimelineItemViewModel。
    /// </summary>
    public static bool IsDiagnosticsEnabled { get; set; }

    public static string DiagnosticsPath { get; } =
        Path.Combine(Path.GetTempPath(), "FaceAssign-contextmenu.log");

    /// <summary>パネルが開いたら、そのViewModelを優先して使う。</summary>
    public static void Attach(FaceAssignToolViewModel viewModel)
    {
        current = viewModel;
        Register();
    }

    /// <summary>プラグイン読み込み時に呼ぶ。パネルを開かなくても右クリック項目を出すため。</summary>
    public static void Register()
    {
        if (registered)
            return;

        EventManager.RegisterClassHandler(
            typeof(ContextMenu),
            ContextMenu.OpenedEvent,
            new RoutedEventHandler(OnContextMenuOpened),
            handledEventsToo: true);

        registered = true;
        Log("registered class handler");
    }

    /// <summary>
    /// 参照は意図的に手放さない。AvalonDock はタブを切り替えただけで Unloaded を投げるので、
    /// ここで null にすると右クリック項目が消えてしまう。別のパネルが Attach すれば置き換わる。
    /// </summary>
    public static void Detach(FaceAssignToolViewModel viewModel)
    {
    }

    private static void OnContextMenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu)
            return;

        try
        {
            var isTimeline = LooksLikeTimeline(menu);

            if (IsDiagnosticsEnabled)
                Log(Describe(menu, isTimeline, current is not null));

            if (!isTimeline)
                return;

            EnsureMenuItem(menu);

            // メニューを見ている間にモデルを載せておく。結果は待たない。
            _ = GetOrCreateViewModel(menu)?.WarmUpAsync();
        }
        catch (Exception ex)
        {
            Log($"EXCEPTION {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// タイムライン上のメニューかどうかを、配置先の祖先の DataContext 型名で大まかに判定する。
    /// 内部実装なので外れることがあるが、外れたときは項目が出ないだけにする。
    /// </summary>
    private static bool LooksLikeTimeline(ContextMenu menu)
    {
        foreach (var context in EnumerateDataContexts(menu))
        {
            var name = context.GetType().FullName;
            if (name is not null && name.Contains("Timeline", StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static IEnumerable<object> EnumerateDataContexts(ContextMenu menu)
    {
        if (menu.DataContext is { } own)
            yield return own;

        for (var node = menu.PlacementTarget as DependencyObject; node is not null; node = GetParent(node))
        {
            if (node is FrameworkElement { DataContext: { } context })
                yield return context;
        }
    }

    private static DependencyObject? GetParent(DependencyObject node)
    {
        if (node is Visual or Visual3D)
        {
            var visualParent = VisualTreeHelper.GetParent(node);
            if (visualParent is not null)
                return visualParent;
        }

        return node is FrameworkElement element ? element.Parent ?? element.TemplatedParent : null;
    }

    private static void EnsureMenuItem(ContextMenu menu)
    {
        var item = menu.Items.OfType<MenuItem>().FirstOrDefault(m => Equals(m.Tag, MarkerTag));

        if (item is null)
        {
            // YMM4 の既定メニューは40項目以上あるので、末尾だと埋もれる。先頭に置く。
            item = new MenuItem { Tag = MarkerTag };
            item.Click += OnClick;

            menu.Items.Insert(0, item);
            if (menu.Items.Count > 1)
                menu.Items.Insert(1, new Separator { Tag = MarkerTag });
        }

        var enabled = GetOrCreateViewModel(menu)?.HasTargets() ?? false;
        item.Header = "セリフから表情を推定して適用";
        item.IsEnabled = enabled;
        item.ToolTip = enabled
            ? "選択中のボイスアイテムを推定し、そのまま書き込みます。Ctrl+Z 1回で戻せます。"
            : "動く立ち絵のボイスアイテムを選択してください。";
    }

    /// <summary>
    /// async void になるので、例外がここから外へ出ると WPF ごと落ちる。必ず握る。
    /// </summary>
    private static async void OnClick(object sender, RoutedEventArgs e)
    {
        var menu = (sender as FrameworkElement)?.Parent as ContextMenu
            ?? ItemsControl.ItemsControlFromItemContainer(sender as DependencyObject) as ContextMenu;

        var viewModel = GetOrCreateViewModel(menu);
        FaceAssignLog.Write($"menu clicked. vm={(viewModel is null ? "null" : "ok")} hasTargets={viewModel?.HasTargets()}");

        if (viewModel is null)
        {
            RequestToolPanel();
            return;
        }

        try
        {
            var failure = await viewModel.EstimateAndApplyAsync();
            FaceAssignLog.Write($"menu click finished. failure={failure ?? "(none)"}");

            // パネルが開いていればログに出ているので、二重に知らせない。
            if (failure is not null && !ReferenceEquals(viewModel, current))
                MessageBox.Show(failure, "表情割り当て", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            FaceAssignLog.Write($"menu click EXCEPTION {ex.GetType().Name}: {ex.Message}" +
                                Environment.NewLine + ex.StackTrace);
            MessageBox.Show($"{ex.GetType().Name}: {ex.Message}", "表情割り当て", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// パネルが開いていればその ViewModel を使う。開いていなければ、右クリックされた場所から
    /// タイムラインを掘り出して、その場限りの ViewModel を組み立てる。
    /// </summary>
    private static FaceAssignToolViewModel? GetOrCreateViewModel(ContextMenu? menu)
    {
        if (current is { HasTimeline: true } panel)
            return panel;

        if (menu is null)
            return null;

        var info = TimelineContextResolver.Resolve(EnumerateDataContexts(menu));
        if (info is null)
            return null;

        var viewModel = new FaceAssignToolViewModel();
        viewModel.SetTimelineToolInfo(info);
        return viewModel;
    }

    /// <summary>
    /// タイムラインを掘り出せなかったときの逃げ道。ツールメニューから
    /// 「表情割り当て」を開かせる。見つからなければ案内だけ出す。
    /// </summary>
    private static void RequestToolPanel()
    {
        var window = Application.Current?.MainWindow;
        var opened = window is not null && TryInvokeToolMenuItem(window);

        if (!opened)
        {
            MessageBox.Show(
                "タイムラインを取得できませんでした。ツール → 表情割り当て を開いてから実行してください。",
                "表情割り当て", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private static bool TryInvokeToolMenuItem(DependencyObject root)
    {
        foreach (var item in FindMenuItems(root))
        {
            if (item.Header is string header && header.Contains("表情割り当て", StringComparison.Ordinal))
            {
                item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                FaceAssignLog.Write("requested tool panel via menu");
                return true;
            }
        }
        return false;
    }

    private static IEnumerable<MenuItem> FindMenuItems(DependencyObject node)
    {
        var count = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            if (child is MenuItem item)
                yield return item;

            foreach (var nested in FindMenuItems(child))
                yield return nested;
        }
    }

    private static string Describe(ContextMenu menu, bool isTimeline, bool hasViewModel)
    {
        var contexts = EnumerateDataContexts(menu)
            .Select(c => c.GetType().FullName ?? "?")
            .Distinct()
            .Take(12);

        return $"opened target={menu.PlacementTarget?.GetType().FullName ?? "(null)"} " +
               $"items={menu.Items.Count} isTimeline={isTimeline} hasVM={hasViewModel}\n" +
               $"    contexts: {string.Join(" | ", contexts)}";
    }

    private static void Log(string message) => FaceAssignLog.Write(message);
}
