using NBitcoin;

namespace Stratis.SmartContracts.Executor.Reflection
{
    public class CallMessage : BaseMessage
    {
        /// <summary>
        /// All transfers have a destination.
        /// </summary>
        public uint160 To { get; set; }

        public byte[] Code { get; set; }

        public MethodCall Method { get; set; }
    }
}