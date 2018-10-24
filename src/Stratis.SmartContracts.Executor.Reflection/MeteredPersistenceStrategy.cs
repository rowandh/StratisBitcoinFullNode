using NBitcoin;
using Stratis.Bitcoin.Utilities;
using Stratis.SmartContracts.Core.State;

namespace Stratis.SmartContracts.Executor.Reflection
{
    /// <summary>
    /// Defines a data persistence strategy for a byte[] key value pair belonging to an address.
    /// Uses a GasMeter to perform accounting
    /// </summary>
    public class MeteredPersistenceStrategy : IPersistenceStrategy
    {
        private readonly IStateRepository stateDb;
        private readonly IGasMeter gasMeter;

        public MeteredPersistenceStrategy(IStateRepository stateDb, IGasMeter gasMeter)
        {
            Guard.NotNull(stateDb, nameof(stateDb));
            Guard.NotNull(gasMeter, nameof(gasMeter));

            this.stateDb = stateDb;
            this.gasMeter = gasMeter;
        }

        public bool ContractExists(uint160 address)
        {
            this.gasMeter.Spend((Gas)GasPriceList.StorageCheckContractExistsCost);

            return this.stateDb.IsExist(address);
        }

        public byte[] FetchBytes(uint160 address, byte[] key)
        {
            byte[] value = this.stateDb.GetStorageValue(address, key);

            Gas operationCost = GasPriceList.StorageRetrieveOperationCost(key, value);
            this.gasMeter.Spend(operationCost);

            return value;
        }

        public void StoreBytes(uint160 address, byte[] key, byte[] value)
        {
            Gas operationCost = GasPriceList.StorageSaveOperationCost(
                key,
                value);

            this.gasMeter.Spend(operationCost);
            this.stateDb.SetStorageValue(address, key, value);
        }
    }
}