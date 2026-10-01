using System.Text.Json.Serialization;
using FaceAssign.Apply;
using FaceAssign.Llm;
using YukkuriMovieMaker.Plugin;

namespace FaceAssign.Settings;

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

    private string baseUrl = "http://127.0.0.1:1234";
    private string modelName = "qwen/qwen3-4b";
    private string apiKey = "";
    private string reasoningEffort = "low";

    // 接続先の雛形を選んだときに欄へ反映させたいので、この4つは変更通知を出す。
    public string BaseUrl { get => baseUrl; set => Set(ref baseUrl, value); }

    /// <summary>
    /// 既定は LM Studio の qwen3-4b。実測で 10行 2〜3秒と、gpt-oss-20b の7倍前後速い。
    /// そのぶん選択の質は落ちる (8行の手動採点で 6/8 → 4/8)。
    /// 接続テスト時に未設定なら、一覧の先頭で自動補完する。
    /// </summary>
    public string ModelName { get => modelName; set => Set(ref modelName, value); }

    /// <summary>実行時に使う生のキー。ファイルには書かない。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    [Newtonsoft.Json.JsonIgnore]
    public string ApiKey { get => apiKey; set => Set(ref apiKey, value); }

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
    public string ReasoningEffort { get => reasoningEffort; set => Set(ref reasoningEffort, value); }

    /// <summary>
    /// 判断理由を出させるか。出力トークンが増えて全体の25〜40%ほど遅くなる (実測)。
    /// 既定は off。理由が欲しいときだけパネルのチェックで有効にする。
    /// </summary>
    public bool IncludeReason { get; set; }

    /// <summary>
    /// 同時に投げるリクエスト数。
    ///
    /// 既定は 1。増やして速くなるかはモデルとサーバ次第で、外すと大きく損をする。
    /// 実測では LM Studio + gpt-oss-20b が 2 で約16%速くなった一方、
    /// 同じ LM Studio でも qwen3-4b は 2 にすると 26秒が 535秒になった (タイムアウト多発)。
    /// Ollama も 2 以上で遅くなる。増やすなら実際に測ってからにすること。
    /// </summary>
    public int Parallelism { get; set; } = 1;

    public FaceWriteTarget WriteTarget { get; set; } = FaceWriteTarget.OverwriteVoiceItem;

    public int LayerOffset { get; set; } = 1;

    public bool KeepUnspecifiedParts { get; set; }

    /// <summary>
    /// システムプロンプトに追記する自由文。候補の選び方に偏りを与えるためのもの。
    /// プロジェクトごとではなく全体に効く。
    /// </summary>
    public string Policy { get; set; } = "";

    public override void Initialize()
    {
    }

    public LlmEndpoint CreateEndpoint() =>
        new(BaseUrl, ModelName, string.IsNullOrWhiteSpace(ApiKey) ? null : ApiKey,
            Temperature, TimeoutSeconds, AppendNoThinkHint, ReasoningEffort, IncludeReason);

}
