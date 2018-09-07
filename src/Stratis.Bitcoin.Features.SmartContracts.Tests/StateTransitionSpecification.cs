using Moq;
using NBitcoin;
using Stratis.Bitcoin.Features.SmartContracts.Networks;
using Stratis.SmartContracts;
using Stratis.SmartContracts.Core.State;
using Stratis.SmartContracts.Executor.Reflection;
using Stratis.SmartContracts.Executor.Reflection.Serialization;
using Xunit;

namespace Stratis.Bitcoin.Features.SmartContracts.Tests
{
    public class StateTransitionSpecification
    {
        [Fact]
        public void ExternalCreate_Success()
        {
            // Preconditions checked:
            // - Has enough gas
            // Execution checked:
            // - Address generator is called with correct txhash and nonce
            // - Start tracking called on ContractStateRepo
            // - Create account is called on ContractStateRepo
            // - ITE factory is called
            // - Create is called on VM with correct args
            // - Gas remaining should be correct
            // - Commit called on (correct) state
            // - Result has correct values

            var block = Mock.Of<IBlock>();
            var network = new SmartContractsRegTest();
            var transactionHash = new uint256();
            var expectedAddressGenerationNonce = 0UL;
            var newContractAddress = uint160.One;
            var gasLimit = (Gas) (GasPriceList.BaseCost + 100000);

            var externalCreateMessage = new ExternalCreateMessage(
                uint160.Zero,
                10,
                gasLimit,
                new byte[0],
                null
            );

            var serializer = Mock.Of<IContractPrimitiveSerializer>();
            var iteFactory = new Mock<IInternalTransactionExecutorFactory>();
            
            var trackedState = new Mock<IContractState>();
            var contractStateRoot = new Mock<IContractStateRoot>();            
            contractStateRoot.Setup(c => c.StartTracking())
                .Returns(trackedState.Object);

            var addressGenerator = new Mock<IAddressGenerator>();
            addressGenerator
                .Setup(a => a.GenerateAddress(transactionHash, expectedAddressGenerationNonce))
                .Returns(newContractAddress);

            var vmExecutionResult = VmExecutionResult.Success(true, "Test");
            var vm = new Mock<ISmartContractVirtualMachine>(MockBehavior.Strict);
            vm.Setup(v => v.Create(trackedState.Object, It.IsAny<ISmartContractState>(), externalCreateMessage.Code,
                    externalCreateMessage.Parameters, null))
                .Returns(vmExecutionResult);

            var state = new State(
                serializer,
                iteFactory.Object,
                vm.Object,
                contractStateRoot.Object,
                block,
                network,
                0,
                transactionHash,
                addressGenerator.Object,
                gasLimit
            );

            StateTransitionResult result = state.Apply(externalCreateMessage);

            addressGenerator.Verify(a => a.GenerateAddress(transactionHash, expectedAddressGenerationNonce), Times.Once);

            contractStateRoot.Verify(sr => sr.StartTracking(), Times.Once);

            trackedState.Verify(ts => ts.CreateAccount(newContractAddress), Times.Once);

            vm.Verify(v => v.Create(trackedState.Object, It.IsAny<ISmartContractState>(), externalCreateMessage.Code, externalCreateMessage.Parameters, null), Times.Once);

            trackedState.Verify(ts => ts.Commit(), Times.Once);

            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Success);
            Assert.Equal(trackedState.Object, state.GetPrivateFieldValue("intermediateState"));
            Assert.Equal(newContractAddress, result.Success.ContractAddress);
            Assert.Equal(vmExecutionResult.Result, result.Success.ExecutionResult);
            Assert.Equal(gasLimit - result.GasConsumed, state.GasRemaining);
            // In this test we only ever spend the base fee.
            Assert.Equal(GasPriceList.BaseCost, result.GasConsumed);
        }

        [Fact]
        public void ExternalCreate_Nested_Create_Success()
        {
            var block = Mock.Of<IBlock>();
            var network = new SmartContractsRegTest();
            var transactionHash = new uint256();
            var expectedAddressGenerationNonce = 0UL;
            var newContractAddress = uint160.One;
            var newContractAddress2 = new uint160(2);

            var gasLimit = (Gas)(GasPriceList.BaseCost + 100000);

            var externalCreateMessage = new ExternalCreateMessage(
                uint160.Zero,
                10,
                gasLimit,
                new byte[0],
                null
            );

            var internalCreateMessage = new InternalCreateMessage(
                newContractAddress,
                0,
                (Gas) (GasPriceList.BaseCost + 1000),
                null,
                "Test"
            );

            var serializer = Mock.Of<IContractPrimitiveSerializer>();
            var iteFactory = new Mock<IInternalTransactionExecutorFactory>();

            var trackedState2 = new Mock<IContractState>();
            var trackedState = new Mock<IContractState>();
            trackedState.Setup(c => c.StartTracking())
                .Returns(trackedState2.Object);

            var contractStateRoot = new Mock<IContractStateRoot>();
            contractStateRoot.Setup(c => c.StartTracking())
                .Returns(trackedState.Object);

            var addressGenerator = new Mock<IAddressGenerator>();
            addressGenerator
                .Setup(a => a.GenerateAddress(transactionHash, expectedAddressGenerationNonce))
                .Returns(newContractAddress);

            addressGenerator
                .Setup(a => a.GenerateAddress(transactionHash, expectedAddressGenerationNonce + 1))
                .Returns(newContractAddress2);

            var vmExecutionResult = VmExecutionResult.Success(true, "Test");
            var vmExecutionResult2 = VmExecutionResult.Success(true, "NestedTest");

            var vm = new Mock<ISmartContractVirtualMachine>(MockBehavior.Strict);

            var state = new State(
                serializer,
                iteFactory.Object,
                vm.Object,
                contractStateRoot.Object,
                block,
                network,
                0,
                transactionHash,
                addressGenerator.Object,
                gasLimit
            );

            // Setup the VM to invoke the state with a nested internal create
            vm.Setup(v => v.Create(trackedState.Object, It.IsAny<ISmartContractState>(), externalCreateMessage.Code,
                    externalCreateMessage.Parameters, null))
                .Callback(() => state.Apply(internalCreateMessage))
                .Returns(vmExecutionResult);

            // Setup the nested VM create result
            vm.Setup(v => v.Create(trackedState2.Object, It.IsAny<ISmartContractState>(), externalCreateMessage.Code,
                    internalCreateMessage.Parameters, internalCreateMessage.Type))
                .Returns(vmExecutionResult2);

            StateTransitionResult result = state.Apply(externalCreateMessage);

            addressGenerator.Verify(a => a.GenerateAddress(transactionHash, expectedAddressGenerationNonce), Times.Once);

            contractStateRoot.Verify(sr => sr.StartTracking(), Times.Once);

            trackedState.Verify(ts => ts.CreateAccount(newContractAddress), Times.Once);

            vm.Verify(v => v.Create(trackedState.Object, It.IsAny<ISmartContractState>(), externalCreateMessage.Code, externalCreateMessage.Parameters, null), Times.Once);

            // Nesting begins here
            // The nested executor starts tracking on the parent state
            trackedState.Verify(ts => ts.StartTracking(), Times.Once);

            // Nested state transition generates a new address with the next nonce
            addressGenerator.Verify(a => a.GenerateAddress(transactionHash, expectedAddressGenerationNonce + 1), Times.Once);

            // VM is called with all nested state params and the original code
            vm.Verify(v => v.Create(trackedState2.Object, It.IsAny<ISmartContractState>(), externalCreateMessage.Code, internalCreateMessage.Parameters, internalCreateMessage.Type), Times.Once);

            // The nested executor calls commit on the nested tracked state
            trackedState2.Verify(ts => ts.Commit(), Times.Once);

            Assert.Equal(1, state.InternalTransfers.Count);
            // TODO - It's a hack to need to test the internal state of the object like this.
            // We expect the intermediateState to be the last "committed to" state
            Assert.Equal(trackedState2.Object, state.GetPrivateFieldValue("intermediateState"));
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Success);
            Assert.Equal(gasLimit - result.GasConsumed, state.GasRemaining);

            // Two nested operations
            Assert.Equal(GasPriceList.BaseCost * 2, result.GasConsumed);
        }

        [Fact]
        public void ExternalCreate_Nested_Create_Failure()
        {
            var block = Mock.Of<IBlock>();
            var network = new SmartContractsRegTest();
            var transactionHash = new uint256();
            var expectedAddressGenerationNonce = 0UL;
            var newContractAddress = uint160.One;
            var newContractAddress2 = new uint160(2);

            var gasLimit = (Gas)(GasPriceList.BaseCost + 100000);

            var externalCreateMessage = new ExternalCreateMessage(
                uint160.Zero,
                10,
                gasLimit,
                new byte[0],
                null
            );

            var internalCreateMessage = new InternalCreateMessage(
                newContractAddress,
                0,
                (Gas)(GasPriceList.BaseCost + 1000),
                null,
                "Test"
            );

            var serializer = Mock.Of<IContractPrimitiveSerializer>();
            var iteFactory = new Mock<IInternalTransactionExecutorFactory>();

            var trackedState2 = new Mock<IContractState>();
            var trackedState = new Mock<IContractState>();
            trackedState.Setup(c => c.StartTracking())
                .Returns(trackedState2.Object);

            var contractStateRoot = new Mock<IContractStateRoot>();
            contractStateRoot.Setup(c => c.StartTracking())
                .Returns(trackedState.Object);

            var addressGenerator = new Mock<IAddressGenerator>();
            addressGenerator
                .Setup(a => a.GenerateAddress(transactionHash, expectedAddressGenerationNonce))
                .Returns(newContractAddress);

            addressGenerator
                .Setup(a => a.GenerateAddress(transactionHash, expectedAddressGenerationNonce + 1))
                .Returns(newContractAddress2);

            var vmExecutionResult = VmExecutionResult.Success(true, "Test");
            var vmExecutionResult2 = VmExecutionResult.Error(new SmartContractAssertException("Error"));

            var vm = new Mock<ISmartContractVirtualMachine>(MockBehavior.Strict);

            var state = new State(
                serializer,
                iteFactory.Object,
                vm.Object,
                contractStateRoot.Object,
                block,
                network,
                0,
                transactionHash,
                addressGenerator.Object,
                gasLimit
            );

            // Setup the VM to invoke the state with a nested internal create
            vm.Setup(v => v.Create(trackedState.Object, It.IsAny<ISmartContractState>(), externalCreateMessage.Code,
                    externalCreateMessage.Parameters, null))
                .Callback(() => state.Apply(internalCreateMessage))
                .Returns(vmExecutionResult);

            // Setup the nested VM create result
            vm.Setup(v => v.Create(trackedState2.Object, It.IsAny<ISmartContractState>(), externalCreateMessage.Code,
                    internalCreateMessage.Parameters, internalCreateMessage.Type))
                .Returns(vmExecutionResult2);

            StateTransitionResult result = state.Apply(externalCreateMessage);

            addressGenerator.Verify(a => a.GenerateAddress(transactionHash, expectedAddressGenerationNonce), Times.Once);

            contractStateRoot.Verify(sr => sr.StartTracking(), Times.Once);

            trackedState.Verify(ts => ts.CreateAccount(newContractAddress), Times.Once);

            vm.Verify(v => v.Create(trackedState.Object, It.IsAny<ISmartContractState>(), externalCreateMessage.Code, externalCreateMessage.Parameters, null), Times.Once);

            // Nesting begins here
            // The nested executor starts tracking on the parent state
            trackedState.Verify(ts => ts.StartTracking(), Times.Once);

            // Nested state transition generates a new address with the next nonce
            addressGenerator.Verify(a => a.GenerateAddress(transactionHash, expectedAddressGenerationNonce + 1), Times.Once);

            // VM is called with all nested state params and the original code
            vm.Verify(v => v.Create(trackedState2.Object, It.IsAny<ISmartContractState>(), externalCreateMessage.Code, internalCreateMessage.Parameters, internalCreateMessage.Type), Times.Once);

            // The nested executor never calls commit on the nested tracked state due to the error
            trackedState2.Verify(ts => ts.Commit(), Times.Never);

            Assert.Equal(0, state.InternalTransfers.Count);
            // We expect the intermediateState to be the last "committed to" state
            Assert.Equal(trackedState.Object, state.GetPrivateFieldValue("intermediateState"));

            // Even though the internal creation failed, the operation was still successful
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Success);
            Assert.Equal(gasLimit - result.GasConsumed, state.GasRemaining);

            // Two nested operations
            Assert.Equal(GasPriceList.BaseCost * 2, result.GasConsumed);
        }

        [Fact]
        public void ExternalCreate_Vm_Error()
        {
            var block = Mock.Of<IBlock>();
            var network = new SmartContractsRegTest();
            var transactionHash = new uint256();
            var expectedAddressGenerationNonce = 0UL;
            var newContractAddress = uint160.One;
            var gasLimit = (Gas)(GasPriceList.BaseCost + 100000);

            var externalCreateMessage = new ExternalCreateMessage(
                uint160.Zero,
                10,
                gasLimit,
                new byte[0],
                null
            );

            var serializer = Mock.Of<IContractPrimitiveSerializer>();
            var iteFactory = new Mock<IInternalTransactionExecutorFactory>();

            var trackedState = new Mock<IContractState>();
            var contractStateRoot = new Mock<IContractStateRoot>();
            contractStateRoot.Setup(c => c.StartTracking())
                .Returns(trackedState.Object);

            var addressGenerator = new Mock<IAddressGenerator>();
            addressGenerator
                .Setup(a => a.GenerateAddress(transactionHash, expectedAddressGenerationNonce))
                .Returns(newContractAddress);

            var vmExecutionResult = VmExecutionResult.Error(new SmartContractAssertException("Error"));
            var vm = new Mock<ISmartContractVirtualMachine>(MockBehavior.Strict);
            vm.Setup(v => v.Create(trackedState.Object, It.IsAny<ISmartContractState>(), externalCreateMessage.Code,
                    externalCreateMessage.Parameters, null))
                .Returns(vmExecutionResult);

            var state = new State(
                serializer,
                iteFactory.Object,
                vm.Object,
                contractStateRoot.Object,
                block,
                network,
                0,
                transactionHash,
                addressGenerator.Object,
                gasLimit
            );

            StateTransitionResult result = state.Apply(externalCreateMessage);

            addressGenerator.Verify(a => a.GenerateAddress(transactionHash, expectedAddressGenerationNonce), Times.Once);

            contractStateRoot.Verify(sr => sr.StartTracking(), Times.Once);

            trackedState.Verify(ts => ts.CreateAccount(newContractAddress), Times.Once);

            vm.Verify(v => v.Create(trackedState.Object, It.IsAny<ISmartContractState>(), externalCreateMessage.Code, externalCreateMessage.Parameters, null), Times.Once);

            trackedState.Verify(ts => ts.Commit(), Times.Never);

            // TODO - It's a hack to need to test the internal state of the object like this.
            Assert.Equal(contractStateRoot.Object, state.GetPrivateFieldValue("intermediateState"));
            Assert.False(result.IsSuccess);
            Assert.True(result.IsFailure);
            Assert.NotNull(result.Error);
            Assert.Equal(vmExecutionResult.ExecutionException, result.Error.VmException);
            Assert.Equal(StateTransitionErrorKind.VmError, result.Error.Kind);
            Assert.Equal(gasLimit - result.GasConsumed, state.GasRemaining);
            Assert.Equal(GasPriceList.BaseCost, result.GasConsumed);
        }
    }
}