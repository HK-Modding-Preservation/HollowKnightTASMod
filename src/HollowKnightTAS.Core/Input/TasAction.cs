using System;

namespace HollowKnightTAS.Core.Input
{
    [Flags]
    public enum TasAction : ushort
    {
        None = 0,
        Left = 1 << 0,
        Right = 1 << 1,
        Up = 1 << 2,
        Down = 1 << 3,
        Jump = 1 << 4,
        Attack = 1 << 5,
        Dash = 1 << 6,
        Cast = 1 << 7,
        QuickCast = 1 << 8,
        SuperDash = 1 << 9,
        DreamNail = 1 << 10,
        AllGameplay = Left
                      | Right
                      | Up
                      | Down
                      | Jump
                      | Attack
                      | Dash
                      | Cast
                      | QuickCast
                      | SuperDash
                      | DreamNail
    }
}
