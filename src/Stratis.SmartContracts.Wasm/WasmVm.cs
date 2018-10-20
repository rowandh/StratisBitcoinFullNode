using System;

namespace Stratis.SmartContracts.Wasm
{
    public class WasmVm
    {
        public VmResult Create(ulong gas, ulong amount, byte[] data, byte[] sender)
        {
            throw new NotImplementedException();
        }

        public VmResult Call(ulong gas, ulong amount, byte[] data, byte[] sender, byte[] destination)
        {
            throw new NotImplementedException();
        }
    }
}
