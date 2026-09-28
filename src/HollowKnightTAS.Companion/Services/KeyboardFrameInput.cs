using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Input;
using HollowKnightTAS.Core.Movie;

namespace HollowKnightTAS.Companion.Services;

public static class KeyboardFrameInput
{
    public static readonly string[] Actions = { "Left", "Right", "Up", "Down", "Submit", "Cancel", "Jump", "Dash", "SuperDash", "DreamNail", "Attack", "Cast", "QuickCast" };

    public static IReadOnlyDictionary<string, bool> Capture(IReadOnlyDictionary<string, string> bindings,
        Key stepKey, Func<int, bool>? isDown = null)
    {
        isDown ??= key => (GetAsyncKeyState(key) & 0x8000) != 0;
        var states = new Dictionary<int, bool>();
        bool Down(int key) => states.TryGetValue(key, out var held) ? held : states[key] = isDown(key);
        var result = new Dictionary<string, bool>();
        foreach (var action in Actions)
        {
            if (!bindings.TryGetValue(action, out var binding))
                throw new InvalidOperationException("等待游戏键位同步后再使用键盘逐帧输入（需要配套 Runtime）。");
            var held = false;
            foreach (var combo in binding.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var active = true;
                foreach (var item in combo.Split(','))
                {
                    var exclude = item.StartsWith('!');
                    var name = exclude ? item.Substring(1) : item;
                    var keys = VirtualKeys(name);
                    if (!exclude && keys.Contains(KeyInterop.VirtualKeyFromKey(stepKey)))
                        throw new InvalidOperationException("逐帧快捷键与游戏键位冲突：" + action + "。请修改逐帧快捷键。");
                    active &= exclude ? !keys.Any(Down) : keys.Any(Down);
                }
                held |= active;
            }
            result[action] = held;
        }
        return result;
    }

    public static MovieV2Document WriteFrame(MovieV2Document movie, long frame, IReadOnlyDictionary<string, bool> states)
    {
        foreach (var action in Actions) movie = MovieV2RangeEditor.Paint(movie, frame, 1, action, states[action]);
        return movie;
    }

    private static int[] VirtualKeys(string name)
    {
        if (name == "Shift") return new[] { 0xA0, 0xA1 };
        if (name == "Control") return new[] { 0xA2, 0xA3 };
        if (name == "Alt") return new[] { 0xA4, 0xA5 };
        if (name == "Command") return new[] { 0x5B, 0x5C };
        var mapped = name switch
        {
            "LeftControl" => "LeftCtrl", "RightControl" => "RightCtrl",
            "LeftCommand" => "LWin", "RightCommand" => "RWin", "AltGr" => "RightAlt",
            "LeftArrow" => "Left", "RightArrow" => "Right", "UpArrow" => "Up", "DownArrow" => "Down",
            "Backquote" => "Oem3", "Minus" => "OemMinus", "Equals" => "OemPlus",
            "LeftBracket" => "OemOpenBrackets", "RightBracket" => "OemCloseBrackets",
            "Backslash" => "Oem5", "Semicolon" => "Oem1", "Quote" => "Oem7",
            "Comma" => "OemComma", "Period" => "OemPeriod", "Slash" => "Oem2",
            "Backspace" => "Back", "PadDivide" => "Divide", "PadMultiply" => "Multiply",
            "PadMinus" => "Subtract", "PadPlus" => "Add", "PadPeriod" => "Decimal", "PadEnter" => "Return",
            _ when name.Length == 4 && name.StartsWith("Key") && char.IsDigit(name[3]) => "D" + name[3],
            _ when name.Length == 4 && name.StartsWith("Pad") && char.IsDigit(name[3]) => "NumPad" + name[3],
            _ => name
        };
        if (!Enum.TryParse<Key>(mapped, true, out var key) || key == Key.None || !Enum.IsDefined(key))
            throw new InvalidOperationException("键盘逐帧输入暂不支持游戏键位：" + name);
        return new[] { KeyInterop.VirtualKeyFromKey(key) };
    }

    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
}
