using FaceAssign.Tachie;

var failures = 0;

void Check(string name, bool ok, string detail = "")
{
    Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {name}{(detail.Length > 0 ? " -- " + detail : "")}");
    if (!ok) failures++;
}

const string ZundaDir = @"C:\ymm4\sozai\char\ずんだもん";
const string MetanDir = @"C:\ymm4\sozai\char\四国めたん";

Console.WriteLine("== ずんだもん ==");
var zunda = PresetIniReader.Read(ZundaDir);
Check("16 セクション読める", zunda.Count == 16, $"count={zunda.Count}");
Check("ユニーク名は15", zunda.Select(p => p.Name).Distinct().Count() == 15);

var sasi = zunda.Where(p => p.Name == "指差し").ToArray();
Check("[指差し] が2件保持される", sasi.Length == 2);
Check("重複ラベルが一意 (指差し / 指差し #2)",
    sasi.Length == 2 && sasi[0].Label == "指差し" && sasi[1].Label == "指差し #2",
    string.Join(" | ", sasi.Select(p => p.Label)));
Check("重複の中身が別物", sasi.Length == 2 && !sasi[0].Parts.SequenceEqual(sasi[1].Parts));

var naku = zunda.Single(p => p.Name == "泣く");
Check("泣く は7パーツ", naku.Parts.Count == 7, $"count={naku.Parts.Count}");
Check("泣く の 眉 が絶対パスに解決",
    naku.Parts[TachiePartSlot.Eyebrow] == Path.Combine(ZundaDir, "眉", "上がり眉.png"),
    naku.Parts[TachiePartSlot.Eyebrow]);
Check("他1/他2/他3 が Etc1/Etc2/Etc3 に、フォルダは全部 他",
    naku.Parts[TachiePartSlot.Etc1] == Path.Combine(ZundaDir, "他", "左腕_手を挙げる.png") &&
    naku.Parts[TachiePartSlot.Etc2] == Path.Combine(ZundaDir, "他", "右腕_手を挙げる.png") &&
    naku.Parts[TachiePartSlot.Etc3] == Path.Combine(ZundaDir, "他", "記号_涙.png"));
Check("参照先PNGが全部実在する", !naku.HasMissingFiles, string.Join(", ", naku.MissingFiles));
Console.WriteLine($"    DescribeParts: {naku.DescribeParts()}");

Console.WriteLine("== 四国めたん (後 フォルダが無いキャラ) ==");
var metan = PresetIniReader.Read(MetanDir);
Check("プリセットが読める", metan.Count > 0, $"count={metan.Count}");
Check("後 スロットを使うプリセットは無い",
    metan.All(p => !p.Parts.ContainsKey(TachiePartSlot.Back1)
                && !p.Parts.ContainsKey(TachiePartSlot.Back2)
                && !p.Parts.ContainsKey(TachiePartSlot.Back3)));
var broken = metan.Where(p => p.HasMissingFiles).ToArray();
Console.WriteLine($"    参照先PNG欠落: {broken.Length} 件 {string.Join(", ", broken.Select(p => p.Label))}");
Console.WriteLine($"    名前: {string.Join(", ", metan.Select(p => p.Label))}");

Console.WriteLine("== 存在しないフォルダ ==");
Check("空リストを返し例外を投げない", PresetIniReader.Read(@"C:\ymm4\sozai\char\いない人").Count == 0);

Console.WriteLine("== 推奨名の判定 ==");
Check("笑顔 は推奨名", RecommendedPresetNames.IsRecommended("笑顔"));
Check("ぬーん は推奨名でない", !RecommendedPresetNames.IsRecommended("ぬーん"));
Check("ずんだもんの現状は必須推奨名をほぼ満たさない",
    RecommendedPresetNames.Required.Count(n => zunda.Any(p => p.Name == n)) <= 1,
    $"一致 {RecommendedPresetNames.Required.Count(n => zunda.Any(p => p.Name == n))}/8");

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

var idempotent = FaceParameterWriter.Apply(fake, naku, keepUnspecified: false);
Check("2回目の適用は変更ゼロ (冪等)", idempotent == 0, idempotent.ToString());

Console.WriteLine();
Console.WriteLine(failures == 0 ? "すべて PASS" : $"{failures} 件 FAIL");
return failures == 0 ? 0 : 1;
