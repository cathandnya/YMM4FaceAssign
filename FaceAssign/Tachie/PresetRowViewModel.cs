using FaceAssign.Llm;
using FaceAssign.Settings;
using YukkuriMovieMaker.Commons;

namespace FaceAssign.Tachie;

/// <summary>プリセット一覧の1行。補足はユーザーが編集し、素材単位で保存される。</summary>
public sealed class PresetRowViewModel(PresetDefinition preset, PresetChoice choice) : Bindable
{
    public PresetDefinition Preset { get; } = preset;

    public PresetChoice Choice { get; } = choice;

    public string Label => Preset.Label;

    public string PartsSummary => Preset.PartsSummary;

    public bool IsRecommendedName => RecommendedPresetNames.IsRecommended(Preset.Name);

    public string Hint
    {
        get => Choice.Hint;
        set
        {
            if (Choice.Hint == value)
                return;
            Choice.Hint = value ?? "";
            OnPropertyChanged(nameof(Hint));
        }
    }

    public string Warning => Preset.HasMissingFiles
        ? $"PNG欠落 {Preset.MissingFiles.Length} 件"
        : IsRecommendedName ? "" : "推奨名に無い";

    public PresetCandidate ToCandidate() =>
        new(Label, string.IsNullOrWhiteSpace(Hint) ? null : Hint);
}
