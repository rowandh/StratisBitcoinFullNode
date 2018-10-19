using System;

namespace Stratis.SmartContracts
{
    /// <summary>
    /// Represents an address used when sending or receiving funds.
    /// </summary>
    public struct Address
    {
        private readonly string addressString;

        public const int Width = 160 / 8;

        public Address(Address other)
        {            
            this.addressString = other.addressString;
        }

        private Address(string str)
        {
            this.addressString = str;
        }
        
        internal static Address Create(string str)
        {
            return new Address(str);
        }
        
        public override string ToString()
        {
            return this.addressString;
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
            return this.addressString == obj.addressString;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(this.addressString);
        }
    }
}