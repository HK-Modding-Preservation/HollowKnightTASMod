using HollowKnightTAS.Companion.Services;
namespace HollowKnightTAS.Companion.ViewModels
{
    public sealed partial class MainViewModel
    {
        internal FsmViewerController CreateFsmViewer() => new(() => SelectedSession?.Client,
            () => IsRestorePresentationFrozen, RequestRuntimeAsync);
    }
}
