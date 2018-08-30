using System;
using NBitcoin;
using Stratis.SmartContracts.Core;
using Stratis.SmartContracts.Executor.Reflection.Serialization;

namespace Stratis.SmartContracts.Executor.Reflection
{
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
}