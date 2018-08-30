using System.Collections.Generic;
using NBitcoin;
using Stratis.SmartContracts.Core.State;
using Stratis.SmartContracts.Core.State.AccountAbstractionLayer;
using Stratis.SmartContracts.Executor.Reflection.ContractLogging;

namespace Stratis.SmartContracts.Executor.Reflection
{
    public class State : IState
    {
        private readonly IContractStateRepository parentRepository;
        private readonly ulong originalNonce;
        private readonly IState parent;
        private readonly IAddressGenerator addressGenerator;

        private State(IState parent, IContractStateRepository repository, IBlock block, Network network, ulong txAmount, uint256 transactionHash, IAddressGenerator addressGenerator, ulong nonce = 0)
            : this(repository, block, network, txAmount, transactionHash, addressGenerator, nonce)
        {
            this.parent = parent;
        }

        public State(IContractStateRepository repository, IBlock block, Network network, ulong txAmount,
            uint256 transactionHash, IAddressGenerator addressGenerator, ulong nonce = 0)
        {
            this.parentRepository = repository;
            this.Repository = repository.StartTracking();
            this.LogHolder = new ContractLogHolder(network);
            this.InternalTransfers = new List<TransferInfo>();
            this.BalanceState = new BalanceState(this.Repository, txAmount, this.InternalTransfers);
            this.Network = network;
            this.originalNonce = nonce;
            this.Nonce = nonce;
            this.Block = block;
            this.TransactionHash = transactionHash;
            this.addressGenerator = addressGenerator;
        }

        public uint256 TransactionHash { get; }

        public IBlock Block { get; }

        public ulong Nonce { get; private set; }

        public Network Network { get; }

        public IContractStateRepository Repository { get; private set; }

        public IContractLogHolder LogHolder { get; }

        public BalanceState BalanceState { get; }

        public List<TransferInfo> InternalTransfers { get; }

        public ulong GetNonceAndIncrement()
        {
            return this.Nonce++;
        }

        public uint160 GetNewAddress()
        {
            return this.addressGenerator.GenerateAddress(this.TransactionHash, this.GetNonceAndIncrement());
        }

        /// <summary>
        /// Reverts the state transition.
        /// </summary>
        public void Rollback()
        {
            // Reset the nonce
            this.Nonce = this.originalNonce;
            this.InternalTransfers.Clear();
            this.LogHolder.Clear();
            this.Repository.Rollback();

            // Because Rollback does not actually clear the repository state, we need to assign a new instance
            // to simulate "clearing" the intermediate state.
            this.Repository = this.parentRepository.StartTracking();
        }

        /// <summary>
        /// Commits the state transition. Updates the parent state if necessary.
        /// </summary>
        public void Commit()
        {
            this.Repository.Commit();

            // Update the parent
            if (this.parent != null)
            {
                this.parent.InternalTransfers.AddRange(this.InternalTransfers);
                this.parent.LogHolder.AddRawLogs(this.LogHolder.GetRawLogs());

                while (this.parent.Nonce < this.Nonce)
                {
                    this.parent.GetNonceAndIncrement();
                }
            }
        }

        public IState Nest(ulong txAmount)
        {
            return new State(this, this.Repository, this.Block, this.Network, txAmount, this.TransactionHash, this.addressGenerator, this.Nonce);
        }
    }
}