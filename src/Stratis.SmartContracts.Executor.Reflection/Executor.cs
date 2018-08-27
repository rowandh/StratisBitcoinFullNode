using System.Collections.Generic;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.SmartContracts.Core;
using Stratis.SmartContracts.Core.State;
using Stratis.SmartContracts.Core.State.AccountAbstractionLayer;
using Stratis.SmartContracts.Executor.Reflection.ContractLogging;
using Stratis.SmartContracts.Executor.Reflection.Serialization;
using Block = Stratis.SmartContracts.Core.Block;

namespace Stratis.SmartContracts.Executor.Reflection
{
    /// <summary>
    /// Deserializes raw contract transaction data, dispatches a call to the VM and commits the result to the state repository
    /// </summary>
    public class Executor : ISmartContractExecutor
    {
        private readonly ILogger logger;
        private readonly IContractPrimitiveSerializer contractPrimitiveSerializer;
        private readonly IContractStateRepository stateSnapshot;
        private readonly ISmartContractResultRefundProcessor refundProcessor;
        private readonly ISmartContractResultTransferProcessor transferProcessor;
        private readonly ISmartContractVirtualMachine vm;
        private readonly ICallDataSerializer serializer;
        private readonly Network network;
        private readonly InternalTransactionExecutorFactory internalTransactionExecutorFactory;
        private readonly IAddressGenerator addressGenerator;

        public Executor(ILoggerFactory loggerFactory,
            IContractPrimitiveSerializer contractPrimitiveSerializer,
            ICallDataSerializer serializer,
            IContractStateRepository stateSnapshot,
            ISmartContractResultRefundProcessor refundProcessor,
            ISmartContractResultTransferProcessor transferProcessor,
            ISmartContractVirtualMachine vm,
            IAddressGenerator addressGenerator,
            Network network,
            InternalTransactionExecutorFactory internalTransactionExecutorFactory)
        {
            this.logger = loggerFactory.CreateLogger(this.GetType());
            this.contractPrimitiveSerializer = contractPrimitiveSerializer;
            this.stateSnapshot = stateSnapshot;
            this.refundProcessor = refundProcessor;
            this.transferProcessor = transferProcessor;
            this.vm = vm;
            this.serializer = serializer;
            this.addressGenerator = addressGenerator;
            this.network = network;
            this.internalTransactionExecutorFactory = internalTransactionExecutorFactory;
        }

        public ISmartContractExecutionResult Execute(ISmartContractTransactionContext transactionContext)
        {
            this.logger.LogTrace("()");

            // Deserialization can't fail because this has already been through SmartContractFormatRule.
            Result<ContractTxData> callDataDeserializationResult = this.serializer.Deserialize(transactionContext.ScriptPubKey.ToBytes());
            ContractTxData callData = callDataDeserializationResult.Value;

            var gasMeter = new GasMeter(callData.GasLimit);
            gasMeter.Spend((Gas)GasPriceList.BaseCost);

            var context = new TransactionContext(
                transactionContext.TransactionHash,
                transactionContext.BlockHeight,
                transactionContext.CoinbaseAddress,
                transactionContext.Sender,
                transactionContext.TxOutValue);

            var creation = callData.IsCreateContract;

            var contractLogger = new ContractLogHolder(this.network);
            var internalTransfers = new List<TransferInfo>();

            var address = creation
                ? this.addressGenerator.GenerateAddress(transactionContext.TransactionHash,
                    context.GetNonceAndIncrement())
                : callData.ContractAddress;

            var state = this.SetupState(contractLogger, internalTransfers, gasMeter, this.stateSnapshot, context, address);

            VmExecutionResult result = callData.IsCreateContract
                ? this.vm.Create(gasMeter, this.stateSnapshot, callData, context, state)
                : this.vm.ExecuteMethod(gasMeter, this.stateSnapshot, callData, context, state);

            var revert = result.ExecutionException != null;

            Transaction internalTransaction = this.transferProcessor.Process(
                this.stateSnapshot,
                creation ? result.NewContractAddress : callData.ContractAddress,
                transactionContext,
                internalTransfers,
                revert);

            (Money fee, List<TxOut> refundTxOuts) = this.refundProcessor.Process(
                callData,
                transactionContext.MempoolFee,
                transactionContext.Sender,
                result.GasConsumed,
                result.ExecutionException);

            var executionResult = new SmartContractExecutionResult
            {
                NewContractAddress = !revert && creation ? result.NewContractAddress : null,
                Exception = result.ExecutionException,
                GasConsumed = result.GasConsumed,
                Return = result.Result,
                InternalTransaction = internalTransaction,
                Fee = fee,
                Refunds = refundTxOuts,
                Logs = contractLogger.GetRawLogs().ToLogs(this.contractPrimitiveSerializer)
            };

            if (revert)
            {
                this.logger.LogTrace("(-)[CONTRACT_EXECUTION_FAILED]");

                this.stateSnapshot.Rollback();
            }
            else
            {
                this.logger.LogTrace("(-)[CONTRACT_EXECUTION_SUCCEEDED]");

                this.stateSnapshot.Commit();
            }

            return executionResult;
        }

        /// <summary>
        /// Sets up the state object for the contract execution
        /// </summary>
        private ISmartContractState SetupState(
            IContractLogHolder contractLogger,
            List<TransferInfo> internalTransferList,
            IGasMeter gasMeter,
            IContractStateRepository repository,
            ITransactionContext transactionContext,
            uint160 contractAddress)
        {
            IPersistenceStrategy persistenceStrategy =
                new MeteredPersistenceStrategy(repository, gasMeter, new BasicKeyEncodingStrategy());

            var persistentState = new PersistentState(persistenceStrategy, this.contractPrimitiveSerializer, contractAddress);

            IInternalTransactionExecutor internalTransactionExecutor = this.internalTransactionExecutorFactory.Create(this.vm, contractLogger, repository, internalTransferList, transactionContext);

            var balanceState = new BalanceState(repository, transactionContext.Amount, internalTransferList);

            var contractState = new SmartContractState(
                new Block(
                    transactionContext.BlockHeight,
                    transactionContext.Coinbase.ToAddress(this.network)
                ),
                new Message(
                    contractAddress.ToAddress(this.network),
                    transactionContext.From.ToAddress(this.network),
                    transactionContext.Amount
                ),
                persistentState,
                gasMeter,
                contractLogger,
                internalTransactionExecutor,
                new InternalHashHelper(),
                () => balanceState.GetBalance(contractAddress));

            return contractState;
        }
    }
}
