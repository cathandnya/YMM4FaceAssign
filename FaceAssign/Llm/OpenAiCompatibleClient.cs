using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FaceAssign.Llm;

/// <summary>OpenAI互換のチャットAPIを叩く。Ollama / LM Studio / クラウドを同じ経路で扱う。</summary>
public sealed partial class OpenAiCompatibleClient : IDisposable
{
    private readonly HttpClient http;
    private readonly LlmEndpoint endpoint;

    public OpenAiCompatibleClient(LlmEndpoint endpoint)
    {
        this.endpoint = endpoint;
        http = new HttpClient { Timeout = TimeSpan.FromSeconds(Math.Max(10, endpoint.TimeoutSeconds)) };
        if (!string.IsNullOrWhiteSpace(endpoint.ApiKey))
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", endpoint.ApiKey);
    }

    public void Dispose() => http.Dispose();

    /// <summary>接続テスト兼モデル一覧。</summary>
    public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct)
    {
        using var response = await http.GetAsync(endpoint.ResolveModelsUrl(), ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new LlmException($"モデル一覧の取得に失敗しました ({(int)response.StatusCode})", body);

        var node = JsonNode.Parse(body);
        var data = node?["data"]?.AsArray();
        if (data is null)
            return [];

        return data.Select(d => d?["id"]?.GetValue<string>()).OfType<string>().ToArray();
    }

    /// <summary>
    /// モデルをVRAMに載せておくためだけの最小リクエスト。
    /// LM Studio は待機中にモデルを降ろすので、初回が10秒以上かかることがある。
    /// 失敗しても何もしない。
    /// </summary>
    public async Task WarmUpAsync(CancellationToken ct)
    {
        var request = new JsonObject
        {
            ["model"] = endpoint.Model,
            ["stream"] = false,
            ["max_tokens"] = 1,
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = "." }),
        };

        try
        {
            using var content = new ByteArrayContent(Encoding.UTF8.GetBytes(request.ToJsonString()));
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
            using var response = await http.PostAsync(endpoint.ResolveChatCompletionsUrl(), content, ct).ConfigureAwait(false);
        }
        catch
        {
            // 暖機は成功しなくても構わない。
        }
    }

    /// <summary>
    /// JSON スキーマで縛ってチャット補完を1回叩く。
    /// サーバが json_schema を拒否した場合は、スキーマ無し + 緩いパースで1回だけ再試行する。
    /// </summary>
    public async Task<JsonNode> CompleteJsonAsync(
        string systemPrompt,
        string userPrompt,
        JsonObject schema,
        string schemaName,
        CancellationToken ct)
    {
        try
        {
            var body = await PostAsync(systemPrompt, userPrompt, schema, schemaName, ct).ConfigureAwait(false);
            return ParseContent(body);
        }
        catch (LlmException ex) when (ex.IsSchemaRejection)
        {
            var body = await PostAsync(systemPrompt, userPrompt, null, schemaName, ct).ConfigureAwait(false);
            return ParseContent(body);
        }
    }

    private async Task<string> PostAsync(
        string systemPrompt,
        string userPrompt,
        JsonObject? schema,
        string schemaName,
        CancellationToken ct)
    {
        var request = new JsonObject
        {
            ["model"] = endpoint.Model,
            ["temperature"] = endpoint.Temperature,
            ["stream"] = false,
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = systemPrompt },
                new JsonObject { ["role"] = "user", ["content"] = userPrompt }),
        };

        // gpt-oss 系は既定で長考する。low にすると体感が大きく変わる。
        // 対応していないサーバは未知フィールドとして無視する。
        if (!string.IsNullOrWhiteSpace(endpoint.ReasoningEffort))
            request["reasoning_effort"] = endpoint.ReasoningEffort;

        if (schema is not null)
        {
            request["response_format"] = new JsonObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = new JsonObject
                {
                    ["name"] = schemaName,
                    ["strict"] = true,
                    ["schema"] = schema.DeepClone(),
                },
            };
        }

        // enum に日本語が入るので、必ず UTF-8 バイトで送る。
        var json = request.ToJsonString();
        using var content = new ByteArrayContent(Encoding.UTF8.GetBytes(json));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };

        using var response = await http.PostAsync(endpoint.ResolveChatCompletionsUrl(), content, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var schemaRejected = schema is not null &&
                (body.Contains("response_format", StringComparison.OrdinalIgnoreCase) ||
                 body.Contains("json_schema", StringComparison.OrdinalIgnoreCase));
            throw new LlmException($"生成に失敗しました ({(int)response.StatusCode})", body, schemaRejected);
        }

        return body;
    }

    private static JsonNode ParseContent(string responseBody)
    {
        var content = JsonNode.Parse(responseBody)?["choices"]?[0]?["message"]?["content"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(content))
            throw new LlmException("応答が空でした", responseBody);

        var cleaned = Sanitize(content);
        try
        {
            return JsonNode.Parse(cleaned) ?? throw new LlmException("JSONを解釈できませんでした", content);
        }
        catch (JsonException ex)
        {
            throw new LlmException($"JSONを解釈できませんでした: {ex.Message}", content);
        }
    }

    /// <summary>
    /// thinking を出すモデルや、コードフェンスで包むモデルに備えて前処理する。
    /// json_schema が効いていれば素通りする。
    /// </summary>
    internal static string Sanitize(string content)
    {
        var s = ThinkBlock().Replace(content, "").Trim();
        s = CodeFence().Replace(s, "").Trim();

        var start = s.IndexOf('{');
        var end = s.LastIndexOf('}');
        return start >= 0 && end > start ? s[start..(end + 1)] : s;
    }

    [GeneratedRegex(@"<think>.*?</think>", RegexOptions.Singleline)]
    private static partial Regex ThinkBlock();

    [GeneratedRegex(@"^```(?:json)?|```$", RegexOptions.Multiline)]
    private static partial Regex CodeFence();
}

public sealed class LlmException(string message, string? responseBody = null, bool isSchemaRejection = false)
    : Exception(message)
{
    public string? ResponseBody { get; } = responseBody;

    public bool IsSchemaRejection { get; } = isSchemaRejection;

    public string DetailedMessage => string.IsNullOrWhiteSpace(ResponseBody)
        ? Message
        : $"{Message}\n{Truncate(ResponseBody!, 800)}";

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
