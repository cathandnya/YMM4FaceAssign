using System.Collections.Frozen;

namespace FaceAssign.Tachie;

/// <summary>
/// 動く立ち絵 (AnimationTachie) の表情パラメータが持つ12個のスロット。
/// 名前は AnimationTachie.ParameterBase のプロパティ名、および .ymmp の JSON プロパティ名と一致する。
/// </summary>
public enum TachiePartSlot
{
    Eyebrow,
    Eye,
    Mouth,
    Hair,
    Complexion,
    Body,
    Back1,
    Back2,
    Back3,
    Etc1,
    Etc2,
    Etc3,
}

public static class TachiePartTables
{
    /// <summary>スロットの並び。プロパティ名としてそのまま使う。</summary>
    public static readonly TachiePartSlot[] AllSlots = Enum.GetValues<TachiePartSlot>();

    /// <summary>preset.ini のキー → スロット。後1/後2/後3 と 他1/他2/他3 は番号付き。</summary>
    public static readonly FrozenDictionary<string, TachiePartSlot> IniKeyToSlot =
        new Dictionary<string, TachiePartSlot>(StringComparer.Ordinal)
        {
            ["眉"] = TachiePartSlot.Eyebrow,
            ["目"] = TachiePartSlot.Eye,
            ["口"] = TachiePartSlot.Mouth,
            ["髪"] = TachiePartSlot.Hair,
            ["顔色"] = TachiePartSlot.Complexion,
            ["体"] = TachiePartSlot.Body,
            ["後1"] = TachiePartSlot.Back1,
            ["後2"] = TachiePartSlot.Back2,
            ["後3"] = TachiePartSlot.Back3,
            ["他1"] = TachiePartSlot.Etc1,
            ["他2"] = TachiePartSlot.Etc2,
            ["他3"] = TachiePartSlot.Etc3,
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>
    /// スロット → 素材フォルダ名。
    /// Back1/2/3 は3つとも "後"、Etc1/2/3 は3つとも "他" に入っている点に注意。
    /// </summary>
    public static readonly FrozenDictionary<TachiePartSlot, string> SlotToFolder =
        new Dictionary<TachiePartSlot, string>
        {
            [TachiePartSlot.Eyebrow] = "眉",
            [TachiePartSlot.Eye] = "目",
            [TachiePartSlot.Mouth] = "口",
            [TachiePartSlot.Hair] = "髪",
            [TachiePartSlot.Complexion] = "顔色",
            [TachiePartSlot.Body] = "体",
            [TachiePartSlot.Back1] = "後",
            [TachiePartSlot.Back2] = "後",
            [TachiePartSlot.Back3] = "後",
            [TachiePartSlot.Etc1] = "他",
            [TachiePartSlot.Etc2] = "他",
            [TachiePartSlot.Etc3] = "他",
        }.ToFrozenDictionary();

    /// <summary>スロットに対応するプロパティ名 (enum 名と同一)。</summary>
    public static string PropertyName(TachiePartSlot slot) => slot.ToString();

    /// <summary>診断表示用の短いパーツ名 (INI キーの代表形)。</summary>
    public static string DisplayKey(TachiePartSlot slot) => slot switch
    {
        TachiePartSlot.Back1 or TachiePartSlot.Back2 or TachiePartSlot.Back3 => "後",
        TachiePartSlot.Etc1 or TachiePartSlot.Etc2 or TachiePartSlot.Etc3 => "他",
        _ => SlotToFolder[slot],
    };
}
