namespace Stratis.SmartContracts.Wasm
{
    /// <summary>
    /// Fields that are serialized and sent as data to a WASM contract transaction.
    /// </summary>
    public class WasmTxData
    {
        public WasmTxData(ulong gasPrice, ulong gasLimit, byte[] data)
        {
            this.GasPrice = gasPrice;
            this.GasLimit = gasLimit;
            this.Data = data;
        }

        public WasmTxData(ulong gasPrice, ulong gasLimit, byte[] address, byte[] data)
        {
            this.GasPrice = gasPrice;
            this.GasLimit = gasLimit;
            this.Data = data;
            this.Address = address;
        }

        public byte[] Address { get; set; }

        public byte[] Data { get; set; }

        public ulong GasLimit { get; set; }

        public ulong GasPrice { get; set; }

        public bool IsCreate => this.Address != null && this.Address.Length == 20;
    }
}
