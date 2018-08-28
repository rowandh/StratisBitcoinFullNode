using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.SmartContracts.Core.State;
using Stratis.SmartContracts.Core.State.AccountAbstractionLayer;
using Stratis.SmartContracts.Executor.Reflection.ContractLogging;

namespace Stratis.SmartContracts.Executor.Reflection
{
    /// <summary>
    /// Factory for creating internal transaction executors
    /// </summary>
    public sealed class InternalTransactionExecutorFactory
    {
        private readonly IKeyEncodingStrategy keyEncodingStrategy;
        private readonly ILoggerFactory loggerFactory;
        private readonly Network network;
        private readonly IAddressGenerator addressGenerator;

        public InternalTransactionExecutorFactory(IKeyEncodingStrategy keyEncodingStrategy,
            ILoggerFactory loggerFactory, Network network, IAddressGenerator addressGenerator)
        {
            this.keyEncodingStrategy = keyEncodingStrategy;
            this.loggerFactory = loggerFactory;
            this.network = network;
            this.addressGenerator = addressGenerator;
        }

        public IInternalTransactionExecutor Create(ISmartContractVirtualMachine vm,
            IContractLogHolder contractLogHolder,
            IContractStateRepository stateRepository, 
            List<TransferInfo> internalTransferList,
            ITransactionContext transactionContext)
        {
            return new InternalTransactionExecutor(
                transactionContext,
                vm,
                contractLogHolder,
                stateRepository,
                internalTransferList,
                this.keyEncodingStrategy,
                this.addressGenerator,
                this.loggerFactory,
                this.network, null
            );
        }
    }
}