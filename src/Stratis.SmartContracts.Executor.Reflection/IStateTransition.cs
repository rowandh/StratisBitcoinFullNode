using NBitcoin;

namespace Stratis.SmartContracts.Executor.Reflection
{
    public interface IStateTransition
    {
        (VmExecutionResult, GasMeter, uint160 address) Apply(ExternalCreateMessage message);
        (VmExecutionResult, GasMeter, uint160 address) Apply(InternalCreateMessage message);
        (VmExecutionResult, GasMeter, uint160 address) Apply(CallMessage message);
    }
}