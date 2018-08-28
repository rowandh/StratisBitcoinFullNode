using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.SmartContracts.Core;
using Stratis.SmartContracts.Core.Exceptions;
using Stratis.SmartContracts.Core.State;
using Stratis.SmartContracts.Core.State.AccountAbstractionLayer;
using Stratis.SmartContracts.Executor.Reflection.ContractLogging;
using Block = Stratis.SmartContracts.Core.Block;

namespace Stratis.SmartContracts.Executor.Reflection
{
    ///<inheritdoc/>
    public sealed class InternalTransactionExecutor : IInternalTransactionExecutor
    {
        private const ulong DefaultGasLimit = GasPriceList.BaseCost - 1;

        private readonly IAddressGenerator addressGenerator;
        private readonly IContractLogHolder contractLogHolder;
        private readonly IContractStateRepository contractStateRepository;
        private readonly List<TransferInfo> internalTransferList;
        private readonly IKeyEncodingStrategy keyEncodingStrategy;
        private readonly ILogger logger;
        private readonly ILoggerFactory loggerFactory;
        private readonly Network network;
        private readonly ISmartContractVirtualMachine vm;
        private readonly ITransactionContext transactionContext;
        private readonly IState baseState;

        public InternalTransactionExecutor(ITransactionContext transactionContext,
            ISmartContractVirtualMachine vm,
            IContractLogHolder contractLogHolder,
            IContractStateRepository contractStateRepository,
            List<TransferInfo> internalTransferList,
            IKeyEncodingStrategy keyEncodingStrategy,
            IAddressGenerator addressGenerator,
            ILoggerFactory loggerFactory,
            Network network,
            IState state)
        {
            this.transactionContext = transactionContext;
            this.contractLogHolder = contractLogHolder;
            this.contractStateRepository = contractStateRepository;
            this.internalTransferList = internalTransferList;
            this.keyEncodingStrategy = keyEncodingStrategy;
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

            ulong gasBudget = (gasLimit != 0) ? gasLimit : DefaultGasLimit;

            // Ensure we have enough gas left to be able to fund the new GasMeter.
            if (smartContractState.GasMeter.GasAvailable < gasBudget)
                throw new InsufficientGasException();

            // Check balance.
            EnsureContractHasEnoughBalance(smartContractState, amountToTransfer);

            // Build objects for VM
            byte[] contractCode = this.contractStateRepository.GetCode(smartContractState.Message.ContractAddress.ToUint160(this.network)); // TODO: Fix this when calling from constructor.

            var address = this.addressGenerator.GenerateAddress(this.baseState.TransactionHash, this.baseState.GetNonceAndIncrement());

            var message = new Message();

            message.To = address;
            message.From = smartContractState.Message.ContractAddress.ToUint160(this.network);
            message.Amount = amountToTransfer;
            message.Code = contractCode;
            message.GasLimit = (Gas) gasBudget;
            message.Method = new MethodCall(null, parameters);
            message.IsCreation = true;

            var nestedState = this.baseState.Nest(amountToTransfer);

            var stateTransition = new StateTransition(nestedState, this.vm, this.network, message);

            (var result, var nestedGasMeter) = stateTransition.Apply();

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

            byte[] contractCode = this.contractStateRepository.GetCode(addressTo.ToUint160(this.network));
            if (contractCode == null || contractCode.Length == 0)
            {
                return TransferResult.Empty();
            }

            // Here, we know contract has code, so we execute it
            // For a method call, send all the gas unless an amount was selected.Should only call trusted methods so re - entrance is less problematic.
            ulong gasBudget = (gasLimit != 0) ? gasLimit : smartContractState.GasMeter.GasAvailable;

            return ExecuteTransferFundsToContract(smartContractState, addressTo, amountToTransfer, methodName, parameters, gasBudget);
        }

        ///<inheritdoc />
        public ITransferResult Transfer(ISmartContractState smartContractState, Address addressTo, ulong amountToTransfer)
        {
            this.logger.LogTrace("({0}:{1},{2}:{3})", nameof(addressTo), addressTo, nameof(amountToTransfer), amountToTransfer);

            // TODO: Spend BaseFee here

            EnsureContractHasEnoughBalance(smartContractState, amountToTransfer);

            // Discern whether this is a contract or an ordinary address.
            byte[] contractCode = this.contractStateRepository.GetCode(addressTo.ToUint160(this.network));
            if (contractCode == null || contractCode.Length == 0)
            {
                this.internalTransferList.Add(new TransferInfo
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
            string methodName = MethodCall.ExternalReceiveHandlerName;
            object[] parameters = new object[] { };
            ulong gasBudget = DefaultGasLimit; // for Transfer always send limited gas to prevent re-entrance.

            return ExecuteTransferFundsToContract(smartContractState, addressTo, amountToTransfer, methodName, parameters, gasBudget);
        }

        /// <summary>
        /// If the address to where the funds will be tranferred to is a contract, instantiate and execute it.
        /// </summary>
        private ITransferResult ExecuteTransferFundsToContract(ISmartContractState smartContractState,
            Address addressTo,
            ulong amountToTransfer,
            string methodName,
            object[] parameters,
            ulong gasBudget)
        {
            this.logger.LogTrace("({0}:{1},{2}:{3})", nameof(addressTo), addressTo, nameof(amountToTransfer), amountToTransfer);

            // Ensure we have enough gas left to be able to fund the new GasMeter.
            if (smartContractState.GasMeter.GasAvailable < gasBudget)
                throw new InsufficientGasException();

            var nestedGasMeter = new GasMeter((Gas)gasBudget);

            IContractStateRepository track = this.contractStateRepository.StartTracking();

            var callData = new CallData((Gas) gasBudget, addressTo.ToUint160(this.network), methodName, parameters);
            
            var context = new TransactionContext(
                this.transactionContext.TransactionHash,
                this.transactionContext.BlockHeight,
                this.transactionContext.Coinbase,
                smartContractState.Message.ContractAddress.ToUint160(this.network),
                amountToTransfer,
                this.transactionContext.Nonce);

            var logHolder = new ContractLogHolder(this.network);

            var state = this.SetupState(smartContractState, logHolder, this.internalTransferList,
                nestedGasMeter, track, context, callData.ContractAddress);

            var call = new MethodCall(methodName, parameters);
            var address = addressTo.ToUint160(this.network);
            var code = track.GetCode(address);
            var type = track.GetContractType(address);

            VmExecutionResult result = this.vm.ExecuteMethod(call,
                state,
                code,
                type);

            // Update parent gas meter.
            smartContractState.GasMeter.Spend(nestedGasMeter.GasConsumed);

            var revert = result.ExecutionException != null;

            if (revert)
            {
                track.Rollback();
                return TransferResult.Failed(result.ExecutionException);
            }

            track.Commit();

            this.internalTransferList.Add(new TransferInfo
            {
                From = smartContractState.Message.ContractAddress.ToUint160(this.network),
                To = addressTo.ToUint160(this.network),
                Value = amountToTransfer
            });

            this.contractLogHolder.AddRawLogs(logHolder.GetRawLogs());

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

        /// <summary>
        /// Sets up the state object for the contract execution
        /// </summary>
        private ISmartContractState SetupState(
            ISmartContractState currentState,
            IContractLogHolder contractLogger,
            List<TransferInfo> itl,
            IGasMeter gasMeter,
            IContractStateRepository repository,
            ITransactionContext txContext,
            uint160 contractAddress)
        {
            IPersistenceStrategy persistenceStrategy =
                new MeteredPersistenceStrategy(repository, gasMeter, new BasicKeyEncodingStrategy());

            var persistentState = new PersistentState(persistenceStrategy, ((PersistentState)currentState.PersistentState).Serializer, contractAddress);

            var balanceState = new BalanceState(repository, txContext.Amount, itl);

            var contractState = new SmartContractState(
                new Block(
                    txContext.BlockHeight,
                    txContext.Coinbase.ToAddress(this.network)
                ),
                new Core.Message(
                    contractAddress.ToAddress(this.network),
                    txContext.From.ToAddress(this.network),
                    txContext.Amount
                ),
                persistentState,
                gasMeter,
                contractLogger,
                this,
                new InternalHashHelper(),
                () => balanceState.GetBalance(contractAddress));

            return contractState;
        }
    }
}