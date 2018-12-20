using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using CSharpFunctionalExtensions;
using Flurl;
using Flurl.Http;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using Newtonsoft.Json.Linq;
using Stratis.Bitcoin.Features.SmartContracts;
using Stratis.Bitcoin.Features.SmartContracts.ReflectionExecutor.Consensus.Rules;
using Stratis.Bitcoin.Features.SmartContracts.Wallet;
using Stratis.Bitcoin.Features.Wallet;
using Stratis.Bitcoin.Features.Wallet.Interfaces;
using Stratis.Bitcoin.Features.Wallet.Models;
using Stratis.Bitcoin.IntegrationTests.Common;
using Stratis.Bitcoin.IntegrationTests.Common.EnvironmentMockUpHelpers;
using Stratis.Bitcoin.Networks;
using Stratis.Bitcoin.Utilities.JsonErrors;
using Stratis.FederatedPeg.Features.FederationGateway;
using Stratis.FederatedPeg.IntegrationTests.Utils;
using Stratis.Sidechains.Networks;
using Stratis.SmartContracts.CLR;
using Stratis.SmartContracts.CLR.Compilation;
using Stratis.SmartContracts.Core;
using Stratis.SmartContracts.Core.State;
using Xunit;

namespace Stratis.FederatedPeg.IntegrationTests
{
    public class ContractExecution
    {
        private const string WalletName = "mywallet";
        private const string WalletPassword = "password";
        private const string WalletPassphrase = "passphrase";
        private const string WalletAccount = "account 0";

        private FederatedPegRegTest network;

        public (Script payToMultiSig, BitcoinAddress sidechainMultisigAddress, BitcoinAddress mainchainMultisigAddress) scriptAndAddresses;

        [Fact]
        public async Task Test()
        {
            using (SidechainNodeBuilder nodeBuilder = SidechainNodeBuilder.CreateSidechainNodeBuilder(this))
            {
                this.network = new FederatedPegRegTest();
                IList<Mnemonic> mnemonics = network.FederationMnemonics;
                var pubKeysByMnemonic = mnemonics.ToDictionary(m => m, m => m.DeriveExtKey().PrivateKey.PubKey);
                this.scriptAndAddresses = this.GenerateScriptAndAddresses(new StratisMain(), network, 2, pubKeysByMnemonic);

                CoreNode user1 = nodeBuilder.CreateSidechainNode(network).WithWallet();
                CoreNode fed1 = nodeBuilder.CreateSidechainFederationNode(network, network.FederationKeys[0]).WithWallet();
                //CoreNode fed2 = nodeBuilder.CreateSidechainFederationNode(network, network.FederationKeys[1]).WithWallet();
                this.AppendToConfig(fed1, "sidechain=1");
                this.AppendToConfig(fed1, $"{FederationGatewaySettings.RedeemScriptParam}={this.scriptAndAddresses.payToMultiSig.ToString()}");
                this.AppendToConfig(fed1, $"{FederationGatewaySettings.PublicKeyParam}={pubKeysByMnemonic[mnemonics[0]].ToString()}");
                //this.AppendToConfig(fed2, "sidechain=1");
                //this.AppendToConfig(fed2, $"{FederationGatewaySettings.RedeemScriptParam}={this.scriptAndAddresses.payToMultiSig.ToString()}");
                //this.AppendToConfig(fed2, $"{FederationGatewaySettings.PublicKeyParam}={pubKeysByMnemonic[mnemonics[1]].ToString()}");

                user1.Start();
                fed1.Start();

                TestHelper.Connect(user1, fed1);

                // Let fed1 get the premine
                TestHelper.WaitLoop(() => user1.FullNode.Chain.Height > network.Consensus.PremineHeight + network.Consensus.CoinbaseMaturity);

                //fed2.Start();
                //TestHelper.Connect(fed1, fed2);
                //TestHelper.Connect(user1, fed2);

                // Send funds from fed1 to user1
                string user1Address = this.GetUnusedAddress(user1);
                Script scriptPubKey = PayToPubkeyHashTemplate.Instance.GenerateScriptPubKey(new BitcoinPubKeyAddress(user1Address, network));
                Result<WalletSendTransactionModel> result = SendTransaction(fed1, scriptPubKey, new Money(100_000, MoneyUnit.BTC));
                Assert.True(result.IsSuccess);
                int currentHeight = user1.FullNode.Chain.Height;
                TestHelper.WaitLoop(() => user1.FullNode.Chain.Height > currentHeight + 2);

                // Send new SC tx from user
                Assert.Equal(new Money(100_000, MoneyUnit.BTC), this.GetBalance(user1));
                byte[] contractCode = ContractCompiler.CompileFile("SmartContracts/BasicTransfer.cs").Compilation;
                string newContractAddress = await SendCreateContractTransaction(user1, contractCode, 0, user1Address);
                TestHelper.WaitLoop(() => fed1.CreateRPCClient().GetRawMempool().Length == 1);
                //TestHelper.WaitLoop(() => fed2.CreateRPCClient().GetRawMempool().Length == 1);
                currentHeight = user1.FullNode.Chain.Height;
                TestHelper.WaitLoop(() => user1.FullNode.Chain.Height > currentHeight + 2);

                // Did code save?
                Assert.NotNull(this.GetContractCode(user1, newContractAddress));
                Assert.NotNull(this.GetContractCode(fed1, newContractAddress));
                //Assert.NotNull(this.GetContractCode(fed2, newContractAddress));
            }
        }

        /// <summary>
        /// Note this is only going to work on smart contract enabled (aka sidechain) nodes
        /// </summary>
        public byte[] GetContractCode(CoreNode node, string address)
        {
            IStateRepositoryRoot stateRoot = node.FullNode.NodeService<IStateRepositoryRoot>();
            return stateRoot.GetCode(address.ToUint160(this.network));
        }

        public async Task<string> SendCreateContractTransaction(CoreNode node,
            byte[] contractCode,
            double amount,
            string sender,
            string[] parameters = null,
            ulong gasLimit = SmartContractFormatRule.GasLimitMaximum / 2, // half of maximum
            ulong gasPrice = SmartContractMempoolValidator.MinGasPrice,
            double feeAmount = 0.01)
        {
            HttpResponseMessage createContractResponse = await $"http://localhost:{node.ApiPort}/api"
                .AppendPathSegment("SmartContracts/build-and-send-create")
                .PostJsonAsync(new
                {
                    amount = amount.ToString(),
                    accountName = WalletAccount,
                    contractCode = contractCode.ToHexString(),
                    feeAmount = feeAmount.ToString(),
                    gasLimit = gasLimit,
                    gasPrice = gasPrice,
                    parameters = parameters,
                    password = WalletPassword,
                    Sender = sender,
                    walletName = WalletName
                });

            string result = await createContractResponse.Content.ReadAsStringAsync();
            return JObject.Parse(result)["newContractAddress"].ToString();
        }

        public Money GetBalance(CoreNode node)
        {
            IEnumerable<Bitcoin.Features.Wallet.UnspentOutputReference> spendableOutputs = node.FullNode.WalletManager().GetSpendableTransactionsInWallet(WalletName);
            return spendableOutputs.Sum(x => x.Transaction.Amount);
        }

        public string GetUnusedAddress(CoreNode node)
        {
            return node.FullNode.WalletManager().GetUnusedAddress(new WalletAccountReference(WalletName, WalletAccount)).Address;
        }

        public Result<WalletSendTransactionModel> SendTransaction(CoreNode coreNode, Script scriptPubKey, Money amount)
        {
            var txBuildContext = new TransactionBuildContext(coreNode.FullNode.Network)
            {
                AccountReference = new WalletAccountReference(WalletName, WalletAccount),
                MinConfirmations = 1,
                FeeType = FeeType.Medium,
                WalletPassword = WalletPassword,
                Recipients = new[] { new Recipient { Amount = amount, ScriptPubKey = scriptPubKey } }.ToList(),
            };

            Transaction trx = (coreNode.FullNode.NodeService<IWalletTransactionHandler>() as SmartContractWalletTransactionHandler).BuildTransaction(txBuildContext);

            // Broadcast to the other node.

            IActionResult result =  coreNode.FullNode.NodeService<SmartContractWalletController>().SendTransaction(new SendTransactionRequest(trx.ToHex()));
            if (result is ErrorResult errorResult)
            {
                var errorResponse = (ErrorResponse)errorResult.Value;
                return Result.Fail<WalletSendTransactionModel>(errorResponse.Errors[0].Message);
            }

            JsonResult response = (JsonResult)result;
            return Result.Ok((WalletSendTransactionModel)response.Value);
        }

        public (Script payToMultiSig, BitcoinAddress sidechainMultisigAddress, BitcoinAddress mainchainMultisigAddress)
            GenerateScriptAndAddresses(Network mainchainNetwork, Network sidechainNetwork, int quorum, Dictionary<Mnemonic, PubKey> pubKeysByMnemonic)
        {
            Script payToMultiSig = PayToMultiSigTemplate.Instance.GenerateScriptPubKey(quorum, pubKeysByMnemonic.Values.ToArray());
            BitcoinAddress sidechainMultisigAddress = payToMultiSig.Hash.GetAddress(sidechainNetwork);
            BitcoinAddress mainchainMultisigAddress = payToMultiSig.Hash.GetAddress(mainchainNetwork);
            return (payToMultiSig, sidechainMultisigAddress, mainchainMultisigAddress);
        }

        private void AppendToConfig(CoreNode node, string configKeyValueItem)
        {
            using (StreamWriter sw = File.AppendText(node.Config))
            {
                sw.WriteLine(configKeyValueItem);
            }
        }
    }
}
