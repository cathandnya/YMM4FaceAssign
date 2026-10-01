using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace FaceAssign;

/// <summary>
/// 処理中であることを知らせる小さなオーバーレイ。
///
/// 実行は右クリックメニューから行うので、ツールパネルを開いていないことがある。
/// その場合ログは見えないため、進捗と結果はここに出す。
/// YMM4 のウィンドウの右下に出し、終わったら少し見せてから自分で消える。
/// </summary>
internal sealed class ProgressOverlay
{
    private static ProgressOverlay? current;

    private readonly Window window;
    private readonly TextBlock text;
    private readonly ProgressBar bar;
    private readonly DispatcherTimer closeTimer;

    private ProgressOverlay()
    {
        text = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6),
        };

        bar = new ProgressBar
        {
            Height = 4,
            Minimum = 0,
            Maximum = 1,
            IsIndeterminate = true,
            Foreground = new SolidColorBrush(Color.FromRgb(0x4F, 0xC3, 0xF7)),
            Background = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(0),
        };

        var panel = new StackPanel { Margin = new Thickness(14, 10, 14, 12) };
        panel.Children.Add(text);
        panel.Children.Add(bar);

        window = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            SizeToContent = SizeToContent.Height,
            Width = 300,
            ResizeMode = ResizeMode.NoResize,
            Content = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x20, 0x20, 0x20)),
                CornerRadius = new CornerRadius(6),
                Child = panel,
            },
        };

        closeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        closeTimer.Tick += (_, _) => Close();
    }

    /// <summary>処理の開始。UIスレッド以外から呼ばれても構わない。</summary>
    public static void Begin(string message) => OnUi(() =>
    {
        current ??= new ProgressOverlay();
        current.closeTimer.Stop();
        current.text.Text = message;
        current.bar.IsIndeterminate = true;
        current.Place();
        current.window.Show();
    });

    public static void Report(double rate, string message) => OnUi(() =>
    {
        if (current is null)
            return;
        current.text.Text = message;
        current.bar.IsIndeterminate = rate <= 0;
        current.bar.Value = Math.Clamp(rate, 0, 1);
    });

    /// <summary>終了。結果をしばらく見せてから消す。</summary>
    public static void Finish(string message, bool isError = false) => OnUi(() =>
    {
        if (current is null)
            return;

        current.text.Text = message;
        current.bar.IsIndeterminate = false;
        current.bar.Value = 1;
        current.bar.Foreground = isError
            ? new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50))
            : new SolidColorBrush(Color.FromRgb(0x66, 0xBB, 0x6A));
        current.closeTimer.Interval = TimeSpan.FromSeconds(isError ? 8 : 4);
        current.closeTimer.Stop();
        current.closeTimer.Start();
    });

    /// <summary>
    /// Hide ではなく Close する。WPF は既定で最後のウィンドウが閉じたら終了するため、
    /// 隠したまま持ち続けると YMM4 が終了できなくなる。
    /// </summary>
    private void Close()
    {
        closeTimer.Stop();
        if (ReferenceEquals(current, this))
            current = null;
        window.Close();
    }

    /// <summary>YMM4 のウィンドウの右下に寄せる。</summary>
    private void Place()
    {
        var owner = Application.Current?.MainWindow;
        if (owner is null || owner.WindowState == WindowState.Minimized)
            return;

        try
        {
            // 所有者を付けておくと、YMM4 本体が閉じるときに一緒に片付く。
            if (window.Owner is null && !ReferenceEquals(owner, window))
                window.Owner = owner;

            window.Left = owner.Left + owner.ActualWidth - window.Width - 28;
            window.Top = owner.Top + owner.ActualHeight - 110;
        }
        catch (Exception)
        {
            // 位置が取れなくても表示自体は続ける。
        }
    }

    private static void OnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
            return;

        if (dispatcher.CheckAccess())
            action();
        else
            dispatcher.BeginInvoke(action);
    }
}
