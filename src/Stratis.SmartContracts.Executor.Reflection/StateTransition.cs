using System;
using NBitcoin;
using Stratis.SmartContracts.Core;
using Stratis.SmartContracts.Core.Exceptions;
using Stratis.SmartContracts.Core.State.AccountAbstractionLayer;
using Stratis.SmartContracts.Executor.Reflection.Serialization;

namespace Stratis.SmartContracts.Executor.Reflection
{
    public enum StateTransitionKind
    {
        None,
        Transfer,
        Create,
        InternalCreate,
        Call   
    }

    public class StateTransitionResult
    {
        public ITransferResult TransferResult { get; set; }

        public VmExecutionResult VmExecutionResult { get; set; }

        public Gas GasConsumed { get; set; }

        public uint160 ContractAddress { get; set; }

        public bool Success { get; set; }
       
        public StateTransitionKind Kind { get; set; }

        public CreateResult CreateResult { get; set; }
    }

    public class StateTransition : IStateTransition
    {
        public StateTransition(InternalTransactionExecutorFactory internalTransactionExecutorFactory,
            IState state,
            ISmartContractVirtualMachine vm, Network network)
        {
            this.InternalTransactionExecutorFactory = internalTransactionExecutorFactory;
            this.State = state;
            this.Vm = vm;
            this.Network = network;
        }

        public InternalTransactionExecutorFactory InternalTransactionExecutorFactory { get; }

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

        public StateTransitionResult Apply(ExternalCreateMessage message)
        {
            var address = this.State.GetNewAddress();

            // Create lambda to invoke VM
            VmExecutionResult VmInvoke(ISmartContractState state) => this.Vm.Create(this.State.Repository, message.Method, state, message.Code);

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
            byte[] contractCode = this.State.Repository.GetCode(message.From);

            var address = this.State.GetNewAddress();

            VmExecutionResult VmInvoke(ISmartContractState state) => this.Vm.Create(this.State.Repository, message.Method, state, contractCode, message.Type);

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

            byte[] contractCode = this.State.Repository.GetCode(message.To);

            if (contractCode == null || contractCode.Length == 0)
            {
                // No contract code at this address
                return new StateTransitionResult
                {
                    Success = false,
                    GasConsumed = (Gas) 0,
                    Kind = StateTransitionKind.None,
                    TransferResult = TransferResult.Empty(),
                    ContractAddress = message.To
                };
            }

            var type = this.State.Repository.GetContractType(message.To);

            VmExecutionResult VmInvoke(ISmartContractState state) => this.Vm.ExecuteMethod(this.State.Repository, message.Method, state, contractCode, type);

            (var result, var gasMeter, var _) = ApplyInternal(VmInvoke, message.To, message);

            // Only append internal value transfer if the execution was successful.
            if (result.ExecutionException != null)
            {
                this.State.InternalTransfers.Add(new TransferInfo
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
            byte[] contractCode = this.State.Repository.GetCode(message.To);

            if (contractCode == null || contractCode.Length == 0)
            {
                // No contract at this address, create a regular P2PKH xfer
                this.State.InternalTransfers.Add(new TransferInfo
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
                this.State.InternalTransfers.Add(new TransferInfo
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

        /// <summary>
        /// Throws an exception if a contract doesn't have a high enough balance to make this transaction.
        /// </summary>
        private bool EnsureContractHasEnoughBalance(uint160 contractAddress, ulong amountToTransfer)
        {
            ulong balance = this.State.BalanceState.GetBalance(contractAddress);

            return balance >= amountToTransfer;
        }
    }
}