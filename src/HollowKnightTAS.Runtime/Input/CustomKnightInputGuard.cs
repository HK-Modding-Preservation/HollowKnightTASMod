using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using InControl;

namespace HollowKnightTAS.Runtime.Input
{
    // Custom Knight 3.5 registers this action set even with both hotkeys unbound.
    // Retain normal InControl updates (including release edges), but supply neutral
    // bindings for the lifetime of the TAS session. Do not persist any CK settings.
    internal sealed class CustomKnightInputGuard : IDisposable
    {
        private readonly Dictionary<PlayerActionSet, BindingSource[][]> originals
            = new Dictionary<PlayerActionSet, BindingSource[][]>();

        public bool TryPrepare(PlayerActionSet set)
        {
            var type = set.GetType();
            if (type.FullName != "CustomKnight.KeyBinds"
                || type.Assembly.GetName().Name != "CustomKnight") return false;
            if (originals.ContainsKey(set)) return true;

            var actions = set.Actions.ToArray();
            if (actions.Length != 2 || actions[0].Name != "OpenSkinList"
                || actions[1].Name != "ReloadSkins")
                throw new InvalidDataException("CustomKnight hotkey layout changed; expected OpenSkinList and ReloadSkins.");

            var saved = actions.Select(action => action.UnfilteredBindings.ToArray()).ToArray();
            originals.Add(set, saved);
            try
            {
                foreach (var action in actions)
                {
                    action.ClearBindings();
                    if (!action.AddBinding(new NeutralBinding()))
                        throw new InvalidOperationException("CustomKnight neutral binding was rejected.");
                }
                Modding.Logger.Log("[HollowKnightTAS] CustomKnight hotkeys neutralized for TAS session.");
            }
            catch
            {
                Restore(set, saved);
                originals.Remove(set);
                throw;
            }
            return true;
        }

        public void Dispose()
        {
            var errors = new List<string>();
            foreach (var item in originals)
                try { Restore(item.Key, item.Value); }
                catch (Exception exception) { errors.Add(exception.Message); }
            originals.Clear();
            if (errors.Count != 0)
                throw new InvalidOperationException("CustomKnight bindings were not restored: " + string.Join("; ", errors));
        }

        private static void Restore(PlayerActionSet set, BindingSource[][] saved)
        {
            for (var index = 0; index < saved.Length; index++)
            {
                var action = set.Actions[index];
                action.ClearBindings();
                foreach (var binding in saved[index])
                    if (!action.AddBinding(binding))
                        throw new InvalidOperationException("CustomKnight original binding was rejected.");
            }
        }

        private sealed class NeutralBinding : BindingSource
        {
            public override string Name => "HKTAS CustomKnight neutral";
            public override string DeviceName => "HollowKnightTAS";
            public override InputDeviceClass DeviceClass => InputDeviceClass.Unknown;
            public override InputDeviceStyle DeviceStyle => InputDeviceStyle.Unknown;
            public override BindingSourceType BindingSourceType => BindingSourceType.UnknownDeviceBindingSource;
            public override float GetValue(InputDevice device) => 0f;
            public override bool GetState(InputDevice device) => false;
            public override bool Equals(BindingSource other) => ReferenceEquals(this, other);
            public override int GetHashCode() => base.GetHashCode();
            public override void Save(BinaryWriter writer) { }
            public override void Load(BinaryReader reader, ushort version) { }
        }
    }
}
