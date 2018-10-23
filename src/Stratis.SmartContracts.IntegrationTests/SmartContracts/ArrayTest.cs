using System;
using Stratis.SmartContracts;

public class ArrayTest : SmartContract
{
    public ArrayTest(ISmartContractState state)
        : base(state)
    {
        var array = ScArray.Wrap("Test", this);

        array[0] = new byte[] { 0xAA };
        array[1] = new byte[] { 0xBB };
        array[2] = new byte[] { 0xCC };
    }

    public void GetArrayElement(uint index)
    {
        var array = ScArray.Wrap("Test", this);

        this.PersistentState.SetBytes("result", array[index]);
    }

    public void GetLength()
    {
        var array = ScArray.Wrap("Test", this);

        this.PersistentState.SetUInt32("length", array.Length);
    }
}