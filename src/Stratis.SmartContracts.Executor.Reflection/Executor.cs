using System.Collections.Generic;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.SmartContracts.Core;
using Stratis.SmartContracts.Core.State;
using Stratis.SmartContracts.Core.State.AccountAbstractionLayer;
using Stratis.SmartContracts.Executor.Reflection.ContractLogging;
using Stratis.SmartContracts.Executor.Reflection.Exceptions;
using Stratis.SmartContracts.Executor.Reflection.Serialization;
using Block = Stratis.SmartContracts.Core.Block;

namespace Stratis.SmartContracts.Executor.Reflection
{
    public interface IState
    {
        IBlock Block { get; }
        int Nonce { get; }
        Network Network { get; }
        uint160 Address { get; }
        IContractStateRepository Repository { get; }
        IContractLogHolder LogHolder { get; }
        BalanceState BalanceState { get; }
        List<TransferInfo> InternalTransfers { get; }
        ulong GetBalance();
        void Rollback();
        void Commit();
    }

    public class State : IState
    {
        private readonly IContractStateRepository parentRepository;
        private readonly int originalNonce;

        public State(IContractStateRepository repository, IBlock block, Network network, ulong txAmount, uint160 contractAddress, int nonce = 0)
        {
            this.parentRepository = repository;
            this.Repository = repository.StartTracking();
            this.LogHolder = new ContractLogHolder(network);
            this.InternalTransfers = new List<TransferInfo>();
            this.BalanceState = new BalanceState(this.Repository, txAmount, this.InternalTransfers);
            this.Address = contractAddress;
            this.Network = network;
            this.originalNonce = nonce;
            this.Nonce = nonce;
            this.Block = block;
        }

        public IBlock Block { get; }

        public int Nonce { get; private set; }

        public Network Network { get; }

        public uint160 Address { get; }

        public IContractStateRepository Repository { get; private set; }

        public IContractLogHolder LogHolder { get; }

        public BalanceState BalanceState { get; }

        public List<TransferInfo> InternalTransfers { get; }

        public ulong GetBalance()
        {
            return this.BalanceState.GetBalance(this.Address);
        }

        /// <summary>
        /// Reverts the state transition.
        /// </summary>
        public void Rollback()
        {
            // Reset the nonce
            this.Nonce = this.originalNonce;
            this.InternalTransfers.Clear();
            this.LogHolder.Clear();
            this.Repository.Rollback();

            // Because Rollback does not actually clear the repository state, we need to assign a new instance
            // to simulate "clearing" the intermediate state.
            this.Repository = this.parentRepository.StartTracking();
        }

        /// <summary>
        /// Commits the state transition.
        /// </summary>
        public void Commit()
        {
            this.Repository.Commit();
        }
    }

    public class Message
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

        public StateTransition(IState state, ISmartContractVirtualMachine vm, Network network, Message message)
        {
            this.State = state;
            this.Vm = vm;
            this.Network = network;
            this.Message = message;
        }

        public Message Message { get; }

        public IState State { get; }

        public Network Network { get; }

        public ISmartContractVirtualMachine Vm { get; }

        public VmExecutionResult Apply()
        {
            var gasMeter = new GasMeter(this.Message.GasLimit);

            gasMeter.Spend((Gas)GasPriceList.BaseCost);

            var contractState = ContractState(gasMeter, this.State);

            var result = this.Message.IsCreation
                ? this.Vm.Create(this.State.Repository, this.Message.Method, contractState, this.Message.Code)
                : this.Vm.ExecuteMethod(this.Message.Method, contractState, this.Message.Code, this.Message.Type);

            // TODO decide the exact conditions under which we revert
            // We ignore most exceptions except out of gas exceptions
            // We don't actually care about most exceptions except out of gas exceptions
            var revert = result.ExecutionException != null;

            if (revert)
            {
                this.State.Rollback();
            }
            else
            {
                this.State.Commit();
            }
            return result;
        }

        public ISmartContractState ContractState(IGasMeter gasMeter, IState state)
        {
            IPersistenceStrategy persistenceStrategy =
                new MeteredPersistenceStrategy(state.Repository, gasMeter, new BasicKeyEncodingStrategy());

            var persistentState = new PersistentState(persistenceStrategy, new ContractPrimitiveSerializer(this.Network), state.Address);

            var contractState = new SmartContractState(
                state.Block,
                new Core.Message(
                    state.Address.ToAddress(this.Network),
                    default(Address),
                    0
                ),
                persistentState,
                gasMeter,
                state.LogHolder,
                null,
                new InternalHashHelper(),
                state.GetBalance);

            return contractState;
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

            //var gasMeter = new GasMeter(callData.GasLimit);
            //gasMeter.Spend((Gas)GasPriceList.BaseCost);

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

            var message = new Message();

            message.To = address;
            message.From = transactionContext.Sender;
            message.Amount = transactionContext.TxOutValue;
            message.Code = code;
            message.Type = type;
            message.GasLimit = callData.GasLimit;
            message.Method = new MethodCall(callData.MethodName, callData.MethodParameters);
            message.IsCreation = creation;
            
            var block = new Block(
                transactionContext.BlockHeight,
                transactionContext.CoinbaseAddress.ToAddress(this.network)
            );

            var state = new State(this.stateSnapshot, block, this.network, transactionContext.TxOutValue, address);

            var stateTransition = new StateTransition(state, this.vm, this.network, message);
            
            var result = stateTransition.Apply();

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

            //if (revert)
            //{
            //    this.logger.LogTrace("(-)[CONTRACT_EXECUTION_FAILED]");

            //    this.stateSnapshot.Rollback();
            //}
            //else
            //{
            //    this.logger.LogTrace("(-)[CONTRACT_EXECUTION_SUCCEEDED]");

            //    this.stateSnapshot.Commit();
            //}

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
                new Core.Message(
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
