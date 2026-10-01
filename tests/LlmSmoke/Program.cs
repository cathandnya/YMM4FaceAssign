using System.Diagnostics;
using FaceAssign.Llm;
using FaceAssign.Tachie;
using LlmSmoke;

const string ZundaDir = @"C:\ymm4\sozai\char\ずんだもん";

var mode = args.Length > 0 ? args[0] : "baseline";
var presets = PresetIniReader.Read(ZundaDir);
var transcript = Script.BuildTranscript();

var endpoints = Environment.GetEnvironmentVariable("FACEASSIGN_MODELS") is { Length: > 0 } spec
    ? spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(m => (Name: $"LM Studio {m}", Endpoint: new LlmEndpoint("http://127.0.0.1:1234", m, TimeoutSeconds: 300)))
        .ToArray()
    : new (string Name, LlmEndpoint Endpoint)[]
    {
        ("Ollama qwen3:8b",       new LlmEndpoint("http://127.0.0.1:11434", "qwen3:8b", TimeoutSeconds: 300)),
        ("LM Studio gpt-oss-20b", new LlmEndpoint("http://127.0.0.1:1234", "openai/gpt-oss-20b", TimeoutSeconds: 300)),
    };

if (mode == "curated")
{
    var bare = presets.Select(p => new PresetCandidate(p.Label)).ToArray();
    var curated = Curated.Build(presets);
    Console.WriteLine($"整備後の候補 {curated.Length} 件: {string.Join(", ", curated.Select(c => c.Label))}");
    Console.WriteLine();

    foreach (var (name, endpoint) in endpoints)
    {
        using var client = new OpenAiCompatibleClient(endpoint);
        Console.WriteLine($"### {name}");
        await Score(client, endpoint, "現状の名前", bare, translate: false);
        await Score(client, endpoint, "推奨名に整備", curated, translate: true);
    }
}
else if (mode == "speed")
{
    var curated = Curated.Build(presets);
    var big = new List<TranscriptLine>();
    var id = 1;
    for (var round = 0; round < 3; round++)
        foreach (var line in Script.Lines)
            big.Add(new TranscriptLine(id++, "ずんだもん", line.Serif));

    var variants = new (string Name, Func<LlmEndpoint, LlmEndpoint> Tweak)[]
    {
        ("既定 (reason あり / effort low)", e => e),
        ("reason なし",                     e => e with { IncludeReason = false }),
        ("effort medium",                   e => e with { ReasoningEffort = "medium" }),
        ("effort 指定なし",                  e => e with { ReasoningEffort = "" }),
    };

    foreach (var (name, baseEndpoint) in endpoints)
    {
        Console.WriteLine($"### {name}  24行");
        foreach (var (variantName, tweak) in variants)
        {
            var endpoint = tweak(baseEndpoint);
            using var client = new OpenAiCompatibleClient(endpoint);
            var runner = new FaceAssignRunner(client, endpoint);
            var sw = Stopwatch.StartNew();
            try
            {
                var outcomes = await runner.RunAsync(big, curated, null, 12, 4, null, CancellationToken.None);
                sw.Stop();
                var unresolved = outcomes.Count(o => o.PresetLabel is null);
                Console.WriteLine($"  {variantName,-32} {sw.ElapsedMilliseconds,7} ms  未決 {unresolved}/24  ({sw.ElapsedMilliseconds / 24.0:F0} ms/行)");
            }
            catch (Exception ex)
            {
                sw.Stop();
                Console.WriteLine($"  {variantName,-32} 失敗: {ex.Message}");
            }
        }
        Console.WriteLine();
    }
}
else if (mode == "par")
{
    var curated = Curated.Build(presets);
    var big = new List<TranscriptLine>();
    var id = 1;
    for (var round = 0; round < 6; round++)
        foreach (var line in Script.Lines)
            big.Add(new TranscriptLine(id++, "ずんだもん", line.Serif));

    foreach (var (name, baseEndpoint) in endpoints)
    {
        var endpoint = baseEndpoint with { IncludeReason = false };
        using var client = new OpenAiCompatibleClient(endpoint);
        var runner = new FaceAssignRunner(client, endpoint);

        // 計測前に一度だけ投げて、モデルのロード時間を混ぜないようにする。
        await runner.RunAsync(big.Take(4).ToArray(), curated, null, 12, 0, null, CancellationToken.None);

        Console.WriteLine($"### {name}  48行 / チャンク12 (reason なし)");
        foreach (var p in new[] { 1, 2, 3 })
        {
            var sw = Stopwatch.StartNew();
            var outcomes = await runner.RunAsync(big, curated, null, 12, 4, null, CancellationToken.None, p);
            sw.Stop();
            var unresolved = outcomes.Count(o => o.PresetLabel is null);
            Console.WriteLine($"  並列 {p}  {sw.ElapsedMilliseconds,7} ms  未決 {unresolved}/48  ({sw.ElapsedMilliseconds / 48.0:F0} ms/行)");
        }
        Console.WriteLine();
    }
}
else if (mode == "reason")
{
    // 理由を書かせると精度が上がるのか。整備後の候補で、同じ台本を3回ずつ回して比べる。
    var curated = Curated.Build(presets);

    foreach (var (name, baseEndpoint) in endpoints)
    {
        Console.WriteLine($"### {name}");
        foreach (var include in new[] { true, false })
        {
            var endpoint = baseEndpoint with { IncludeReason = include };
            using var client = new OpenAiCompatibleClient(endpoint);
            var runner = new FaceAssignRunner(client, endpoint);
            var hits = new List<int>();
            long totalMs = 0;

            for (var trial = 0; trial < 3; trial++)
            {
                var sw = Stopwatch.StartNew();
                var outcomes = await runner.RunAsync(transcript, curated, null, 12, 4, null, CancellationToken.None, 1);
                sw.Stop();
                totalMs += sw.ElapsedMilliseconds;

                var hit = Script.Lines.Count(e =>
                {
                    var o = outcomes.FirstOrDefault(x => x.Id == e.Id);
                    return o?.PresetLabel is not null && Curated.Translate(e.Acceptable).Contains(o.PresetLabel);
                });
                hits.Add(hit);
            }

            Console.WriteLine($"  理由 {(include ? "あり" : "なし"),-4}  妥当 {string.Join("/", hits)} (平均 {hits.Average():F1}/8)  平均 {totalMs / 3} ms");
        }
        Console.WriteLine();
    }
}
else if (mode == "chunk")
{
    // ネットワーク無しで分割だけ検証する。71行のうち1行だけ選んだ状況を再現。
    var lines = new List<TranscriptLine>();
    for (var i = 0; i < 71; i++)
        lines.Add(new TranscriptLine(null, i % 2 == 0 ? "ずんだもん" : "四国めたん", $"セリフ{i}"));
    lines[50] = lines[50] with { Id = 1 };

    var chunks = FaceAssignRunner.Chunk(lines, 12, 4);
    Console.WriteLine($"1行だけ対象: チャンク {chunks.Count} 個 / 送る行数 {chunks.Sum(c => c.Count)}");
    foreach (var c in chunks)
        Console.WriteLine($"    {c.Count} 行 (対象 {c.Count(l => l.Id is not null)}) : {string.Join(" / ", c.Select(l => (l.Id is null ? "" : "#") + l.Serif))}");

    // 離れた3行を選んだ場合
    var lines2 = new List<TranscriptLine>();
    for (var i = 0; i < 71; i++)
        lines2.Add(new TranscriptLine(null, "ずんだもん", $"セリフ{i}"));
    var id2 = 1;
    foreach (var i in new[] { 5, 40, 70 })
        lines2[i] = lines2[i] with { Id = id2++ };
    var chunks2 = FaceAssignRunner.Chunk(lines2, 12, 4);
    Console.WriteLine($"離れた3行: チャンク {chunks2.Count} 個 / 送る行数 {chunks2.Sum(c => c.Count)}");
    foreach (var c in chunks2)
        Console.WriteLine($"    {c.Count} 行 (対象 {c.Count(l => l.Id is not null)})");

    // 連続20行を選んだ場合
    var lines3 = new List<TranscriptLine>();
    for (var i = 0; i < 71; i++)
        lines3.Add(new TranscriptLine(i >= 30 && i < 50 ? i - 29 : null, "ずんだもん", $"セリフ{i}"));
    var chunks3 = FaceAssignRunner.Chunk(lines3, 12, 4);
    Console.WriteLine($"連続20行: チャンク {chunks3.Count} 個 / 送る行数 {chunks3.Sum(c => c.Count)}");
    foreach (var c in chunks3)
        Console.WriteLine($"    {c.Count} 行 (対象 {c.Count(l => l.Id is not null)})");
}
else if (mode == "small")
{
    // 右クリックの実際の使い方に近い、少数選択のレイテンシを測る。
    var curated = presets.Select(p => new PresetCandidate(p.Label)).ToArray();

    // 71行のタイムラインを模し、そのうち一部だけを対象にする。
    List<TranscriptLine> Build(int targetCount)
    {
        var lines = new List<TranscriptLine>();
        for (var i = 0; i < 71; i++)
            lines.Add(new TranscriptLine(null, i % 3 == 0 ? "四国めたん" : "ずんだもん", Script.Lines[i % Script.Lines.Length].Serif));
        var id = 1;
        for (var i = 40; i < 40 + targetCount; i++)
            lines[i] = lines[i] with { Id = id++ };
        return lines;
    }

    foreach (var (name, baseEndpoint) in endpoints)
    {
        var endpoint = baseEndpoint with { IncludeReason = false };
        using var client = new OpenAiCompatibleClient(endpoint);
        var runner = new FaceAssignRunner(client, endpoint);

        var cold = Stopwatch.StartNew();
        try { await runner.RunAsync(Build(1), curated, null, 12, 4, null, CancellationToken.None, 2); }
        catch (Exception ex) { Console.WriteLine($"### {name} 失敗: {ex.Message}"); continue; }
        cold.Stop();

        Console.WriteLine($"### {name}");
        Console.WriteLine($"  1行 (初回/ロード込み)  {cold.ElapsedMilliseconds,6} ms");

        foreach (var count in new[] { 1, 3, 5, 10 })
        {
            var sw = Stopwatch.StartNew();
            var outcomes = await runner.RunAsync(Build(count), curated, null, 12, 4, null, CancellationToken.None, 2);
            sw.Stop();
            var decided = outcomes.Count(o => o.PresetLabel is not null);
            Console.WriteLine($"  {count,2}行 (暖機後)          {sw.ElapsedMilliseconds,6} ms   決定 {decided}/{count}");
        }
        Console.WriteLine();
    }
}
else if (mode == "url")
{
    // ネットワーク不要。エンドポイントURLの組み立てだけ確認する。
    var cases = new (string Input, string ExpectedChat)[]
    {
        ("http://127.0.0.1:1234",     "http://127.0.0.1:1234/v1/chat/completions"),
        ("http://127.0.0.1:1234/",    "http://127.0.0.1:1234/v1/chat/completions"),
        ("http://127.0.0.1:1234/v1",  "http://127.0.0.1:1234/v1/chat/completions"),
        ("http://127.0.0.1:1234/v1/", "http://127.0.0.1:1234/v1/chat/completions"),
        ("http://127.0.0.1:11434",    "http://127.0.0.1:11434/v1/chat/completions"),
        ("https://api.openai.com/v1", "https://api.openai.com/v1/chat/completions"),
        ("https://generativelanguage.googleapis.com/v1beta/openai/",
         "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions"),
        ("https://example.com/proxy/llm",
         "https://example.com/proxy/llm/chat/completions"),
    };

    var failures = 0;
    foreach (var (input, expected) in cases)
    {
        var actual = new LlmEndpoint(input, "m").ResolveChatCompletionsUrl();
        var ok = actual == expected;
        if (!ok) failures++;
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {input}");
        if (!ok)
        {
            Console.WriteLine($"          期待 {expected}");
            Console.WriteLine($"          実際 {actual}");
        }
    }
    Console.WriteLine(failures == 0 ? "すべて PASS" : $"{failures} 件 FAIL");
}
else if (mode == "mixed")
{
    // 実際の使われ方: 2キャラが交互に喋るタイムラインを全選択した状態。
    // 対象はキャラごとに分けるので、間に挟まる相手のセリフが文脈として混ざる。
    var timeline = new List<TranscriptLine>();
    for (var i = 0; i < 71; i++)
        timeline.Add(new TranscriptLine(null, i % 3 == 0 ? "四国めたん" : "ずんだもん", Script.Lines[i % Script.Lines.Length].Serif));

    foreach (var who in new[] { "四国めたん", "ずんだもん" })
    {
        var id = 1;
        var forChar = timeline
            .Select(l => l.CharacterName == who ? l with { Id = id++ } : l with { Id = null })
            .ToArray();

        var targets = forChar.Count(l => l.Id is not null);
        foreach (var ctx in new[] { 4, 2, 0 })
        {
            var chunks = FaceAssignRunner.Chunk(forChar, 12, ctx);
            var total = chunks.Sum(c => c.Count);
            var biggest = chunks.Max(c => c.Count);
            Console.WriteLine($"{who,-8} 対象{targets,3}行  文脈{ctx}  チャンク{chunks.Count,2}個  送信計{total,4}行  最大{biggest,3}行");
        }
    }
}
else if (mode == "mixedrun")
{
    // 2キャラ交互の71行タイムラインを全選択した場合の所要時間。
    var candidates = presets.Select(p => new PresetCandidate(p.Label)).ToArray();

    var timeline = new List<TranscriptLine>();
    for (var i = 0; i < 71; i++)
        timeline.Add(new TranscriptLine(null, i % 3 == 0 ? "四国めたん" : "ずんだもん", Script.Lines[i % Script.Lines.Length].Serif));

    foreach (var (name, baseEndpoint) in endpoints)
    {
        var endpoint = baseEndpoint with { IncludeReason = false };
        using var client = new OpenAiCompatibleClient(endpoint);
        var runner = new FaceAssignRunner(client, endpoint);

        await runner.RunAsync(timeline.Take(3).Select((l, i) => l with { Id = i + 1 }).ToArray(),
            candidates, null, 12, 4, null, CancellationToken.None, 2);

        var total = Stopwatch.StartNew();
        var done = 0;
        foreach (var who in new[] { "四国めたん", "ずんだもん" })
        {
            var id = 1;
            var forChar = timeline.Select(l => l.CharacterName == who ? l with { Id = id++ } : l with { Id = null }).ToArray();
            var sw = Stopwatch.StartNew();
            var par = int.TryParse(Environment.GetEnvironmentVariable("FACEASSIGN_PAR"), out var v) ? v : 2;
            var outcomes = await runner.RunAsync(forChar, candidates, null, 12, 4, null, CancellationToken.None, par);
            sw.Stop();
            done += outcomes.Count(o => o.PresetLabel is not null);
            Console.WriteLine($"  {who,-8} {outcomes.Count,2}行  {sw.ElapsedMilliseconds,6} ms  決定 {outcomes.Count(o => o.PresetLabel is not null)}");
        }
        total.Stop();
        Console.WriteLine($"### {name}  合計 {total.ElapsedMilliseconds} ms  決定 {done}/71  ({total.ElapsedMilliseconds / 71.0:F0} ms/行)");
        Console.WriteLine();
    }
}
else if (mode == "chunktime")
{
    // 束ごとに1本ずつ投げて、どれが遅いのか見る。
    var candidates = presets.Select(p => new PresetCandidate(p.Label)).ToArray();
    var timeline = new List<TranscriptLine>();
    for (var i = 0; i < 71; i++)
        timeline.Add(new TranscriptLine(null, i % 3 == 0 ? "四国めたん" : "ずんだもん", Script.Lines[i % Script.Lines.Length].Serif));

    var id = 1;
    var forChar = timeline.Select(l => l.CharacterName == "ずんだもん" ? l with { Id = id++ } : l with { Id = null }).ToArray();
    var chunks = FaceAssignRunner.Chunk(forChar, 12, 4);
    Console.WriteLine($"ずんだもん: {chunks.Count} 束");

    var (name, baseEndpoint) = endpoints[0];
    var endpoint = baseEndpoint with { IncludeReason = false };
    using var client = new OpenAiCompatibleClient(endpoint);
    var runner = new FaceAssignRunner(client, endpoint);

    for (var i = 0; i < chunks.Count; i++)
    {
        var chunk = chunks[i];
        var targets = chunk.Count(l => l.Id is not null);
        var sw = Stopwatch.StartNew();
        var outcomes = await runner.RunAsync(chunk, candidates, null, 99, 0, null, CancellationToken.None, 1);
        sw.Stop();
        var decided = outcomes.Count(o => o.PresetLabel is not null);
        var warn = outcomes.Select(o => o.Warning).OfType<string>().FirstOrDefault();
        Console.WriteLine($"  束{i + 1}: {chunk.Count,2}行 (対象{targets,2})  {sw.ElapsedMilliseconds,7} ms  決定 {decided}/{targets}  {warn}");
    }
}
else if (mode == "scale")
{
    // 24行 / チャンク20 で2分割させ、ID の取りこぼしと重複を見る。
    var curated = Curated.Build(presets);
    var big = new List<TranscriptLine>();
    var id = 1;
    for (var round = 0; round < 3; round++)
        foreach (var line in Script.Lines)
            big.Add(new TranscriptLine(id++, "ずんだもん", line.Serif));

    foreach (var (name, endpoint) in endpoints)
    {
        using var client = new OpenAiCompatibleClient(endpoint);
        var runner = new FaceAssignRunner(client, endpoint);
        var chunks = FaceAssignRunner.Chunk(big, 20, 4);
        var sw = Stopwatch.StartNew();
        var outcomes = await runner.RunAsync(big, curated, null, 20, 4, null, CancellationToken.None);
        sw.Stop();

        var ids = outcomes.Select(o => o.Id).ToArray();
        var missing = big.Select(l => l.Id!.Value).Except(ids).ToArray();
        var dup = ids.GroupBy(x => x).Where(g => g.Count() > 1).Select(g => g.Key).ToArray();
        var unresolved = outcomes.Count(o => o.PresetLabel is null);
        Console.WriteLine($"### {name}  24行 / {chunks.Count}チャンク  {sw.ElapsedMilliseconds} ms");
        Console.WriteLine($"    返答 {outcomes.Count}/24  欠落 {missing.Length}  重複 {dup.Length}  変更しない {unresolved}");
        foreach (var w in outcomes.Where(o => o.Warning is not null).Take(5))
            Console.WriteLine($"    ! #{w.Id} {w.Warning}");
    }
}

async Task Score(OpenAiCompatibleClient client, LlmEndpoint endpoint, string condition, PresetCandidate[] candidates, bool translate)
{
    var runner = new FaceAssignRunner(client, endpoint);
    var sw = Stopwatch.StartNew();
    IReadOnlyList<FaceAssignOutcome> outcomes;
    try
    {
        outcomes = await runner.RunAsync(transcript, candidates, null, 20, 4, null, CancellationToken.None);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  [{condition}] 失敗: {ex.Message}");
        return;
    }
    sw.Stop();

    var hit = 0;
    var report = new List<string>();
    foreach (var e in Script.Lines)
    {
        var acceptable = translate ? Curated.Translate(e.Acceptable) : e.Acceptable;
        var o = outcomes.FirstOrDefault(x => x.Id == e.Id);
        var ok = o?.PresetLabel is not null && acceptable.Contains(o.PresetLabel);
        if (ok) hit++;
        report.Add($"    {(ok ? "o" : "x")} #{e.Id} {o?.PresetLabel ?? "(変更しない)"}  <- {e.Serif}");
    }

    Console.WriteLine($"  [{condition}] 妥当 {hit}/{Script.Lines.Length}  {sw.ElapsedMilliseconds} ms");
    foreach (var line in report)
        Console.WriteLine(line);
    Console.WriteLine();
}
