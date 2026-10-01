using System.Text;
using System.Text.Json.Nodes;

namespace FaceAssign.Llm;

/// <summary>
/// プロンプトと JSON スキーマの組み立て。
///
/// 出力は「候補の番号」で返させる。プリセット名を日本語で書かせると出力トークンが増えて
/// そのぶん遅くなるため。番号は enum で実在する候補だけに縛るので、範囲外は構造的に返せない。
/// キー名も1文字に縮めてある。
/// </summary>
public static class FaceAssignRequestBuilder
{
    /// <summary>「どのプリセットも合わない」を表す番号。</summary>
    public const int KeepIndex = -1;

    /// <param name="itemCount">
    /// そのリクエストの対象行数。いまは使っていない。
    /// minItems/maxItems で配列長を固定したところ、返ってくる件数がむしろ減ったので外した
    /// (24行が12行しか決まらなくなった)。
    /// </param>
    public static JsonObject BuildSchema(IReadOnlyList<PresetCandidate> candidates, bool includeReason, int itemCount)
    {
        var presetEnum = new JsonArray { KeepIndex };
        for (var i = 0; i < candidates.Count; i++)
            presetEnum.Add(i);

        var properties = new JsonObject
        {
            ["i"] = new JsonObject { ["type"] = "integer" },
            ["p"] = new JsonObject { ["type"] = "integer", ["enum"] = presetEnum },
        };

        if (includeReason)
            properties["r"] = new JsonObject { ["type"] = "string" };

        return new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray("a"),
            ["properties"] = new JsonObject
            {
                ["a"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["additionalProperties"] = false,
                        ["required"] = includeReason ? new JsonArray("i", "p", "r") : new JsonArray("i", "p"),
                        ["properties"] = properties,
                    },
                },
            },
        };
    }

    public static string BuildSystemPrompt(
        IReadOnlyList<PresetCandidate> candidates,
        string? policy,
        bool appendNoThinkHint,
        bool includeReason = true)
    {
        var sb = new StringBuilder();
        sb.AppendLine("あなたは動画の台本に立ち絵の表情を割り当てる担当です。");
        sb.AppendLine("各セリフについて、下の候補から最もふさわしい表情の番号をひとつ選びます。");
        sb.AppendLine();
        sb.AppendLine("判断の指針:");
        sb.AppendLine("- セリフの感情を優先する。直前の流れも考慮してよい。");
        sb.AppendLine("- 淡々とした説明や相槌には、平常に近い表情を選ぶ。連続して同じ表情になるのは構わない。");
        sb.AppendLine($"- 判断に迷う場合や、どの候補もしっくりこない場合は {KeepIndex} を返してよい。無理に選ばない。");

        if (includeReason)
            sb.AppendLine("- r は40字以内の日本語で、なぜその表情かを簡潔に書く。");

        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(policy))
        {
            sb.AppendLine("作品の方針:");
            sb.AppendLine(policy!.Trim());
            sb.AppendLine();
        }

        sb.AppendLine("表情の候補 (番号: 名前):");
        for (var i = 0; i < candidates.Count; i++)
            sb.AppendLine($"{i}: {candidates[i].Label}");
        sb.AppendLine($"{KeepIndex}: どの候補も合わない");
        sb.AppendLine();
        sb.AppendLine("入力の各行は [キャラクター名 #ID] セリフ の形式です。");
        sb.AppendLine("#ID が付いている行だけが対象で、付いていない行は文脈として読むだけにします。");
        sb.AppendLine("対象の行すべてに、過不足なくひとつずつ割り当てを返してください。");
        sb.AppendLine(includeReason
            ? """出力は {"a":[{"i":ID,"p":番号,"r":"理由"}]} の形式の JSON のみ。"""
            : """出力は {"a":[{"i":ID,"p":番号}]} の形式の JSON のみ。""");

        if (appendNoThinkHint)
        {
            sb.AppendLine();
            sb.Append("/no_think");
        }

        return sb.ToString();
    }

    public static string BuildUserPrompt(IReadOnlyList<TranscriptLine> lines)
    {
        var sb = new StringBuilder();
        foreach (var line in lines)
        {
            var serif = line.Serif.Replace("\r", "").Replace("\n", " ").Trim();
            sb.AppendLine(line.Id is int id
                ? $"[{line.CharacterName} #{id}] {serif}"
                : $"[{line.CharacterName}] {serif}");
        }
        return sb.ToString();
    }
}
