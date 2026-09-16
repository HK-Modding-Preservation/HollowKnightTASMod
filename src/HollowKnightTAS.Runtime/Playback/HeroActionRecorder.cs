using System;
using HollowKnightTAS.Core.Input;
using InControl;

namespace HollowKnightTAS.Runtime.Playback
{
    public sealed class HeroActionRecorder
    {
        private TasAction previousHeld;

        public InputSample Capture(HeroActions actions, ulong inputTick)
        {
            if (actions == null)
            {
                throw new ArgumentNullException(nameof(actions));
            }

            var held = TasAction.None;
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

            var axisX = Quantize(actions.moveVector.X);
            var axisY = Quantize(actions.moveVector.Y);
            if (axisX == InputSample.AxisScale
                || axisX == -InputSample.AxisScale)
            {
                axisX = 0;
            }
            else if (axisX != 0)
            {
                held &= ~(TasAction.Left | TasAction.Right);
            }

            if (axisY == InputSample.AxisScale
                || axisY == -InputSample.AxisScale)
            {
                axisY = 0;
            }
            else if (axisY != 0)
            {
                held &= ~(TasAction.Up | TasAction.Down);
            }

            var sample = InputSample.FromHeld(
                inputTick,
                held,
                previousHeld,
                axisX,
                axisY);
            previousHeld = held;
            return sample;

            void Add(TasAction action, PlayerAction playerAction)
            {
                if (playerAction.IsPressed)
                {
                    held |= action;
                }
            }
        }

        public void Reset(TasAction initialHeld = TasAction.None)
        {
            previousHeld = initialHeld;
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
