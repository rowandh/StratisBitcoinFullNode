using Stratis.SmartContracts.Core.State;

namespace Stratis.SmartContracts.Executor.Reflection
{
    public interface ISmartContractVirtualMachine
    {
        VmExecutionResult Create(IContractStateRepository repository,
            MethodCall methodCall,
            ISmartContractState contractState,
            byte[] contractCode,
            string typeName = null);

        VmExecutionResult ExecuteMethod(IContractStateRepository repository,
            MethodCall methodCall,
            ISmartContractState contractState, byte[] contractCode, string typeName);
    }
}
