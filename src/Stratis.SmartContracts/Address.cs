using System;
using System.Collections.Generic;

namespace Stratis.SmartContracts
{
    //public class Mapping<TValue>
    //{
    //    private IPersistentState state;

    //    public Mapping(IPersistentState state)
    //    {
    //        this.state = state;
    //    }

    //    public TValue this[string key]
    //    {
    //        get
    //        {
    //            return this.state.GetBytes(key);
    //        }
    //    }

    //    public TValue Value { get; }
    //}

    public struct ScArray
    {
        private IPersistentState state;
        private IInternalHashHelper hasher;
        private ISerializer serializer;
        private byte[] slot;

        private ScArray(byte[] slot, ISerializer serializer, IPersistentState state, IInternalHashHelper hasher)
        {
            this.slot = slot;
            this.state = state;
            this.hasher = hasher;
            this.serializer = serializer;
        }

        public static ScArray Wrap(string name, SmartContract contract)
        {
            var slot = contract.Hasher.Keccak256(contract.Serializer.Serialize(name));
            return new ScArray(slot, contract.Serializer, contract.PersistentState, contract.Hasher);
        }

        public byte[] this[uint index]
        {
            get
            {
                var indexBytes = this.serializer.Serialize(index);
                var newBytes = new byte[this.slot.Length + indexBytes.Length];
                Buffer.BlockCopy(this.slot, 0, newBytes, 0, this.slot.Length);
                Buffer.BlockCopy(indexBytes, 0, newBytes, this.slot.Length, indexBytes.Length);
                
                return this.state.GetBytes(this.hasher.Keccak256(newBytes));
            }
            set
            {
                var indexBytes = this.serializer.Serialize(index);
                var newBytes = new byte[this.slot.Length + indexBytes.Length];
                Buffer.BlockCopy(this.slot, 0, newBytes, 0, this.slot.Length);
                Buffer.BlockCopy(indexBytes, 0, newBytes, this.slot.Length, indexBytes.Length);

                var length = this.Length + 1;
                this.state.SetBytes(this.slot, this.serializer.Serialize(length));
                this.state.SetBytes(this.hasher.Keccak256(newBytes), value);
            }
        }

        public uint Length => this.serializer.ToUInt32(this.state.GetBytes(this.slot));
    }

    /// <summary>
    /// Helper struct that represents a STRAT address and is used when sending or receiving funds.
    /// <para>
    /// Note that the format of the address is not validated on construction, but when trying to send funds to this address.
    /// </para>
    /// </summary>
    public struct Address
    {
        /// <summary>
        /// The address as a string, in base58 format.
        /// </summary>
        public readonly string Value;

        /// <summary>
        /// Create a new address
        /// </summary>
        public Address(string address)
        {
            this.Value = address;
        }

        public override string ToString()
        {           
            return this.Value;
        }

        public static explicit operator Address(string value)
        {
            return new Address(value);
        }

        public static implicit operator string(Address x)
        {
            return x.Value;
        }

        public static bool operator ==(Address obj1, Address obj2)
        {
            return obj1.Equals(obj2);
        }

        public static bool operator !=(Address obj1, Address obj2)
        {
            return !obj1.Equals(obj2);
        }

        public override bool Equals(object obj)
        {
            if (obj is Address other)
            {
                return this.Equals(other);
            }

            return false;
        }

        public bool Equals(Address obj)
        {
            return this.Value == obj.Value;
        }

        public override int GetHashCode()
        {
            return this.Value.GetHashCode();
        }
    }
}