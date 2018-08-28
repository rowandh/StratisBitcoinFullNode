using Microsoft.Extensions.Logging;
using NBitcoin;

namespace Stratis.SmartContracts.Executor.Reflection
{
    /// <summary>
    /// Factory for creating internal transaction executors
    /// </summary>
    public sealed class InternalTransactionExecutorFactory
    {
        private readonly ILoggerFactory loggerFactory;
        private readonly Network network;
        private readonly IAddressGenerator addressGenerator;
        private readonly ISmartContractVirtualMachine vm;

        public InternalTransactionExecutorFactory(
            ILoggerFactory loggerFactory,
            Network network,
            IAddressGenerator addressGenerator,
            ISmartContractVirtualMachine vm)
        {
            this.loggerFactory = loggerFactory;
            this.network = network;
            this.addressGenerator = addressGenerator;
            this.vm = vm;
        }

        public IInternalTransactionExecutor Create(IState baseState)
        {
            return new InternalTransactionExecutor(
                this,
                this.vm,
                this.addressGenerator,
                this.loggerFactory,
                this.network,
                baseState
            );
        }
    }
}