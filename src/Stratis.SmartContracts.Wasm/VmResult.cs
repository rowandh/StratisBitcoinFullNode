using System.Collections.Generic;
using Stratis.SmartContracts.Core.State.AccountAbstractionLayer;

namespace Stratis.SmartContracts.Wasm
{
    public class VmResult
    {
        public List<TransferInfo> InternalTransfers { get; }

        public object Result { get; }
    }
}