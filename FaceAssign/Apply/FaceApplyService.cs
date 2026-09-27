using FaceAssign.Tachie;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;
using YukkuriMovieMaker.UndoRedo;

namespace FaceAssign.Apply;

public sealed record ApplyResult(bool Success, string Message);

/// <summary>
/// 提案をタイムラインへ書き込む。どちらの経路も Lock は書き換えの周りだけで取り、
/// Record() は最後に1回だけ呼ぶ。これで Ctrl+Z 1回で全部戻る。
/// </summary>
public static class FaceApplyService
{
    public static ApplyResult Apply(
        Timeline timeline,
        UndoRedoManager undoRedoManager,
        AsyncAwaitStatus asyncAwaitStatus,
        IReadOnlyList<FaceAssignProposal> proposals,
        FaceWriteTarget target,
        int layerOffset,
        bool keepUnspecified)
    {
        // 推定中にアイテムが消されている可能性があるので、直前に生存確認する。
        var accepted = proposals
            .Where(p => p.CanApply)
            .Where(p => timeline.Items.Contains(p.Item))
            .ToArray();

        var stale = proposals.Count(p => p.CanApply) - accepted.Length;
        if (accepted.Length == 0)
            return new ApplyResult(false, "適用できる行がありません。");

        var suffix = stale > 0 ? $" (削除済みの {stale} 件は除外)" : "";

        return target == FaceWriteTarget.OverwriteVoiceItem
            ? OverwriteVoiceItems(undoRedoManager, asyncAwaitStatus, accepted, keepUnspecified, suffix)
            : InsertFaceItems(timeline, undoRedoManager, asyncAwaitStatus, accepted, layerOffset, keepUnspecified, suffix);
    }

    private static ApplyResult OverwriteVoiceItems(
        UndoRedoManager undoRedoManager,
        AsyncAwaitStatus asyncAwaitStatus,
        IReadOnlyList<FaceAssignProposal> accepted,
        bool keepUnspecified,
        string suffix)
    {
        var changed = 0;

        using (asyncAwaitStatus.Lock(false))
        {
            foreach (var proposal in accepted)
            {
                var face = proposal.Item.TachieFaceParameter;
                if (face is null)
                {
                    face = TachieCharacterProbe.CreateFaceParameter(proposal.Item.Character!);
                    if (face is null)
                        continue;
                    proposal.Item.TachieFaceParameter = face;
                }

                changed += FaceParameterWriter.Apply(face, proposal.Preset!, keepUnspecified);
            }

            undoRedoManager.Record();
        }

        return new ApplyResult(true, $"ボイスアイテム {accepted.Count} 件の表情を書き換えました (変更スロット {changed}){suffix}。Ctrl+Z 1回で戻せます。");
    }

    private static ApplyResult InsertFaceItems(
        Timeline timeline,
        UndoRedoManager undoRedoManager,
        AsyncAwaitStatus asyncAwaitStatus,
        IReadOnlyList<FaceAssignProposal> accepted,
        int layerOffset,
        bool keepUnspecified,
        string suffix)
    {
        using (asyncAwaitStatus.Lock(true))
        {
            var newItems = new List<IItem>(accepted.Count);

            foreach (var proposal in accepted)
            {
                var faceItem = new TachieFaceItem(proposal.Item)
                {
                    Layer = Math.Max(0, proposal.Item.Layer + layerOffset),
                };

                if (faceItem.TachieFaceParameter is null)
                    continue;

                FaceParameterWriter.Apply(faceItem.TachieFaceParameter, proposal.Preset!, keepUnspecified);
                newItems.Add(faceItem);
            }

            if (newItems.Count == 0)
                return new ApplyResult(false, "挿入できる表情アイテムがありませんでした。");

            var minFrame = newItems.Min(i => i.Frame);
            var minLayer = newItems.Min(i => i.Layer);

            if (!timeline.TryAddItems([.. newItems], minFrame, minLayer, false))
                return new ApplyResult(false, $"レイヤー {minLayer} 付近に空きがないため挿入できませんでした。変更は行っていません。");

            undoRedoManager.Record();
            return new ApplyResult(true, $"表情アイテム {newItems.Count} 件を挿入しました{suffix}。Ctrl+Z 1回で戻せます。");
        }
    }
}
