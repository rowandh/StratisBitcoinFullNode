namespace Stratis.SmartContracts.Executor.Reflection
{
    public class InternalCreateMessage : BaseMessage
    {
        /// <summary>
        /// Internal creates need a method call with params and an empty method name.
        /// </summary>
        public MethodCall Method { get; set; }

        /// <summary>
        /// Internal creates need to specify the Type they are creating.
        /// </summary>
        public string Type { get; set; }
    }
}