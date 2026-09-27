using FaceAssign.Llm;
using FaceAssign.Tachie;

namespace LlmSmoke;

/// <summary>
/// 「推奨名に整えたらどうなるか」を、preset.ini を触らずに測るための読み替え表。
/// 実運用ではユーザーが preset.ini のセクション名をこう書き換える想定。
/// </summary>
public static class Curated
{
    public static readonly Dictionary<string, string> Rename = new(StringComparer.Ordinal)
    {
        ["にこにこ"] = "笑顔",
        ["パカー"] = "大笑い",
        ["泣く"] = "悲しみ",
        ["シャキーン"] = "怒り",
        ["あせり"] = "焦り",
        ["考える"] = "真剣",
        ["考える2"] = "困惑",
        ["困り2"] = "不安",
        ["ぬーん"] = "呆れ",
        ["カメラ目線"] = "標準",
        ["こっち見る"] = "得意",
        ["見る"] = "無表情",
        ["上向き"] = "疑問",
    };

    /// <summary>感情ではないもの (指差し/食べる) は候補から外す。整備時の「候補に含めない」に相当。</summary>
    public static readonly HashSet<string> Excluded = new(StringComparer.Ordinal) { "指差し", "食べる" };

    public static PresetCandidate[] Build(IReadOnlyList<PresetDefinition> presets) =>
        [.. presets
            .Where(p => !Excluded.Contains(p.Name))
            .Select(p => new PresetCandidate(Rename.TryGetValue(p.Name, out var n) ? n : p.Label))
            .DistinctBy(c => c.Label)];

    /// <summary>採点用に、元の期待値を読み替え後の名前へ変換する。</summary>
    public static string[] Translate(string[] acceptable) =>
        [.. acceptable.Select(a => Rename.TryGetValue(a, out var n) ? n : a).Distinct()];
}
