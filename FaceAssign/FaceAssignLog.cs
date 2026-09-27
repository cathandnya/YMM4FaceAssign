using System.IO;

namespace FaceAssign;

/// <summary>
/// 調査用のファイルログ。パネルのログは開発中に読めないので、同じ内容をここへ落とす。
/// 原因が分かったら IsEnabled を false に戻す。
/// </summary>
internal static class FaceAssignLog
{
    private const int MaxLines = 500;

    private static readonly object Gate = new();
    private static readonly LinkedList<string> Lines = new();

    /// <summary>
    /// ファイルにも落とすか。パネルのログタブに同じ内容が出るので既定はオフ。
    /// パネルを開けない状況を調べるときだけ true にする。
    /// </summary>
    public static bool IsEnabled { get; set; }

    /// <summary>
    /// パネルが開いているときだけ、そこへ通知する。
    /// += ではなく代入にしているのは、右クリック実行で使い捨てる ViewModel が
    /// 購読しっぱなしになるのを避けるため。
    /// </summary>
    public static Action? Changed { get; set; }

    /// <summary>新しいものが上。</summary>
    public static string Text
    {
        get { lock (Gate) return string.Join(Environment.NewLine, Lines); }
    }

    public static string Path { get; } =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FaceAssign-debug.log");

    public static void Write(string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss} {message}";

        lock (Gate)
        {
            Lines.AddFirst(line);
            while (Lines.Count > MaxLines)
                Lines.RemoveLast();
        }

        if (IsEnabled)
        {
            try
            {
                lock (Gate)
                    File.AppendAllText(Path, line + Environment.NewLine);
            }
            catch
            {
                // ログが書けないことは機能の失敗ではない。
            }
        }

        try
        {
            Changed?.Invoke();
        }
        catch
        {
            // 通知先の都合で処理を止めない。
        }
    }
}
