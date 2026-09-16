using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using HollowKnightTAS.Core.Input;
using InControl;

namespace HollowKnightTAS.Runtime.Input
{
    public sealed class BindingDescriptor
    {
        public BindingDescriptor(int index, BindingSource binding)
        {
            Index = index;
            Name = binding.Name ?? string.Empty;
            SourceType = binding.BindingSourceType.ToString();
            DeviceClass = binding.DeviceClass.ToString();
            DeviceStyle = binding.DeviceStyle.ToString();
        }

        public int Index { get; }
        public string Name { get; }
        public string SourceType { get; }
        public string DeviceClass { get; }
        public string DeviceStyle { get; }
    }

    public sealed class BindingActionSnapshot
    {
        public BindingActionSnapshot(
            TasAction action,
            string actionName,
            IEnumerable<BindingDescriptor> bindings)
        {
            Action = action;
            ActionName = actionName;
            Bindings = new ReadOnlyCollection<BindingDescriptor>(bindings.ToList());
        }

        public TasAction Action { get; }
        public string ActionName { get; }
        public IReadOnlyList<BindingDescriptor> Bindings { get; }
    }

    public sealed class BindingRestoreReport
    {
        public BindingRestoreReport(
            bool attempted,
            bool equivalent,
            string message,
            IEnumerable<BindingActionSnapshot> before,
            IEnumerable<BindingActionSnapshot> after)
        {
            Attempted = attempted;
            Equivalent = equivalent;
            Message = message;
            Before = new ReadOnlyCollection<BindingActionSnapshot>(before.ToList());
            After = new ReadOnlyCollection<BindingActionSnapshot>(after.ToList());
        }

        public bool Attempted { get; }
        public bool Equivalent { get; }
        public string Message { get; }
        public IReadOnlyList<BindingActionSnapshot> Before { get; }
        public IReadOnlyList<BindingActionSnapshot> After { get; }
    }

    public sealed class BindingLease : IDisposable
    {
        private readonly IReadOnlyList<ActionSlot> slots;
        private readonly IReadOnlyDictionary<TasAction, BindingSource[]> originals;
        private readonly IReadOnlyList<BindingActionSnapshot> before;
        private BindingRestoreReport? restoreReport;
        private bool acquired;

        private BindingLease(
            IReadOnlyList<ActionSlot> slots,
            IReadOnlyDictionary<TasAction, BindingSource[]> originals,
            IReadOnlyList<BindingActionSnapshot> before)
        {
            this.slots = slots;
            this.originals = originals;
            this.before = before;
        }

        public IReadOnlyList<BindingActionSnapshot> Before => before;

        public static BindingLease Acquire(
            HeroActions actions,
            IReadOnlyDictionary<TasAction, BindingSource> tasBindings)
        {
            if (actions == null)
            {
                throw new ArgumentNullException(nameof(actions));
            }

            if (tasBindings == null)
            {
                throw new ArgumentNullException(nameof(tasBindings));
            }

            var slots = CreateSlots(actions);
            var originals = new Dictionary<TasAction, BindingSource[]>();
            foreach (var slot in slots)
            {
                if (!tasBindings.TryGetValue(slot.Action, out var tasBinding)
                    || tasBinding == null)
                {
                    throw new ArgumentException(
                        "Missing TAS binding for " + slot.Action + ".",
                        nameof(tasBindings));
                }

                originals.Add(slot.Action, slot.PlayerAction.UnfilteredBindings.ToArray());
            }

            var before = Capture(slots);
            var lease = new BindingLease(slots, originals, before);
            try
            {
                foreach (var slot in slots)
                {
                    slot.PlayerAction.ClearBindings();
                    if (!slot.PlayerAction.AddBinding(tasBindings[slot.Action]))
                    {
                        throw new InvalidOperationException(
                            "Failed to attach TAS binding for " + slot.Action + ".");
                    }
                }

                lease.acquired = true;
                return lease;
            }
            catch (Exception attachException)
            {
                lease.acquired = true;
                var restored = lease.Restore();
                if (!restored.Equivalent)
                {
                    throw new InvalidOperationException(
                        "TAS binding attachment failed and original bindings could not be restored: "
                        + restored.Message,
                        attachException);
                }
                throw;
            }
        }

        public IReadOnlyDictionary<TasAction, float> ReadOriginalValues(InputDevice device)
        {
            var result = new SortedDictionary<TasAction, float>();
            foreach (var slot in slots)
            {
                var strongest = 0f;
                foreach (var binding in originals[slot.Action])
                {
                    float value;
                    try
                    {
                        value = Math.Abs(binding.GetValue(device));
                    }
                    catch
                    {
                        value = 0f;
                    }

                    if (value > strongest)
                    {
                        strongest = value;
                    }
                }

                result[slot.Action] = strongest;
            }

            return result;
        }

        public BindingRestoreReport Restore()
        {
            if (restoreReport != null)
            {
                return restoreReport;
            }

            if (!acquired)
            {
                restoreReport = new BindingRestoreReport(
                    false,
                    true,
                    "Lease was never acquired.",
                    before,
                    Capture(slots));
                return restoreReport;
            }

            var errors = new List<string>();
            foreach (var slot in slots)
            {
                try
                {
                    slot.PlayerAction.ClearBindings();
                    foreach (var binding in originals[slot.Action])
                    {
                        if (!slot.PlayerAction.AddBinding(binding))
                        {
                            errors.Add(
                                slot.Action + ": AddBinding returned false for " + binding.Name);
                        }
                    }
                }
                catch (Exception exception)
                {
                    errors.Add(slot.Action + ": " + exception.GetType().Name);
                }
            }

            var equivalent = errors.Count == 0 && ReferencesAndOrderMatch();
            if (!equivalent && errors.Count == 0)
            {
                errors.Add("Binding references or order differ from the attach snapshot.");
            }

            restoreReport = new BindingRestoreReport(
                true,
                equivalent,
                equivalent ? "Equivalent" : string.Join("; ", errors),
                before,
                Capture(slots));
            return restoreReport;
        }

        public void Dispose()
        {
            Restore();
        }

        private bool ReferencesAndOrderMatch()
        {
            foreach (var slot in slots)
            {
                var expected = originals[slot.Action];
                var actual = slot.PlayerAction.UnfilteredBindings;
                if (expected.Length != actual.Count)
                {
                    return false;
                }

                for (var index = 0; index < expected.Length; index++)
                {
                    if (!ReferenceEquals(expected[index], actual[index]))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static IReadOnlyList<BindingActionSnapshot> Capture(
            IEnumerable<ActionSlot> slots)
        {
            return new ReadOnlyCollection<BindingActionSnapshot>(
                slots.Select(
                        slot => new BindingActionSnapshot(
                            slot.Action,
                            slot.PlayerAction.Name,
                            slot.PlayerAction.UnfilteredBindings.Select(
                                (binding, index) => new BindingDescriptor(index, binding))))
                    .ToList());
        }

        private static IReadOnlyList<ActionSlot> CreateSlots(HeroActions actions)
        {
            return new ReadOnlyCollection<ActionSlot>(
                new[]
                {
                    new ActionSlot(TasAction.Left, actions.left),
                    new ActionSlot(TasAction.Right, actions.right),
                    new ActionSlot(TasAction.Up, actions.up),
                    new ActionSlot(TasAction.Down, actions.down),
                    new ActionSlot(TasAction.Jump, actions.jump),
                    new ActionSlot(TasAction.Attack, actions.attack),
                    new ActionSlot(TasAction.Dash, actions.dash),
                    new ActionSlot(TasAction.Cast, actions.cast),
                    new ActionSlot(TasAction.QuickCast, actions.quickCast),
                    new ActionSlot(TasAction.SuperDash, actions.superDash),
                    new ActionSlot(TasAction.DreamNail, actions.dreamNail)
                });
        }

        private sealed class ActionSlot
        {
            public ActionSlot(TasAction action, PlayerAction playerAction)
            {
                Action = action;
                PlayerAction = playerAction;
            }

            public TasAction Action { get; }
            public PlayerAction PlayerAction { get; }
        }
    }
}
