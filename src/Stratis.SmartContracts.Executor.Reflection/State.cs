using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
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
        private class StateSnapshot
        {
            public StateSnapshot(IContractLogHolder logHolder,
                List<TransferInfo> internalTransfers, ulong nonce)
            {
                this.Logs = logHolder.GetRawLogs().ToImmutableList();
                this.InternalTransfers = internalTransfers.ToImmutableList();
                this.Nonce = nonce;
            }

            public ImmutableList<RawLog> Logs { get; }

            public ImmutableList<TransferInfo> InternalTransfers { get; }

            public ulong Nonce { get; }
        }

        public State(InternalTransactionExecutorFactory internalTransactionExecutorFactory,
            ISmartContractVirtualMachine vm, IContractStateRepository repository, IBlock block, Network network,
            ulong txAmount,
            uint256 transactionHash, IAddressGenerator addressGenerator, ulong nonce = 0)
        {
            this.Repository = repository;
            this.LogHolder = new ContractLogHolder(network);
            this.InternalTransfers = new List<TransferInfo>();
            this.BalanceState = new BalanceState(this.Repository, txAmount, this.InternalTransfers);
            this.Network = network;
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

        public IContractStateRepository Repository { get; }

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
        private void Rollback(StateSnapshot snapshot)
        {
            // Reset the nonce
            this.Nonce = snapshot.Nonce;

            // Rollback internal transfers
            this.InternalTransfers.Clear();
            this.InternalTransfers.AddRange(snapshot.InternalTransfers);
            
            // Rollback logs
            this.LogHolder.Clear();
            this.LogHolder.AddRawLogs(snapshot.Logs);
        }

        private StateSnapshot TakeSnapshot()
        {
            return new StateSnapshot(this.LogHolder, this.InternalTransfers, this.Nonce);
        }

        public StateTransitionResult Apply(ExternalCreateMessage message)
        {
            var stateSnapshot = this.TakeSnapshot();

            // We can't snapshot the state so we start tracking again
            var nestedState = this.Repository.StartTracking();

            var address = this.GetNewAddress();

            var gasMeter = new GasMeter(message.GasLimit);

            gasMeter.Spend((Gas)GasPriceList.BaseCost);

            var contractState = ContractState(gasMeter, address, message, nestedState);

            var result = this.Vm.Create(nestedState, message.Method, contractState, message.Code);

            var revert = result.ExecutionException != null;

            if (revert)
            {
                this.Rollback(stateSnapshot);
            }
            else
            {
                nestedState.Commit();
            }

            return new StateTransitionResult
            {
                Success = !revert,
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

            var stateSnapshot = this.TakeSnapshot();

            // We can't snapshot the state so we start tracking again
            var nestedState = this.Repository.StartTracking();

            // Get the code using the sender. We are creating an instance of a Type in its assembly.
            byte[] contractCode = nestedState.GetCode(message.From);

            var address = this.GetNewAddress();
            
            var gasMeter = new GasMeter(message.GasLimit);

            gasMeter.Spend((Gas)GasPriceList.BaseCost);

            var contractState = ContractState(gasMeter, address, message, nestedState);

            var result = this.Vm.Create(nestedState, message.Method, contractState, contractCode, message.Type);

            var revert = result.ExecutionException != null;

            if (revert)
            {
                this.Rollback(stateSnapshot);
            }
            else
            {
                nestedState.Commit();
            }

            CreateResult createResult = revert
                ? CreateResult.Failed()
                : CreateResult.Succeeded(address.ToAddress(this.Network));

            return new StateTransitionResult
            {
                Success = !revert,
                GasConsumed = gasMeter.GasConsumed,
                VmExecutionResult = result,
                ContractAddress = address,
                Kind = StateTransitionKind.InternalCreate,
                TransferResult = null,
                CreateResult = createResult
            };
        }

        public StateTransitionResult Apply(InternalCallMessage message)
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

            var stateSnapshot = this.TakeSnapshot();

            // We can't snapshot the state so we start tracking again
            var nestedState = this.Repository.StartTracking();

            var type = nestedState.GetContractType(message.To);

            var gasMeter = new GasMeter(message.GasLimit);

            gasMeter.Spend((Gas)GasPriceList.BaseCost);

            var contractState = ContractState(gasMeter, message.To, message, nestedState);

            var result = this.Vm.ExecuteMethod(nestedState, message.Method, contractState, contractCode, type);

            var revert = result.ExecutionException != null;

            if (revert)
            {
                this.Rollback(stateSnapshot);
            }
            else
            {
                nestedState.Commit();

                // Only append internal value transfer if the execution was successful.
                this.InternalTransfers.Add(new TransferInfo
                {
                    From = message.From,
                    To = message.To,
                    Value = message.Amount
                });
            }

            return new StateTransitionResult
            {
                Success = !revert,
                GasConsumed = gasMeter.GasConsumed,
                VmExecutionResult = result,
                Kind = StateTransitionKind.Call,
                TransferResult = TransferResult.Transferred(result.Result),
                ContractAddress = message.To
            };
        }

        public StateTransitionResult Apply(ExternalCallMessage message)
        {
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

            var stateSnapshot = this.TakeSnapshot();

            // We can't snapshot the state so we start tracking again
            var nestedState = this.Repository.StartTracking();

            var type = nestedState.GetContractType(message.To);

            var gasMeter = new GasMeter(message.GasLimit);

            gasMeter.Spend((Gas)GasPriceList.BaseCost);

            var contractState = ContractState(gasMeter, message.To, message, nestedState);

            var result = this.Vm.ExecuteMethod(nestedState, message.Method, contractState, contractCode, type);

            var revert = result.ExecutionException != null;

            if (revert)
            {
                this.Rollback(stateSnapshot);
            }
            else
            {
                // External call, so we don't need to add the transfer
                nestedState.Commit();
            }

            return new StateTransitionResult
            {
                Success = !revert,
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
                    Success = true,
                    GasConsumed = (Gas)0,
                    Kind = StateTransitionKind.Transfer,
                    TransferResult = TransferResult.Empty(),
                    ContractAddress = message.To
                };
            }

            return this.Apply(message as ExternalCallMessage);
        }

        public ISmartContractState ContractState(IGasMeter gasMeter, uint160 address, BaseMessage message,
            IContractStateRepository repository)
        {
            IPersistenceStrategy persistenceStrategy =
                new MeteredPersistenceStrategy(repository, gasMeter, new BasicKeyEncodingStrategy());

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