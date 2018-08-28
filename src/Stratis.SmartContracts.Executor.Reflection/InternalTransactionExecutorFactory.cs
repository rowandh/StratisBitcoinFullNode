using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.SmartContracts.Core.State;

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

        public InternalTransactionExecutorFactory(ILoggerFactory loggerFactory, Network network,
            IAddressGenerator addressGenerator)
        {
            this.loggerFactory = loggerFactory;
            this.network = network;
            this.addressGenerator = addressGenerator;
        }

        public IInternalTransactionExecutor Create(ISmartContractVirtualMachine vm,
            IState baseState)
        {
            return new InternalTransactionExecutor(
                this,
                vm,
                this.addressGenerator,
                this.loggerFactory,
                this.network,
                baseState
            );
        }
    }
}