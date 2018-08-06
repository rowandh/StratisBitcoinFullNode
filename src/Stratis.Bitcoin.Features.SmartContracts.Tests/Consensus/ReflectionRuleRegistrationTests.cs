using System.Linq;
using NBitcoin;
using Stratis.Bitcoin.Configuration.Logging;
<<<<<<< HEAD
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Features.Consensus.CoinViews;
using Stratis.Bitcoin.Features.SmartContracts.Consensus;
using Stratis.Bitcoin.Features.SmartContracts.ReflectionExecutor;
using Stratis.Bitcoin.Features.SmartContracts.ReflectionExecutor.Consensus.Rules;
using Stratis.Bitcoin.Tests.Common;
using Stratis.Bitcoin.Utilities;
using Stratis.SmartContracts.Core;
using Stratis.SmartContracts.Core.Receipts;
using Stratis.SmartContracts.Core.State;
=======
using Stratis.Bitcoin.Features.SmartContracts.ReflectionExecutor;
using Stratis.Bitcoin.Features.SmartContracts.ReflectionExecutor.Consensus.Rules;
using Stratis.Bitcoin.Tests.Common;
>>>>>>> master
using Xunit;

namespace Stratis.Bitcoin.Features.SmartContracts.Tests.Consensus
{
    public sealed class ReflectionRuleRegistrationTests
    {
        [Fact]
        public void ReflectionVirtualMachineFeature_OnInitialize_RulesAdded()
        {
            Network network = KnownNetworks.StratisRegTest;
<<<<<<< HEAD
            var chain = new ConcurrentChain(network);
            var contractState = new ContractStateRepositoryRoot();
            var executorFactory = new Mock<ISmartContractExecutorFactory>();
            var loggerFactory = new ExtendedLoggerFactory();
            var receiptStorage = new Mock<ISmartContractReceiptStorage>();

            var consensusRules = new SmartContractPowConsensusRuleEngine(
                chain, new Mock<ICheckpoints>().Object, new Configuration.Settings.ConsensusSettings(),
                DateTimeProvider.Default, executorFactory.Object, loggerFactory, network,
                new Base.Deployments.NodeDeployments(network, chain), contractState,
                new Mock<ILookaheadBlockPuller>().Object,
                new Mock<ICoinView>().Object, receiptStorage.Object);
=======
            var loggerFactory = new ExtendedLoggerFactory();
>>>>>>> master

            var feature = new ReflectionVirtualMachineFeature(loggerFactory, network);
            feature.Initialize();

            Assert.Single(network.Consensus.Rules.Where(r => r.GetType() == typeof(SmartContractFormatRule)));
        }
    }
}