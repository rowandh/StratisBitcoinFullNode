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
using TransactionBuildContext = Stratis.Bitcoin.Features.Wallet.TransactionBuildContext;

namespace Stratis.Features.FederatedPeg
{
    public interface IMultisigTransactionHandler
    {
        /// <summary>
        /// Builds a new multisig transaction based on information from the <see cref="TransactionBuildContext"/>.
        /// </summary>
        /// <param name="context">The context that is used to build a new transaction.</param>
        /// <param name="secrets">List of mnemonic-passphrase pairs</param>
        /// <returns>The new transaction.</returns>
        Transaction BuildTransaction(TransactionBuildContext context, SecretModel[] secrets);

        Transaction BuildTransaction(BuildMultisigTransactionRequest request);
    }

    /// <summary>
    /// A handler that has various functionalities related to transaction operations.
    /// </summary>
    public class MultisigTransactionHandler : WalletTransactionHandler, IMultisigTransactionHandler
    {
        private readonly IFederationWalletTransactionHandler federationWalletTransactionHandler;

        private readonly IFederationWalletManager federationWalletManager;
        private readonly IFederatedPegSettings federatedPegSettings;

        private readonly ILogger logger;

        private readonly Network network;

        private readonly StandardTransactionPolicy transactionPolicy;

        private readonly IWalletManager walletManager;

        private readonly IWalletFeePolicy walletFeePolicy;

        public MultisigTransactionHandler(
            ILoggerFactory loggerFactory,
            IWalletManager walletManager,
            IWalletFeePolicy walletFeePolicy,
            Network network,
            StandardTransactionPolicy transactionPolicy,
            IFederationWalletTransactionHandler federationWalletTransactionHandler,
            IFederationWalletManager federationWalletManager,
            IFederatedPegSettings federatedPegSettings)
            : base(loggerFactory, walletManager, walletFeePolicy, network, transactionPolicy)
        {
            this.federationWalletTransactionHandler = federationWalletTransactionHandler;
            this.federationWalletManager = federationWalletManager;
            this.federatedPegSettings = federatedPegSettings;
            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
        }

        /// <inheritdoc />
        public Transaction BuildTransaction(TransactionBuildContext context, SecretModel[] secrets)
        {
            if (secrets == null || secrets.Length == 0)
                throw new WalletException("Could not build the transaction. Details: no private keys provided");

            this.InitializeTransactionBuilder(context);

            //if (context.Shuffle)
            //    context.TransactionBuilder.Shuffle();

            Transaction unsignedTransaction = context.TransactionBuilder.BuildTransaction(false);

            var signedTransactions = new List<Transaction>();
            foreach (SecretModel secret in secrets)
            {
                TransactionBuildContext contextCopy = context.Clone(this.network);

                this.InitializeTransactionBuilder(contextCopy);

                var mnemonic = new Mnemonic(secret.Mnemonic);
                ExtKey extKey = mnemonic.DeriveExtKey(secret.Passphrase);
                Transaction transaction = contextCopy.TransactionBuilder.AddKeys(extKey.PrivateKey).SignTransaction(unsignedTransaction);

                signedTransactions.Add(transaction);
            }

            context.TransactionBuilder.SignTransaction(unsignedTransaction);
            Transaction combinedTransaction = context.TransactionBuilder.CombineSignatures(signedTransactions.ToArray());

            if (context.TransactionBuilder.Verify(combinedTransaction, out TransactionPolicyError[] errors))
                return combinedTransaction;

            string errorsMessage = string.Join(" - ", errors.Select(s => s.ToString()));
            LoggerExtensions.LogError(this.logger, $"Build transaction failed: {errorsMessage}");
            throw new WalletException($"Could not build the transaction. Details: {errorsMessage}");
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

                (List<Coin> _, List<Wallet.UnspentOutputReference> unspentOutputs) = FederationWalletTransactionHandler.DetermineCoins(this.federationWalletManager, this.network, multiSigContext, this.federatedPegSettings);

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

                var txBuilder = new TransactionBuilder(this.network);

                txBuilder.AddKeys(privateKeys);

                Transaction signedTransaction = txBuilder.SignTransaction(transaction);

                if (this.federationWalletManager.ValidateTransaction(signedTransaction, true))
                {
                    return signedTransaction;

                }
                
                return null;
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

        /// <summary>
        /// Initializes the context transaction builder from information in <see cref="TransactionBuildContext"/>.
        /// </summary>
        /// <param name="context">Transaction build context.</param>
        protected override void InitializeTransactionBuilder(TransactionBuildContext context)
        {
            Guard.NotNull(context, nameof(context));
            Guard.NotNull(context.Recipients, nameof(context.Recipients));
            Guard.NotNull(context.AccountReference, nameof(context.AccountReference));

            // If inputs are selected by the user, we just choose them all.
            if (context.SelectedInputs != null && context.SelectedInputs.Any())
            {
                context.TransactionBuilder.CoinSelector = new AllCoinsSelector();
            }

            this.AddRecipients(context);
            this.AddOpReturnOutput(context);
            this.AddCoins(context);
            this.FindChangeAddress(context);
            this.AddFee(context);

            if (context.Time.HasValue)
                context.TransactionBuilder.SetTimeStamp(context.Time.Value);
        }
    }
}
