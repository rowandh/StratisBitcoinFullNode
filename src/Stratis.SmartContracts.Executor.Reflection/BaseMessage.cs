using NBitcoin;

namespace Stratis.SmartContracts.Executor.Reflection
{
    public abstract class BaseMessage
    {
        /// <summary>
        /// All transfers have a recipient.
        /// </summary>
        public uint160 From { get; set; }

        /// <summary>
        /// All transfers have an amount.
        /// </summary>
        public ulong Amount { get; set; }

        /// <summary>
        /// All transfers have some gas limit associated with them. This is even required for fallback calls.
        /// </summary>
        public Gas GasLimit { get; set; }
    }
}