using NBitcoin;

namespace Stratis.SmartContracts.Executor.Reflection
{
    public class InternalCreateMessage : BaseMessage
    {
        public InternalCreateMessage(uint160 from, ulong amount, Gas gasLimit, object[] parameters, string typeName)
            : base(from, amount, gasLimit)
        {
            this.Parameters = parameters;
            this.Type = typeName;
        }

        /// <summary>
        /// Internal creates need a method call with params and an empty method name.
        /// </summary>
        public object[] Parameters{ get; }

        /// <summary>
        /// Internal creates need to specify the Type they are creating.
        /// </summary>
        public string Type { get; }
    }
}