using FaceAssign.Llm;

namespace LlmSmoke;

/// <summary>採点用の台本。期待は「これなら妥当」と言える範囲を複数許す。</summary>
public static class Script
{
    public sealed record Expectation(int Id, string Serif, string[] Acceptable);

    public static readonly Expectation[] Lines =
    [
        new(1,  "こんにちは、ずんだもんなのだ！",             ["カメラ目線", "にこにこ", "こっち見る"]),
        new(2,  "やったー！ついに動画が完成したのだ！",         ["にこにこ", "パカー"]),
        new(3,  "うう……でも財布を落としてしまったのだ",       ["泣く", "困り2", "ぬーん"]),
        new(4,  "ふざけるな！誰がそんなこと許したのだ！",       ["シャキーン", "パカー"]),
        new(5,  "うーん、これはどういう仕組みなのだ……",        ["考える", "考える2", "ぬーん"]),
        new(6,  "え、うそ、後ろに誰かいるのだ……？",           ["あせり", "パカー", "見る"]),
        new(7,  "まあ、そういうこともあるのだ",               ["ぬーん", "見る", "カメラ目線", "こっち見る"]),
        new(8,  "そこ、よく見てほしいのだ",                   ["指差し", "指差し #2", "カメラ目線"]),
    ];

    /// <summary>四国めたんの合いの手。文脈としてだけ渡る (ID 無し)。</summary>
    public static IReadOnlyList<TranscriptLine> BuildTranscript()
    {
        var t = new List<TranscriptLine>();
        foreach (var line in Lines)
        {
            t.Add(new TranscriptLine(line.Id, "ずんだもん", line.Serif));
            if (line.Id == 2)
                t.Add(new TranscriptLine(null, "四国めたん", "おめでとう。長かったわね"));
            if (line.Id == 4)
                t.Add(new TranscriptLine(null, "四国めたん", "落ち着きなさいよ"));
        }
        return t;
    }
}
