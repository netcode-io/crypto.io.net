using System;

namespace Crypto.IO
{
    /// <summary>
    /// EthashAux
    /// </summary>
    public class EthashAux
    {
        public static (H256 value, H256 mixHash) Eval(int epoch, H256 headerHash, ulong nonce)
        {
            var hash = Ethash.Hash256FromBytes(headerHash.Data);
            var context = Ethash.GetGlobalEpochContext(epoch);
            var result = Ethash.Hash(context, hash, nonce);
            //H256 mix { reinterpret_cast<byte*>(result.mix_hash.bytes), H256.ConstructFromPointer};
            //H256 final { reinterpret_cast<byte*>(result.final_hash.bytes), H256.ConstructFromPointer};
            //return { final, mix};
            throw new NotImplementedException();
        }
    }
}
