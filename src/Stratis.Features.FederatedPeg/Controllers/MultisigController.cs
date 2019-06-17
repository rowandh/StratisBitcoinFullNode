﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Features.Wallet;
using Stratis.Bitcoin.Features.Wallet.Models;
using Stratis.Bitcoin.Utilities;
using Stratis.Bitcoin.Utilities.JsonErrors;
using Stratis.Bitcoin.Utilities.ModelStateErrors;
using Stratis.Features.FederatedPeg.Models;

namespace Stratis.Features.FederatedPeg.Controllers
{
    /// <summary>
    /// Controller providing operations on a wallet.
    /// </summary>
    [Route("api/[controller]")]
    public class MultisigController : Controller
    {
        private readonly IFedMultiSigWithdrawalTransactionBuilder fedMultiSigWithdrawalTransactionBuilder;

        /// <summary>Specification of the network the node runs on - regtest/testnet/mainnet.</summary>
        private readonly Network network;

        /// <summary>Instance logger.</summary>
        private readonly ILogger logger;

        public MultisigController(
            ILoggerFactory loggerFactory,
            IFedMultiSigWithdrawalTransactionBuilder fedMultiSigWithdrawalTransactionBuilder,
            Network network)
        {
            this.fedMultiSigWithdrawalTransactionBuilder = fedMultiSigWithdrawalTransactionBuilder;
            this.network = network;
            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
        }

        /// <summary>
        /// Builds a transaction and returns the hex to use when executing the transaction.
        /// </summary>
        /// <param name="request">An object containing the parameters used to build a transaction.</param>
        /// <returns>A JSON object including the transaction ID, the hex used to execute
        /// the transaction, and the transaction fee.</returns>
        [Route("build-transaction")]
        [HttpPost]
        public IActionResult BuildTransaction([FromBody] BuildMultisigTransactionRequest request)
        {
            Guard.NotNull(request, nameof(request));

            if (!this.ModelState.IsValid)
                return ModelStateErrors.BuildErrorResponse(this.ModelState);

            try
            {
                Transaction transactionResult = this.fedMultiSigWithdrawalTransactionBuilder.BuildTransaction(request);

                var model = new WalletBuildTransactionModel
                {
                    Hex = transactionResult.ToHex(),
                    Fee = string.IsNullOrEmpty(request.FeeAmount) ? null : Money.Parse(request.FeeAmount),
                    TransactionId = transactionResult.GetHash()
                };

                return this.Json(model);
            }
            catch (Exception e)
            {
                LoggerExtensions.LogError(this.logger, "Exception occurred: {0}", e.ToString());
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }
    }
}