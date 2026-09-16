using System;

namespace HollowKnightTAS.Runtime.Automation.Mutation
{
    public sealed class StateMutationApplication
    {
        private readonly Action rollback;

        public StateMutationApplication(
            string typedDiff,
            Action rollback)
        {
            TypedDiff = typedDiff;
            this.rollback = rollback;
        }

        public string TypedDiff { get; }

        public void Rollback()
        {
            rollback();
        }
    }

    public sealed class StateMutationResult
    {
        public StateMutationResult(
            string beforeSha256,
            string afterSha256,
            string typedDiff)
        {
            BeforeSha256 = beforeSha256;
            AfterSha256 = afterSha256;
            TypedDiff = typedDiff;
        }

        public string BeforeSha256 { get; }
        public string AfterSha256 { get; }
        public string TypedDiff { get; }
    }
}
