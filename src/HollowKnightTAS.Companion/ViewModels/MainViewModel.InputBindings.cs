using System;
using System.Collections.Generic;
using System.Linq;

namespace HollowKnightTAS.Companion.ViewModels
{
    public partial class MainViewModel
    {
        public IReadOnlyDictionary<string, string> InputBindingLabels { get; private set; }
            = new Dictionary<string, string>();

        private void UpdateInputBindingLabels(IReadOnlyDictionary<string, string> fields)
        {
            if (IsRestorePresentationFrozen) return;
            keyboardBindings = fields.Where(pair => pair.Key.StartsWith("inputKeyboard.", StringComparison.Ordinal))
                .ToDictionary(pair => pair.Key.Substring("inputKeyboard.".Length), pair => pair.Value);
            const string prefix = "inputBinding.";
            var labels = fields.Where(pair => pair.Key.StartsWith(prefix, StringComparison.Ordinal))
                .ToDictionary(pair => pair.Key.Substring(prefix.Length), pair => pair.Value);
            if (labels.Count == InputBindingLabels.Count
                && labels.All(pair => InputBindingLabels.TryGetValue(pair.Key, out var old) && old == pair.Value)) return;
            InputBindingLabels = labels;
            OnPropertyChanged(nameof(InputBindingLabels));
        }
    }
}
