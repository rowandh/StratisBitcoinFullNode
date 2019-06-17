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
using Recipient = Stratis.Features.FederatedPeg.Wallet.Recipient;

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
                
                var now = Utils.DateTimeToUnixTime(DateTimeOffset.UtcNow);

                // FederationWalletTransactionHandler only supports signing with a single key - the fed wallet key
                // However we still want to use it to determine what coins we need, so hack this together here to pass in what FederationWalletTransactionHandler.DetermineCoins
                var multiSigContext = new Wallet.TransactionBuildContext(recipients)
                {
                    MinConfirmations = WithdrawalTransactionBuilder.MinConfirmations,
                    IgnoreVerify = true
                };

                (List<Coin> coins, List<Wallet.UnspentOutputReference> unspentOutputs) = FederationWalletTransactionHandler.DetermineCoins(this.federationWalletManager, this.network, multiSigContext, this.federatedPegSettings);

                var transactionBuilder = new TransactionBuilder(this.network);

                transactionBuilder.AddCoins(coins);

                MultiSigAddress changeAddress = this.federationWalletManager.GetWallet().MultiSigAddress;

                transactionBuilder.SetChange(changeAddress.ScriptPubKey);

                foreach (Recipient recipient in recipients)
                {
                    transactionBuilder.Send(recipient.ScriptPubKey, recipient.Amount);
                }

                Key[] privateKeys = request
                    .Secrets
                    .Select(secret => new Mnemonic(secret.Mnemonic).DeriveExtKey(secret.Passphrase).PrivateKey)
                    .ToArray();

                transactionBuilder.AddKeys(privateKeys);

                transactionBuilder.SetTimeStamp(now);

                Transaction transaction = transactionBuilder.BuildTransaction(true);

                return transaction;
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
