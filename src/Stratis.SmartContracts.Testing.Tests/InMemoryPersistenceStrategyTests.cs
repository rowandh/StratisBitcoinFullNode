using NBitcoin;
using Xunit;

namespace Stratis.SmartContracts.Testing.Tests
{
    public class InMemoryPersistenceStrategyTests
    {
        private readonly InMemoryPersistenceStrategy strategy;
        private readonly uint160 address;

        public InMemoryPersistenceStrategyTests()
        {
            this.strategy = new InMemoryPersistenceStrategy();
            this.address = new uint160("0x0000000000000000000000000000000000000001");
        }

        [Fact]
        public void ContractExists_Failure()
        {
            Assert.False(this.strategy.ContractExists(this.address));
        }

        [Fact]
        public void ContractExists_AfterStoringBytes_Success()
        {
            this.strategy.StoreBytes(this.address, new byte[] { }, new byte[] { });
            Assert.True(this.strategy.ContractExists(this.address));
        }

        [Theory]
        [InlineData(new byte[] { }, new byte[] { })]
        [InlineData(new byte[] {0xAA, 0xBB, 0xCC}, new byte[] { })]
        [InlineData(new byte[] { }, new byte[] {0xAA, 0xBB, 0xCC})]
        [InlineData(new byte[] {0xCC, 0xBB, 0xAA}, new byte[] {0xAA, 0xBB, 0xCC})]
        public void StoreBytes_Success(byte[] key, byte[] value)
        {
            this.strategy.StoreBytes(this.address, key, value);
            Assert.Equal(value, this.strategy.FetchBytes(this.address, key),
                StructuralEqualityComparer<byte[]>.Default);
        }

        [Theory]
        [InlineData(new byte[] { })]
        [InlineData(new byte[] {0xAA, 0xBB, 0xCC})]
        public void FetchBytes_KeyNotFound_Returns_Null(byte[] key)
        {
            Assert.Null(this.strategy.FetchBytes(this.address, key));
        }
    }
}