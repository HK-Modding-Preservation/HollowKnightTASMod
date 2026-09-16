using System;
using System.IO;
using HollowKnightTAS.Core.Input;
using InControl;

namespace HollowKnightTAS.Runtime.Input
{
    public sealed class TasBindingSource : BindingSource
    {
        private TasAction action;
        private float value;

        public TasBindingSource(TasAction action)
        {
            this.action = action;
        }

        public TasAction Action => action;
        public float CurrentValue => value;

        public override string Name => "HKTAS " + action;
        public override string DeviceName => "HollowKnightTAS";
        public override InputDeviceClass DeviceClass => InputDeviceClass.Unknown;
        public override InputDeviceStyle DeviceStyle => InputDeviceStyle.Unknown;
        public override BindingSourceType BindingSourceType =>
            BindingSourceType.UnknownDeviceBindingSource;

        public void SetValue(float newValue)
        {
            if (float.IsNaN(newValue)
                || float.IsInfinity(newValue)
                || newValue < 0f
                || newValue > 1f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(newValue),
                    newValue,
                    "TAS binding values must be finite and in [0, 1].");
            }

            value = newValue;
        }

        public override float GetValue(InputDevice inputDevice)
        {
            return value;
        }

        public override bool GetState(InputDevice inputDevice)
        {
            return value > 0.5f;
        }

        public override bool Equals(BindingSource other)
        {
            return ReferenceEquals(this, other);
        }

        public override int GetHashCode()
        {
            return base.GetHashCode();
        }

        public override void Save(BinaryWriter writer)
        {
            if (writer == null)
            {
                throw new ArgumentNullException(nameof(writer));
            }

            writer.Write((ushort)action);
        }

        public override void Load(BinaryReader reader, ushort dataFormatVersion)
        {
            if (reader == null)
            {
                throw new ArgumentNullException(nameof(reader));
            }

            action = (TasAction)reader.ReadUInt16();
            value = 0f;
        }
    }
}
