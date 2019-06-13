using System.ComponentModel.DataAnnotations;
using Stratis.Bitcoin.Features.Wallet.Models;

namespace Stratis.Features.FederatedPeg.Models
{
    /// <summary>
    /// A class containing the necessary parameters for a build transaction request.
    /// </summary>
    public class BuildMultisigTransactionRequest : BuildTransactionRequest
    {
        [Required(ErrorMessage = "Mnemonic phrases are required.")]
        public SecretModel[] Secrets { get; set; }
    }

    public class SecretModel
    {
        [Required(ErrorMessage = "Mnemonic is required.")]
        public string Mnemonic { get; set; }

        [Required(ErrorMessage = "Passphrase is required.")]
        public string Passphrase { get; set; }
    }
}
