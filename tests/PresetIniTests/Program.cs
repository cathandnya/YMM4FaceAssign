using FaceAssign.Tachie;
using PresetIniTests;

var failures = 0;

void Check(string name, bool ok, string detail = "")
{
    Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {name}{(detail.Length > 0 ? " -- " + detail : "")}");
    if (!ok) failures++;
}

var dir = Fixture.Create();
Console.WriteLine($"フィクスチャ: {dir}");

try
{
    Console.WriteLine("== 読み込み ==");
    var presets = PresetIniReader.Read(dir);
    Check("7 セクション読める", presets.Count == 7, $"count={presets.Count}");
    Check("未知のキーだけのセクションも残る", presets.Any(p => p.Name == "未知キーだけ" && p.Parts.Count == 0));
    Check("コメント行は読み飛ばす", presets.All(p => !p.Name.StartsWith(';')));

    Console.WriteLine("== 同名セクション ==");
    var dup = presets.Where(p => p.Name == Fixture.DuplicatedName).ToArray();
    Check("2件とも保持される", dup.Length == 2);
    Check("ラベルが一意になる",
        dup.Length == 2 && dup[0].Label == "指差し" && dup[1].Label == "指差し #2",
        string.Join(" | ", dup.Select(p => p.Label)));
    Check("中身は別物", dup.Length == 2 && !dup[0].Parts.SequenceEqual(dup[1].Parts));

    Console.WriteLine("== パーツの解決 ==");
    var naku = presets.Single(p => p.Name == "泣く");
    Check("7パーツ", naku.Parts.Count == 7, $"count={naku.Parts.Count}");
    Check("眉 が絶対パスに解決",
        naku.Parts[TachiePartSlot.Eyebrow] == Path.Combine(dir, "眉", "上がり眉.png"),
        naku.Parts[TachiePartSlot.Eyebrow]);
    Check("他1/他2/他3 が Etc1/2/3 に、フォルダは全部 他",
        naku.Parts[TachiePartSlot.Etc1] == Path.Combine(dir, "他", "左腕_手を挙げる.png") &&
        naku.Parts[TachiePartSlot.Etc2] == Path.Combine(dir, "他", "右腕_手を挙げる.png") &&
        naku.Parts[TachiePartSlot.Etc3] == Path.Combine(dir, "他", "記号_涙.png"));

    var back = presets.Single(p => p.Name == "背景つき");
    Check("後1/後2 が Back1/Back2 に、フォルダは両方 後",
        back.Parts[TachiePartSlot.Back1] == Path.Combine(dir, "後", "後ろ_窓.png") &&
        back.Parts[TachiePartSlot.Back2] == Path.Combine(dir, "後", "後ろ_棚.png"));

    Check("未知のキーは捨てる", presets.Single(p => p.Name == "未知キーだけ").Parts.Count == 0);

    Console.WriteLine("== 参照先の欠落 ==");
    Check("実在するものは欠落にしない", !naku.HasMissingFiles, string.Join(", ", naku.MissingFiles));
    var broken = presets.Single(p => p.Name == "壊れた参照");
    Check("欠落を検出する", broken.HasMissingFiles && broken.MissingFiles.Length == 1);
    Check("欠落しても候補からは外さない", broken.Parts.Count == 1);
    Console.WriteLine($"    PartsSummary: {broken.PartsSummary}");

    Console.WriteLine("== 存在しないフォルダ ==");
    Check("空リストを返し例外を投げない",
        PresetIniReader.Read(Path.Combine(dir, "いないキャラ")).Count == 0);

    Console.WriteLine("== FaceParameterWriter (12スロット全書き) ==");
    var fake = new FakeFaceParameter { Eye = "old-eye.png", Mouth = "old-mouth.png", Hair = "old-hair.png" };
    var changed = FaceParameterWriter.Apply(fake, naku, keepUnspecified: false);
    Check("眉/目/髪/顔色/他1-3 が書き込まれる",
        fake.Eyebrow == naku.Parts[TachiePartSlot.Eyebrow] &&
        fake.Eye == naku.Parts[TachiePartSlot.Eye] &&
        fake.Hair == naku.Parts[TachiePartSlot.Hair] &&
        fake.Etc3 == naku.Parts[TachiePartSlot.Etc3]);
    Check("プリセットが指定しない 口 は null に上書きされる", fake.Mouth is null, fake.Mouth ?? "null");
    Check("変更スロット数が返る", changed > 0, changed.ToString());

    var keep = new FakeFaceParameter { Mouth = "keep-mouth.png" };
    FaceParameterWriter.Apply(keep, naku, keepUnspecified: true);
    Check("keepUnspecified=true なら 口 は残る", keep.Mouth == "keep-mouth.png", keep.Mouth ?? "null");

    Check("2回目の適用は変更ゼロ (冪等)", FaceParameterWriter.Apply(fake, naku, keepUnspecified: false) == 0);
}
finally
{
    Fixture.Delete(dir);
}

Console.WriteLine();
Console.WriteLine(failures == 0 ? "すべて PASS" : $"{failures} 件 FAIL");
return failures == 0 ? 0 : 1;
