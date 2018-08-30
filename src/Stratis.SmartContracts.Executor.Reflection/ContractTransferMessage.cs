namespace Stratis.SmartContracts.Executor.Reflection
{
    public class ContractTransferMessage : CallMessage
    {
        public ContractTransferMessage()
        {
            this.Method = MethodCall.Receive();
        }
    }
}