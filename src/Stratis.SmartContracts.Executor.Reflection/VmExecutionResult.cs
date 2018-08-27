using System;

namespace Stratis.SmartContracts.Executor.Reflection
{
    public class VmExecutionResult
    {
        public object Result { get; }

        public Exception ExecutionException { get; }

        private VmExecutionResult(object result,
            Exception e = null)
        {
            this.Result = result;
            this.ExecutionException = e;
        }

        public static VmExecutionResult Success(object result)
        {
            return new VmExecutionResult(result);
        }

        public static VmExecutionResult Error(Exception e)
        {
            return new VmExecutionResult(null, e);
        }
    }
}