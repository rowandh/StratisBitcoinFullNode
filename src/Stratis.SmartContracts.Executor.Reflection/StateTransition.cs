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

    //public class StateTransition : IStateTransition
    //{
    //    public StateTransition(InternalTransactionExecutorFactory internalTransactionExecutorFactory,
    //        IState state,
    //        ISmartContractVirtualMachine vm, Network network)
    //    {
    //        this.InternalTransactionExecutorFactory = internalTransactionExecutorFactory;
    //        this.State = state;
    //        this.Vm = vm;
    //        this.Network = network;
    //    }

    //    public InternalTransactionExecutorFactory InternalTransactionExecutorFactory { get; }

    //    public IState State { get; }

    //    public Network Network { get; }

    //    public ISmartContractVirtualMachine Vm { get; }
    //}
}