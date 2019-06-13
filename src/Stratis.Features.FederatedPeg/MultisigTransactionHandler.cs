using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.Policy;
using Stratis.Bitcoin.Features.Wallet;
using Stratis.Bitcoin.Features.Wallet.Interfaces;
using Stratis.Bitcoin.Features.Wallet.Models;
using Stratis.Bitcoin.Utilities;
using Stratis.Features.FederatedPeg.Models;

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
            StandardTransactionPolicy transactionPolicy)
            : base(loggerFactory, walletManager, walletFeePolicy, network, transactionPolicy)
        {
            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
        }

        /// <inheritdoc />
        public Transaction BuildTransaction(TransactionBuildContext context, SecretModel[] secrets)
        {
            if (secrets == null || secrets.Length == 0)
                throw new WalletException("Could not build the transaction. Details: no private keys provided");

            this.InitializeTransactionBuilder(context);

            if (context.Shuffle)
                context.TransactionBuilder.Shuffle();

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

            Transaction combinedTransaction = context.TransactionBuilder.CombineSignatures(signedTransactions.ToArray());

            if (context.TransactionBuilder.Verify(combinedTransaction, out TransactionPolicyError[] errors))
                return combinedTransaction;

            string errorsMessage = string.Join(" - ", errors.Select(s => s.ToString()));
            LoggerExtensions.LogError(this.logger, $"Build transaction failed: {errorsMessage}");
            throw new WalletException($"Could not build the transaction. Details: {errorsMessage}");
        }

        public Transaction BuildTransaction(BuildMultisigTransactionRequest request)
        {
            var recipients = new List<Recipient>();
            foreach (RecipientModel recipientModel in request.Recipients)
            {
                recipients.Add(new Recipient
                {
                    ScriptPubKey = BitcoinAddress.Create(recipientModel.DestinationAddress, this.network).ScriptPubKey,
                    Amount = recipientModel.Amount
                });
            }

            var context = new TransactionBuildContext(this.network)
            {
                AccountReference = new WalletAccountReference(request.WalletName, request.AccountName),
                TransactionFee = string.IsNullOrEmpty(request.FeeAmount) ? null : Money.Parse(request.FeeAmount),
                MinConfirmations = request.AllowUnconfirmed ? 0 : 1,
                Shuffle = request.ShuffleOutputs ?? true, // We shuffle transaction outputs by default as it's better for anonymity.
                OpReturnData = request.OpReturnData,
                OpReturnAmount = string.IsNullOrEmpty(request.OpReturnAmount) ? null : Money.Parse(request.OpReturnAmount),
                WalletPassword = request.Password,
                SelectedInputs = request.Outpoints?.Select(u => new OutPoint(uint256.Parse(u.TransactionId), u.Index)).ToList(),
                AllowOtherInputs = false,
                Recipients = recipients
            };

            if (!string.IsNullOrEmpty(request.FeeType))
            {
                context.FeeType = FeeParser.Parse(request.FeeType);
            }

            return this.BuildTransaction(context, request.Secrets);
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
