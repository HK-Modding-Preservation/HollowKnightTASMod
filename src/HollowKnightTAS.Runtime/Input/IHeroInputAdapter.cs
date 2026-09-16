using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using HollowKnightTAS.Core.Input;
using InControl;

namespace HollowKnightTAS.Runtime.Input
{
    public sealed class CapturedHeroInput
    {
        public CapturedHeroInput(
            ulong inputTick,
            TasAction held,
            TasAction pressed,
            TasAction released,
            short axisX,
            short axisY)
        {
            InputTick = inputTick;
            Held = held;
            Pressed = pressed;
            Released = released;
            AxisX = axisX;
            AxisY = axisY;
        }

        public ulong InputTick { get; }
        public TasAction Held { get; }
        public TasAction Pressed { get; }
        public TasAction Released { get; }
        public short AxisX { get; }
        public short AxisY { get; }

        public bool Matches(InputSample expected)
        {
            return MatchesControlSignal(expected)
                   && CommittedEdgesMatch(expected);
        }

        public bool MatchesControlSignal(InputSample expected)
        {
            return Held == expected.Held
                   && AxisX == EffectiveAxisX(expected)
                   && AxisY == EffectiveAxisY(expected);
        }

        public bool CommittedEdgesMatch(InputSample expected)
        {
            return Pressed == expected.Pressed
                   && Released == expected.Released;
        }

        private static short EffectiveAxisX(InputSample sample)
        {
            if (sample.AxisX != 0)
            {
                return sample.AxisX;
            }

            if ((sample.Held & TasAction.Right) != 0)
            {
                return InputSample.AxisScale;
            }

            if ((sample.Held & TasAction.Left) != 0)
            {
                return -InputSample.AxisScale;
            }

            return 0;
        }

        private static short EffectiveAxisY(InputSample sample)
        {
            if (sample.AxisY != 0)
            {
                return sample.AxisY;
            }

            if ((sample.Held & TasAction.Up) != 0)
            {
                return InputSample.AxisScale;
            }

            if ((sample.Held & TasAction.Down) != 0)
            {
                return -InputSample.AxisScale;
            }

            return 0;
        }
    }

    public interface IHeroInputAdapter : IDisposable
    {
        string CandidateId { get; }
        void Attach(HeroActions actions);
        void Prepare(InputSample sample);
        CapturedHeroInput Observe(ulong inputTick);
        BindingRestoreReport DetachAndRestore();
    }

    internal abstract class HeroInputAdapterBase : IHeroInputAdapter
    {
        private static readonly TasAction[] OrderedActions =
        {
            TasAction.Left,
            TasAction.Right,
            TasAction.Up,
            TasAction.Down,
            TasAction.Jump,
            TasAction.Attack,
            TasAction.Dash,
            TasAction.Cast,
            TasAction.QuickCast,
            TasAction.SuperDash,
            TasAction.DreamNail
        };

        private readonly IReadOnlyDictionary<TasAction, TasBindingSource> tasBindings;
        private HeroActions? actions;
        private BindingLease? lease;

        protected HeroInputAdapterBase()
        {
            tasBindings = new ReadOnlyDictionary<TasAction, TasBindingSource>(
                OrderedActions.ToDictionary(
                    action => action,
                    action => new TasBindingSource(action)));
        }

        public abstract string CandidateId { get; }

        internal IReadOnlyList<BindingActionSnapshot> BindingBefore =>
            lease?.Before ?? Array.Empty<BindingActionSnapshot>();

        public void Attach(HeroActions heroActions)
        {
            if (heroActions == null)
            {
                throw new ArgumentNullException(nameof(heroActions));
            }

            if (actions != null)
            {
                throw new InvalidOperationException("Input adapter is already attached.");
            }

            lease = BindingLease.Acquire(
                heroActions,
                tasBindings.ToDictionary(
                    item => item.Key,
                    item => (BindingSource)item.Value));
            actions = heroActions;
        }

        public void Prepare(InputSample sample)
        {
            RequireAttached();
            foreach (var item in tasBindings)
            {
                item.Value.SetValue(sample.GetValue(item.Key));
            }
        }

        public CapturedHeroInput Observe(ulong inputTick)
        {
            var current = RequireAttached();
            return new CapturedHeroInput(
                inputTick,
                ReadBits(current, action => action.IsPressed),
                ReadBits(current, action => action.WasPressed),
                ReadBits(current, action => action.WasReleased),
                Quantize(current.moveVector.X),
                Quantize(current.moveVector.Y));
        }

        internal IReadOnlyDictionary<TasAction, float> ObservePhysical()
        {
            RequireAttached();
            return lease?.ReadOriginalValues(InControl.InputManager.ActiveDevice)
                   ?? new Dictionary<TasAction, float>();
        }

        public BindingRestoreReport DetachAndRestore()
        {
            var report = lease?.Restore()
                         ?? new BindingRestoreReport(
                             false,
                             true,
                             "Adapter was not attached.",
                             Array.Empty<BindingActionSnapshot>(),
                             Array.Empty<BindingActionSnapshot>());
            lease = null;
            actions = null;
            foreach (var source in tasBindings.Values)
            {
                source.SetValue(0f);
            }

            return report;
        }

        public void Dispose()
        {
            DetachAndRestore();
        }

        private HeroActions RequireAttached()
        {
            return actions
                   ?? throw new InvalidOperationException("Input adapter is not attached.");
        }

        private static TasAction ReadBits(
            HeroActions actions,
            Func<PlayerAction, bool> predicate)
        {
            var value = TasAction.None;
            Add(TasAction.Left, actions.left);
            Add(TasAction.Right, actions.right);
            Add(TasAction.Up, actions.up);
            Add(TasAction.Down, actions.down);
            Add(TasAction.Jump, actions.jump);
            Add(TasAction.Attack, actions.attack);
            Add(TasAction.Dash, actions.dash);
            Add(TasAction.Cast, actions.cast);
            Add(TasAction.QuickCast, actions.quickCast);
            Add(TasAction.SuperDash, actions.superDash);
            Add(TasAction.DreamNail, actions.dreamNail);
            return value;

            void Add(TasAction action, PlayerAction playerAction)
            {
                if (predicate(playerAction))
                {
                    value |= action;
                }
            }
        }

        private static short Quantize(float value)
        {
            var clamped = Math.Max(-1f, Math.Min(1f, value));
            return (short)Math.Round(
                clamped * InputSample.AxisScale,
                MidpointRounding.AwayFromZero);
        }
    }
}
