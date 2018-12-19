using System;
using System.Collections.Generic;
using System.Text;
using NBitcoin;

namespace Stratis.FederatedPeg.IntegrationTests.Utils
{
    public class AddressBalance
    {
        public string Address { get; set; }
        public Money Balance { get; set; }
    }
}
