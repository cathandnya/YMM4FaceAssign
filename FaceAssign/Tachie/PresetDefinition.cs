using System.Collections.Immutable;
using System.IO;

namespace FaceAssign.Tachie;

/// <summary>preset.ini の1セクション。</summary>
/// <param name="Name">セクション名そのまま。重複しうる。</param>
/// <param name="Label">一意な表示・enum 用ラベル。重複時は2件目以降に " #2" が付く。</param>
/// <param name="OccurrenceIndex">同名内での出現順 (0 始まり)。</param>
/// <param name="Parts">スロット → 絶対パス。preset.ini が指定していないスロットは含まれない。</param>
/// <param name="MissingFiles">参照先が存在しなかった絶対パス。</param>
public sealed record PresetDefinition(
    string Name,
    string Label,
    int OccurrenceIndex,
    ImmutableDictionary<TachiePartSlot, string> Parts,
    ImmutableArray<string> MissingFiles)
{
    public bool HasMissingFiles => !MissingFiles.IsDefaultOrEmpty;

    /// <summary>一覧に出す1行サマリ。欠落があればその旨も出す。</summary>
    public string PartsSummary => HasMissingFiles
        ? $"{DescribeParts()}  [PNG欠落 {MissingFiles.Length} 件]"
        : DescribeParts();

    /// <summary>診断パネルの「パーツ内容」列。例: 眉=上がり眉 / 目=_ / 他=左腕_手を挙げる,記号_涙</summary>
    public string DescribeParts()
    {
        var groups = Parts
            .OrderBy(kv => kv.Key)
            .GroupBy(kv => TachiePartTables.DisplayKey(kv.Key))
            .Select(g => $"{g.Key}={string.Join(",", g.Select(kv => Path.GetFileNameWithoutExtension(kv.Value)))}");
        return string.Join(" / ", groups);
    }
}
