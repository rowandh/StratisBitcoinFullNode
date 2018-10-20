using System.Collections.Generic;
using NBitcoin;
using Stratis.SmartContracts.Core;
using Stratis.SmartContracts.Core.Receipts;

namespace Stratis.SmartContracts.Wasm
{
    public class WasmExecutionResult : IContractExecutionResult
    {
        public ContractErrorMessage ErrorMessage { get; set; }
        public Gas GasConsumed { get; set; }
        public uint160 NewContractAddress { get; set; }
        public uint160 To { get; set; }
        public object Return { get; set; }
        public bool Revert { get; }
        public Transaction InternalTransaction { get; set; }
        public ulong Fee { get; set; }
        public TxOut Refund { get; set; }
        public IList<Log> Logs { get; set; }
    }
}