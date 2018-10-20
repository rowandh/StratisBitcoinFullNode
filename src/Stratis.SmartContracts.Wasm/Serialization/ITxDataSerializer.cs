namespace Stratis.SmartContracts.Wasm.Serialization
{
    public interface ITxDataSerializer
    {
        (bool success, WasmTxData result) Deserialize(byte[] txData);

        byte[] Serialize(WasmTxData txData);
    }
}