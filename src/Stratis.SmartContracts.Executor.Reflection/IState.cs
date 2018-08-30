using System.Collections.Generic;
using NBitcoin;
using Stratis.SmartContracts.Core.State;
using Stratis.SmartContracts.Core.State.AccountAbstractionLayer;
using Stratis.SmartContracts.Executor.Reflection.ContractLogging;

namespace Stratis.SmartContracts.Executor.Reflection
{
    public interface IState : IStateTransition
    {
        IBlock Block { get; }
        ulong Nonce { get; }
        Network Network { get; }
        IAddressGenerator AddressGenerator { get; }
        InternalTransactionExecutorFactory InternalTransactionExecutorFactory { get; }
        ISmartContractVirtualMachine Vm { get; }
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
}