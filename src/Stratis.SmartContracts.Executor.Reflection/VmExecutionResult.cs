using System;

namespace Stratis.SmartContracts.Executor.Reflection
{
    public class VmExecutionResult
    {
        public Gas GasConsumed { get; }

        public object Result { get; }

        public Exception ExecutionException { get; }

        private VmExecutionResult(Gas gasConsumed,
            object result,
            Exception e = null)
        {
            this.GasConsumed = gasConsumed;
            this.Result = result;
            this.ExecutionException = e;
        }

        public static VmExecutionResult Success(Gas gasConsumed, object result)
        {
            return new VmExecutionResult(gasConsumed, result);
        }

        public static VmExecutionResult Error(Gas gasConsumed, Exception e)
        {
            return new VmExecutionResult(gasConsumed, null, e);
        }
    }
}