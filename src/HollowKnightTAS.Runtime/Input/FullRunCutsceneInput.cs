using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;

namespace HollowKnightTAS.Runtime.Input
{
    // Vanilla's generic cutscene input bypasses HeroActions and reads physical
    // keyboard/controller buttons. Use the same recorded action edges as gameplay.
    internal sealed class FullRunCutsceneInput : IDisposable
    {
        private readonly List<ILHook> hooks = new List<ILHook>();

        public FullRunCutsceneInput(Func<bool> pressed)
        {
            try
            {
                foreach (var name in new[] { "CutsceneInput", "StagCutsceneInput", "BetaEndInput" })
                {
                    var method = typeof(InputHandler).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
                        ?? throw new MissingMethodException("InputHandler." + name);
                    hooks.Add(new ILHook(method, il =>
                    {
                        var keyboard = il.Body.Instructions.Where(i => i.MatchCall(typeof(UnityEngine.Input), "get_anyKeyDown")).ToArray();
                        var controller = il.Body.Instructions.Where(i => i.Operand is MethodReference m
                            && m.Name == "get_WasPressed").ToArray();
                        if (keyboard.Length != 1 || controller.Length != 1)
                            throw new InvalidOperationException("Unsupported cutscene input IL: " + name);
                        var cursor = new ILCursor(il);
                        cursor.Goto(keyboard[0], MoveType.Before);
                        cursor.Remove();
                        cursor.EmitDelegate(pressed);
                        cursor.Goto(controller[0], MoveType.Before);
                        cursor.Remove();
                        cursor.Emit(OpCodes.Pop);
                        cursor.Emit(OpCodes.Ldc_I4_0);
                    }));
                }
            }
            catch { Dispose(); throw; }
        }

        public void Dispose()
        {
            foreach (var hook in hooks) hook.Dispose();
            hooks.Clear();
        }
    }
}
