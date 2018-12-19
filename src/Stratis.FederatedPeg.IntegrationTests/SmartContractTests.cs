using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Stratis.Bitcoin.Features.SmartContracts.Models;
using Stratis.Bitcoin.Features.Wallet;
using Stratis.Bitcoin.IntegrationTests.Common;
using Stratis.FederatedPeg.IntegrationTests.Utils;
using Stratis.SmartContracts.CLR.Compilation;
using Xunit;

namespace Stratis.FederatedPeg.IntegrationTests
{
    public class SmartContractTests : IClassFixture<SidechainTestContextFixture>
    {
        private readonly SidechainTestContext context;

        public SmartContractTests(SidechainTestContextFixture contextFixture)
        {
            this.context = contextFixture.Context;
            contextFixture.Initialize().Wait(); // TODO: Dirty, fix later
        }

        [Fact]
        public async Task CanCreateContract()
        {
            string fundedAddress = this.context.GetAddressBalances(this.context.SideUser).First().Address; // TODO: This makes assumptions about the addresses held byt this node - should be altered as we add more tests

            // Send create contract tx
            byte[] contractCode = ContractCompiler.CompileFile("SmartContracts/BasicTransfer.cs").Compilation;
            BuildCreateContractTransactionResponse createResponse = await this.context.SendCreateContractTransaction(this.context.SideUser, contractCode, 1, fundedAddress);

            // Block is mined
            int currentHeight = this.context.SideUser.FullNode.Chain.Height;
            TestHelper.WaitLoop(() => this.context.SideUser.FullNode.Chain.Height >= currentHeight + 1);

            // Contract code is stored as expected
            Assert.NotNull(this.context.GetContractCode(this.context.SideUser, createResponse.NewContractAddress));
        }
    }
}
