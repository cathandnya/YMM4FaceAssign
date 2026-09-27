using FaceAssign.Tachie;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Project.Items;

namespace FaceAssign.Apply;

/// <summary>提案テーブルの1行。適用前に目で確認して、外したい行のチェックを外す。</summary>
public sealed class FaceAssignProposal(int id, VoiceItem item, string characterName) : Bindable
{
    private PresetDefinition? preset;
    private string? reason;
    private string? warning;
    private bool isAccepted = true;

    public int Id { get; } = id;

    public VoiceItem Item { get; } = item;

    public string CharacterName { get; } = characterName;

    public string Serif => Item.Serif?.Replace("\r", "").Replace("\n", " ") ?? "";

    /// <summary>null は「変更しない」。</summary>
    public PresetDefinition? Preset
    {
        get => preset;
        set
        {
            if (!Set(ref preset, value))
                return;
            OnPropertyChanged(nameof(PresetLabel));
            OnPropertyChanged(nameof(CanApply));
        }
    }

    public string PresetLabel => Preset?.Label ?? "(変更しない)";

    public string? Reason { get => reason; set => Set(ref reason, value); }

    public string? Warning { get => warning; set => Set(ref warning, value); }

    public bool IsAccepted
    {
        get => isAccepted;
        set
        {
            if (Set(ref isAccepted, value))
                OnPropertyChanged(nameof(CanApply));
        }
    }

    public bool CanApply => IsAccepted && Preset is not null;

    /// <summary>そのキャラクターで選べるプリセット。テーブル上で選び直すためのもの。</summary>
    public IReadOnlyList<PresetDefinition> Choices { get; init; } = [];
}
