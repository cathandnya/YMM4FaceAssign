namespace FaceAssign.Settings;

/// <summary>
/// 接続先の雛形。選ぶとエンドポイントとモデル名が入る。
///
/// モデル名はあくまで出発点で、正しいかは「接続テスト」で確かめること
/// (接続テストは実際に使えるモデルの一覧をログに出す)。
/// </summary>
public sealed record LlmPreset(
    string Name,
    string BaseUrl,
    string ModelName,
    string ReasoningEffort,
    string Note)
{
    public override string ToString() => Name;

    public static readonly LlmPreset[] All =
    [
        new("ローカル",
            "http://127.0.0.1:1234",
            "qwen/qwen3-4b",
            "low",
            "LM Studio の既定ポート。Ollama を使うなら 11434 に変え、並列リクエスト数を 1 に戻す。"),

        new("OpenAI",
            "https://api.openai.com/v1",
            "gpt-5.1-mini",
            "low",
            "APIキーが必要。"),

        new("Claude",
            "https://api.anthropic.com/v1/",
            "claude-haiku-4-5-20251001",
            "",
            "APIキーが必要。OpenAI互換のエンドポイントを使う。"),

        new("Gemini",
            "https://generativelanguage.googleapis.com/v1beta/openai/",
            "gemini-2.5-flash",
            "",
            "APIキーが必要。reasoning_effort は受け付けないので空にする。"),
    ];
}
