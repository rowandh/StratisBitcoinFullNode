using NBitcoin;

namespace Stratis.SmartContracts.Executor.Reflection
{
    public class ExternalCreateMessage : BaseMessage
    {
        public ExternalCreateMessage(uint160 from, ulong amount, Gas gasLimit, byte[] code, object[] parameters)
            : base(from, amount, gasLimit)
        {
            this.Code = code;
            this.Parameters = parameters;
        }

        public byte[] Code { get; }

        /// <summary>
        /// The parameters to use when creating the contract.
        /// </summary>
        public object[] Parameters { get; }
    }
}