using System;

namespace HollowKnightTAS.Core.Control
{
    public sealed class ControlResult
    {
        public ControlResult(
            bool success,
            SimulationControlMode mode,
            string error)
        {
            Success = success;
            Mode = mode;
            Error = error ?? string.Empty;
        }

        public bool Success { get; }
        public SimulationControlMode Mode { get; }
        public string Error { get; }
    }

    public sealed class SimulationControlStateMachine
    {
        private StepRequest? activeRequest;
        private int committedMovieTicks;
        private int committedVisualUpdates;

        public SimulationControlMode Mode { get; private set; } =
            SimulationControlMode.Running;
        public StepRequest? ActiveRequest => activeRequest;
        public int CommittedMovieTicks => committedMovieTicks;
        public int CommittedVisualUpdates => committedVisualUpdates;
        public string FaultMessage { get; private set; } = string.Empty;

        public bool MovieTickGateOpen
        {
            get
            {
                if (Mode == SimulationControlMode.Running)
                {
                    return true;
                }

                if (Mode != SimulationControlMode.Stepping
                    || activeRequest == null)
                {
                    return false;
                }

                var request = activeRequest.Value;
                return request.Boundary == StepBoundary.MovieTick
                       && committedMovieTicks < request.Count;
            }
        }

        public bool StepQuotaSatisfied
        {
            get
            {
                if (Mode != SimulationControlMode.Stepping
                    || activeRequest == null)
                {
                    return false;
                }

                var request = activeRequest.Value;
                return request.Boundary == StepBoundary.MovieTick
                    ? committedMovieTicks == request.Count
                    : committedVisualUpdates == request.Count;
            }
        }

        public ControlResult RequestPause()
        {
            if (Mode != SimulationControlMode.Running)
            {
                return Failure("Only Running can enter Pausing.");
            }

            Mode = SimulationControlMode.Pausing;
            return Success();
        }

        public ControlResult CompletePause()
        {
            if (Mode != SimulationControlMode.Pausing)
            {
                return Failure("Only Pausing can enter Paused.");
            }

            Mode = SimulationControlMode.Paused;
            return Success();
        }

        public ControlResult RequestStep(StepRequest request)
        {
            if (Mode != SimulationControlMode.Paused)
            {
                return Failure("Only Paused can enter Stepping.");
            }

            activeRequest = request;
            committedMovieTicks = 0;
            committedVisualUpdates = 0;
            Mode = SimulationControlMode.Stepping;
            return Success();
        }

        public ControlResult CommitMovieTick()
        {
            if (Mode != SimulationControlMode.Stepping
                || activeRequest == null
                || activeRequest.Value.Boundary != StepBoundary.MovieTick)
            {
                return Failure(
                    "A movie tick can only commit during a MovieTick step.");
            }

            if (committedMovieTicks >= activeRequest.Value.Count)
            {
                return Failure("The active MovieTick step quota is already full.");
            }

            committedMovieTicks++;
            return Success();
        }

        public ControlResult CommitVisualUpdate()
        {
            if (Mode != SimulationControlMode.Stepping
                || activeRequest == null
                || activeRequest.Value.Boundary != StepBoundary.VisualUpdate)
            {
                return Failure(
                    "A visual update can only commit during a VisualUpdate step.");
            }

            if (committedVisualUpdates >= activeRequest.Value.Count)
            {
                return Failure(
                    "The active VisualUpdate step quota is already full.");
            }

            committedVisualUpdates++;
            return Success();
        }

        public ControlResult CompleteStep(StepResult result)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            if (Mode != SimulationControlMode.Stepping
                || activeRequest == null)
            {
                return Failure("Only Stepping can complete a step.");
            }

            var request = activeRequest.Value;
            if (!request.Equals(result.Request))
            {
                return Failure("Step result does not match the active request.");
            }

            if (!StepQuotaSatisfied)
            {
                return Failure("The active step quota is incomplete.");
            }

            if (request.Boundary == StepBoundary.MovieTick
                && result.MovieTickDelta != request.Count)
            {
                return Failure(
                    "MovieTick result delta does not match the request.");
            }

            if (request.Boundary == StepBoundary.VisualUpdate
                && result.VisualTickDelta != request.Count)
            {
                return Failure(
                    "VisualUpdate result delta does not match the request.");
            }

            activeRequest = null;
            committedMovieTicks = 0;
            committedVisualUpdates = 0;
            Mode = SimulationControlMode.Paused;
            return Success();
        }

        public ControlResult InterruptStepAtCompletedBoundary()
        {
            if (Mode != SimulationControlMode.Stepping || activeRequest == null)
                return Failure("Only Stepping can be interrupted at a completed boundary.");
            // The caller owns the completed-frame boundary and must record the
            // counters before this transition. Never pretend the quota finished.
            activeRequest = null;
            committedMovieTicks = 0;
            committedVisualUpdates = 0;
            Mode = SimulationControlMode.Paused;
            return Success();
        }

        public ControlResult RequestRestore()
        {
            if (Mode == SimulationControlMode.Running)
            {
                return Failure("Simulation control is already Running.");
            }

            if (Mode == SimulationControlMode.Restoring)
            {
                return Failure("Simulation control is already Restoring.");
            }

            activeRequest = null;
            committedMovieTicks = 0;
            committedVisualUpdates = 0;
            Mode = SimulationControlMode.Restoring;
            return Success();
        }

        public ControlResult CompleteRestore()
        {
            if (Mode != SimulationControlMode.Restoring)
            {
                return Failure("Only Restoring can return to Running.");
            }

            FaultMessage = string.Empty;
            Mode = SimulationControlMode.Running;
            return Success();
        }

        public ControlResult Fault(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                throw new ArgumentException(
                    "A non-empty fault message is required.",
                    nameof(message));
            }

            activeRequest = null;
            committedMovieTicks = 0;
            committedVisualUpdates = 0;
            FaultMessage = message;
            Mode = SimulationControlMode.Faulted;
            return Success();
        }

        private ControlResult Success()
        {
            return new ControlResult(true, Mode, string.Empty);
        }

        private ControlResult Failure(string error)
        {
            return new ControlResult(false, Mode, error);
        }
    }
}
