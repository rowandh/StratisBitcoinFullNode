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
    public interface IState
    {
        int Nonce { get; }
        Network Network { get; }
        uint160 Address { get; }
        IContractStateRepository Repository { get; }
        IContractLogHolder LogHolder { get; }
        BalanceState BalanceState { get; }
        GasMeter GasMeter { get; set; }
        List<TransferInfo> InternalTransfers { get; }
        ISmartContractState ContractState(ISmartContractVirtualMachine vm, IContractPrimitiveSerializer serializer, InternalTransactionExecutorFactory internalTransactionExecutorFactory);
    }

    public class State : IState
    {
        public State(IContractStateRepository repository, Network network, ulong txAmount, uint160 contractAddress, int nonce = 0)
        {
            this.Repository = repository.StartTracking();
            this.LogHolder = new ContractLogHolder(network);
            this.InternalTransfers = new List<TransferInfo>();
            this.BalanceState = new BalanceState(this.Repository, txAmount, this.InternalTransfers);
            this.Address = contractAddress;
            this.Network = network;
            this.Nonce = nonce;
        }

        public int Nonce { get; }

        public Network Network { get; }

        public uint160 Address { get; }

        public IContractStateRepository Repository { get; }

        public IContractLogHolder LogHolder { get; }

        public BalanceState BalanceState { get; }

        public GasMeter GasMeter { get; set; }

        public List<TransferInfo> InternalTransfers { get; }
        
        public ISmartContractState ContractState(ISmartContractVirtualMachine vm, IContractPrimitiveSerializer serializer, InternalTransactionExecutorFactory internalTransactionExecutorFactory)
        {
            IPersistenceStrategy persistenceStrategy =
                new MeteredPersistenceStrategy(this.Repository, this.GasMeter, new BasicKeyEncodingStrategy());

            var persistentState = new PersistentState(persistenceStrategy, serializer, this.Address);

            IInternalTransactionExecutor internalTransactionExecutor = internalTransactionExecutorFactory
                .Create(
                    vm, this.LogHolder, 
                    this.Repository,
                    this.InternalTransfers,
                    null);

            var balanceState = this.BalanceState;

            var contractState = new SmartContractState(
                new Block(
                    0,
                    new Address("TEST")
                ),
                new Message(
                    this.Address.ToAddress(this.Network),
                    default(Address),
                    0
                ),
                persistentState,
                this.GasMeter,
                this.LogHolder,
                internalTransactionExecutor,
                new InternalHashHelper(),
                () => balanceState.GetBalance(this.Address));

            return contractState;
        }
    }

    public class Message2
    {
        public Gas GasLimit { get; set; }

        public uint160 To { get; set; }

        public uint160 From { get; set; }

        public ulong Amount { get; set; }
        
        public byte[] Code { get; set; }

        public MethodCall Method { get; set; }

        public string Type { get; set; }

        public bool IsCreation { get; set; }
    }

    public class StateTransition
    {
        // Spend base gas
        // Invoke VM
        // -> Can nest a state transition based on this one but with new:
        //      - State repository (nested)
        //      - Gas meter (different gas allowance)
        //      - Balance
        //      - Log holder (logs are only committed if execution successful)
        //      - Internal transfers (only committed if execution successful)
        // and same
        //      - Nonce
        // Commit

        public StateTransition(ISmartContractVirtualMachine vm)
        {
            this.Vm = vm;
        }

        public ISmartContractVirtualMachine Vm { get; }

        public void Apply(State state, Message2 message)
        {

        }
    }

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

            // Generate address (if required)
            // Get code from DB (if required)
            // Get type name from DB (if required)
            // Create state
            var address = creation
                ? this.addressGenerator.GenerateAddress(transactionContext.TransactionHash, context.GetNonceAndIncrement())
                : callData.ContractAddress;

            var code = creation
                ? callData.ContractExecutionCode
                : this.stateSnapshot.GetCode(callData.ContractAddress);

            var type = creation
                ? null
                : this.stateSnapshot.GetContractType(callData.ContractAddress);

            var message = new Message2();

            message.To = address;
            message.From = transactionContext.Sender;
            message.Amount = transactionContext.TxOutValue;
            message.Code = code;
            message.Type = type;
            message.GasLimit = callData.GasLimit;
            message.Method = new MethodCall(callData.MethodName, callData.MethodParameters);
            message.IsCreation = creation;

            var stateTransition = new StateTransition(this.vm);
            var state2 = new State(this.stateSnapshot, this.network, transactionContext.TxOutValue, address);
            
            stateTransition.Apply(state2, message);

            var state = this.SetupState(contractLogger, internalTransfers, gasMeter, this.stateSnapshot, context, address);

            VmExecutionResult result = callData.IsCreateContract
                ? this.vm.Create(this.stateSnapshot, message.Method, state, message.Code)
                : this.vm.ExecuteMethod(this.stateSnapshot, callData, state);

            var revert = result.ExecutionException != null;

            Transaction internalTransaction = this.transferProcessor.Process(
                this.stateSnapshot,
                address,
                transactionContext,
                internalTransfers,
                revert);

            (Money fee, List<TxOut> refundTxOuts) = this.refundProcessor.Process(
                callData,
                transactionContext.MempoolFee,
                transactionContext.Sender,
                gasMeter.GasConsumed,
                result.ExecutionException);

            var executionResult = new SmartContractExecutionResult
            {
                NewContractAddress = !revert && creation ? address : null,
                Exception = result.ExecutionException,
                GasConsumed = gasMeter.GasConsumed,
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
