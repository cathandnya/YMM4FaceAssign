using FaceAssign.Apply;

namespace FaceAssign.State;

/// <summary>
/// プロジェクトごとに .ymmp の ToolStates へ保存される小さな設定。
/// 提案テーブルそのものは ToolState.ProjectTemporaryState (メモリのみ) に置く。
/// </summary>
public sealed class FaceAssignSavedState
{
    public FaceWriteTarget WriteTarget { get; set; } = FaceWriteTarget.OverwriteVoiceItem;

    public int LayerOffset { get; set; } = 1;

    public bool UseSelectionOnly { get; set; } = true;

    public bool KeepUnspecifiedParts { get; set; }

    /// <summary>「基本はカメラ目線、強調時のみ指差し」のような、この動画全体の方針。</summary>
    public string Policy { get; set; } = "";
}
