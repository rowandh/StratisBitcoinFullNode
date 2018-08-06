﻿using System;
using System.Collections.Generic;
using NBitcoin;
using NBitcoin.DataEncoders;
using NBitcoin.Protocol;
<<<<<<< HEAD
using Stratis.Bitcoin.Features.SmartContracts.Consensus;
=======
>>>>>>> master
using Stratis.Bitcoin.Networks;

namespace Stratis.Bitcoin.Features.SmartContracts.Networks
{
    public sealed class SmartContractsRegTest : Network
    {
        /// <summary>
        /// Took the 'InitReg' from above and adjusted it slightly (set a static flag + removed the hash check)
        /// </summary>
        public SmartContractsRegTest()
        {
            this.Name = "SmartContractsRegTest";
            this.RootFolderName = StratisMain.StratisRootFolderName;
            this.DefaultConfigFilename = StratisMain.StratisDefaultConfigFilename;
            this.Magic = 0xDAB5BFFA;
            this.DefaultPort = 18444;
            this.RPCPort = 18332;
            this.MaxTipAge = BitcoinMain.BitcoinDefaultMaxTipAgeInSeconds;
            this.MinTxFee = 1000;
            this.FallbackFee = 20000;
            this.MinRelayTxFee = 1000;

<<<<<<< HEAD
            var consensusFactory = new SmartContractPowConsensusFactory();
=======
            var consensusFactory = new SmartContractConsensusFactory();
>>>>>>> master

            Block genesisBlock = BitcoinMain.CreateBitcoinGenesisBlock(consensusFactory, 1296688602, 2, 0x207fffff, 1, Money.Coins(50m));
            ((SmartContractBlockHeader)genesisBlock.Header).HashStateRoot = new uint256("21B463E3B52F6201C0AD6C991BE0485B6EF8C092E64583FFA655CC1B171FE856");

            this.Genesis = genesisBlock;

            // Taken from StratisX.
            var consensusOptions = new PosConsensusOptions(
                maxBlockBaseSize: 1_000_000,
                maxStandardVersion: 2,
                maxStandardTxWeight: 100_000,
                maxBlockSigopsCost: 20_000
            );

            var buriedDeployments = new BuriedDeploymentsArray
            {
                [BuriedDeployments.BIP34] = 100000000,
                [BuriedDeployments.BIP65] = 100000000,
                [BuriedDeployments.BIP66] = 100000000
            };

            var bip9Deployments = new BIP9DeploymentsArray
            {
                [BIP9Deployments.TestDummy] = new BIP9DeploymentsParameters(28, 0, 999999999),
                [BIP9Deployments.CSV] = new BIP9DeploymentsParameters(0, 0, 999999999),
                [BIP9Deployments.Segwit] = new BIP9DeploymentsParameters(1, BIP9DeploymentsParameters.AlwaysActive, 999999999)
            };

            this.Consensus = new NBitcoin.Consensus(
                consensusFactory: consensusFactory,
                consensusOptions: consensusOptions,
                coinType: default(int),
                hashGenesisBlock: genesisBlock.Header.GetHash(),
                subsidyHalvingInterval: 150,
                majorityEnforceBlockUpgrade: 750,
                majorityRejectBlockOutdated: 950,
                majorityWindow: 1000,
                buriedDeployments: buriedDeployments,
                bip9Deployments: bip9Deployments,
                bip34Hash: new uint256(),
                ruleChangeActivationThreshold: 108, // 95% of 2016
                minerConfirmationWindow: 144, // nPowTargetTimespan / nPowTargetSpacing
                maxReorgLength: 500,
                defaultAssumeValid: null, // turn off assumevalid for regtest.
                maxMoney: long.MaxValue,
                coinbaseMaturity: 5,
                premineHeight: default(long),
                premineReward: Money.Zero,
                proofOfWorkReward: Money.Coins(50),
                powTargetTimespan: TimeSpan.FromSeconds(14 * 24 * 60 * 60), // two weeks
                powTargetSpacing: TimeSpan.FromSeconds(10 * 60),
                powAllowMinDifficultyBlocks: true,
                powNoRetargeting: true,
                powLimit: new Target(new uint256("7fffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff")),
                minimumChainWork: uint256.Zero,
                isProofOfStake: default(bool),
                lastPowBlock: default(int),
                proofOfStakeLimit: null,
                proofOfStakeLimitV2: null,
                proofOfStakeReward: Money.Zero
            );

            Assert(this.Consensus.HashGenesisBlock == uint256.Parse("93867319cf92c86f957a9652c1fbe7cc8cbe70c53a915ac96ee7c59cb80f94b4"));

            this.Base58Prefixes = new byte[12][];
            this.Base58Prefixes[(int)Base58Type.PUBKEY_ADDRESS] = new byte[] { (111) };
            this.Base58Prefixes[(int)Base58Type.SCRIPT_ADDRESS] = new byte[] { (196) };
            this.Base58Prefixes[(int)Base58Type.SECRET_KEY] = new byte[] { (239) };
            this.Base58Prefixes[(int)Base58Type.ENCRYPTED_SECRET_KEY_NO_EC] = new byte[] { 0x01, 0x42 };
            this.Base58Prefixes[(int)Base58Type.ENCRYPTED_SECRET_KEY_EC] = new byte[] { 0x01, 0x43 };
            this.Base58Prefixes[(int)Base58Type.EXT_PUBLIC_KEY] = new byte[] { (0x04), (0x35), (0x87), (0xCF) };
            this.Base58Prefixes[(int)Base58Type.EXT_SECRET_KEY] = new byte[] { (0x04), (0x35), (0x83), (0x94) };
            this.Base58Prefixes[(int)Base58Type.PASSPHRASE_CODE] = new byte[] { 0x2C, 0xE9, 0xB3, 0xE1, 0xFF, 0x39, 0xE2 };
            this.Base58Prefixes[(int)Base58Type.CONFIRMATION_CODE] = new byte[] { 0x64, 0x3B, 0xF6, 0xA8, 0x9A };
            this.Base58Prefixes[(int)Base58Type.STEALTH_ADDRESS] = new byte[] { 0x2b };
            this.Base58Prefixes[(int)Base58Type.ASSET_ID] = new byte[] { 115 };
            this.Base58Prefixes[(int)Base58Type.COLORED_ADDRESS] = new byte[] { 0x13 };

            Bech32Encoder encoder = Encoders.Bech32("tb");
            this.Bech32Encoders = new Bech32Encoder[2];
            this.Bech32Encoders[(int)Bech32Type.WITNESS_PUBKEY_ADDRESS] = encoder;
            this.Bech32Encoders[(int)Bech32Type.WITNESS_SCRIPT_ADDRESS] = encoder;

            this.Checkpoints = new Dictionary<int, CheckpointInfo>();

            this.DNSSeeds = new List<DNSSeedData>();
            this.SeedNodes = new List<NetworkAddress>();
        }
    }
}