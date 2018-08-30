using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.SmartContracts.Core;
using Stratis.SmartContracts.Core.Exceptions;
using Stratis.SmartContracts.Core.State.AccountAbstractionLayer;

namespace Stratis.SmartContracts.Executor.Reflection
{
    ///<inheritdoc/>
    public sealed class InternalTransactionExecutor : IInternalTransactionExecutor
    {
        private const ulong DefaultGasLimit = GasPriceList.BaseCost * 2 - 1;

        private readonly IAddressGenerator addressGenerator;
        private readonly ILogger logger;
        private readonly ILoggerFactory loggerFactory;
        private readonly Network network;
        private readonly ISmartContractVirtualMachine vm;
        private readonly IState baseState;
        private readonly InternalTransactionExecutorFactory internalTransactionExecutorFactory;

        public InternalTransactionExecutor(
            InternalTransactionExecutorFactory internalTransactionExecutorFactory,
            ISmartContractVirtualMachine vm,
            IAddressGenerator addressGenerator,
            ILoggerFactory loggerFactory,
            Network network,
            IState state)
        {
            this.internalTransactionExecutorFactory = internalTransactionExecutorFactory;
            this.loggerFactory = loggerFactory;
            this.logger = loggerFactory.CreateLogger(this.GetType());
            this.network = network;
            this.vm = vm;
            this.addressGenerator = addressGenerator;
            this.baseState = state;
        }

        ///<inheritdoc />
        public ICreateResult Create<T>(ISmartContractState smartContractState,
            ulong amountToTransfer,
            object[] parameters,
            ulong gasLimit = 0)
        {
            // TODO: Expend any neccessary costs.

            ulong gasBudget = (gasLimit != 0) ? gasLimit : smartContractState.GasMeter.GasAvailable;

            // Ensure we have enough gas left to be able to fund the new GasMeter.
            if (smartContractState.GasMeter.GasAvailable < gasBudget)
                throw new InsufficientGasException();

            // Check balance.
            EnsureContractHasEnoughBalance(smartContractState, amountToTransfer);

            // Build objects for VM
            // TODO: Fix this when calling from constructor.
            byte[] contractCode = this.baseState.Repository.GetCode(smartContractState.Message.ContractAddress.ToUint160(this.network));

            var message = new InternalCreateMessage
            {
                From = smartContractState.Message.ContractAddress.ToUint160(this.network),
                Amount = amountToTransfer,
                Code = contractCode,
                GasLimit = (Gas) gasBudget,
                Method = new MethodCall(null, parameters),
                Type = typeof(T).Name
            };

            var nestedState = this.baseState.Nest(amountToTransfer);

            var stateTransition = new StateTransition(this.internalTransactionExecutorFactory, nestedState, this.vm, this.network);

            (var result, var nestedGasMeter, var address) = stateTransition.Apply(message);

            // Update parent gas meter.
            smartContractState.GasMeter.Spend(nestedGasMeter.GasConsumed);

            return CreateResult.Succeeded(address.ToAddress(this.network));
        }

        ///<inheritdoc />
        public ITransferResult Call(
            ISmartContractState smartContractState,
            Address addressTo,
            ulong amountToTransfer,
            string methodName,
            object[] parameters,
            ulong gasLimit = 0)
        {
            // TODO: Spend BaseFee here

            EnsureContractHasEnoughBalance(smartContractState, amountToTransfer);

            byte[] contractCode = this.baseState.Repository.GetCode(addressTo.ToUint160(this.network));

            if (contractCode == null || contractCode.Length == 0)
            {
                return TransferResult.Empty();
            }

            // Here, we know contract has code, so we execute it
            // For a method call, send all the gas unless an amount was selected.Should only call trusted methods so re - entrance is less problematic.
            ulong gasBudget = (gasLimit != 0) ? gasLimit : smartContractState.GasMeter.GasAvailable;

            var message = new CallMessage
            {
                To = addressTo.ToUint160(this.network),
                From = smartContractState.Message.ContractAddress.ToUint160(this.network),
                Amount = amountToTransfer,
                Code = contractCode,
                GasLimit = (Gas) gasBudget,
                Method = new MethodCall(methodName, parameters)
            };

            // Ensure we have enough gas left to be able to fund the new GasMeter.
            if (smartContractState.GasMeter.GasAvailable < message.GasLimit)
                throw new InsufficientGasException();

            var state = this.baseState.Nest(amountToTransfer);

            var stateTransition = new StateTransition(this.internalTransactionExecutorFactory, state, this.vm, this.network);

            (var result, var _, uint160 _) = stateTransition.Apply(message);

            // TODO this should also be done in the state transition
            this.baseState.InternalTransfers.Add(new TransferInfo
            {
                From = smartContractState.Message.ContractAddress.ToUint160(this.network),
                To = addressTo.ToUint160(this.network),
                Value = amountToTransfer
            });

            this.logger.LogTrace("(-)");

            return TransferResult.Transferred(result.Result);
        }

        ///<inheritdoc />
        public ITransferResult Transfer(ISmartContractState smartContractState, Address addressTo, ulong amountToTransfer)
        {
            this.logger.LogTrace("({0}:{1},{2}:{3})", nameof(addressTo), addressTo, nameof(amountToTransfer), amountToTransfer);

            // TODO: Spend BaseFee here

            EnsureContractHasEnoughBalance(smartContractState, amountToTransfer);

            // Discern whether this is a contract or an ordinary address.
            // TODO this should also be done in the state transition
            byte[] contractCode = this.baseState.Repository.GetCode(addressTo.ToUint160(this.network));

            if (contractCode == null || contractCode.Length == 0)
            {
                this.baseState.InternalTransfers.Add(new TransferInfo
                {
                    From = smartContractState.Message.ContractAddress.ToUint160(this.network),
                    To = addressTo.ToUint160(this.network),
                    Value = amountToTransfer
                });

                this.logger.LogTrace("(-)[TRANSFER_TO_SENDER]:Transfer {0} from {1} to {2}.", smartContractState.Message.ContractAddress, addressTo, amountToTransfer);
                return TransferResult.Empty();
            }

            this.logger.LogTrace("(-)[TRANSFER_TO_CONTRACT]");

            // Calling a receive handler:
            ulong gasBudget = DefaultGasLimit; // for Transfer always send limited gas to prevent re-entrance.

            var message = new ContractTransferMessage
            {
                To = addressTo.ToUint160(this.network),
                From = smartContractState.Message.ContractAddress.ToUint160(this.network),
                Amount = amountToTransfer,
                GasLimit = (Gas) gasBudget,
                Code = contractCode
            };

            // Ensure we have enough gas left to be able to fund the new GasMeter.
            if (smartContractState.GasMeter.GasAvailable < message.GasLimit)
                throw new InsufficientGasException();

            var state = this.baseState.Nest(amountToTransfer);

            var stateTransition = new StateTransition(this.internalTransactionExecutorFactory, state, this.vm, this.network);

            (var result, var _, uint160 _) = stateTransition.Apply(message);

            // TODO this should also be done in the state transition
            this.baseState.InternalTransfers.Add(new TransferInfo
            {
                From = smartContractState.Message.ContractAddress.ToUint160(this.network),
                To = addressTo.ToUint160(this.network),
                Value = amountToTransfer
            });

            this.logger.LogTrace("(-)");

            return TransferResult.Transferred(result.Result);
        }

        /// <summary>
        /// Throws an exception if a contract doesn't have a high enough balance to make this transaction.
        /// </summary>
        private void EnsureContractHasEnoughBalance(ISmartContractState smartContractState, ulong amountToTransfer)
        {
            ulong balance = smartContractState.GetBalance();
            if (balance < amountToTransfer)
            {
                this.logger.LogTrace("(-)[INSUFFICIENT_BALANCE]:{0}={1}", nameof(balance), balance);
                throw new InsufficientBalanceException();
            }
        }
    }
}