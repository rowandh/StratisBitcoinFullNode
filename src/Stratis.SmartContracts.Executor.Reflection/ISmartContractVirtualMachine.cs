using Stratis.SmartContracts.Core.State;

namespace Stratis.SmartContracts.Executor.Reflection
{
    public interface ISmartContractVirtualMachine
    {
        VmExecutionResult Create(IContractStateRepository repository,
            ICreateData createData,
            ISmartContractState contractState,
            string typeName = null);

        VmExecutionResult ExecuteMethod(IContractStateRepository repository,
            ICallData callData,
            ISmartContractState contractState);
    }
}
