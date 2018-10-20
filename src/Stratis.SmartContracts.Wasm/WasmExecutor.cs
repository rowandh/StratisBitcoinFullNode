using Stratis.SmartContracts.Core;
using Stratis.SmartContracts.Core.State;
using Stratis.SmartContracts.Wasm.Serialization;

namespace Stratis.SmartContracts.Wasm
{
    /// <summary>
    /// Handles execution of transactions in the WASM VM.
    /// Maps result of execution to the UTXO model.
    /// </summary>
    public class WasmExecutor : IContractExecutor
    {
        private readonly ITxDataSerializer txDataSerializer;
        private readonly IStateRepository stateRepository;

        public WasmExecutor(
            ITxDataSerializer txDataSerializer,
            IStateRepository stateRepository)
        {
            this.txDataSerializer = txDataSerializer;
            this.stateRepository = stateRepository;
        }

        public IContractExecutionResult Execute(IContractTransactionContext transactionContext)
        {
            // Deserialization can't fail due to mempool rule
            (var _, WasmTxData txData) = this.txDataSerializer.Deserialize(transactionContext.Data);

            if (txData.IsCreate)
            {
                // Apply a create message
            }
            else
            {
                // Apply a call message
            }

            // Apply any Gas refunds


            return new WasmExecutionResult();
        }
    }
}
