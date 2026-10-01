using FaceAssign.Llm;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Project;

namespace FaceAssign.Tachie;

/// <summary>1キャラクター分のプリセット。候補は enum に入るのでキャラ単位で持つ。</summary>
public sealed class CharacterPresetsViewModel : Bindable
{
    private CharacterPresetsViewModel(Character character, string directory, IReadOnlyList<PresetDefinition> presets)
    {
        Character = character;
        Directory = directory;
        Presets = presets;
    }

    public Character Character { get; }

    public string Directory { get; }

    public string CharacterName => Character.Name;

    public IReadOnlyList<PresetDefinition> Presets { get; }

    /// <summary>LLM に渡す候補。絞り込みはせず、読み込んだプリセットを全部使う。</summary>
    public IReadOnlyList<PresetCandidate> Candidates { get; private init; } = [];

    public static CharacterPresetsViewModel? TryCreate(Character character, out string? reason)
    {
        reason = null;

        if (!TachieCharacterProbe.IsAnimationTachie(character))
        {
            reason = $"{character.Name}: 動く立ち絵ではないのでスキップします ({character.TachieType?.Name ?? "不明"})";
            return null;
        }

        var dir = TachieCharacterProbe.GetTachieDirectory(character);
        if (string.IsNullOrEmpty(dir))
        {
            reason = $"{character.Name}: 素材フォルダが取得できませんでした";
            return null;
        }

        var presets = PresetIniReader.Read(dir);
        if (presets.Count == 0)
        {
            reason = $"{character.Name}: preset.ini が見つからないか、有効なプリセットがありません";
            return null;
        }

        return new CharacterPresetsViewModel(character, dir, presets)
        {
            Candidates = [.. presets.Select(p => new PresetCandidate(p.Label))],
        };
    }

}
