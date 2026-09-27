using System.Reflection;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.UndoRedo;

namespace FaceAssign;

/// <summary>
/// 右クリックされた場所の DataContext から、タイムライン操作に必要な4つを掘り出す。
///
/// YMM4 はこれらを公開していないので、ViewModel のメンバを型で探している
/// (フィールド名の決め打ちを避けたのは、名前が変わっても拾えるようにするため)。
/// 取れなければ null を返し、呼び出し側はツールパネルを開く導線に落とす。
/// </summary>
internal static class TimelineContextResolver
{
    private const BindingFlags Members =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    public static TimelineToolInfo? Resolve(IEnumerable<object> dataContexts)
    {
        Timeline? timeline = null;
        Scenes? scenes = null;
        UndoRedoManager? undoRedoManager = null;
        AsyncAwaitStatus? asyncAwaitStatus = null;

        foreach (var context in dataContexts)
        {
            foreach (var value in EnumerateMemberValues(context))
            {
                switch (value)
                {
                    case Timeline t:
                        timeline ??= t;
                        break;
                    case Scenes s:
                        scenes ??= s;
                        break;
                    case UndoRedoManager u:
                        undoRedoManager ??= u;
                        break;
                    case AsyncAwaitStatus a:
                        asyncAwaitStatus ??= a;
                        break;
                    case Scene scene:
                        timeline ??= scene.Timeline;
                        scenes ??= scene.Scenes;
                        break;
                }
            }

            if (timeline is not null && scenes is not null && undoRedoManager is not null && asyncAwaitStatus is not null)
                break;
        }

        if (timeline is null || undoRedoManager is null || asyncAwaitStatus is null)
        {
            FaceAssignLog.Write(
                $"resolve failed: timeline={timeline is not null} scenes={scenes is not null} " +
                $"undo={undoRedoManager is not null} async={asyncAwaitStatus is not null}");
            return null;
        }

        // Scenes だけは無くても書き込みには使わないので、空でも進める。
        return new TimelineToolInfo(timeline, scenes!, undoRedoManager, asyncAwaitStatus);
    }

    private static IEnumerable<object> EnumerateMemberValues(object context)
    {
        var type = context.GetType();

        foreach (var field in type.GetFields(Members))
        {
            object? value = null;
            try
            {
                value = field.GetValue(context);
            }
            catch (Exception)
            {
                // 触れないメンバは飛ばす。
            }

            if (value is not null)
                yield return value;
        }

        foreach (var property in type.GetProperties(Members))
        {
            if (property.GetIndexParameters().Length > 0 || !property.CanRead)
                continue;

            object? value = null;
            try
            {
                value = property.GetValue(context);
            }
            catch (Exception)
            {
                // ゲッターが例外を投げるものがあるので握る。
            }

            if (value is not null)
                yield return value;
        }
    }
}
