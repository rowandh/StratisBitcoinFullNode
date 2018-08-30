namespace Stratis.SmartContracts.Executor.Reflection
{
    public class ExternalCreateMessage : BaseMessage
    {
        public byte[] Code { get; set; }

        public MethodCall Method { get; set; }
    }
}