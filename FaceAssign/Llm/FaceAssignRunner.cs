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

        var schema = FaceAssignRequestBuilder.BuildSchema(candidates, endpoint.IncludeReason);
        var system = FaceAssignRequestBuilder.BuildSystemPrompt(candidates, policy, endpoint.AppendNoThinkHint, endpoint.IncludeReason);

        var chunks = Chunk(transcript, maxTargetsPerChunk, contextLines);
        var perChunk = new IReadOnlyList<FaceAssignOutcome>[chunks.Count];
        var done = 0;

        // ローカルGPUでは並列にしても伸びないことが多いが、リモート相手だと効く。
        using var gate = new SemaphoreSlim(Math.Max(1, parallelism));

        var tasks = chunks.Select(async (chunk, index) =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                perChunk[index] = await ProcessChunkAsync(chunk, system, schema, candidates, progress, chunks.Count, ct)
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
        JsonObject schema,
        IReadOnlyList<PresetCandidate> candidates,
        IProgress<FaceAssignProgress>? progress,
        int totalChunks,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var targetIds = chunk.Where(l => l.Id is not null).Select(l => l.Id!.Value).ToArray();
        var user = FaceAssignRequestBuilder.BuildUserPrompt(chunk);

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
            var retryJson = await client
                .CompleteJsonAsync(system, FaceAssignRequestBuilder.BuildUserPrompt(retryLines), schema, "face_assignments", ct)
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

    /// <summary>
    /// 対象行を maxTargets 件ずつに束ね、その束の先頭から contextLines 行だけ手前を文脈として足す。
    ///
    /// 対象と対象の間にある他キャラのセリフは文脈として残すが、束の外までは遡らない。
    /// ここを絞らないと、1行だけ選んだときにその手前の全セリフを送ってしまい、
    /// 1行の推定が数十秒かかる (実測 49.8 秒)。
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

        // 対象が離れていたら束を分ける。間の行はどちらの文脈でもないので送る意味がない。
        var groups = new List<List<int>>();
        foreach (var index in targetIndices)
        {
            var last = groups.Count > 0 ? groups[^1] : null;
            var tooFar = last is not null && index - last[^1] > Math.Max(1, contextLines);

            if (last is null || tooFar || last.Count >= maxTargets)
                groups.Add([index]);
            else
                last.Add(index);
        }

        var chunks = new List<IReadOnlyList<TranscriptLine>>();

        foreach (var group in groups)
        {
            var first = group[0];
            var last = group[^1];
            var start = Math.Max(0, first - contextLines);

            var lines = new List<TranscriptLine>(last - start + 1);
            for (var i = start; i <= last; i++)
            {
                // 先頭の文脈部分に前の束の対象が紛れ込むことがあるので、ID を落としておく。
                var line = transcript[i];
                lines.Add(i < first && line.Id is not null ? line with { Id = null } : line);
            }

            chunks.Add(lines);
        }

        return chunks;
    }
}
