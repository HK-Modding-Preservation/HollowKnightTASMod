using System;
using System.Collections.Generic;
using HollowKnightTAS.Core.Movie;

namespace HollowKnightTAS.Companion.Services
{
    // Selection history only; changing a selected ID must never mutate Runtime.
    public sealed class LifecycleBranchHistory
    {
        private readonly List<string> path = new List<string>();
        private int index = -1;
        public void Record(string parent, string child)
        {
            if (!MovieProtocolV1.IsLowerSha256(parent) || !MovieProtocolV1.IsLowerSha256(child))
                throw new ArgumentException("Branch history requires content-addressed IDs.");
            if (index < 0 || path[index] != parent)
            { path.Clear(); path.Add(parent); index = 0; }
            if (index + 1 < path.Count) path.RemoveRange(index + 1, path.Count - index - 1);
            path.Add(child);
            index++;
            if (path.Count > 101) { path.RemoveAt(0); index--; }
        }

        public bool TryUndo(string selected, out string branch) => Move(selected, -1, out branch);
        public bool TryRedo(string selected, out string branch) => Move(selected, 1, out branch);
        private bool Move(string selected, int direction, out string branch)
        {
            branch = selected;
            if (index < 0 || path[index] != selected)
            { path.Clear(); index = -1; return false; }
            var next = index + direction;
            if (next < 0 || next >= path.Count) return false;
            index = next;
            branch = path[index];
            return true;
        }
    }
}
