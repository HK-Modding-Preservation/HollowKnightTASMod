using System;
using System.IO;
using System.Linq;
using HollowKnightTAS.Companion.Services;
using HollowKnightTAS.Core.Movie;

namespace HollowKnightTAS.Companion.ViewModels;

public sealed partial class MainViewModel
{
    private InitialSaveSnapshot? sequenceInitialSaves;
    public InitialSaveSnapshot? SequenceInitialSaves => sequenceInitialSaves;

    public System.Threading.Tasks.Task ApplyEditedInitialSavesAsync(InitialSaveSnapshot original, InitialSaveSnapshot edited)
    {
        if (sequenceInitialSaves?.Id != original.Id)
            throw new InvalidOperationException("序列已切换，请重新打开存档编辑器。");
        // Reuse document switching: preserve the old timeline and require a fresh shadow.
        return ApplySequenceAsync(new SequenceFile(MovieText, edited), null);
    }
    private string initialSaveCacheRoot = Path.Combine(FrameSaveRoot, "initial-saves");
    public string SequenceBindingStatus => sequenceInitialSaves == null
        ? "未绑定初始存档（旧序列）"
        : "初始存档已绑定：" + string.Join(" / ", Enumerable.Range(1, 4).Select(slot =>
            $"{slot} 槽" + (sequenceInitialSaves.Hashes.ContainsKey($"user{slot}.dat") ? "有档" : "空槽")));

    private string BaselineCachePath(string id)
    {
        if (!MovieProtocolV1.IsLowerSha256(id)) throw new InvalidDataException("初始存档标识无效。");
        return Path.Combine(initialSaveCacheRoot, id + SequencePackage.Extension);
    }

    private void SetSequenceInitialSaves(InitialSaveSnapshot? snapshot)
    {
        if (snapshot != null)
        {
            var path = BaselineCachePath(snapshot.Id);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // Immutable cache keeps timeline restores independent of the package's path.
            if (!File.Exists(path)) SequencePackage.WriteAsync(path, "", snapshot).GetAwaiter().GetResult();
            else if (SequencePackage.Read(path).InitialSaves?.Id != snapshot.Id)
                throw new InvalidDataException("时间线初始存档缓存校验失败。");
        }
        sequenceInitialSaves = snapshot;
        if (fullRunMovies != null) fullRunMovies.SequenceInitialSaves = snapshot;
        lastAutoSaveText = "";
        OnPropertyChanged(nameof(SequenceBindingStatus));
    }

    private InitialSaveSnapshot? GetTimelineInitialSaves(TimelineTree tree)
    {
        if (!string.IsNullOrEmpty(tree.InitialSavesId))
        {
            var snapshot = sequenceInitialSaves?.Id == tree.InitialSavesId ? sequenceInitialSaves
                : SequencePackage.Read(BaselineCachePath(tree.InitialSavesId)).InitialSaves;
            if (snapshot?.Id != tree.InitialSavesId || !TimelineTree.SameBaseline(tree.OriginalHashes, snapshot.Hashes))
                throw new InvalidDataException("时间线初始存档缺失或校验失败。");
            return snapshot;
        }
        if (fullRunMovies?.IsPending == true && !TimelineTree.SameBaseline(tree.OriginalHashes, fullRunMovies.SessionInitialSaves!.Hashes))
            throw new InvalidOperationException("旧时间线没有绑定初始存档，且当前存档与其起点不同。");
        // Old timelines already carry hashes. Only reuse bytes proven to match those
        // hashes; never recapture unrelated current real saves on their next restart.
        return fullRunMovies?.IsPending == true ? fullRunMovies.SessionInitialSaves : null;
    }
}
