using System.Collections.ObjectModel;
using FaceAssign.Apply;
using FaceAssign.Llm;
using FaceAssign.Settings;
using FaceAssign.State;
using FaceAssign.Tachie;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;
using YukkuriMovieMaker.UndoRedo;

namespace FaceAssign;

public class FaceAssignToolViewModel : Bindable, IToolViewModel, ITimelineToolViewModel
{
    private Timeline? timeline;
    private UndoRedoManager? undoRedoManager;
    private AsyncAwaitStatus? asyncAwaitStatus;
    private CancellationTokenSource? cancellation;

    private string diagnostics = "(まだスキャンしていません)";
    private string progressText = "";
    private double progressRate;
    private bool isProcessing;
    private CharacterPresetsViewModel? selectedCharacter;
    private DateTime lastWarmUp = DateTime.MinValue;
    private string? lastFailure;

    public FaceAssignToolViewModel()
    {
        // 実行はタイムラインの右クリックメニューからのみ。パネルは設定と確認に徹する。
        ScanCommand = new ActionCommand(_ => timeline is not null && !IsProcessing, _ => Scan());
        CancelCommand = new ActionCommand(_ => IsProcessing, _ => cancellation?.Cancel());
        TestConnectionCommand = new ActionCommand(_ => !IsProcessing, async _ => await TestConnectionAsync());
        CopyRecommendedNamesCommand = new ActionCommand(_ => true, _ => CopyRecommendedNames());
        OpenPresetFolderCommand = new ActionCommand(_ => SelectedCharacter is not null, _ => OpenPresetFolder());
        SaveSettingsCommand = new ActionCommand(_ => true, _ => SaveSettings());
    }

    public string Title => "表情割り当て";

    public bool CanSuspend => !IsProcessing;

    public event EventHandler<CreateNewToolViewRequestedEventArgs>? CreateNewToolViewRequested;

    public static FaceAssignSettings Settings => FaceAssignSettings.Default;

    /// <summary>パネルから設定を直接編集するためのバインド先。</summary>
    public FaceAssignSettings Config => FaceAssignSettings.Default;

    public ObservableCollection<CharacterPresetsViewModel> Characters { get; } = [];

    public ObservableCollection<FaceAssignProposal> Proposals { get; } = [];

    public ActionCommand ScanCommand { get; }

    public ActionCommand CancelCommand { get; }

    public ActionCommand TestConnectionCommand { get; }

    public ActionCommand CopyRecommendedNamesCommand { get; }

    public ActionCommand OpenPresetFolderCommand { get; }

    public ActionCommand SaveSettingsCommand { get; }

    /// <summary>右クリック実行で使い捨てた ViewModel の分も含めて、まとめてここに出る。</summary>
    public string LogText => FaceAssignLog.Text;

    /// <summary>
    /// パネル側から、ログが増えたときに呼ばれる。
    /// 推定は非同期なので、UIスレッド以外から来ることがある。
    /// </summary>
    public void RefreshLog()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(() => OnPropertyChanged(nameof(LogText)));
            return;
        }

        OnPropertyChanged(nameof(LogText));
    }

    public string Diagnostics { get => diagnostics; private set => Set(ref diagnostics, value); }

    public string ProgressText { get => progressText; private set => Set(ref progressText, value); }

    public double ProgressRate { get => progressRate; private set => Set(ref progressRate, value); }

    /// <summary>システムプロンプトへの追記。全体設定なので右クリック実行でも効く。</summary>
    public string Policy
    {
        get => Settings.Policy;
        set { Settings.Policy = value ?? ""; OnPropertyChanged(nameof(Policy)); }
    }

    public CharacterPresetsViewModel? SelectedCharacter
    {
        get => selectedCharacter;
        set => Set(ref selectedCharacter, value);
    }

    public bool IsProcessing
    {
        get => isProcessing;
        private set
        {
            if (!Set(ref isProcessing, value))
                return;
            OnPropertyChanged(nameof(CanSuspend));
            RaiseCommandStates();
        }
    }

    public bool IsOverwriteVoiceItem
    {
        get => Settings.WriteTarget == FaceWriteTarget.OverwriteVoiceItem;
        set { if (value) SetWriteTarget(FaceWriteTarget.OverwriteVoiceItem); }
    }

    public bool IsInsertTachieFaceItem
    {
        get => Settings.WriteTarget == FaceWriteTarget.InsertTachieFaceItem;
        set { if (value) SetWriteTarget(FaceWriteTarget.InsertTachieFaceItem); }
    }

    public int LayerOffset
    {
        get => Settings.LayerOffset;
        set { Settings.LayerOffset = value; OnPropertyChanged(nameof(LayerOffset)); }
    }

    public bool KeepUnspecifiedParts
    {
        get => Settings.KeepUnspecifiedParts;
        set { Settings.KeepUnspecifiedParts = value; OnPropertyChanged(nameof(KeepUnspecifiedParts)); }
    }

    /// <summary>速度と確認しやすさのトレードオフ。パネルから切り替えられるようにしておく。</summary>
    public bool IncludeReason
    {
        get => Settings.IncludeReason;
        set { Settings.IncludeReason = value; OnPropertyChanged(nameof(IncludeReason)); }
    }

    public void SetTimelineToolInfo(TimelineToolInfo info)
    {
        timeline = info.Timeline;
        undoRedoManager = info.UndoRedoManager;
        asyncAwaitStatus = info.AsyncAwaitStatus;

        Characters.Clear();
        Proposals.Clear();
        SelectedCharacter = null;
        Diagnostics = "(まだスキャンしていません)";
        Append($"タイムライン \"{info.Timeline.Name}\" を受け取りました。");
        RaiseCommandStates();
    }

    public ToolState SaveState()
    {
        var saved = new FaceAssignSavedState
        {
            WriteTarget = Settings.WriteTarget,
            LayerOffset = Settings.LayerOffset,
            KeepUnspecifiedParts = Settings.KeepUnspecifiedParts,
        };

        // 提案テーブルは生きた VoiceItem 参照を持つので保存できない。メモリだけに置く。
        return new ToolState { Title = Title, ProjectTemporaryState = Proposals.ToList() }
            .WithSavedState(saved, null);
    }

    public void LoadState(ToolState stateData)
    {
        if (stateData.GetSavedState<FaceAssignSavedState>() is { } saved)
        {
            Settings.WriteTarget = saved.WriteTarget;
            Settings.LayerOffset = saved.LayerOffset;
            Settings.KeepUnspecifiedParts = saved.KeepUnspecifiedParts;

            // 以前はプロジェクトに保存していた。全体設定が空なら引き継ぐ。
            if (!string.IsNullOrWhiteSpace(saved.Policy) && string.IsNullOrWhiteSpace(Settings.Policy))
                Policy = saved.Policy;
            OnPropertyChanged(nameof(IsOverwriteVoiceItem));
            OnPropertyChanged(nameof(IsInsertTachieFaceItem));
            OnPropertyChanged(nameof(LayerOffset));
            OnPropertyChanged(nameof(KeepUnspecifiedParts));
        }

        if (stateData.ProjectTemporaryState is List<FaceAssignProposal> restored)
        {
            Proposals.Clear();
            foreach (var p in restored)
                Proposals.Add(p);
        }
    }

    /// <summary>
    /// preset.ini を読み直して、キャラクターごとの候補と診断を作る。
    /// 推定は必要になれば自分で呼ぶので、手で押すのは preset.ini を編集した後だけでよい。
    /// </summary>
    private void Scan()
    {
        if (timeline is null)
            return;

        var targets = GetTargets(out var scope);
        Append(scope);
        Scan(targets);
    }

    private void Scan(IReadOnlyList<VoiceItem> targets)
    {
        if (timeline is null)
            return;

        Characters.Clear();
        Proposals.Clear();
        SelectedCharacter = null;

        var skipped = new List<string>();
        foreach (var character in targets.Select(v => v.Character).OfType<Character>().Distinct())
        {
            var vm = CharacterPresetsViewModel.TryCreate(character, Settings, out var reason);
            if (vm is null)
            {
                if (reason is not null)
                    skipped.Add(reason);
                continue;
            }
            Characters.Add(vm);
        }

        SelectedCharacter = Characters.FirstOrDefault();

        var lines = Characters.Select(c => c.Diagnostics).ToList();
        lines.AddRange(skipped);
        Diagnostics = lines.Count > 0 ? string.Join(Environment.NewLine, lines) : "対象のキャラクターがありません。";

        Append($"対象 {targets.Count} 件 / 動く立ち絵のキャラクター {Characters.Count} 件。");

        // 候補の既定値をこの時点で確定させ、素材単位の設定として残す。
        Settings.Save();
        RaiseCommandStates();
    }

    /// <summary>対象に、まだ候補を作っていない動く立ち絵のキャラクターが含まれているか。</summary>
    private bool NeedsScan(IReadOnlyList<VoiceItem> targets)
    {
        if (Characters.Count == 0)
            return true;

        var loaded = Characters.Select(c => c.Character).ToHashSet();
        return targets
            .Select(v => v.Character)
            .OfType<Character>()
            .Distinct()
            .Any(c => !loaded.Contains(c) && TachieCharacterProbe.IsAnimationTachie(c));
    }

    /// <summary>
    /// 対象は常に選択中のボイスアイテム。右クリックから呼ぶので、
    /// 選択していないものに勝手に書き込まない。
    /// </summary>
    private IReadOnlyList<VoiceItem> GetTargets(out string scope)
    {
        var selected = timeline!.SelectedItems.OfType<VoiceItem>()
            .OrderBy(v => v.Frame).ThenBy(v => v.Layer).ToArray();

        scope = selected.Length > 0
            ? $"選択中のボイスアイテム {selected.Length} 件を対象にします。"
            : "ボイスアイテムが選択されていません。";
        return selected;
    }

    /// <summary>文脈用に、タイムライン全体のボイスアイテムを時間順で返す。</summary>
    private IReadOnlyList<VoiceItem> GetAllVoiceItemsInOrder() =>
        [.. timeline!.Items.OfType<VoiceItem>().OrderBy(v => v.Frame).ThenBy(v => v.Layer)];

    /// <summary>
    /// キャラクターごとに台本を組み立てて推定する。
    /// YMM4 のオブジェクトに触るのは UI スレッドのこのメソッド内だけで、
    /// 非同期区間には ID とセリフのコピーしか渡さない。
    /// </summary>
    /// <summary>右クリックメニューから、推定してそのまま適用する。</summary>
    /// <returns>失敗したときだけ理由を返す。成功なら null。</returns>
    public async Task<string?> EstimateAndApplyAsync()
    {
        lastFailure = null;
        await EstimateAsync(autoApply: true);
        return lastFailure;
    }

    /// <summary>パネルが無いときに使う。タイムラインを直接差し込む。</summary>
    public bool HasTimeline => timeline is not null;

    /// <summary>
    /// モデルを載せておく。右クリックメニューが開いた時点で呼ぶと、
    /// 項目をクリックする頃にはロードが終わっている。
    /// 30秒に1回までに間引く。
    /// </summary>
    public async Task WarmUpAsync()
    {
        if (IsProcessing || string.IsNullOrWhiteSpace(Settings.ModelName))
            return;

        if (DateTime.UtcNow - lastWarmUp < TimeSpan.FromSeconds(30))
            return;

        lastWarmUp = DateTime.UtcNow;

        try
        {
            var endpoint = Settings.CreateEndpoint() with { TimeoutSeconds = 60 };
            using var client = new OpenAiCompatibleClient(endpoint);
            await client.WarmUpAsync(CancellationToken.None);
        }
        catch
        {
            // 暖機の失敗は無視する。
        }
    }

    /// <summary>対象にできるボイスアイテムがあるか。右クリックメニューの有効/無効に使う。</summary>
    public bool HasTargets()
    {
        if (timeline is null || IsProcessing)
            return false;

        return timeline.SelectedItems
            .OfType<VoiceItem>()
            .Any(v => v.Character is not null && TachieCharacterProbe.IsAnimationTachie(v.Character));
    }

    private async Task EstimateAsync(bool autoApply)
    {
        FaceAssignLog.Write($"EstimateAsync autoApply={autoApply} timeline={(timeline is null ? "null" : "ok")} " +
                            $"isProcessing={IsProcessing} model=\"{Settings.ModelName}\" url=\"{Settings.BaseUrl}\"");

        if (timeline is null || IsProcessing)
        {
            Fail(timeline is null
                ? "タイムラインを受け取れていません。パネルを開き直してください。"
                : "実行中です。終わるまで待ってください。");
            return;
        }

        if (string.IsNullOrWhiteSpace(Settings.ModelName))
        {
            Fail("モデル名が未設定です。ツール → 表情割り当て の設定タブで指定してください。");
            return;
        }

        var targets = GetTargets(out var scope);
        Append(scope);

        // preset.ini の読み込みは数ミリ秒で済む。足りないキャラがいれば黙って読み直す。
        if (NeedsScan(targets))
            Scan(targets);

        if (Characters.Count == 0)
        {
            Fail("動く立ち絵のキャラクターが見つかりませんでした。");
            return;
        }

        cancellation = new CancellationTokenSource();
        IsProcessing = true;
        Proposals.Clear();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        ProgressOverlay.Begin("表情を推定しています…");

        try
        {
            var endpoint = Settings.CreateEndpoint();
            using var client = new OpenAiCompatibleClient(endpoint);
            var runner = new FaceAssignRunner(client, endpoint);
            var allVoices = GetAllVoiceItemsInOrder();

            foreach (var character in Characters)
            {
                var candidates = character.Candidates;
                if (candidates.Count == 0)
                {
                    Append($"{character.CharacterName}: 候補のプリセットが1件も選ばれていないのでスキップします。");
                    continue;
                }

                // 対象行に ID を振り、他キャラの行は ID 無しの文脈として混ぜる。
                var items = new Dictionary<int, VoiceItem>();
                var transcript = new List<TranscriptLine>(allVoices.Count);
                var nextId = 1;

                foreach (var voice in allVoices)
                {
                    var isTarget = ReferenceEquals(voice.Character, character.Character) && targets.Contains(voice);
                    if (isTarget)
                    {
                        var id = nextId++;
                        items[id] = voice;
                        transcript.Add(new TranscriptLine(id, voice.CharacterName ?? "", voice.Serif ?? ""));
                    }
                    else
                    {
                        transcript.Add(new TranscriptLine(null, voice.CharacterName ?? "", voice.Serif ?? ""));
                    }
                }

                if (items.Count == 0)
                    continue;

                Append($"{character.CharacterName}: {items.Count} 行 / 候補 {candidates.Count} 件 で推定します。");

                var progress = new Progress<FaceAssignProgress>(p =>
                {
                    ProgressText = $"{character.CharacterName} {p.Message}";
                    ProgressRate = p.TotalChunks == 0 ? 0 : (double)p.DoneChunks / p.TotalChunks;
                    ProgressOverlay.Report(ProgressRate, $"{character.CharacterName}: {p.Message}");
                });

                var outcomes = await runner.RunAsync(
                    transcript,
                    [.. candidates.Select(c => c.ToCandidate())],
                    Policy,
                    Settings.MaxLinesPerRequest,
                    Settings.ContextLineCount,
                    progress,
                    cancellation.Token,
                    Settings.Parallelism);

                var byLabel = character.Presets.ToDictionary(p => p.Label, StringComparer.Ordinal);
                var choices = character.Candidates.Select(c => c.Preset).ToArray();

                foreach (var outcome in outcomes)
                {
                    if (!items.TryGetValue(outcome.Id, out var item))
                        continue;

                    Proposals.Add(new FaceAssignProposal(outcome.Id, item, character.CharacterName)
                    {
                        Preset = outcome.PresetLabel is not null && byLabel.TryGetValue(outcome.PresetLabel, out var preset) ? preset : null,
                        Reason = outcome.Reason,
                        Warning = outcome.Warning,
                        IsAccepted = outcome.PresetLabel is not null,
                        Choices = choices,
                    });
                }
            }

            stopwatch.Stop();
            var decided = Proposals.Count(p => p.Preset is not null);
            var warned = Proposals.Count(p => p.Warning is not null);
            var perLine = Proposals.Count == 0 ? 0 : stopwatch.ElapsedMilliseconds / Proposals.Count;
            Append($"推定が終わりました。{decided}/{Proposals.Count} 行に表情が決まりました" +
                   (warned > 0 ? $" (警告 {warned} 件)" : "") +
                   $"。{stopwatch.Elapsed.TotalSeconds:F1} 秒 / 1行あたり {perLine} ms。内容を確認して適用してください。");

            if (Settings.IncludeReason && Proposals.Count >= 10)
                Append("遅いと感じたら「理由を出す」を外してください。25〜40% ほど速くなります。");

            if (autoApply)
                ApplyProposals();

            ProgressOverlay.Finish(
                lastFailure ?? $"{decided}/{Proposals.Count} 行に表情を割り当てました ({stopwatch.Elapsed.TotalSeconds:F1} 秒)",
                lastFailure is not null);
        }
        catch (OperationCanceledException)
        {
            Fail("推定を中止しました。");
            ProgressOverlay.Finish("中止しました", true);
        }
        catch (LlmException ex)
        {
            Fail($"推定に失敗しました: {ex.DetailedMessage}");
            ProgressOverlay.Finish($"失敗: {ex.Message}", true);
        }
        catch (Exception ex)
        {
            Fail($"推定に失敗しました: {ex.GetType().Name}: {ex.Message}");
            ProgressOverlay.Finish($"失敗: {ex.Message}", true);
        }
        finally
        {
            IsProcessing = false;
            ProgressText = "";
            ProgressRate = 0;
            cancellation?.Dispose();
            cancellation = null;
            RaiseCommandStates();
        }
    }

    private void ApplyProposals()
    {
        if (timeline is null || undoRedoManager is null || asyncAwaitStatus is null)
            return;

        try
        {
            var result = FaceApplyService.Apply(
                timeline, undoRedoManager, asyncAwaitStatus,
                [.. Proposals], Settings.WriteTarget, Settings.LayerOffset, Settings.KeepUnspecifiedParts);

            Append(result.Message);
            if (result.Success)
                Settings.Save();
            else
                lastFailure = result.Message;
        }
        catch (Exception ex)
        {
            Fail($"適用に失敗しました: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private async Task TestConnectionAsync()
    {
        IsProcessing = true;
        try
        {
            var endpoint = Settings.CreateEndpoint();
            using var client = new OpenAiCompatibleClient(endpoint);
            var models = await client.ListModelsAsync(CancellationToken.None);
            Append($"接続できました ({endpoint.ResolveModelsUrl()})。モデル {models.Count} 件: {string.Join(", ", models.Take(8))}");

            if (string.IsNullOrWhiteSpace(Settings.ModelName) && models.Count > 0)
            {
                Settings.ModelName = models[0];
                Settings.Save();
                Append($"モデル名が空だったので \"{models[0]}\" を設定しました。");
            }
            else if (!string.IsNullOrWhiteSpace(Settings.ModelName) && !models.Contains(Settings.ModelName))
            {
                Append($"注意: 設定中のモデル \"{Settings.ModelName}\" が一覧にありません。");
            }
        }
        catch (Exception ex)
        {
            Append($"接続できませんでした: {ex.Message}");
        }
        finally
        {
            IsProcessing = false;
        }
    }

    private void SaveSettings()
    {
        Settings.Save();
        Append("設定を保存しました。");
    }

    private void SetWriteTarget(FaceWriteTarget value)
    {
        if (Settings.WriteTarget == value)
            return;
        Settings.WriteTarget = value;
        OnPropertyChanged(nameof(IsOverwriteVoiceItem));
        OnPropertyChanged(nameof(IsInsertTachieFaceItem));
    }

    private void CopyRecommendedNames()
    {
        try
        {
            System.Windows.Clipboard.SetText(RecommendedPresetNames.ToClipboardText());
            Append("推奨プリセット名をコピーしました。preset.ini のセクション名を整えてから、もう一度スキャンしてください。");
        }
        catch (Exception ex)
        {
            Append($"クリップボードにコピーできませんでした: {ex.Message}");
        }
    }

    private void OpenPresetFolder()
    {
        var path = SelectedCharacter?.PresetPath;
        if (path is null)
            return;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{path}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Append($"フォルダを開けませんでした: {ex.Message}");
        }
    }

    private void RaiseCommandStates()
    {
        ScanCommand.RaiseCanExecuteChanged();
        CancelCommand.RaiseCanExecuteChanged();
        TestConnectionCommand.RaiseCanExecuteChanged();
        OpenPresetFolderCommand.RaiseCanExecuteChanged();
    }

    private void Fail(string message)
    {
        lastFailure = message;
        Append(message);
    }

    private void Append(string message) => FaceAssignLog.Write(message);
}
