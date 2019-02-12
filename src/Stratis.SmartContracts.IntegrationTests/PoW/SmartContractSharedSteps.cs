using Stratis.Bitcoin.Features.SmartContracts.Wallet;
using Stratis.Bitcoin.Features.Wallet.Models;
using Stratis.Bitcoin.IntegrationTests.Common;
using Stratis.Bitcoin.IntegrationTests.Common.EnvironmentMockUpHelpers;

namespace Stratis.SmartContracts.IntegrationTests.PoW
{
    public static class SmartContractSharedSteps
    {
        public static void SendTransaction(CoreNode scSender, CoreNode scReceiver, SmartContractWalletController senderWalletController, string responseHex)
        {
            // Avoids a scenario where an SC transaction hits a node and attempts to reference a coinbase that does not exist in the coinview yet.
            TestHelper.WaitLoop(() => TestHelper.AreNodesSynced(scReceiver, scSender));

            senderWalletController.SendTransaction(new SendTransactionRequest
            {
                Hex = responseHex
            });

            TestHelper.WaitLoop(() => scReceiver.CreateRPCClient().GetRawMempool().Length > 0);
        }
    }
}