namespace Stratis.SmartContracts.Executor.Reflection
{
    public class ContractTransferMessage : ExternalCallMessage
    {
        public ContractTransferMessage()
        {
            this.Method = MethodCall.Receive();
        }
    }
}