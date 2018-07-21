using System;
using Stratis.SmartContracts;

public class InterContract1 : SmartContract
{
    public InterContract1(ISmartContractState state) : base(state) { }

    public uint ReturnInt()
    {
        this.PersistentState.SetString("test", "testString");
        return (uint) this.Balance;
    }
}