using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using HollowKnightTAS.Companion.Services;

namespace HollowKnightTAS.Companion.ViewModels;

public sealed partial class MainViewModel
{
    private bool keyboardFrameInputEnabled;
    private IReadOnlyDictionary<string, string> keyboardBindings = new Dictionary<string, string>();
    private IReadOnlyDictionary<string, bool>? pendingKeyboardFrame;
    public bool KeyboardFrameInputEnabled
    {
        get => keyboardFrameInputEnabled;
        set => Set(ref keyboardFrameInputEnabled, value);
    }

    public void ExecuteStepShortcut()
    {
        if (!StepCommand.CanExecute(null)) return;
        try
        {
            if (KeyboardFrameInputEnabled)
            {
                if (fullRunMovies?.IsPending != true || startupBoot?.IsWaiting != true)
                    throw new InvalidOperationException("键盘逐帧输入需要已暂停的全流程 TAS 会话。");
                // Capture before any await; a released key must not change this frame's intent.
                pendingKeyboardFrame = KeyboardFrameInput.Capture(keyboardBindings, ConfiguredAdvance);
            }
            StepCommand.Execute(null);
        }
        catch (Exception ex) { Status = ex.Message; }
        finally { pendingKeyboardFrame = null; }
    }

    private async Task WriteKeyboardFrameAsync(IReadOnlyDictionary<string, bool> input)
    {
        if (draftRequiresRestart)
            throw new InvalidOperationException("请先应用／回放草稿，再使用键盘逐帧输入。");
        var previous = MovieText;
        var frame = startupBoot!.NativeCompletedFrames == 0 ? 0 : (await ReadReadyFrameSnapshotAsync()).Frame;
        if (previous != MovieText) throw new InvalidOperationException("序列已改变，请重新按逐帧键。");
        AppendKeyboardFrame(frame, input);
    }

    private void AppendKeyboardFrame(long frame, IReadOnlyDictionary<string, bool> input)
    {
        var movie = GridAny().V2Document ?? throw new InvalidOperationException("键盘逐帧输入需要 Movie v2。");
        if (InputGridEditor.Count(movie) <= frame)
        {
            AppendGridBlankFrames(frame + 1);
            movie = GridAny().V2Document!;
        }
        GridStart = frame.ToString(CultureInfo.InvariantCulture);
        GridCount = "1";
        EditGridDocument(KeyboardFrameInput.WriteFrame(movie, frame, input));
    }
}
