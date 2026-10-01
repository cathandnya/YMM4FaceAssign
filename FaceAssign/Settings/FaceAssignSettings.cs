using System.Text.Json.Serialization;
using FaceAssign.Apply;
using FaceAssign.Llm;
using YukkuriMovieMaker.Plugin;

namespace FaceAssign.Settings;

/// <summary>
/// 名前だけで意味が伝わらないプリセットに添える補足。
/// 素材に属する情報なので、プロジェクトではなく立ち絵ディレクトリ単位で覚える。
/// </summary>
public sealed class PresetChoice
{
    public string Hint { get; set; } = "";
}

/// <summary>
/// user\setting\&lt;version&gt;\FaceAssign.Settings.FaceAssignSettings.json に保存される全体設定。
/// </summary>
public class FaceAssignSettings : SettingsBase<FaceAssignSettings>
{
    public override SettingsCategory Category => SettingsCategory.Tool;

    public override string Name => "表情割り当て";

    // 設定項目はツールパネル側にまとめてあるので、YMM4 の設定画面には出さない。
    public override bool HasSettingView => false;

    public override object SettingView => null!;

    public string BaseUrl { get; set; } = "http://127.0.0.1:1234";

    /// <summary>
    /// 既定は LM Studio の qwen3-4b。実測で 10行 2〜3秒と、gpt-oss-20b の7倍前後速い。
    /// そのぶん選択の質は落ちる (8行の手動採点で 6/8 → 4/8)。
    /// 質を優先するなら openai/gpt-oss-20b に変える。
    /// 接続テスト時に未設定なら、一覧の先頭で自動補完する。
    /// </summary>
    public string ModelName { get; set; } = "qwen/qwen3-4b";

    /// <summary>実行時に使う生のキー。ファイルには書かない。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    [Newtonsoft.Json.JsonIgnore]
    public string ApiKey { get; set; } = "";

    /// <summary>DPAPI で暗号化したキー。ファイルに残るのはこちらだけ。</summary>
    public string ProtectedApiKey
    {
        get => DpapiSecret.Protect(ApiKey);
        set => ApiKey = DpapiSecret.Unprotect(value);
    }

    /// <summary>
    /// 平文で保存していた頃の設定ファイルからの移行用。
    /// 読み込み時に拾って、次の保存で暗号化された側へ移す。書き出しは常に空。
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("ApiKey")]
    [Newtonsoft.Json.JsonProperty("ApiKey")]
    public string LegacyApiKey
    {
        get => "";
        set
        {
            if (!string.IsNullOrEmpty(value))
                ApiKey = value;
        }
    }

    public double Temperature { get; set; }

    public int TimeoutSeconds { get; set; } = 180;

    /// <summary>
    /// 1リクエストあたりの対象行数。20 だとモデルによっては後半を落とす (gpt-oss-20b で実測)。
    /// 取りこぼしは自動で聞き直すが、既定はやや控えめにしておく。
    /// </summary>
    public int MaxLinesPerRequest { get; set; } = 12;

    /// <summary>チャンクの手前に付ける、番号なしの文脈行数。</summary>
    public int ContextLineCount { get; set; } = 4;

    /// <summary>Qwen3 系の thinking を抑える /no_think を system プロンプトに足す。</summary>
    public bool AppendNoThinkHint { get; set; } = true;

    /// <summary>
    /// gpt-oss 系の reasoning_effort。medium は low の約2倍遅かった (実測) ので既定は low。
    /// 対応していないサーバでは無視される。
    /// </summary>
    public string ReasoningEffort { get; set; } = "low";

    /// <summary>
    /// 判断理由を出させるか。出力トークンが増えて全体の25〜40%ほど遅くなる (実測)。
    /// 既定は off。理由が欲しいときだけパネルのチェックで有効にする。
    /// </summary>
    public bool IncludeReason { get; set; }

    /// <summary>
    /// 同時に投げるリクエスト数。LM Studio は 2 で約16%速くなり3以上は頭打ち、
    /// Ollama は 2 以上にするとむしろ遅くなった (実測)。Ollama を使うときは 1 に戻すこと。
    /// </summary>
    public int Parallelism { get; set; } = 2;

    public FaceWriteTarget WriteTarget { get; set; } = FaceWriteTarget.OverwriteVoiceItem;

    public int LayerOffset { get; set; } = 1;

    public bool KeepUnspecifiedParts { get; set; }

    /// <summary>
    /// システムプロンプトに追記する自由文。候補の選び方に偏りを与えるためのもの。
    /// プロジェクトごとではなく全体に効く。
    /// </summary>
    public string Policy { get; set; } = "";

    /// <summary>立ち絵ディレクトリのパス → プリセットラベル → 補足。</summary>
    public Dictionary<string, Dictionary<string, PresetChoice>> PresetChoices { get; set; } = [];

    public override void Initialize()
    {
    }

    public LlmEndpoint CreateEndpoint() =>
        new(BaseUrl, ModelName, string.IsNullOrWhiteSpace(ApiKey) ? null : ApiKey,
            Temperature, TimeoutSeconds, AppendNoThinkHint, ReasoningEffort, IncludeReason);

    public Dictionary<string, PresetChoice> GetChoices(string tachieDirectory)
    {
        if (!PresetChoices.TryGetValue(tachieDirectory, out var choices))
        {
            choices = [];
            PresetChoices[tachieDirectory] = choices;
        }
        return choices;
    }
}
