using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using NBitcoin;
using Stratis.Bitcoin.IntegrationTests.Common;
using Xunit;

namespace Stratis.FederatedPeg.IntegrationTests.Utils
{
    public class SidechainTestContextFixture : IDisposable
    {
        private bool hasInit;

        public SidechainTestContext Context { get; }

        public SidechainTestContextFixture()
        {
            this.Context = new SidechainTestContext();
            this.hasInit = false;
        }

        public async Task Initialize()
        {
            if (this.hasInit)
                return;

            // Set everything up
            this.Context.StartAndConnectNodes();
            this.Context.EnableSideFedWallets();
            this.Context.EnableMainFedWallets();

            // Fund a main chain node
            TestHelper.MineBlocks(this.Context.MainUser, (int)this.Context.MainChainNetwork.Consensus.CoinbaseMaturity + (int)this.Context.MainChainNetwork.Consensus.PremineHeight);
            TestHelper.WaitForNodeToSync(this.Context.MainUser, this.Context.FedMain1);
            Assert.True(this.Context.GetBalance(this.Context.MainUser) > this.Context.MainChainNetwork.Consensus.PremineReward);

            // Let sidechain progress to point where fed has the premine
            TestHelper.WaitLoop(() => this.Context.SideUser.FullNode.Chain.Height >= this.Context.SideUser.FullNode.Network.Consensus.PremineHeight);
            TestHelper.WaitForNodeToSync(this.Context.SideUser, this.Context.FedSide1);
            Block block = this.Context.SideUser.FullNode.Chain.GetBlock((int)this.Context.SideChainNetwork.Consensus.PremineHeight).Block;
            Transaction coinbase = block.Transactions[0];
            Assert.Single(coinbase.Outputs);
            Assert.Equal(this.Context.SideChainNetwork.Consensus.PremineReward, coinbase.Outputs[0].Value);
            Assert.Equal(this.Context.scriptAndAddresses.payToMultiSig.PaymentScript, coinbase.Outputs[0].ScriptPubKey);

            // Send significant funds to sidechain user
            string sidechainAddress = this.Context.GetUnusedAddress(this.Context.SideUser);
            await this.Context.DepositToSideChain(this.Context.MainUser, 100_000, sidechainAddress);
            TestHelper.WaitLoop(() => this.Context.FedMain1.CreateRPCClient().GetRawMempool().Length == 1);
            TestHelper.MineBlocks(this.Context.FedMain1, 15);

            // Sidechain user has balance - transfer complete
            Assert.Equal(new Money(100_000, MoneyUnit.BTC), this.Context.GetBalance(this.Context.SideUser));

            this.hasInit = true;
        }

        public void Dispose()
        {
            this.Context.Dispose();
        }
    }
}