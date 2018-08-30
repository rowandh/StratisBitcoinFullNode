using System;
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
        uint160 GetNewAddress();
    }

    public class State : IState
    {
        private readonly IContractStateRepository parentRepository;
        private readonly ulong originalNonce;
        private readonly IState parent;
        private readonly IAddressGenerator addressGenerator;

        private State(IState parent, IContractStateRepository repository, IBlock block, Network network, ulong txAmount, uint256 transactionHash, IAddressGenerator addressGenerator, ulong nonce = 0)
            : this(repository, block, network, txAmount, transactionHash, addressGenerator, nonce)
        {
            this.parent = parent;
        }

        public State(IContractStateRepository repository, IBlock block, Network network, ulong txAmount,
            uint256 transactionHash, IAddressGenerator addressGenerator, ulong nonce = 0)
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
            this.addressGenerator = addressGenerator;
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

        public uint160 GetNewAddress()
        {
            return this.addressGenerator.GenerateAddress(this.TransactionHash, this.GetNonceAndIncrement());
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
            return new State(this, this.Repository, this.Block, this.Network, txAmount, this.TransactionHash, this.addressGenerator, this.Nonce);
        }
    }

    public abstract class BaseMessage
    {
        /// <summary>
        /// All transfers have a recipient.
        /// </summary>
        public uint160 From { get; set; }

        /// <summary>
        /// All transfers have an amount.
        /// </summary>
        public ulong Amount { get; set; }

        /// <summary>
        /// All transfers have some gas limit associated with them. This is even required for fallback calls.
        /// </summary>
        public Gas GasLimit { get; set; }
    }

    public class P2PKHTransferMessage : BaseMessage
    {

    }

    public class ContractTransferMessage : CallMessage
    {
        public ContractTransferMessage()
        {
            this.Method = MethodCall.Receive();
        }
    }

    public class ExternalCreateMessage : BaseMessage
    {
        public byte[] Code { get; set; }

        public MethodCall Method { get; set; }
    }

    public class InternalCreateMessage : BaseMessage
    {
        public byte[] Code { get; set; }

        /// <summary>
        /// Internal creates need a method call with params and an empty method name.
        /// </summary>
        public MethodCall Method { get; set; }

        /// <summary>
        /// Internal creates need to specify the Type they are creating.
        /// </summary>
        public string Type { get; set; }
    }

    public class CallMessage : BaseMessage
    {
        /// <summary>
        /// All transfers have a destination.
        /// </summary>
        public uint160 To { get; set; }

        public byte[] Code { get; set; }

        public MethodCall Method { get; set; }
    }

    public class StateTransition
    {
        public StateTransition(
            InternalTransactionExecutorFactory internalTransactionExecutorFactory, 
            IState state,
            ISmartContractVirtualMachine vm, Network network, BaseMessage message)
        {
            this.InternalTransactionExecutorFactory = internalTransactionExecutorFactory;
            this.State = state;
            this.Vm = vm;
            this.Network = network;
            this.Message = message;
        }

        public InternalTransactionExecutorFactory InternalTransactionExecutorFactory { get; }

        public BaseMessage Message { get; }

        public IState State { get; }

        public Network Network { get; }

        public ISmartContractVirtualMachine Vm { get; }

        private (VmExecutionResult, GasMeter, uint160) ApplyInternal(Func<ISmartContractState, VmExecutionResult> vmInvoke, uint160 address, BaseMessage message)
        {
            var gasMeter = new GasMeter(message.GasLimit);

            gasMeter.Spend((Gas)GasPriceList.BaseCost);

            var contractState = ContractState(gasMeter, this.State, address, message);

            var result = vmInvoke(contractState);
            
            var revert = result.ExecutionException != null;

            if (revert)
            {
                this.State.Rollback();
            }
            else
            {
                this.State.Commit();
            }

            return (result, gasMeter, address);
        }

        public (VmExecutionResult, GasMeter, uint160 address) Apply(ExternalCreateMessage message)
        {
            var address = this.State.GetNewAddress();

            // Create lamba to invoke VM
            VmExecutionResult VmInvoke(ISmartContractState state) => this.Vm.Create(this.State.Repository, message.Method, state, message.Code);

            return ApplyInternal(VmInvoke, address, message);
        }

        public (VmExecutionResult, GasMeter, uint160 address) Apply(InternalCreateMessage message)
        {
            var address = this.State.GetNewAddress();

            VmExecutionResult VmInvoke(ISmartContractState state) => this.Vm.Create(this.State.Repository, message.Method, state, message.Code, message.Type);

            return this.ApplyInternal(VmInvoke, address, message);
        }

        public (VmExecutionResult, GasMeter, uint160 address) Apply(CallMessage message)
        {
            var type = this.State.Repository.GetContractType(message.To);

            VmExecutionResult VmInvoke(ISmartContractState state) => this.Vm.ExecuteMethod(this.State.Repository, message.Method, state, message.Code, type);
            
            return this.ApplyInternal(VmInvoke, message.To, message);
        }

        public ISmartContractState ContractState(IGasMeter gasMeter, IState state, uint160 address, BaseMessage message)
        {
            IPersistenceStrategy persistenceStrategy =
                new MeteredPersistenceStrategy(state.Repository, gasMeter, new BasicKeyEncodingStrategy());

            var persistentState = new PersistentState(persistenceStrategy, new ContractPrimitiveSerializer(this.Network), address);

            var contractState = new SmartContractState(
                state.Block,
                new Message(
                    address.ToAddress(this.Network),
                    message.From.ToAddress(this.Network),
                    message.Amount
                ),
                persistentState,
                gasMeter,
                state.LogHolder,
                this.InternalTransactionExecutorFactory.Create(this.State),
                new InternalHashHelper(),
                () => state.BalanceState.GetBalance(address));

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

            var state = new State(this.stateSnapshot, block, this.network, transactionContext.TxOutValue, transactionContext.TransactionHash, this.addressGenerator);

            var stateTransition = new StateTransition(this.internalTransactionExecutorFactory, state, this.vm, this.network, null);

            VmExecutionResult result;
            IGasMeter gasMeter;
            uint160 address;

            if (creation)
            {
                var message = new ExternalCreateMessage
                {
                    From = transactionContext.Sender,
                    Amount = transactionContext.TxOutValue,
                    Code = callData.ContractExecutionCode,
                    GasLimit = callData.GasLimit,
                    Method = new MethodCall(null, callData.MethodParameters) // TODO handle constructor MethodCall name
                };

                (result, gasMeter, address) = stateTransition.Apply(message);
            }
            else
            {
                var message = new CallMessage
                {
                    To = callData.ContractAddress,
                    From = transactionContext.Sender,
                    Amount = transactionContext.TxOutValue,
                    Code = this.stateSnapshot.GetCode(callData.ContractAddress),
                    GasLimit = callData.GasLimit,
                    Method = new MethodCall(callData.MethodName, callData.MethodParameters),
                };

                (result, gasMeter, address) = stateTransition.Apply(message);
            }

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

            return executionResult;
        }
    }
}
