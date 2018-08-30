using NBitcoin;

namespace Stratis.SmartContracts.Executor.Reflection
{
    public class InternalCallMessage : BaseMessage
    {
        /// <summary>
        /// All transfers have a destination.
        /// </summary>
        public uint160 To { get; set; }

        public MethodCall Method { get; set; }
    }
}