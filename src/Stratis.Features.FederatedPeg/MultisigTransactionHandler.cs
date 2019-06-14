using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.Policy;
using Stratis.Bitcoin.Features.Wallet;
using Stratis.Bitcoin.Features.Wallet.Interfaces;
using Stratis.Bitcoin.Utilities;
using Stratis.Features.FederatedPeg.Interfaces;
using Stratis.Features.FederatedPeg.Models;
using Stratis.Features.FederatedPeg.TargetChain;
using Stratis.Features.FederatedPeg.Wallet;

namespace Stratis.Features.FederatedPeg
{
    public interface IMultisigTransactionHandler
    {
        /// <summary>
        /// Builds a new multisig transaction based on information from the <see cref="FederatedPeg.Wallet.TransactionBuildContext"/>.
        /// </summary>
        /// <returns>The new transaction.</returns>
        Transaction BuildTransaction(BuildMultisigTransactionRequest request);
    }

    /// <summary>
    /// A handler that has various functionalities related to transaction operations.
    /// </summary>
    public class MultisigTransactionHandler : IMultisigTransactionHandler
    {
        private readonly IFederationWalletTransactionHandler federationWalletTransactionHandler;

        private readonly IFederationWalletManager federationWalletManager;

        private readonly IFederatedPegSettings federatedPegSettings;

        private readonly ILogger logger;

        private readonly Network network;

        public MultisigTransactionHandler(ILoggerFactory loggerFactory,
            Network network,
            IFederationWalletTransactionHandler federationWalletTransactionHandler,
            IFederationWalletManager federationWalletManager,
            IFederatedPegSettings federatedPegSettings)
        {
            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
            this.network = network;
            this.federationWalletTransactionHandler = federationWalletTransactionHandler;
            this.federationWalletManager = federationWalletManager;
            this.federatedPegSettings = federatedPegSettings;
        }

        public Transaction BuildTransaction(BuildMultisigTransactionRequest request)
        {
            // Builds a transaction on mainnet for withdrawing federation funds
            try
            {
                List<Wallet.Recipient> recipients = request
                    .Recipients
                    .Select(recipientModel => new Wallet.Recipient
                    {
                        ScriptPubKey = BitcoinAddress.Create(recipientModel.DestinationAddress, this.network).ScriptPubKey,
                        Amount = recipientModel.Amount
                    })
                    .ToList();

                // Build the multisig transaction template.
                string walletPassword = this.federationWalletManager.Secret.WalletPassword;

                //bool sign = (walletPassword ?? "") != "";

                var multiSigContext = new Wallet.TransactionBuildContext(recipients)
                {
                    MinConfirmations = WithdrawalTransactionBuilder.MinConfirmations,
                    Shuffle = false,
                    IgnoreVerify = true,
                    WalletPassword = walletPassword,
                    Sign = true,
                    //Time = this.network.Consensus.IsProofOfStake ? blockTime : (uint?)null
                };

                //multiSigContext.Recipients = new List<Recipient> { recipient.WithPaymentReducedByFee(FederatedPegSettings.CrossChainTransferFee) }; // The fee known to the user is taken.

                (List<Coin> coins, List<Wallet.UnspentOutputReference> unspentOutputs) = FederationWalletTransactionHandler.DetermineCoins(this.federationWalletManager, this.network, multiSigContext, this.federatedPegSettings);

                //multiSigContext.TransactionFee = this.federatedPegSettings.GetWithdrawalTransactionFee(coins.Count); // The "actual fee". Everything else goes to the fed.
                multiSigContext.SelectedInputs = unspentOutputs.Select(u => u.ToOutPoint()).ToList();
                multiSigContext.AllowOtherInputs = false;

                // Build the unsigned transaction.
                Transaction transaction = this.federationWalletTransactionHandler.BuildTransaction(multiSigContext);

                this.logger.LogDebug("transaction = {0}", transaction.ToString(this.network, RawFormat.BlockExplorer));

                Key[] privateKeys = request
                    .Secrets
                    .Select(secret => new Mnemonic(secret.Mnemonic).DeriveExtKey(secret.Passphrase).PrivateKey)
                    .ToArray();


                //txBuilder.AddKeys(privateKeys);

                var signed = privateKeys.Select(pk =>
                {
                    var txBuilder = new TransactionBuilder(this.network);
                    txBuilder.AddKeys(pk);
                    return txBuilder.SignTransaction(transaction);
                })
                .ToArray();

                var combined = new TransactionBuilder(this.network).CombineSignatures(signed);

                var fee = transaction.GetFee(coins.Cast<ICoin>().ToArray());
                
                return combined;
            }
            catch (Exception error)
            {
                if (error is WalletException walletException &&
                    (walletException.Message == FederationWalletTransactionHandler.NoSpendableTransactionsMessage
                     || walletException.Message == FederationWalletTransactionHandler.NotEnoughFundsMessage))
                {
                    this.logger.LogWarning("Not enough spendable transactions in the wallet. Should be resolved when a pending transaction is included in a block.");
                }
                else
                {
                    this.logger.LogError("Could not create transaction {0}", error.Message);
                }
            }

            this.logger.LogTrace("(-)[FAIL]");

            return null;
        }
    }
}
