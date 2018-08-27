using Stratis.SmartContracts.Core.State;

namespace Stratis.SmartContracts.Executor.Reflection
{
    public interface ISmartContractVirtualMachine
    {
        VmExecutionResult Create(IGasMeter gasMeter,
            IContractStateRepository repository,
            ICreateData createData,
            ITransactionContext transactionContext,
            ISmartContractState contractState,
            string typeName = null);

        VmExecutionResult ExecuteMethod(IGasMeter gasMeter,
            IContractStateRepository repository,
            ICallData callData,
            ITransactionContext transactionContext,
            ISmartContractState contractState);
    }
}
