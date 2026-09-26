using System;
using System.Collections.Generic;
using System.Linq;
using HutongGames.PlayMaker;

namespace HollowKnightTAS.Runtime.Observation
{
    internal sealed class ObservationFsm
    {
        private readonly ObservationValues values;
        internal ObservationFsm(ObservationValues values) { this.values = values; }

        internal object Capture(PlayMakerFSM component, int componentIndex, bool details)
        {
            // PlayMakerFSM.Fsm assigns Owner and FsmState.Actions can load actions.
            // Read only the existing backing fields to avoid either mutation.
            var fsm = values.Read(component, "fsm") as Fsm;
            if (fsm == null) return ObservationData.Map("componentIndex", componentIndex, "omitted", true, "reason", "fsmNotInitialized");
            var variableContainer = values.Read(fsm, "variables");
            var variables = new List<object?>();
            if (variableContainer != null)
            {
                foreach (var field in values.Fields(variableContainer.GetType()))
                {
                    if (!field.FieldType.IsArray || !typeof(NamedVariable).IsAssignableFrom(field.FieldType.GetElementType()!)) continue;
                    var array = field.GetValue(variableContainer) as Array;
                    if (array == null) continue;
                    foreach (var variable in array) variables.Add(values.Encode(variable));
                }
            }
            var result = ObservationData.Map("componentIndex", componentIndex, "name", values.Read(fsm, "name"),
                "enabled", component.enabled, "activeInHierarchy", component.gameObject.activeInHierarchy,
                "activeState", values.Read(fsm, "activeStateName"), "initialized", values.Read(fsm, "initialized"),
                "variables", variables, "variableCount", variables.Count, "variablesAvailable", variableContainer != null,
                "variablesOmittedReason", variableContainer == null ? "variableContainerNotInitialized" : null);
            if (!details) return result;
            var states = values.Read(fsm, "states") as FsmState[] ?? Array.Empty<FsmState>();
            var active = values.Read(fsm, "activeState") as FsmState;
            result["startState"] = values.Read(fsm, "startState");
            result["globalTransitions"] = Transitions(values.Read(fsm, "globalTransitions") as FsmTransition[]);
            result["states"] = states.Select(state => ObservationData.Map("name", values.Read(state, "name"),
                "transitions", Transitions(values.Read(state, "transitions") as FsmTransition[]),
                "actionsLoaded", values.Read(state, "actions") != null,
                "actionTypes", (values.Read(state, "actions") as FsmStateAction[])?.Select(action => action?.GetType().FullName).ToArray())).ToArray();
            if (active == null) result["currentState"] = null;
            else
            {
                var actions = values.Read(active, "actions") as FsmStateAction[];
                result["currentState"] = ObservationData.Map("name", values.Read(active, "name"),
                    "stateTime", values.Read(active, "<StateTime>k__BackingField"),
                    "realStartTime", values.Read(active, "<RealStartTime>k__BackingField"),
                    "activeActionIndex", values.Read(active, "activeActionIndex"),
                    "actions", actions == null ? ObservationData.Omitted("actionsNotInitialized")
                        : (object)actions.Select((action, index) => action == null ? ObservationData.Map("index", index, "value", null)
                            : ObservationData.Map("index", index, "type", action.GetType().FullName, "data", values.CaptureFields(action))).ToArray());
            }
            return result;
        }

        private object[] Transitions(FsmTransition[]? transitions)
        {
            return (transitions ?? Array.Empty<FsmTransition>()).Select(transition => ObservationData.Map(
                "event", values.Read(values.Read(transition, "fsmEvent"), "name"), "toState", values.Read(transition, "toState"))).Cast<object>().ToArray();
        }
    }
}
