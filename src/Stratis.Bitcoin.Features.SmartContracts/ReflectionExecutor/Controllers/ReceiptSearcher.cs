﻿using System.Collections.Generic;
using System.Linq;
using System.Text;
using NBitcoin;
using Stratis.Bitcoin.Interfaces;
using Stratis.SmartContracts.CLR;
using Stratis.SmartContracts.Core;
using Stratis.SmartContracts.Core.Interfaces;
using Stratis.SmartContracts.Core.Receipts;

namespace Stratis.Bitcoin.Features.SmartContracts.ReflectionExecutor.Controllers
{
    /// <summary>
    /// Searches the chain for receipts that match the given criteria and returns the receipts.
    /// </summary>
    public class ReceiptSearcher
    {
        private readonly ChainIndexer chainIndexer;
        private readonly IBlockStore blockStore;
        private readonly IReceiptRepository receiptRepository;
        private readonly Network network;
        private readonly ReceiptMatcher matcher;

        public ReceiptSearcher(ChainIndexer chainIndexer, IBlockStore blockStore, IReceiptRepository receiptRepository, Network network)
        {
            this.chainIndexer = chainIndexer;
            this.blockStore = blockStore;
            this.receiptRepository = receiptRepository;
            this.network = network;
            this.matcher = new ReceiptMatcher();
        }

        public List<Receipt> SearchReceipts(string contractAddress, string eventName, int fromBlock, int? toBlock, IEnumerable<byte[]> topics)
        {
            byte[] eventBytes = Encoding.UTF8.GetBytes(eventName);

            var topicsList = new List<byte[]> { eventBytes };

            if (topics != null)
            {
                topicsList.AddRange(topics);
            }

            return this.SearchReceipts(contractAddress, fromBlock, toBlock, topicsList);
        }

        public List<Receipt> SearchReceipts(string contractAddress, int fromBlock = 0, int? toBlock = null, IEnumerable<byte[]> topics = null)
        {
            topics = topics ?? Enumerable.Empty<byte[]>();

            // Build the bytes we can use to check for this event.
            uint160 addressUint160 = contractAddress.ToUint160(this.network);

            var chainIndexerRangeQuery = new ChainIndexerRangeQuery(this.chainIndexer);

            // WORKAROUND
            // This is a workaround due to the BlockStore.GetBlocks returning null for genesis.
            // We don't ever expect any receipts in the genesis block, so it's safe to ignore it.
            if (fromBlock == 0)
                fromBlock = 1;

            // Loop through all headers and check bloom.
            IEnumerable<ChainedHeader> blockHeaders = chainIndexerRangeQuery.EnumerateRange(fromBlock, toBlock);

            // Match the blocks where the combination of all receipts passes the filter.
            var matches = new List<ChainedHeader>();
            foreach (ChainedHeader chainedHeader in blockHeaders)
            {
                var scHeader = (ISmartContractBlockHeader)chainedHeader.Header;

                if (scHeader.LogsBloom.Test(addressUint160, topics))
                    matches.Add(chainedHeader);
            }

            // For all matching headers, get the block from local db.
            List<uint256> matchedBlockHashes = matches.Select(m => m.HashBlock).ToList();
            List<NBitcoin.Block> blocks = this.blockStore.GetBlocks(matchedBlockHashes);

            List<uint256> transactionHashes = blocks
                .SelectMany(block => block.Transactions)
                .Select(t => t.GetHash())
                .ToList();

            IList<Receipt> receipts = this.receiptRepository.RetrieveMany(transactionHashes);

            // For each block, get all receipts, and if they match, add to list to return.
            return this.matcher.MatchReceipts(receipts, addressUint160, topics);
        }
    }
}