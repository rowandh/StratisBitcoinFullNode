﻿using System;
using System.Collections.Generic;
using Moq;
using Stratis.SmartContracts.Core.State;
using Stratis.SmartContracts.Core.State.AccountAbstractionLayer;
using Stratis.SmartContracts.Executor.Reflection;
using Stratis.SmartContracts.Executor.Reflection.ContractLogging;
using Xunit;

namespace Stratis.Bitcoin.Features.SmartContracts.Tests
{
    public class StateTests
    {
        private readonly Mock<IContractStateRoot> contractStateRoot;
        private readonly Mock<IContractState> trackedState;

        public StateTests()
        {
            this.trackedState = new Mock<IContractState>();
            this.contractStateRoot = new Mock<IContractStateRoot>();
            this.contractStateRoot.Setup(c => c.StartTracking())
                .Returns(this.trackedState.Object);
        }

        [Fact]
        public void State_Snapshot_Uses_Tracked_ContractState()
        {
            var state = new State(
                null,
                null,
                null,
                this.contractStateRoot.Object,
                null,
                null,
                0,
                null,
                null
            );

            IState newState = state.Snapshot();

            this.contractStateRoot.Verify(s => s.StartTracking(), Times.Once);

            Assert.NotSame(newState.ContractState, state.ContractState);
        }

        [Fact]
        public void TransitionTo_Fails_If_New_State_Is_Not_Child()
        {
            var state = new State(
                null,
                null,
                null,
                this.contractStateRoot.Object,
                null,
                null,
                0,
                null,
                null
            );

            IState newState = state.Snapshot();

            IState newState2 = newState.Snapshot();

            Assert.Throws<ArgumentException>(() => state.TransitionTo(newState2));
        }

        [Fact]
        public void TransitionTo_Updates_State_Correctly()
        {
            var state = new State(
                null,
                null,
                null,
                this.contractStateRoot.Object,
                null,
                null,
                0,
                null,
                null
            );

            // TODO pass in initial internal transfers list
            // TODO pass in initial contract log holder

            var newTransfers = new List<TransferInfo>
            {
                new TransferInfo(),
                new TransferInfo(),
                new TransferInfo()
            };

            var newLogs = new List<RawLog>
            {
                new RawLog(null, null),
                new RawLog(null, null)
            };

            ulong newNonce = 999;

            var testLogHolder = new Mock<IContractLogHolder>();
            testLogHolder.Setup(lh => lh.GetRawLogs())
                .Returns(newLogs);

            var testState = new Mock<IState>();
            testState.SetupGet(ts => ts.InternalTransfers)
                .Returns(newTransfers);
            testState.Setup(ts => ts.LogHolder)
                .Returns(testLogHolder.Object);
            testState.Setup(ts => ts.ContractState)
                .Returns(this.trackedState.Object);
            testState.Setup(ts => ts.Nonce)
                .Returns(newNonce);

            state.SetPrivateFieldValue("child", testState.Object);

            state.TransitionTo(testState.Object);

            this.trackedState.Verify(s => s.Commit(), Times.Once);

            Assert.Equal(newTransfers.Count, state.InternalTransfers.Count);
            Assert.Contains(newTransfers[0], state.InternalTransfers);
            Assert.Contains(newTransfers[1], state.InternalTransfers);
            Assert.Contains(newTransfers[2], state.InternalTransfers);
            Assert.Contains(newLogs[0], state.LogHolder.GetRawLogs());
            Assert.Contains(newLogs[1], state.LogHolder.GetRawLogs());
            Assert.Equal(newNonce, state.Nonce);
        }
    }
}