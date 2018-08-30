﻿using Microsoft.Extensions.Logging;
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

            // Here, we know contract has code, so we execute it
            // For a method call, send all the gas unless an amount was selected.Should only call trusted methods so re - entrance is less problematic.
            ulong gasBudget = (gasLimit != 0) ? gasLimit : smartContractState.GasMeter.GasAvailable;
            
            var message = new CallMessage
            {
                To = addressTo.ToUint160(this.network),
                From = smartContractState.Message.ContractAddress.ToUint160(this.network),
                Amount = amountToTransfer,
                GasLimit = (Gas) gasBudget,
                Method = new MethodCall(methodName, parameters)
            };

            var state = this.baseState.Nest(amountToTransfer);

            var stateTransition = new StateTransition(this.internalTransactionExecutorFactory, state, this.vm, this.network);

            (var result, var _, uint160 _) = stateTransition.Apply(message);

            // TODO null currently used to indicate a transfer only took place
            if (result == null) return TransferResult.Empty();

            return TransferResult.Transferred(result.Result);
        }

        ///<inheritdoc />
        public ITransferResult Transfer(ISmartContractState smartContractState, Address addressTo, ulong amountToTransfer)
        {
            this.logger.LogTrace("({0}:{1},{2}:{3})", nameof(addressTo), addressTo, nameof(amountToTransfer), amountToTransfer);

            // TODO: Spend BaseFee here

            EnsureContractHasEnoughBalance(smartContractState, amountToTransfer);

            // Calling a receive handler:
            ulong gasBudget = DefaultGasLimit; // for Transfer always send limited gas to prevent re-entrance.

            // Ensure we have enough gas left to be able to fund the new GasMeter.
            if (smartContractState.GasMeter.GasAvailable < gasBudget)
                throw new InsufficientGasException();

            var message = new ContractTransferMessage
            {
                To = addressTo.ToUint160(this.network),
                From = smartContractState.Message.ContractAddress.ToUint160(this.network),
                Amount = amountToTransfer,
                GasLimit = (Gas) gasBudget
            };

            var state = this.baseState.Nest(amountToTransfer);

            var stateTransition = new StateTransition(this.internalTransactionExecutorFactory, state, this.vm, this.network);

            (var result, var _, uint160 _) = stateTransition.Apply(message);

            // TODO null currently used to indicate a transfer only took place
            if (result == null) return TransferResult.Empty();

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