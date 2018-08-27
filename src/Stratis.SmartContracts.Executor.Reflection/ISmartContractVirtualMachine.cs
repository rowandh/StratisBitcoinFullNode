using Stratis.SmartContracts.Core.State;

namespace Stratis.SmartContracts.Executor.Reflection
{
    public interface ISmartContractVirtualMachine
    {
        VmExecutionResult Create(IGasMeter gasMeter,
            IContractStateRepository repository,
            ICreateData createData,
            ISmartContractState contractState,
            string typeName = null);

        VmExecutionResult ExecuteMethod(IGasMeter gasMeter,
            IContractStateRepository repository,
            ICallData callData,
            ISmartContractState contractState);
    }
}
