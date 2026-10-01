using System.Collections.ObjectModel;
using FaceAssign.Settings;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Project;

namespace FaceAssign.Tachie;

/// <summary>1キャラクター分のプリセットと診断。候補は enum に入るのでキャラ単位で持つ。</summary>
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

    public ObservableCollection<PresetRowViewModel> Rows { get; } = [];

    public string Diagnostics { get; private set; } = "";

    public string PresetPath => PresetIniReader.GetPresetPath(Directory);

    /// <summary>LLM に渡す候補。絞り込みはせず、読み込んだプリセットを全部使う。</summary>
    public IReadOnlyList<PresetRowViewModel> Candidates => [.. Rows];

    public static CharacterPresetsViewModel? TryCreate(Character character, FaceAssignSettings settings, out string? reason)
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

        var vm = new CharacterPresetsViewModel(character, dir, presets);
        vm.BuildRows(settings);
        vm.BuildDiagnostics();
        return vm;
    }

    private void BuildRows(FaceAssignSettings settings)
    {
        var stored = settings.GetChoices(Directory);

        Rows.Clear();
        foreach (var preset in Presets)
        {
            if (!stored.TryGetValue(preset.Label, out var choice))
            {
                choice = new PresetChoice();
                stored[preset.Label] = choice;
            }

            Rows.Add(new PresetRowViewModel(preset, choice));
        }

        // preset.ini から消えたラベルの設定は捨てる。整備で名前が変わっても腐らせない。
        foreach (var stale in stored.Keys.Except(Presets.Select(p => p.Label)).ToArray())
            stored.Remove(stale);
    }

    private void BuildDiagnostics()
    {
        var lines = new List<string>();
        var matched = Presets.Count(p => RecommendedPresetNames.IsRecommended(p.Name));
        lines.Add($"{CharacterName}: プリセット {Presets.Count} 件 / 推奨名一致 {matched} 件");

        var missingRequired = RecommendedPresetNames.Required.Where(n => Presets.All(p => p.Name != n)).ToArray();
        if (missingRequired.Length > 0)
            lines.Add($"    必須の推奨名が未定義: {string.Join(", ", missingRequired)}");

        var duplicated = Presets.GroupBy(p => p.Name).Where(g => g.Count() > 1)
            .Select(g => $"{g.Key} ({g.Count()}件)").ToArray();
        if (duplicated.Length > 0)
            lines.Add($"    同名のプリセットがあります: {string.Join(", ", duplicated)}");

        var broken = Presets.Count(p => p.HasMissingFiles);
        if (broken > 0)
            lines.Add($"    参照先PNGが見つからないプリセット: {broken} 件");

        if (matched == 0)
            lines.Add("    推奨名に一致するプリセットがありません。名前を感情語に整えると精度が上がります。");

        Diagnostics = string.Join(Environment.NewLine, lines);
    }

    public void RefreshDiagnostics()
    {
        BuildDiagnostics();
        OnPropertyChanged(nameof(Diagnostics));
    }
}
