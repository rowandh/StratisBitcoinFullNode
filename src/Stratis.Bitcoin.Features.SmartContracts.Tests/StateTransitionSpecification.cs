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
            Assert.Equal(newContractAddress, result.Success.ContractAddress);
            Assert.Equal(vmExecutionResult.Result, result.Success.ExecutionResult);
            Assert.Equal(gasLimit - result.GasConsumed, state.GasRemaining);
            // In this test we only ever spend the base fee.
            Assert.Equal(GasPriceList.BaseCost, result.GasConsumed);
        }
    }
}