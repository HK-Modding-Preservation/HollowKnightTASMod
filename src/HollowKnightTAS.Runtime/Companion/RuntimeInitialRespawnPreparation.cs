using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace HollowKnightTAS.Runtime.Companion
{
    // Request preparation before loading. Respawn itself is only an observation
    // boundary: never delay the native coroutine to manufacture a matching root.
    internal sealed class RuntimeInitialRespawnPreparation : IDisposable
    {
        private bool armed;
        private bool consumed;
        private bool disposed;
        private readonly Action<string> failure;
        public string Error { get; private set; } = string.Empty;
        public int InsertedWaitFrames => 0;
        public int PreparationStartFrame { get; private set; } = -1;
        public int NativeStartFrame { get; private set; } = -1;
        private bool nativeCompleted;
        private Type? controller;
        public string Status => Error.Length != 0 ? "Faulted"
            : disposed ? "Disposed"
            : nativeCompleted ? "NativeCompleted"
            : NativeStartFrame >= 0 ? "ForwardingNative"
            : consumed ? "Calibrating"
            : armed ? "WaitingForRespawn" : "NotArmed";

        internal RuntimeInitialRespawnPreparation(Action<string> failure)
        {
            this.failure = failure;
            On.HeroController.Respawn += OnRespawn;
            On.GameManager.Update += OnGameManagerUpdate;
        }

        internal void Arm()
        {
            if (disposed || consumed)
                throw new InvalidOperationException("Initial respawn preparation is process-scoped and one-shot.");
            if (armed) return;
            armed = true;
            PreparationStartFrame = Time.frameCount;
            TryBeginPreparation();
        }

        private void OnGameManagerUpdate(On.GameManager.orig_Update original, GameManager manager)
        {
            if (armed && !consumed && !disposed) TryBeginPreparation();
            original(manager);
        }

        private void TryBeginPreparation()
        {
            if (controller != null || Error.Length != 0) return;
            try
            {
                controller = AppDomain.CurrentDomain.GetAssemblies()
                    .SingleOrDefault(a => a.GetName().Name == "HollowKnightTAS.ClockPayload")
                    ?.GetType("HollowKnightTAS.ClockPayload.ClockController");
                // Mod initialization can precede the verified payload handoff.
                // Wait through existing updates, never by delaying Respawn.
                if (controller == null) return;
                Invoke(controller, "BeginRecordingClockCalibration");
            }
            catch (Exception exception) { Fail(exception.Message); }
        }

        private IEnumerator OnRespawn(On.HeroController.orig_Respawn original, HeroController hero)
        {
            var preparingThisRespawn = armed && !consumed;
            if (preparingThisRespawn)
            {
                consumed = true;
                try
                {
                    var fault = controller?.GetProperty("DoublePhaseCalibrationFaultCode")?.GetValue(null, null);
                    if (fault is not int code || code != 0)
                        throw new InvalidOperationException("Initial respawn clock preparation fault: " + fault);
                    var ready = controller?.GetProperty("DoublePhaseCalibrationApplied")?.GetValue(null, null) is true
                        && BitConverter.DoubleToInt64Bits(Time.timeAsDouble)
                           == BitConverter.DoubleToInt64Bits(Time.fixedTimeAsDouble);
                    if (!ready && Error.Length == 0)
                        Fail("Clock preparation was not ready at native Respawn; the native coroutine was not delayed.");
                }
                catch (Exception exception) { Fail(exception.Message); }
                finally
                {
                    EndPreparation();
                }
            }
            // Preparation failure invalidates TAS, not the user's normal load.
            // Forward every native yield unchanged; never patch Hero/FSM/body.
            if (preparingThisRespawn) NativeStartFrame = Time.frameCount;
            var routine = original(hero);
            try
            {
                while (routine.MoveNext()) yield return routine.Current;
                if (preparingThisRespawn) nativeCompleted = true;
            }
            finally { (routine as IDisposable)?.Dispose(); }
        }

        private static void Invoke(Type controller, string method)
        {
            var member = controller.GetMethod(method, BindingFlags.Public | BindingFlags.Static)
                ?? throw new InvalidOperationException("ClockPayload does not support " + method + ".");
            member.Invoke(null, null);
        }

        private void Fail(string reason)
        {
            Error = "Initial respawn preparation failed: " + reason;
            failure(Error);
        }

        private void EndPreparation()
        {
            if (controller == null) return;
            var activeController = controller;
            controller = null;
            try { Invoke(activeController, "CancelRecordingClockCalibration"); }
            catch (Exception exception) { Fail(exception.Message); }
        }

        public void Dispose()
        {
            disposed = true;
            EndPreparation();
            On.HeroController.Respawn -= OnRespawn;
            On.GameManager.Update -= OnGameManagerUpdate;
        }
    }
}
