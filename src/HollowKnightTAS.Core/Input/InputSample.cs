using System;

namespace HollowKnightTAS.Core.Input
{
    public readonly struct InputSample : IEquatable<InputSample>
    {
        public const short AxisScale = 10000;

        public InputSample(
            ulong inputTick,
            TasAction held,
            TasAction pressed,
            TasAction released,
            short axisX,
            short axisY)
        {
            ValidateActions(held, nameof(held));
            ValidateActions(pressed, nameof(pressed));
            ValidateActions(released, nameof(released));
            ValidateAxis(axisX, nameof(axisX));
            ValidateAxis(axisY, nameof(axisY));

            if ((pressed & ~held) != TasAction.None)
            {
                throw new ArgumentException("Pressed actions must also be held.", nameof(pressed));
            }

            if ((released & held) != TasAction.None)
            {
                throw new ArgumentException("Released actions cannot also be held.", nameof(released));
            }

            if ((pressed & released) != TasAction.None)
            {
                throw new ArgumentException(
                    "An action cannot be pressed and released in the same sample.");
            }

            if (axisX != 0 && (held & (TasAction.Left | TasAction.Right)) != TasAction.None)
            {
                throw new ArgumentException(
                    "Digital left/right and AxisX cannot appear in the same sample.");
            }

            if (axisY != 0 && (held & (TasAction.Up | TasAction.Down)) != TasAction.None)
            {
                throw new ArgumentException(
                    "Digital up/down and AxisY cannot appear in the same sample.");
            }

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

        public static InputSample FromHeld(
            ulong inputTick,
            TasAction held,
            TasAction previousHeld,
            short axisX = 0,
            short axisY = 0)
        {
            return new InputSample(
                inputTick,
                held,
                held & ~previousHeld,
                previousHeld & ~held,
                axisX,
                axisY);
        }

        public float GetValue(TasAction action)
        {
            switch (action)
            {
                case TasAction.Left:
                    return AxisX < 0
                        ? -AxisX / (float)AxisScale
                        : IsHeld(action) ? 1f : 0f;
                case TasAction.Right:
                    return AxisX > 0
                        ? AxisX / (float)AxisScale
                        : IsHeld(action) ? 1f : 0f;
                case TasAction.Down:
                    return AxisY < 0
                        ? -AxisY / (float)AxisScale
                        : IsHeld(action) ? 1f : 0f;
                case TasAction.Up:
                    return AxisY > 0
                        ? AxisY / (float)AxisScale
                        : IsHeld(action) ? 1f : 0f;
                default:
                    ValidateSingleAction(action);
                    return IsHeld(action) ? 1f : 0f;
            }
        }

        public bool IsHeld(TasAction action)
        {
            ValidateSingleAction(action);
            return (Held & action) != TasAction.None;
        }

        public bool Equals(InputSample other)
        {
            return InputTick == other.InputTick
                   && Held == other.Held
                   && Pressed == other.Pressed
                   && Released == other.Released
                   && AxisX == other.AxisX
                   && AxisY == other.AxisY;
        }

        public override bool Equals(object? value)
        {
            return value is InputSample other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = InputTick.GetHashCode();
                hash = (hash * 397) ^ (int)Held;
                hash = (hash * 397) ^ (int)Pressed;
                hash = (hash * 397) ^ (int)Released;
                hash = (hash * 397) ^ AxisX;
                hash = (hash * 397) ^ AxisY;
                return hash;
            }
        }

        private static void ValidateAxis(short value, string name)
        {
            if (value < -AxisScale || value > AxisScale)
            {
                throw new ArgumentOutOfRangeException(
                    name,
                    value,
                    "Axis values must be in [-10000, 10000].");
            }
        }

        private static void ValidateActions(TasAction value, string name)
        {
            if ((value & ~TasAction.AllGameplay) != TasAction.None)
            {
                throw new ArgumentOutOfRangeException(name, value, "Unknown TAS action bit.");
            }
        }

        private static void ValidateSingleAction(TasAction action)
        {
            ValidateActions(action, nameof(action));
            var bits = (ushort)action;
            if (bits == 0 || (bits & (bits - 1)) != 0)
            {
                throw new ArgumentException(
                    "Exactly one gameplay action is required.",
                    nameof(action));
            }
        }
    }
}
