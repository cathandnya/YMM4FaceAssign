using System.Collections.Immutable;
using System.IO;
using System.Text;

namespace FaceAssign.Tachie;

/// <summary>
/// 立ち絵フォルダ直下の preset.ini を読む。
/// YMM4 本体の Presets.Load() と同じ寛容さを狙う: BOM を許容し、同名セクションを保持し、
/// 壊れた行は黙って捨てる。決して例外を投げない。
/// </summary>
public static class PresetIniReader
{
    public const string FileName = "preset.ini";

    public static string GetPresetPath(string tachieDirectory) => Path.Combine(tachieDirectory, FileName);

    /// <summary>preset.ini が無い場合は空リストを返す。</summary>
    public static IReadOnlyList<PresetDefinition> Read(string tachieDirectory)
    {
        var path = GetPresetPath(tachieDirectory);
        if (!File.Exists(path))
            return [];

        string[] lines;
        try
        {
            // UTF-8 BOM 付きが実際の姿。BOM 無しでも読めるよう detect に任せる。
            lines = File.ReadAllLines(path, Encoding.UTF8);
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }

        var sections = new List<(string Name, Dictionary<TachiePartSlot, string> Parts)>();
        Dictionary<TachiePartSlot, string>? current = null;

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == ';' || line[0] == '#')
                continue;

            if (line[0] == '[')
            {
                // 末尾の ']' までを名前とする。名前に ']' を含む場合は最後の ']' を境界にする。
                var close = line.LastIndexOf(']');
                if (close <= 1)
                    continue;
                var name = line[1..close].Trim();
                if (name.Length == 0)
                    continue;
                current = [];
                sections.Add((name, current));
                continue;
            }

            if (current is null)
                continue;

            var eq = line.IndexOf('=');
            if (eq <= 0)
                continue;

            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            if (value.Length == 0)
                continue;
            if (!TachiePartTables.IniKeyToSlot.TryGetValue(key, out var slot))
                continue;

            var folder = TachiePartTables.SlotToFolder[slot];
            current[slot] = Path.Combine(tachieDirectory, folder, value);
        }

        // 同名セクションに一意なラベルを振る。1件目は素の名前、2件目以降は " #2"。
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var results = new List<PresetDefinition>(sections.Count);
        foreach (var (name, parts) in sections)
        {
            var occurrence = seen.TryGetValue(name, out var n) ? n : 0;
            seen[name] = occurrence + 1;
            var label = occurrence == 0 ? name : $"{name} #{occurrence + 1}";

            var missing = parts.Values.Where(p => !File.Exists(p)).ToImmutableArray();

            results.Add(new PresetDefinition(
                name,
                label,
                occurrence,
                parts.ToImmutableDictionary(),
                missing));
        }

        return results;
    }
}
