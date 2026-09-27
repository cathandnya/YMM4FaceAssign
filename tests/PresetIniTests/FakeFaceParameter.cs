// 動く立ち絵の表情パラメータを模した型。実物 (internal) と同じ12スロットを持つ。
sealed class FakeFaceParameter
{
    public string? Eyebrow { get; set; }
    public string? Eye { get; set; }
    public string? Mouth { get; set; }
    public string? Hair { get; set; }
    public string? Complexion { get; set; }
    public string? Body { get; set; }
    public string? Back1 { get; set; }
    public string? Back2 { get; set; }
    public string? Back3 { get; set; }
    public string? Etc1 { get; set; }
    public string? Etc2 { get; set; }
    public string? Etc3 { get; set; }
}
