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
        IBlock Block { get; }
        ulong Nonce { get; }
        Network Network { get; }
        IContractStateRepository Repository { get; }
        IContractLogHolder LogHolder { get; }
        BalanceState BalanceState { get; }
        List<TransferInfo> InternalTransfers { get; }
        void Rollback();
        void Commit();
        IState Nest(ulong txAmount);
        ulong GetNonceAndIncrement();
        uint256 TransactionHash { get; }
    }

    public class State : IState
    {
        private readonly IContractStateRepository parentRepository;
        private readonly ulong originalNonce;
        private readonly IState parent;

        private State(IState parent, IContractStateRepository repository, IBlock block, Network network, ulong txAmount, uint256 transactionHash, ulong nonce = 0)
            : this(repository, block, network, txAmount, transactionHash, nonce)
        {
            this.parent = parent;
        }

        public State(IContractStateRepository repository, IBlock block, Network network, ulong txAmount,
            uint256 transactionHash, ulong nonce = 0)
        {
            this.parentRepository = repository;
            this.Repository = repository.StartTracking();
            this.LogHolder = new ContractLogHolder(network);
            this.InternalTransfers = new List<TransferInfo>();
            this.BalanceState = new BalanceState(this.Repository, txAmount, this.InternalTransfers);
            this.Network = network;
            this.originalNonce = nonce;
            this.Nonce = nonce;
            this.Block = block;
            this.TransactionHash = transactionHash;
        }

        public uint256 TransactionHash { get; }

        public IBlock Block { get; }

        public ulong Nonce { get; private set; }

        public Network Network { get; }

        public IContractStateRepository Repository { get; private set; }

        public IContractLogHolder LogHolder { get; }

        public BalanceState BalanceState { get; }

        public List<TransferInfo> InternalTransfers { get; }

        public ulong GetNonceAndIncrement()
        {
            return this.Nonce++;
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
        /// Commits the state transition. Updates the parent state if necessary.
        /// </summary>
        public void Commit()
        {
            this.Repository.Commit();

            // Update the parent
            if (this.parent != null)
            {
                this.parent.InternalTransfers.AddRange(this.InternalTransfers);
                this.parent.LogHolder.AddRawLogs(this.LogHolder.GetRawLogs());
                while (this.parent.Nonce < this.Nonce)
                {
                    this.parent.GetNonceAndIncrement();
                }
            }
        }

        public IState Nest(ulong txAmount)
        {
            return new State(this, this.Repository, this.Block, this.Network, txAmount, this.TransactionHash, this.Nonce);
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
    public class StateTransition
    {
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

        public (VmExecutionResult, GasMeter) Apply()
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

            return (result, gasMeter);
        }

        public ISmartContractState ContractState(IGasMeter gasMeter, IState state)
        {
            IPersistenceStrategy persistenceStrategy =
                new MeteredPersistenceStrategy(state.Repository, gasMeter, new BasicKeyEncodingStrategy());

            var persistentState = new PersistentState(persistenceStrategy, new ContractPrimitiveSerializer(this.Network), this.Message.To);

            var contractState = new SmartContractState(
                state.Block,
                new Core.Message(
                    this.Message.To.ToAddress(this.Network),
                    this.Message.From.ToAddress(this.Network),
                    this.Message.Amount
                ),
                persistentState,
                gasMeter,
                state.LogHolder,
                null,
                new InternalHashHelper(),
                () => state.BalanceState.GetBalance(this.Message.To));

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

            var creation = callData.IsCreateContract;

            var block = new Block(
                transactionContext.BlockHeight,
                transactionContext.CoinbaseAddress.ToAddress(this.network)
            );

            var state = new State(this.stateSnapshot, block, this.network, transactionContext.TxOutValue, transactionContext.TransactionHash);

            // Generate address (if required)
            // Get code from DB (if required)
            // Get type name from DB (if required)
            // Create state
            var address = creation
                ? this.addressGenerator.GenerateAddress(state.TransactionHash, state.GetNonceAndIncrement())
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

            var stateTransition = new StateTransition(state, this.vm, this.network, message);
            
            (var result, var gasMeter) = stateTransition.Apply();

            var revert = result.ExecutionException != null;

            Transaction internalTransaction = this.transferProcessor.Process(
                this.stateSnapshot,
                address,
                transactionContext,
                state.InternalTransfers,
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
                Logs = state.LogHolder.GetRawLogs().ToLogs(this.contractPrimitiveSerializer)
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
    }
}
