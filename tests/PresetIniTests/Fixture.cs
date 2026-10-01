using System.Text;

namespace PresetIniTests;

/// <summary>
/// テスト用の立ち絵フォルダを一時ディレクトリに作る。
///
/// 以前は実際の素材フォルダを読んでいたが、ユーザーが preset.ini を整備した途端に
/// テストが落ちた。検証したい挙動 (重複セクション、後/他 の番号付きキー、PNG欠落) を
/// こちらで用意する。
/// </summary>
public static class Fixture
{
    public const string DuplicatedName = "指差し";

    private const string Ini = """
        [指差し]
        他1=右腕_指差し.png
        他2=左腕_腰.png

        [にこにこ]
        目=にっこり2.png
        顔色=ほっぺ2.png

        [泣く]
        眉=上がり眉.png
        目=_.png
        髪=枝豆萎え.png
        顔色=ほっぺ2.png
        他1=左腕_手を挙げる.png
        他2=右腕_手を挙げる.png
        他3=記号_涙.png

        [指差し]
        目=普通白目_目逸らし.png
        他1=右腕_指差し.png

        ; コメント行は無視される
        [背景つき]
        後1=後ろ_窓.png
        後2=後ろ_棚.png

        [壊れた参照]
        目=存在しない.png

        [未知キーだけ]
        靴=ない.png
        """;

    /// <summary>作成した立ち絵フォルダのパスを返す。</summary>
    public static string Create()
    {
        var dir = Path.Combine(Path.GetTempPath(), "FaceAssignFixture", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        File.WriteAllText(Path.Combine(dir, "preset.ini"), Ini, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        // 「壊れた参照」以外のパーツ画像を用意する。中身は見ないので空でよい。
        var parts = new (string Folder, string[] Files)[]
        {
            ("眉", ["上がり眉.png"]),
            ("目", ["にっこり2.png", "_.png", "普通白目_目逸らし.png"]),
            ("髪", ["枝豆萎え.png"]),
            ("顔色", ["ほっぺ2.png"]),
            ("他", ["右腕_指差し.png", "左腕_腰.png", "左腕_手を挙げる.png", "右腕_手を挙げる.png", "記号_涙.png"]),
            ("後", ["後ろ_窓.png", "後ろ_棚.png"]),
        };

        foreach (var (folder, files) in parts)
        {
            var path = Path.Combine(dir, folder);
            Directory.CreateDirectory(path);
            foreach (var file in files)
                File.WriteAllBytes(Path.Combine(path, file), []);
        }

        return dir;
    }

    public static void Delete(string dir)
    {
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch (IOException)
        {
            // 消せなくてもテストの成否には関係ない。
        }
    }
}
