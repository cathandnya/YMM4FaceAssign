namespace FaceAssign.Tachie;

public static class FaceParameterWriter
{
    /// <summary>
    /// プリセットを表情パラメータへ書き込む。
    ///
    /// 既定では12スロット全部を書き、プリセットが指定していないスロットには null を入れる。
    /// これは YMM4 本体のプリセット ComboBox (ParameterBase.CopyFaceFrom) と同じ挙動で、
    /// null は「立ち絵アイテム側の ItemParameter を継承」を意味する。
    /// 「現状維持」にすると前回の実行結果に依存して非冪等になるため既定にしない。
    ///
    /// EyeAnimation / MouthAnimation は触らない (プリセットは元々これらに関与しない。
    /// 書き換えると口パク・まばたきが壊れる)。
    /// </summary>
    /// <returns>実際に値が変わったスロット数。</returns>
    public static int Apply(object faceParameter, PresetDefinition preset, bool keepUnspecified)
    {
        var accessor = FaceSlotAccessor.For(faceParameter);
        var changed = 0;

        foreach (var slot in TachiePartTables.AllSlots)
        {
            var hasValue = preset.Parts.TryGetValue(slot, out var value);
            if (!hasValue)
            {
                if (keepUnspecified)
                    continue;
                value = null;
            }

            var before = accessor.Get(faceParameter, slot);
            if (string.Equals(before, value, StringComparison.Ordinal))
                continue;

            accessor.Set(faceParameter, slot, value);
            changed++;
        }

        return changed;
    }
}
