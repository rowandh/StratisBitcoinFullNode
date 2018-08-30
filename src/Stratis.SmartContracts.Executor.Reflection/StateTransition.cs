using NBitcoin;

namespace Stratis.SmartContracts.Executor.Reflection
{
    public class StateTransitionResult
    {
        public VmExecutionResult VmExecutionResult { get; set; }

        public Gas GasConsumed { get; set; }

        public uint160 ContractAddress { get; set; }

        public bool Success { get; set; }
    }
}