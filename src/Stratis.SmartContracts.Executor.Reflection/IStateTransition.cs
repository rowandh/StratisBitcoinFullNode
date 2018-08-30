namespace Stratis.SmartContracts.Executor.Reflection
{
    public interface IStateTransition
    {
        StateTransitionResult Apply(ExternalCreateMessage message);
        StateTransitionResult Apply(InternalCreateMessage message);
        StateTransitionResult Apply(CallMessage message);
        StateTransitionResult Apply(ContractTransferMessage message);
    }
}