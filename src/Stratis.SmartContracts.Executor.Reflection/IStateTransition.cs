﻿namespace Stratis.SmartContracts.Executor.Reflection
{
    public interface IStateTransition
    {
        StateTransitionResult Apply(ExternalCreateMessage message);
        StateTransitionResult Apply(InternalCreateMessage message);
        StateTransitionResult Apply(ExternalCallMessage message);
        StateTransitionResult Apply(InternalCallMessage message);
        StateTransitionResult Apply(ContractTransferMessage message);
    }
}