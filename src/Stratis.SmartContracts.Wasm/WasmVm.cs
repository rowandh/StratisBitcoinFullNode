using System;

namespace Stratis.SmartContracts.Wasm
{
    public class WasmVm
    {
        public VmResult Create(ulong gas, ulong amount, byte[] data, byte[] sender)
        {
            // Invoke
            // Get WASM bytecode left in memory
            // Validate
            // Inject gas metering
            // Store
            throw new NotImplementedException();
        }

        public VmResult Call(ulong gas, ulong amount, byte[] data, byte[] sender, byte[] destination)
        {
            // Retrieve code
            // Load WASM module
            // Invoke `main` with supplied data
            throw new NotImplementedException();
        }
    }
}
