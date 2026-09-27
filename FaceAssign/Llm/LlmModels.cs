namespace FaceAssign.Llm;

/// <summary>OpenAI互換エンドポイントの接続設定。</summary>
public sealed record LlmEndpoint(
    string BaseUrl,
    string Model,
    string? ApiKey = null,
    double Temperature = 0,
    int TimeoutSeconds = 180,
    bool AppendNoThinkHint = true,
    string ReasoningEffort = "low",
    bool IncludeReason = true)
{
    /// <summary>
    /// ユーザーは "http://127.0.0.1:1234" とも "http://127.0.0.1:1234/v1" とも貼るので、
    /// 末尾を正規化して /v1 を一度だけ付ける。
    /// </summary>
    public string ResolveChatCompletionsUrl() => $"{NormalizeBase()}/chat/completions";

    public string ResolveModelsUrl() => $"{NormalizeBase()}/models";

    private string NormalizeBase()
    {
        var b = BaseUrl.Trim().TrimEnd('/');
        return b.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? b : b + "/v1";
    }
}

/// <summary>台本の1行。対象キャラの行だけ Id を持ち、それ以外は文脈として渡す。</summary>
public sealed record TranscriptLine(int? Id, string CharacterName, string Serif);

/// <summary>LLM に見せるプリセット候補。</summary>
public sealed record PresetCandidate(string Label, string? Hint = null);

/// <summary>LLM が返した1行分の割り当て。</summary>
public sealed record FaceAssignment(int Id, string Preset, string Reason);
