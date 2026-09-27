using System.Collections.Frozen;

namespace FaceAssign.Tachie;

/// <summary>
/// LLM が選びやすいプリセット名の例示。プラグインは preset.ini を書き換えない。
/// 整備はユーザーが YMM4 本体の表情プリセット機能かテキストエディタで行う。
/// </summary>
public static class RecommendedPresetNames
{
    /// <summary>これだけあれば一通り回る。</summary>
    public static readonly string[] Required =
        ["標準", "笑顔", "怒り", "悲しみ", "驚き", "照れ", "困惑", "呆れ"];

    /// <summary>表現の幅が出る。</summary>
    public static readonly string[] Recommended =
        ["大笑い", "焦り", "得意", "真剣", "不安", "不満"];

    /// <summary>任意。</summary>
    public static readonly string[] Optional =
        ["泣き", "眠い", "無表情", "疑問"];

    public static readonly FrozenSet<string> All =
        Required.Concat(Recommended).Concat(Optional).ToFrozenSet(StringComparer.Ordinal);

    /// <summary>クリップボードにコピーする用のテキスト。</summary>
    public static string ToClipboardText() =>
        $"""
        # 推奨プリセット名 (preset.ini のセクション名)
        # 必須
        {string.Join(" ", Required)}
        # 推奨
        {string.Join(" ", Recommended)}
        # 任意
        {string.Join(" ", Optional)}

        # 命名のルール
        # - 感情を表す一般的な語にする。擬音 (ぬーん/パカー) は LLM が意味を取れない。
        # - 強度は同じ語幹+強度の語で分ける: 笑顔/大笑い、怒り/激怒。数字だけの区別 (考える2) は避ける。
        # - ポーズだけのプリセット (指差し/食べる/カメラ目線) は候補から外す。消す必要はない。
        # - 同名セクションを作らない。
        # - 1キャラ 8〜15個。
        """;

    public static bool IsRecommended(string presetName) => All.Contains(presetName);
}
