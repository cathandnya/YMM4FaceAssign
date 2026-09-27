namespace FaceAssign.Apply;

/// <summary>推定した表情をどこに書き込むか。</summary>
public enum FaceWriteTarget
{
    /// <summary>ボイスアイテム自身の TachieFaceParameter を上書きする。タイムラインが汚れない。</summary>
    OverwriteVoiceItem,

    /// <summary>別レイヤーに表情アイテムを挿入する。後から目で見て微調整しやすい。</summary>
    InsertTachieFaceItem,
}
