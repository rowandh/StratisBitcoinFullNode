using System.Collections.Generic;
using NBitcoin;
using Stratis.SmartContracts.CLR;

namespace Stratis.SmartContracts.Testing
{
    /// <summary>
    /// Represents a contract state store in memory.
    /// </summary>
    public class InMemoryPersistenceStrategy : IPersistenceStrategy
    {
        public readonly Dictionary<uint160, Dictionary<byte[], byte[]>> db = new Dictionary<uint160, Dictionary<byte[], byte[]>>();

        public readonly object dbLock = new object();

        public bool ContractExists(uint160 address)
        {
            lock (this.dbLock)
            {
                return this.db.ContainsKey(address);
            }
        }

        public byte[] FetchBytes(uint160 address, byte[] key)
        {
            lock (this.dbLock)
            {
                if(!this.db.ContainsKey(address))
                    return null;

                return this.db[address].ContainsKey(key)
                    ? this.db[address][key]
                    : null;
            }
        }

        public void StoreBytes(uint160 address, byte[] key, byte[] value)
        {
            lock (this.dbLock)
            {
                if (!this.db.ContainsKey(address))
                    this.db[address] = new Dictionary<byte[], byte[]>(StructuralEqualityComparer<byte[]>.Default);

                this.db[address][key] = value;
            }
        }
    }
}
