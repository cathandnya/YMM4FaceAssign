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

    /// <summary>
    /// 以前はここに作品の方針を保存していた。いまは全体設定へ移したので、
    /// 既存プロジェクトから引き継ぐためだけに残している。
    /// </summary>
    public string Policy { get; set; } = "";
}
