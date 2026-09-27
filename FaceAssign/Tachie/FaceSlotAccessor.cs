using System.Collections.Concurrent;
using System.Reflection;

namespace FaceAssign.Tachie;

/// <summary>
/// 表情パラメータの12スロットにリフレクションでアクセスする。
///
/// AnimationTachie の FaceParameter / ParameterBase は internal なので、参照を足しても
/// C# から型名を書けない。依存先を「型」ではなく「プロパティ名」にすることで、
/// .ymmp の JSON プロパティ名 (= 既存プロジェクトを壊さずには変えられない契約) に寄せている。
///
/// setter の内部で UndoRedoable.Set が PropertyChanged と undo コマンドを発火するため、
/// リフレクション経由の代入は通常の代入と等価に扱える。
/// </summary>
public sealed class FaceSlotAccessor
{
    public const string AnimationTachieFaceParameterTypeName =
        "YukkuriMovieMaker.Plugin.Tachie.AnimationTachie.FaceParameter";

    private static readonly ConcurrentDictionary<Type, FaceSlotAccessor> Cache = new();

    private readonly PropertyInfo[] properties;

    public Type TargetType { get; }

    /// <summary>対象が動く立ち絵の表情パラメータかどうか。</summary>
    public bool IsAnimationTachie { get; }

    private FaceSlotAccessor(Type type)
    {
        TargetType = type;
        IsAnimationTachie = type.FullName == AnimationTachieFaceParameterTypeName;

        var slots = TachiePartTables.AllSlots;
        properties = new PropertyInfo[slots.Length];

        for (var i = 0; i < slots.Length; i++)
        {
            var name = TachiePartTables.PropertyName(slots[i]);
            var prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)
                ?? throw new InvalidOperationException(
                    $"表情パラメータ {type.FullName} にプロパティ {name} がありません。" +
                    "このYMM4バージョンには対応していない可能性があります。");

            if (prop.PropertyType != typeof(string))
                throw new InvalidOperationException(
                    $"表情パラメータ {type.FullName} のプロパティ {name} の型が string ではありません ({prop.PropertyType})。");

            if (!prop.CanRead || !prop.CanWrite)
                throw new InvalidOperationException(
                    $"表情パラメータ {type.FullName} のプロパティ {name} が読み書きできません。");

            properties[i] = prop;
        }
    }

    public static FaceSlotAccessor For(Type type) => Cache.GetOrAdd(type, static t => new FaceSlotAccessor(t));

    public static FaceSlotAccessor For(object faceParameter) => For(faceParameter.GetType());

    public string? Get(object faceParameter, TachiePartSlot slot)
        => (string?)properties[(int)slot].GetValue(faceParameter);

    public void Set(object faceParameter, TachiePartSlot slot, string? value)
        => properties[(int)slot].SetValue(faceParameter, value);
}
