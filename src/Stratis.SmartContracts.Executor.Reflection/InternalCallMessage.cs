using NBitcoin;

namespace Stratis.SmartContracts.Executor.Reflection
{
    public class InternalCallMessage : BaseMessage
    {
        public InternalCallMessage(uint160 to, uint160 from, ulong amount, Gas gasLimit, MethodCall methodCall)
            : base(from, amount, gasLimit)
        {
            this.To = to;
            this.Method = methodCall;
        }

        /// <summary>
        /// All transfers have a destination.
        /// </summary>
        public uint160 To { get; }

        public MethodCall Method { get; }
    }
}