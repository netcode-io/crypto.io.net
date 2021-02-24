using System;

namespace CryptoPool.IO.Ethash
{
    /// <summary>
    /// EthashAux
    /// </summary>
    public class EthashAux
    {
        public static Result Eval(int epoch, H256 headerHash, ulong nonce)
        {
            //var headerHash = Ethash.Hash256FromBytes(headerHash.data());
            //var context = Ethash.GetGlobalEpochContext(epoch);
            //var result = Ethash.Hash(context, headerHash, nonce);
            //H256 mix { reinterpret_cast<byte*>(result.mix_hash.bytes), H256.ConstructFromPointer};
            //H256 final { reinterpret_cast<byte*>(result.final_hash.bytes), H256.ConstructFromPointer};
            //return { final, mix};
            throw new NotImplementedException();
        }
    }
}
