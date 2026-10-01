using System.Net.Http;
using System.Text.Json.Nodes;

namespace FaceAssign.Llm;

/// <summary>1行分の推定結果。PresetLabel が null なら「変更しない」。</summary>
public sealed record FaceAssignOutcome(int Id, string? PresetLabel, string? Reason, string? Warning);

public sealed record FaceAssignProgress(int DoneChunks, int TotalChunks, string Message);

/// <summary>
/// 台本を分割して LLM に投げ、行ID → プリセットの対応を作る。
/// 返ってこなかった行や候補外のラベルは「変更しない」に倒す。決して素で添字アクセスしない。
/// </summary>
public sealed class FaceAssignRunner(OpenAiCompatibleClient client, LlmEndpoint endpoint)
{
    /// <summary>返答が来なかった行に付く警告。再質問の対象を見分けるのに使う。</summary>
    internal const string NoAnswerWarning = "LLMが判定を返しませんでした";

    public async Task<IReadOnlyList<FaceAssignOutcome>> RunAsync(
        IReadOnlyList<TranscriptLine> transcript,
        IReadOnlyList<PresetCandidate> candidates,
        string? policy,
        int maxTargetsPerChunk,
        int contextLines,
        IProgress<FaceAssignProgress>? progress,
        CancellationToken ct,
        int parallelism = 1)
    {
        if (candidates.Count == 0)
            throw new InvalidOperationException("候補のプリセットがありません。");

        var system = FaceAssignRequestBuilder.BuildSystemPrompt(candidates, policy, endpoint.AppendNoThinkHint, endpoint.IncludeReason);

        var chunks = Chunk(transcript, maxTargetsPerChunk, contextLines);

        // 本番の前に小さなリクエストを1本通して、モデルのロードを済ませておく。
        // これをやらないと最初の束がロード待ちのままタイムアウトし、リトライも同じく待たされる。
        // 実測では、冷えた状態の最初の束が 255 秒、暖まっていれば 3.9 秒だった。
        //
        // ロード済みなら一瞬で返るので、待たされたときだけ知らせる。
        // 無条件に出すと、手でロード済みでも「準備中」が表示されて紛らわしい。
        var warmUp = client.WarmUpAsync(ct);
        if (await Task.WhenAny(warmUp, Task.Delay(TimeSpan.FromSeconds(1), ct)).ConfigureAwait(false) != warmUp)
            progress?.Report(new FaceAssignProgress(0, chunks.Count, "モデルを読み込んでいます… (初回は時間がかかります)"));

        if (!await warmUp.ConfigureAwait(false))
            progress?.Report(new FaceAssignProgress(0, chunks.Count, "モデルの準備に失敗しました。そのまま続けます…"));

        var perChunk = new IReadOnlyList<FaceAssignOutcome>[chunks.Count];
        var done = 0;

        // ローカルGPUでは並列にしても伸びないことが多いが、リモート相手だと効く。
        using var gate = new SemaphoreSlim(Math.Max(1, parallelism));

        var tasks = chunks.Select(async (chunk, index) =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                perChunk[index] = await ProcessChunkAsync(chunk, system, candidates, progress, chunks.Count, ct)
                    .ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
                var finished = Interlocked.Increment(ref done);
                progress?.Report(new FaceAssignProgress(finished, chunks.Count, $"{finished}/{chunks.Count} 完了"));
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);

        var results = perChunk.Where(r => r is not null).SelectMany(r => r).ToList();

        progress?.Report(new FaceAssignProgress(chunks.Count, chunks.Count, "完了"));
        return results;
    }

    /// <summary>1チャンクを投げ、取りこぼした行があれば一度だけ聞き直す。</summary>
    private async Task<IReadOnlyList<FaceAssignOutcome>> ProcessChunkAsync(
        IReadOnlyList<TranscriptLine> chunk,
        string system,
        IReadOnlyList<PresetCandidate> candidates,
        IProgress<FaceAssignProgress>? progress,
        int totalChunks,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var targetIds = chunk.Where(l => l.Id is not null).Select(l => l.Id!.Value).ToArray();
        var user = FaceAssignRequestBuilder.BuildUserPrompt(chunk);
        var schema = FaceAssignRequestBuilder.BuildSchema(candidates, endpoint.IncludeReason, targetIds.Length);

        JsonNode? json = null;
        string? failure = null;
        try
        {
            json = await client.CompleteJsonAsync(system, user, schema, "face_assignments", ct).ConfigureAwait(false);
        }
        // HttpClient のタイムアウトも OperationCanceledException で来るので、
        // ユーザーによるキャンセルかどうかはトークンで見分ける。
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            failure = $"タイムアウトしました ({endpoint.TimeoutSeconds} 秒)";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (LlmException ex)
        {
            // サーバが何を拒否したかは応答本文にしか書かれていないことが多い。
            failure = ex.DetailedMessage;
        }
        catch (HttpRequestException ex)
        {
            failure = $"接続できません: {ex.Message}";
        }

        var outcomes = Interpret(json, failure, targetIds, candidates);

        // まとめて投げると、モデルによっては後半を落とす (gpt-oss-20b で実測)。
        // 取りこぼした行だけを、同じ文脈のまま一度だけ聞き直す。
        var missing = outcomes
            .Where(o => o.Warning == NoAnswerWarning)
            .Select(o => o.Id)
            .ToHashSet();

        if (failure is not null || missing.Count == 0 || missing.Count == targetIds.Length)
            return outcomes;

        progress?.Report(new FaceAssignProgress(0, totalChunks, $"{missing.Count} 行を聞き直し中"));

        var retryLines = chunk
            .Select(l => l.Id is int lid && !missing.Contains(lid) ? l with { Id = null } : l)
            .ToArray();

        try
        {
            var retrySchema = FaceAssignRequestBuilder.BuildSchema(candidates, endpoint.IncludeReason, missing.Count);
            var retryJson = await client
                .CompleteJsonAsync(system, FaceAssignRequestBuilder.BuildUserPrompt(retryLines), retrySchema, "face_assignments", ct)
                .ConfigureAwait(false);

            var retried = Interpret(retryJson, null, [.. missing], candidates)
                .Where(o => o.Warning != NoAnswerWarning)
                .ToDictionary(o => o.Id);

            return [.. outcomes.Select(o => retried.TryGetValue(o.Id, out var better) ? better : o)];
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // 聞き直しの失敗は致命ではない。元の「変更しない」のままにする。
            return outcomes;
        }
    }

    private static IReadOnlyList<FaceAssignOutcome> Interpret(
        JsonNode? json,
        string? failure,
        IReadOnlyList<int> targetIds,
        IReadOnlyList<PresetCandidate> candidates)
    {
        var byId = new Dictionary<int, FaceAssignOutcome>();

        if (failure is null && json?["a"] is JsonArray array)
        {
            foreach (var entry in array)
            {
                if (entry is null)
                    continue;

                var id = ReadInt(entry["i"]);
                if (id is null)
                    continue;

                // 余分な id は無視する。返ってこなかった id は後段で埋める。
                if (!targetIds.Contains(id.Value) || byId.ContainsKey(id.Value))
                    continue;

                var reason = entry["r"]?.GetValue<string>();
                var index = ReadInt(entry["p"]);

                if (index is null)
                {
                    byId[id.Value] = new FaceAssignOutcome(id.Value, null, reason, "表情の番号を読み取れませんでした");
                    continue;
                }

                if (index.Value == FaceAssignRequestBuilder.KeepIndex)
                {
                    byId[id.Value] = new FaceAssignOutcome(id.Value, null, reason, null);
                    continue;
                }

                byId[id.Value] = index.Value >= 0 && index.Value < candidates.Count
                    ? new FaceAssignOutcome(id.Value, candidates[index.Value].Label, reason, null)
                    : new FaceAssignOutcome(id.Value, null, reason, $"候補の範囲外の番号を返しました: {index.Value}");
            }
        }

        var outcomes = new List<FaceAssignOutcome>(targetIds.Count);
        foreach (var id in targetIds)
        {
            outcomes.Add(byId.TryGetValue(id, out var found)
                ? found
                : new FaceAssignOutcome(id, null, null, failure ?? NoAnswerWarning));
        }

        return outcomes;
    }

    /// <summary>スキーマが効いていれば数値で来るが、文字列で返すサーバもあるので両方受ける。</summary>
    private static int? ReadInt(JsonNode? node)
    {
        if (node is null)
            return null;

        try
        {
            return node.GetValue<int>();
        }
        catch (Exception)
        {
            try
            {
                return int.TryParse(node.GetValue<string>(), out var parsed) ? parsed : null;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    /// <summary>対象と対象の間に残す他キャラのセリフの数。</summary>
    private const int BridgeLines = 1;

    /// <summary>
    /// 対象行を maxTargets 件ずつに束ね、その束の先頭から contextLines 行だけ手前を文脈として足す。
    ///
    /// 対象と対象の間にある他キャラのセリフは、直前の BridgeLines 行だけ残す。
    /// 全部入れると、交互に喋るタイムラインで 12行の束が 38行に膨らんで遅くなる (実測)。
    ///
    /// 対象がどれだけ離れていても束は分けない。間に残すのが1行だけなので、
    /// 離れていても送る量は増えないし、分けるとリクエスト回数が増えるだけで損をする。
    /// </summary>
    internal static IReadOnlyList<IReadOnlyList<TranscriptLine>> Chunk(
        IReadOnlyList<TranscriptLine> transcript,
        int maxTargets,
        int contextLines)
    {
        maxTargets = Math.Max(1, maxTargets);
        contextLines = Math.Max(0, contextLines);

        var targetIndices = new List<int>();
        for (var i = 0; i < transcript.Count; i++)
        {
            if (transcript[i].Id is not null)
                targetIndices.Add(i);
        }

        var groups = new List<List<int>>();
        foreach (var index in targetIndices)
        {
            var last = groups.Count > 0 ? groups[^1] : null;

            if (last is null || last.Count >= maxTargets)
                groups.Add([index]);
            else
                last.Add(index);
        }

        var chunks = new List<IReadOnlyList<TranscriptLine>>();

        foreach (var group in groups)
        {
            var lines = new List<TranscriptLine>();
            var first = group[0];

            // 束の手前の文脈。前の束の対象が紛れ込むことがあるので ID を落とす。
            for (var i = Math.Max(0, first - contextLines); i < first; i++)
                lines.Add(transcript[i] with { Id = null });

            var previous = first - 1;
            foreach (var index in group)
            {
                // 直前の対象との間は、対象に近い側だけ残す。
                for (var i = Math.Max(previous + 1, index - BridgeLines); i < index; i++)
                    lines.Add(transcript[i] with { Id = null });

                lines.Add(transcript[index]);
                previous = index;
            }

            chunks.Add(lines);
        }

        return chunks;
    }

}
