using NBitcoin;

namespace Stratis.SmartContracts.Executor.Reflection
{
    public class ExternalCreateMessage : BaseMessage
    {
        public ExternalCreateMessage(uint160 from, ulong amount, Gas gasLimit, byte[] code, object[] parameters)
            : base(from, amount, gasLimit)
        {
            this.Code = code;
            this.Method = new MethodCall(null, parameters);
        }

        public byte[] Code { get; }

        public MethodCall Method { get; }
    }
}