using System;
using System.Collections.Generic;
using NBitcoin;
using Stratis.SmartContracts.Core;
using Stratis.SmartContracts.Core.Exceptions;
using Stratis.SmartContracts.Core.State;
using Stratis.SmartContracts.Core.State.AccountAbstractionLayer;
using Stratis.SmartContracts.Executor.Reflection.ContractLogging;
using Stratis.SmartContracts.Executor.Reflection.Serialization;

namespace Stratis.SmartContracts.Executor.Reflection
{
    public class State : IState
    {
        private readonly IContractStateRepository parentRepository;
        private readonly ulong originalNonce;
        private readonly IState parent;

        private State(IState parent, ulong txAmount)
            : this(parent.InternalTransactionExecutorFactory, parent.Vm, parent.Repository, parent.Block, parent.Network, txAmount, parent.TransactionHash, parent.AddressGenerator, parent.Nonce)
        {
            this.parent = parent;
        }

        public State(InternalTransactionExecutorFactory internalTransactionExecutorFactory,
            ISmartContractVirtualMachine vm, IContractStateRepository repository, IBlock block, Network network,
            ulong txAmount,
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
            this.AddressGenerator = addressGenerator;
            this.InternalTransactionExecutorFactory = internalTransactionExecutorFactory;
            this.Vm = vm;
        }

        public IAddressGenerator AddressGenerator { get; }

        public uint256 TransactionHash { get; }

        public IBlock Block { get; }

        public ulong Nonce { get; private set; }

        public Network Network { get; }

        public IContractStateRepository Repository { get; private set; }

        public IContractLogHolder LogHolder { get; }

        public BalanceState BalanceState { get; }

        public List<TransferInfo> InternalTransfers { get; }

        public InternalTransactionExecutorFactory InternalTransactionExecutorFactory { get; }

        public ISmartContractVirtualMachine Vm { get; }

        public ulong GetNonceAndIncrement()
        {
            return this.Nonce++;
        }

        public uint160 GetNewAddress()
        {
            return this.AddressGenerator.GenerateAddress(this.TransactionHash, this.GetNonceAndIncrement());
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
            return new State(this, txAmount);
        }

        private (VmExecutionResult, GasMeter, uint160) ApplyInternal(Func<ISmartContractState, VmExecutionResult> vmInvoke, uint160 address, BaseMessage message)
        {
            var gasMeter = new GasMeter(message.GasLimit);

            gasMeter.Spend((Gas)GasPriceList.BaseCost);

            var contractState = ContractState(gasMeter, address, message);

            var result = vmInvoke(contractState);

            var revert = result.ExecutionException != null;

            if (revert)
            {
                this.Rollback();
            }
            else
            {
                this.Commit();
            }

            return (result, gasMeter, address);
        }

        public StateTransitionResult Apply(ExternalCreateMessage message)
        {
            var address = this.GetNewAddress();

            // Create lambda to invoke VM
            VmExecutionResult VmInvoke(ISmartContractState state) => this.Vm.Create(this.Repository, message.Method, state, message.Code);

            (var result, var gasMeter, var _) = ApplyInternal(VmInvoke, address, message);

            return new StateTransitionResult
            {
                Success = result.ExecutionException != null,
                GasConsumed = gasMeter.GasConsumed,
                VmExecutionResult = result,
                ContractAddress = address,
                Kind = StateTransitionKind.Create,
                TransferResult = null
            };
        }

        public StateTransitionResult Apply(InternalCreateMessage message)
        {
            var enoughBalance = EnsureContractHasEnoughBalance(message.From, message.Amount);

            if (!enoughBalance)
                throw new InsufficientBalanceException();

            // Get the code using the sender. We are creating an instance of a Type in its assembly.
            byte[] contractCode = this.Repository.GetCode(message.From);

            var address = this.GetNewAddress();

            VmExecutionResult VmInvoke(ISmartContractState state) => this.Vm.Create(this.Repository, message.Method, state, contractCode, message.Type);

            (var result, var gasMeter, var _) = ApplyInternal(VmInvoke, address, message);

            CreateResult createResult = result.ExecutionException != null
                ? CreateResult.Failed()
                : CreateResult.Succeeded(address.ToAddress(this.Network));

            return new StateTransitionResult
            {
                Success = result.ExecutionException != null,
                GasConsumed = gasMeter.GasConsumed,
                VmExecutionResult = result,
                ContractAddress = address,
                Kind = StateTransitionKind.InternalCreate,
                TransferResult = null,
                CreateResult = createResult
            };
        }

        public StateTransitionResult Apply(CallMessage message)
        {
            var enoughBalance = EnsureContractHasEnoughBalance(message.From, message.Amount);

            if (!enoughBalance)
                throw new InsufficientBalanceException();

            byte[] contractCode = this.Repository.GetCode(message.To);

            if (contractCode == null || contractCode.Length == 0)
            {
                // No contract code at this address
                return new StateTransitionResult
                {
                    Success = false,
                    GasConsumed = (Gas)0,
                    Kind = StateTransitionKind.None,
                    TransferResult = TransferResult.Empty(),
                    ContractAddress = message.To
                };
            }

            var type = this.Repository.GetContractType(message.To);

            VmExecutionResult VmInvoke(ISmartContractState state) => this.Vm.ExecuteMethod(this.Repository, message.Method, state, contractCode, type);

            (var result, var gasMeter, var _) = ApplyInternal(VmInvoke, message.To, message);

            // Only append internal value transfer if the execution was successful.
            if (result.ExecutionException != null)
            {
                this.InternalTransfers.Add(new TransferInfo
                {
                    From = message.From,
                    To = message.To,
                    Value = message.Amount
                });
            }

            return new StateTransitionResult
            {
                Success = result.ExecutionException != null,
                GasConsumed = gasMeter.GasConsumed,
                VmExecutionResult = result,
                Kind = StateTransitionKind.Call,
                TransferResult = TransferResult.Transferred(result.Result),
                ContractAddress = message.To
            };
        }

        public StateTransitionResult Apply(ContractTransferMessage message)
        {
            var enoughBalance = EnsureContractHasEnoughBalance(message.From, message.Amount);

            if (!enoughBalance)
                throw new InsufficientBalanceException();

            // If it's not a contract, create a regular P2PKH tx
            // If it is a contract, do a regular contract call
            byte[] contractCode = this.Repository.GetCode(message.To);

            if (contractCode == null || contractCode.Length == 0)
            {
                // No contract at this address, create a regular P2PKH xfer
                this.InternalTransfers.Add(new TransferInfo
                {
                    From = message.From,
                    To = message.To,
                    Value = message.Amount
                });

                return new StateTransitionResult
                {
                    Success = false,
                    GasConsumed = (Gas)0,
                    Kind = StateTransitionKind.Transfer,
                    TransferResult = TransferResult.Empty(),
                    ContractAddress = message.To
                };
            }

            var result = this.Apply(message as CallMessage);

            // Only append internal value transfer if the execution was successful.
            if (result.VmExecutionResult.ExecutionException != null)
            {
                this.InternalTransfers.Add(new TransferInfo
                {
                    From = message.From,
                    To = message.To,
                    Value = message.Amount
                });
            }

            return new StateTransitionResult
            {
                Success = result.VmExecutionResult != null,
                GasConsumed = result.GasConsumed,
                VmExecutionResult = result.VmExecutionResult,
                Kind = StateTransitionKind.Call,
                TransferResult = TransferResult.Transferred(result.VmExecutionResult.Result),
                ContractAddress = message.To
            };
        }

        public ISmartContractState ContractState(IGasMeter gasMeter, uint160 address, BaseMessage message)
        {
            IPersistenceStrategy persistenceStrategy =
                new MeteredPersistenceStrategy(this.Repository, gasMeter, new BasicKeyEncodingStrategy());

            var persistentState = new PersistentState(persistenceStrategy, new ContractPrimitiveSerializer(this.Network), address);

            var contractState = new SmartContractState(
                this.Block,
                new Message(
                    address.ToAddress(this.Network),
                    message.From.ToAddress(this.Network),
                    message.Amount
                ),
                persistentState,
                gasMeter,
                this.LogHolder,
                this.InternalTransactionExecutorFactory.Create(this),
                new InternalHashHelper(),
                () => this.BalanceState.GetBalance(address));

            return contractState;
        }

        /// <summary>
        /// Throws an exception if a contract doesn't have a high enough balance to make this transaction.
        /// </summary>
        private bool EnsureContractHasEnoughBalance(uint160 contractAddress, ulong amountToTransfer)
        {
            ulong balance = this.BalanceState.GetBalance(contractAddress);

            return balance >= amountToTransfer;
        }
    }
}